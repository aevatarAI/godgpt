using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Subscription.Dtos;
using Aevatar.Application.Grains.Subscription.Models;
using Aevatar.Application.Grains.Subscription.Providers;
using Aevatar.Application.Grains.Subscription.SEvents;
using Aevatar.Application.Grains.Subscription.States;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Orleans.Providers;

namespace Aevatar.Application.Grains.Subscription;

/// <summary>
/// GAgent for managing platform prices with event sourcing (data storage layer).
/// Handles price data storage and price synchronization.
/// </summary>
[StorageProvider(ProviderName = "PubSubStore")]
[LogConsistencyProvider(ProviderName = "LogStorage")]
[GAgent(nameof(PlatformPriceGAgent))]
public class PlatformPriceGAgent : 
    GAgentBase<PlatformPriceState, PlatformPriceEventBase>,
    IPlatformPriceGAgent
{
    private readonly ILogger<PlatformPriceGAgent> _logger;
    private readonly IPlatformPriceProviderFactory _providerFactory;

    private ISubscriptionProductGAgent ProductGAgent =>
        GrainFactory.GetGrain<ISubscriptionProductGAgent>(SubscriptionGAgentKeys.ProductGAgentKey);

    public PlatformPriceGAgent(
        ILogger<PlatformPriceGAgent> logger,
        IPlatformPriceProviderFactory providerFactory)
    {
        _logger = logger;
        _providerFactory = providerFactory;
    }

    #region Platform Price Sync Operations

    public async Task SyncAllPricesAsync()
    {
        _logger.LogInformation("Starting full platform price sync");
        
        try
        {
            // Get all listed products
            var allProducts = await ProductGAgent.GetAllProductsAsync();
            
            if (!allProducts.Any())
            {
                _logger.LogWarning("No products found for sync");
                await MarkSyncCompleted();
                return;
            }
            
            _logger.LogInformation("Found {Count} products to sync", allProducts.Count);

            var syncedCount = 0;
            var platforms = allProducts.Select(p => p.Platform).Distinct().ToList();
            
            foreach (var platform in platforms)
            {
                if (!_providerFactory.HasProvider(platform)) continue;
                
                var products = allProducts.Where(p => p.Platform == platform).ToList();
                var allPlatformPrices = await _providerFactory.GetProvider(platform).GetAllPricesAsync();
                
                foreach (var product in products)
                {
                    try
                    {
                        var platformPrices = allPlatformPrices
                            .Where(p => p.PlatformProductId == product.PlatformProductId)
                            .ToList();
                        
                        syncedCount += await SyncProductPricesFromPlatformAsync(
                            product.Id, platform, platformPrices);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Error syncing prices for product {ProductId} ({Platform})",
                            product.Id, platform);
                    }
                }
            }

            await MarkSyncCompleted();

            _logger.LogInformation(
                "Platform price sync completed. Synced {Synced}/{Total} products",
                syncedCount, allProducts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during full platform price sync");
            throw;
        }
    }

    public async Task SyncProductPricesAsync(Guid productId)
    {
        _logger.LogInformation("Syncing prices for product: {ProductId}", productId.ToString());

        try
        {
            // Find our internal product by product ID
            var product = await ProductGAgent.GetProductAsync(productId);
            
            if (product == null)
            {
                _logger.LogWarning(
                    "No internal product found for product: {ProductId}", 
                    productId);
                return;
            }
            var provider = _providerFactory.HasProvider(product.Platform);
            if (!provider)
            {
                _logger.LogWarning(
                    "Platform {Platform} not supported for syncing prices", 
                    productId);
                return;
            }

            // Fetch prices via provider
            var prices = await _providerFactory.GetProvider(product.Platform).GetPricesAsync(product.PlatformProductId);
            
            _logger.LogDebug(
                "Found {Count} active prices for product {ProductId}", 
                prices.Count, productId);
            
            await SyncProductPricesFromPlatformAsync(productId, product.Platform, prices);

            _logger.LogInformation(
                "Platform product price sync completed: {ProductId}", productId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Error syncing platform product prices: {ProductId}", productId);
            throw;
        }
    }

    #endregion

    #region General Price Management

    public async Task<PlatformPriceDto> SetPriceAsync(Guid productId, SetPriceDto dto)
    {
        _logger.LogInformation(
            "Setting price for product {ProductId}: {Price} {Currency} ({Platform})", 
            productId, dto.Price, dto.Currency, dto.Platform);

        RaiseEvent(new PriceSetEvent
        {
            ProductId = productId,
            Platform = dto.Platform,
            PlatformPriceId = dto.PlatformPriceId ?? string.Empty,
            Price = dto.Price,
            Currency = dto.Currency
        });
        
        await ConfirmEvents();
        
        // Find the price we just set
        var prices = State.ProductPrices.GetValueOrDefault(productId) ?? new List<PlatformPrice>();
        var price = prices.FirstOrDefault(p => 
            p.Platform == dto.Platform && p.Currency == dto.Currency);
        
        return MapToDto(price!);
    }

    public async Task DeletePriceAsync(Guid productId, PaymentPlatform platform, string currency)
    {
        _logger.LogInformation(
            "Deleting price for product {ProductId}: {Currency} ({Platform})", 
            productId, currency, platform);

        // Find the platform price ID if exists
        var prices = State.ProductPrices.GetValueOrDefault(productId) ?? new List<PlatformPrice>();
        var price = prices.FirstOrDefault(p => p.Platform == platform && p.Currency == currency);
        
        RaiseEvent(new PriceDeletedEvent
        {
            ProductId = productId,
            Platform = platform,
            Currency = currency,
            PlatformPriceId = price?.PlatformPriceId ?? string.Empty
        });
        
        await ConfirmEvents();
    }

    public async Task DeletePriceByPlatformIdAsync(string platformPriceId)
    {
        if (!State.PriceIdToProductMap.TryGetValue(platformPriceId, out _))
        {
            _logger.LogDebug("[PlatformPriceGAgent] Price not found in state, skipping delete: {PriceId}", platformPriceId);
            return;
        }

        _logger.LogInformation(
            "[PlatformPriceGAgent] Deleting price by platform ID: {PriceId}", platformPriceId);

        RaiseEvent(new PriceDeletedEvent
        {
            PlatformPriceId = platformPriceId,
        });

        await ConfirmEvents();
    }

    #endregion

    #region Query Operations (AlwaysInterleave for high concurrency)

    public Task<List<PlatformPrice>> GetPricesByProductIdAsync(Guid productId)
    {
        var prices = State.ProductPrices.GetValueOrDefault(productId) ?? new List<PlatformPrice>();
        return Task.FromResult(prices.ToList());
    }

    public Task<PlatformPrice?> GetPriceByPlatformPriceIdAsync(string platformPriceId)
    {
        if (!State.PriceIdToProductMap.TryGetValue(platformPriceId, out var productId))
            return Task.FromResult<PlatformPrice?>(null);
        
        var prices = State.ProductPrices.GetValueOrDefault(productId) ?? new List<PlatformPrice>();
        var price = prices.FirstOrDefault(p => p.PlatformPriceId == platformPriceId);
        return Task.FromResult(price);
    }

    public Task<DateTime> GetLastPlatformPriceSyncTimeAsync()
    {
        return Task.FromResult(State.LastPlatformPriceSyncAt);
    }

    #endregion

    #region Abstract Implementation

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Manages platform prices and synchronization.");
    }

    protected sealed override void GAgentTransitionState(
        PlatformPriceState state,
        StateLogEventBase<PlatformPriceEventBase> @event)
    {
        switch (@event)
        {
            case PriceSetEvent priceSet:
                if (!state.ProductPrices.ContainsKey(priceSet.ProductId))
                    state.ProductPrices[priceSet.ProductId] = new List<PlatformPrice>();
                
                var prices = state.ProductPrices[priceSet.ProductId];
                var existing = string.IsNullOrWhiteSpace(priceSet.PlatformPriceId) ? prices.FirstOrDefault(p => 
                    p.Platform == priceSet.Platform && p.Currency == priceSet.Currency) : 
                    prices.FirstOrDefault(p => p.PlatformPriceId == priceSet.PlatformPriceId);
                
                if (existing != null)
                {
                    existing.Price = priceSet.Price;
                    existing.PlatformPriceId = priceSet.PlatformPriceId;
                    existing.LastSyncedAt = DateTime.UtcNow;
                }
                else
                {
                    var newPrice = new PlatformPrice
                    {
                        Id = Guid.NewGuid(),
                        ProductId = priceSet.ProductId,
                        Price = priceSet.Price,
                        Currency = priceSet.Currency,
                        PlatformPriceId = priceSet.PlatformPriceId,
                        Platform = priceSet.Platform,
                        LastSyncedAt = DateTime.UtcNow
                    };
                    prices.Add(newPrice);
                    
                    if (!string.IsNullOrEmpty(priceSet.PlatformPriceId))
                        state.PriceIdToProductMap[priceSet.PlatformPriceId] = priceSet.ProductId;
                }
                break;
                
            case PriceDeletedEvent priceDeleted:
                if (!string.IsNullOrEmpty(priceDeleted.PlatformPriceId) && 
                    state.PriceIdToProductMap.TryGetValue(priceDeleted.PlatformPriceId, out var productId))
                {
                    if (state.ProductPrices.TryGetValue(productId, out var priceList))
                        priceList.RemoveAll(p => p.PlatformPriceId == priceDeleted.PlatformPriceId);
                    state.PriceIdToProductMap.Remove(priceDeleted.PlatformPriceId);
                }
                else if (priceDeleted.ProductId != Guid.Empty && 
                         state.ProductPrices.TryGetValue(priceDeleted.ProductId, out var list))
                {
                    list.RemoveAll(p => p.Platform == priceDeleted.Platform && 
                                       p.Currency == priceDeleted.Currency);
                }
                break;
                
            case PlatformPriceSyncCompletedEvent syncCompleted:
                state.LastPlatformPriceSyncAt = syncCompleted.SyncedAt;
                break;
        }
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Syncs prices for a single product: updates/adds from platform, deletes those not on platform.
    /// </summary>
    /// <returns>Number of price operations performed.</returns>
    private async Task<int> SyncProductPricesFromPlatformAsync(
        Guid productId,
        PaymentPlatform platform,
        List<PlatformPriceInfo> platformPrices)
    {
        var operationCount = 0;

        State.ProductPrices.TryGetValue(productId, out var localPrices);
        var localPlatformPrices = (localPrices ?? new List<PlatformPrice>())
            .Where(p => p.Platform == platform)
            .ToList();
        
        var platformPriceIds = platformPrices.Select(p => p.PriceId).ToHashSet();

        foreach (var localPrice in localPlatformPrices)
        {
            if (!string.IsNullOrEmpty(localPrice.PlatformPriceId) &&
                !platformPriceIds.Contains(localPrice.PlatformPriceId))
            {
                _logger.LogDebug("Deleting price not found on platform: {PriceId}", 
                    localPrice.PlatformPriceId);
                await DeletePriceByPlatformIdAsync(localPrice.PlatformPriceId);
                operationCount++;
            }
        }

        foreach (var platformPrice in platformPrices)
        {
            await SetPriceAsync(productId, new SetPriceDto
            {
                Platform = platform,
                PlatformPriceId = platformPrice.PriceId,
                Price = platformPrice.Price,
                Currency = platformPrice.Currency
            });
            operationCount++;
        }

        return operationCount;
    }

    private async Task MarkSyncCompleted()
    {
        RaiseEvent(new PlatformPriceSyncCompletedEvent
        {
            SyncedAt = DateTime.UtcNow
        });
        
        await ConfirmEvents();
    }

    private static PlatformPriceDto MapToDto(PlatformPrice price)
    {
        return new PlatformPriceDto
        {
            Price = price.Price,
            Currency = price.Currency,
            PlatformPriceId = price.PlatformPriceId,
            LastSyncedAt = price.LastSyncedAt
        };
    }

    #endregion
}
