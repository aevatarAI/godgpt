using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Core.Abstractions;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Subscription.Dtos;
using Aevatar.Application.Grains.Subscription.Models;
using Orleans;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Subscription;

/// <summary>
/// GAgent interface for managing platform prices (data storage layer).
/// </summary>
public interface IPlatformPriceGAgent : IGAgent
{
    // Sync all product prices
    Task SyncAllPricesAsync();
    Task SyncProductPricesAsync(Guid productId);
    
    // General price management (admin manual configuration for Apple/Google prices)
    Task<PlatformPriceDto> SetPriceAsync(Guid productId, SetPriceDto dto);
    Task DeletePriceAsync(Guid productId, PaymentPlatform platform, string currency);
    
    /// <summary>
    /// Delete price by platform-specific price ID (e.g., Stripe price_xxx).
    /// </summary>
    Task DeletePriceByPlatformIdAsync(string platformPriceId);
    
    // Query - [AlwaysInterleave] for high concurrency reads
    [AlwaysInterleave]
    Task<List<PlatformPrice>> GetPricesByProductIdAsync(Guid productId);
    
    [AlwaysInterleave]
    Task<PlatformPrice?> GetPriceByPlatformPriceIdAsync(string platformPriceId);
    
    [AlwaysInterleave]
    Task<DateTime> GetLastPlatformPriceSyncTimeAsync();
}
