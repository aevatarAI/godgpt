using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;
using Stripe;
using System;

namespace Aevatar.Application.Grains.Webhook;

public interface IStripeEventProcessingGrain : IGrainWithStringKey
{
    Task<string> ParseEventAndGetUserIdAsync([Immutable] string json);
}

[StatelessWorker]
[Reentrant]
public class StripeEventProcessingGrain : Grain, IStripeEventProcessingGrain
{
    private readonly ILogger<StripeEventProcessingGrain> _logger;
    private readonly StripeClient _stripeClient;

    public StripeEventProcessingGrain(
        ILogger<StripeEventProcessingGrain> logger,
        IOptionsMonitor<StripeOptions> stripeOptions)
    {
        _logger = logger;
        _stripeClient = new StripeClient(stripeOptions.CurrentValue.SecretKey);
    }

    [ReadOnly]
    public async Task<string> ParseEventAndGetUserIdAsync([Immutable] string json)
    {
        var stripeEvent = EventUtility.ParseEvent(json);
        _logger.LogInformation("[StripeEventProcessingGrain][ParseEventAndGetUserIdAsync] Type: {0}", stripeEvent.Type);

        if (stripeEvent.Type == "checkout.session.completed")
        {
            var session = stripeEvent.Data.Object as global::Stripe.Checkout.Session;
            if (TryGetUserIdFromMetadata(session.Metadata, out var userId))
            {
                _logger.LogDebug("[StripeEventProcessingGrain][ParseEventAndGetUserIdAsync] Type={0}, UserId={1}",
                    stripeEvent.Type, userId);
                return userId;
            }

            _logger.LogWarning("[StripeEventProcessingGrain][ParseEventAndGetUserIdAsync] Type={0}, not found userid",
                stripeEvent.Type);
        }
        else if (stripeEvent.Type is "invoice.paid" or "invoice.payment_failed")
        {
            var invoice = stripeEvent.Data.Object as global::Stripe.Invoice;
            if (TryGetUserIdFromMetadata(invoice?.Parent?.SubscriptionDetails?.Metadata, out var userId))
            {
                _logger.LogDebug("[StripeEventProcessingGrain][ParseEventAndGetUserIdAsync] Type={0}, UserId={1}",
                    stripeEvent.Type, userId);
                return userId;
            }

            _logger.LogWarning("[StripeEventProcessingGrain][ParseEventAndGetUserIdAsync] Type={0}, not found userid",
                stripeEvent.Type);
        }
        else if (stripeEvent.Type is "customer.subscription.deleted" or "customer.subscription.updated")
        {
            var subscription = stripeEvent.Data.Object as global::Stripe.Subscription;
            if (TryGetUserIdFromMetadata(subscription.Metadata, out var userId))
            {
                return userId;
            }
        }
        else if (stripeEvent.Type == "charge.refunded")
        {
            var charge = stripeEvent.Data.Object as Stripe.Charge;
            var paymentIntentService = new PaymentIntentService(_stripeClient);
            var paymentIntent = paymentIntentService.Get(charge.PaymentIntentId);
            if (TryGetUserIdFromMetadata(paymentIntent.Metadata, out var userId))
            {
                return userId;
            }
        }

        return string.Empty;
    }

    private bool TryGetUserIdFromMetadata(IDictionary<string, string> metadata, out string userId)
    {
        userId = null;
        if (metadata != null && metadata.TryGetValue("internal_user_id", out var id) && !string.IsNullOrEmpty(id))
        {
            userId = id;
            return true;
        }

        return false;
    }
}