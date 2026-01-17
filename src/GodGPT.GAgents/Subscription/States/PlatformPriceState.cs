using System;
using System.Collections.Generic;
using Aevatar.Application.Grains.Subscription.Models;
using Aevatar.Core.Abstractions;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.States;

/// <summary>
/// State for PlatformPriceGAgent, manages platform prices for products.
/// </summary>
[GenerateSerializer]
public class PlatformPriceState : StateBase
{
    /// <summary>
    /// Prices grouped by ProductId, each product can have multiple platform/currency prices.
    /// </summary>
    [Id(0)] 
    public Dictionary<Guid, List<PlatformPrice>> ProductPrices { get; set; } = new();
    
    /// <summary>
    /// Platform PriceId to internal ProductId mapping for webhook lookup.
    /// </summary>
    [Id(1)] 
    public Dictionary<string, Guid> PriceIdToProductMap { get; set; } = new();
    
    /// <summary>
    /// Last platform price full sync time.
    /// </summary>
    [Id(2)] 
    public DateTime LastPlatformPriceSyncAt { get; set; }
}
