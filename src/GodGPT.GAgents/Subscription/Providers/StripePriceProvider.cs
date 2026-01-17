using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;

namespace Aevatar.Application.Grains.Subscription.Providers;

/// <summary>
/// Stripe implementation of IPlatformPriceProvider.
/// Fetches prices from Stripe API.
/// </summary>
public class StripePriceProvider : IPlatformPriceProvider
{
    private readonly StripeClient _stripeClient;
    private readonly ILogger<StripePriceProvider> _logger;

    public PaymentPlatform Platform => PaymentPlatform.Stripe;

    public StripePriceProvider(
        IOptions<StripeOptions> stripeOptions,
        ILogger<StripePriceProvider> logger)
    {
        _stripeClient = new StripeClient(stripeOptions.Value.SecretKey);
        _logger = logger;
    }

    public async Task<List<PlatformPriceInfo>> GetAllPricesAsync()
    {
        _logger.LogDebug("[StripePriceProvider] Fetching all prices");

        try
        {
            var options = new PriceListOptions
            {
                Active = true,
                Limit = 100
            };
            
            var allPrices = await FetchAllPricesWithPaginationAsync(options);
            var result = allPrices
                .Where(p => p.BillingScheme == "per_unit")
                .Select(ConvertToInfo)
                .ToList();

            _logger.LogDebug(
                "[StripePriceProvider] Found {Count} prices",
                result.Count);

            return result;
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "[StripePriceProvider] Error fetching all prices");
            throw;
        }
    }

    public async Task<List<PlatformPriceInfo>> GetPricesAsync(string platformProductId)
    {
        _logger.LogDebug("[StripePriceProvider] Fetching prices for product: {ProductId}", platformProductId);

        try
        {
            var options = new PriceListOptions
            {
                Product = platformProductId,
                Active = true,
                Limit = 100
            };

            var allPrices = await FetchAllPricesWithPaginationAsync(options);

            var result = allPrices
                .Where(p => p.BillingScheme == "per_unit")
                .Select(ConvertToInfo)
                .ToList();

            _logger.LogDebug(
                "[StripePriceProvider] Found {Count} prices for product {ProductId}",
                result.Count, platformProductId);

            return result;
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex,
                "[StripePriceProvider] Error fetching prices for product: {ProductId}",
                platformProductId);
            throw;
        }
    }

    public async Task<PlatformPriceInfo?> GetPriceByIdAsync(string platformPriceId)
    {
        _logger.LogDebug("[StripePriceProvider] Fetching price: {PriceId}", platformPriceId);

        try
        {
            var priceService = new PriceService(_stripeClient);
            var stripePrice = await priceService.GetAsync(platformPriceId);

            if (stripePrice.BillingScheme != "per_unit")
            {
                _logger.LogWarning(
                    "[StripePriceProvider] Price is tiered, not supported: {PriceId}",
                    platformPriceId);
                return null;
            }

            return ConvertToInfo(stripePrice);
        }
        catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing")
        {
            _logger.LogWarning("[StripePriceProvider] Price not found: {PriceId}", platformPriceId);
            return null;
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "[StripePriceProvider] Error fetching price: {PriceId}", platformPriceId);
            throw;
        }
    }

    /// <summary>
    /// Fetches all prices with pagination support.
    /// Handles Stripe's cursor-based pagination to retrieve all records.
    /// </summary>
    private async Task<List<Price>> FetchAllPricesWithPaginationAsync(PriceListOptions options)
    {
        var priceService = new PriceService(_stripeClient);
        var allPrices = new List<Price>();
        string? startingAfter = null;

        do
        {
            options.StartingAfter = startingAfter;
            var response = await priceService.ListAsync(options);
            allPrices.AddRange(response.Data);

            if (!response.HasMore || response.Data.Count == 0)
            {
                break;
            }

            startingAfter = response.Data.Last().Id;
        } while (true);

        _logger.LogDebug(
            "[StripePriceProvider] Fetched {TotalCount} prices in total (with pagination)",
            allPrices.Count);

        return allPrices;
    }

    private static PlatformPriceInfo ConvertToInfo(Price stripePrice)
    {
        // Convert from Stripe smallest unit (cents) to actual amount
        var amount = stripePrice.UnitAmountDecimal.HasValue
            ? stripePrice.UnitAmountDecimal.Value / 100m
            : 0m;

        return new PlatformPriceInfo
        {
            PriceId = stripePrice.Id,
            PlatformProductId = stripePrice.ProductId ?? string.Empty,
            Price = amount,
            Currency = stripePrice.Currency.ToUpperInvariant()
        };
    }
}
