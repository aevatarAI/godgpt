using System;
using Aevatar.Application.Grains.Common.Constants;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.Models;

/// <summary>
/// Platform price model embedded in GAgent state.
/// Supports multiple currencies per product.
/// </summary>
[GenerateSerializer]
public class PlatformPrice
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public Guid ProductId { get; set; }
    [Id(2)] public decimal Price { get; set; }
    [Id(3)] public string Currency { get; set; } = "USD";
    [Id(4)] public string PlatformPriceId { get; set; } = string.Empty;
    [Id(5)] public PaymentPlatform Platform { get; set; }
    [Id(6)] public DateTime LastSyncedAt { get; set; }
}
