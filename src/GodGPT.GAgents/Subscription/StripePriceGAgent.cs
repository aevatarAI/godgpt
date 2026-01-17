using System;
using System.Text.Json;
using System.Threading.Tasks;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Helpers;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Subscription.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Stripe;

namespace Aevatar.Application.Grains.Subscription;

/// <summary>
/// GAgent for Stripe-specific price webhook handling.
/// Delegates storage to PlatformPriceGAgent.
/// </summary>
public class StripePriceGAgent : Grain, IStripePriceGAgent
{
    private readonly ILogger<StripePriceGAgent> _logger;
    private readonly StripeOptions _stripeOptions;

    private IPlatformPriceGAgent PriceGAgent =>
        GrainFactory.GetGrain<IPlatformPriceGAgent>(SubscriptionGAgentKeys.PriceGAgentKey);

    private ISubscriptionProductGAgent ProductGAgent =>
        GrainFactory.GetGrain<ISubscriptionProductGAgent>(SubscriptionGAgentKeys.ProductGAgentKey);

    public StripePriceGAgent(
        ILogger<StripePriceGAgent> logger,
        IOptions<StripeOptions> stripeOptions)
    {
        _logger = logger;
        _stripeOptions = stripeOptions.Value;
    }

    #region Webhook Handler

    public async Task<bool> HandleWebhookAsync(string json, string signature)
    {
        _logger.LogInformation("[StripePriceGAgent] Processing Stripe price webhook");

        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(signature))
        {
            _logger.LogWarning("[StripePriceGAgent] Invalid webhook parameters: json or signature is empty");
            return false;
        }

        // Validate webhook signature and parse event
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(json, signature, _stripeOptions.WebhookSecret);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "[StripePriceGAgent] Webhook signature validation failed");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[StripePriceGAgent] Unexpected error processing webhook: {Message}",
                ex.Message);
            return false;
        }

        _logger.LogInformation(
            "[StripePriceGAgent] Processing event: Type={Type}, Id={Id}",
            stripeEvent.Type, stripeEvent.Id);

        // Handle price events
        try
        {
            switch (stripeEvent.Type)
            {
                case EventTypes.PriceCreated:
                    return await HandlePriceCreatedEventAsync(stripeEvent);

                case EventTypes.PriceUpdated:
                    return await HandlePriceUpdatedEventAsync(stripeEvent);

                case EventTypes.PriceDeleted:
                    return await HandlePriceDeletedEventAsync(stripeEvent);

                default:
                    _logger.LogDebug(
                        "[StripePriceGAgent] Ignoring unhandled event type: {Type}",
                        stripeEvent.Type);
                    return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StripePriceGAgent] Error handling price event: {Type}", stripeEvent.Type);
            return false;
        }
    }

    private async Task<bool> HandlePriceCreatedEventAsync(Event stripeEvent)
    {
        if (!TryExtractPrice(stripeEvent, out var price))
            return false;

        if (string.IsNullOrEmpty(price.ProductId))
        {
            _logger.LogWarning("[StripePriceGAgent] Product ID is empty for price: {PriceId}", price.Id);
            return false;
        }

        _logger.LogDebug("[StripePriceGAgent] Price created: {Price}", JsonSerializer.Serialize(price));

        // Find internal product by Stripe product ID
        var product = await ProductGAgent.GetProductByPlatformProductIdAsync(
            price.ProductId, PaymentPlatform.Stripe);

        if (product == null)
        {
            _logger.LogWarning(
                "[StripePriceGAgent] No internal product found for Stripe product: {StripeProductId}",
                price.ProductId);
            return false;
        }

        _logger.LogInformation(
            "[StripePriceGAgent] Handling price.created: PriceId={PriceId}, ProductId={ProductId}",
            price.Id, product.Id);

        return await UpsertPriceFromStripeAsync(product.Id, price);
    }

    private async Task<bool> HandlePriceUpdatedEventAsync(Event stripeEvent)
    {
        if (!TryExtractPrice(stripeEvent, out var price))
            return false;

        // Find product by price ID from storage
        var existingPrice = await PriceGAgent.GetPriceByPlatformPriceIdAsync(price.Id);
        if (existingPrice == null)
        {
            _logger.LogWarning("[StripePriceGAgent] No product mapping found for price: {PriceId}", price.Id);
            return false;
        }

        _logger.LogDebug("[StripePriceGAgent] Price updated: {Price}", JsonSerializer.Serialize(price));

        // If price is deactivated, remove it
        if (!price.Active)
        {
            _logger.LogInformation("[StripePriceGAgent] Price deactivated, removing: PriceId={PriceId}", price.Id);
            await PriceGAgent.DeletePriceByPlatformIdAsync(price.Id);
            return true;
        }

        _logger.LogInformation(
            "[StripePriceGAgent] Handling price.updated: PriceId={PriceId}, ProductId={ProductId}",
            price.Id, existingPrice.ProductId);

        return await UpsertPriceFromStripeAsync(existingPrice.ProductId, price);
    }

    private async Task<bool> HandlePriceDeletedEventAsync(Event stripeEvent)
    {
        if (!TryExtractPrice(stripeEvent, out var price))
            return false;

        _logger.LogInformation("[StripePriceGAgent] Handling price.deleted: PriceId={PriceId}", price.Id);
        await PriceGAgent.DeletePriceByPlatformIdAsync(price.Id);
        return true;
    }

    #endregion

    #region Private Helpers

    private bool TryExtractPrice(Event stripeEvent, out Price price)
    {
        price = stripeEvent.Data.Object as Price;
        if (price == null)
        {
            _logger.LogWarning("[StripePriceGAgent] Failed to parse price object from event");
            return false;
        }

        if (string.IsNullOrEmpty(price.Id))
        {
            _logger.LogWarning("[StripePriceGAgent] Price ID is empty");
            return false;
        }

        return true;
    }

    private async Task<bool> UpsertPriceFromStripeAsync(Guid productId, Price stripePrice)
    {
        if (stripePrice.BillingScheme != "per_unit")
        {
            _logger.LogWarning(
                "[StripePriceGAgent] Skipping tiered price: {PriceId}",
                stripePrice.Id);
            return false;
        }

        // Use UnitAmountDecimal with fallback to UnitAmount
        var amount = stripePrice.UnitAmountDecimal.HasValue 
            ? stripePrice.UnitAmountDecimal.Value / 100m  // Stripe uses cents
            : 0m;

        _logger.LogDebug(
            "[StripePriceGAgent] Upserting price: PriceId={PriceId}, Amount={Amount}, Currency={Currency}",
            stripePrice.Id, amount, stripePrice.Currency);

        await PriceGAgent.SetPriceAsync(productId, new SetPriceDto
        {
            Platform = PaymentPlatform.Stripe,
            PlatformPriceId = stripePrice.Id,
            Price = amount,
            Currency = stripePrice.Currency.ToUpperInvariant()
        });

        return true;
    }

    #endregion
}
