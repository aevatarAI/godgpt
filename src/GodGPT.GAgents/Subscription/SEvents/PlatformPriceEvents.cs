using System;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Core.Abstractions;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.SEvents;

/// <summary>
/// Base event class for platform price events.
/// </summary>
[GenerateSerializer]
public class PlatformPriceEventBase : StateLogEventBase<PlatformPriceEventBase>
{
    [Id(0)] public override Guid Id { get; set; } = Guid.NewGuid();
}

/// <summary>
/// Event raised when a price is set (created or updated).
/// </summary>
[GenerateSerializer]
public class PriceSetEvent : PlatformPriceEventBase
{
    [Id(1)] public Guid ProductId { get; set; }
    [Id(2)] public PaymentPlatform Platform { get; set; }
    [Id(3)] public string PlatformPriceId { get; set; } = string.Empty;
    [Id(4)] public decimal Price { get; set; }
    [Id(5)] public string Currency { get; set; } = "USD";
}

/// <summary>
/// Event raised when a price is deleted.
/// </summary>
[GenerateSerializer]
public class PriceDeletedEvent : PlatformPriceEventBase
{
    [Id(1)] public Guid ProductId { get; set; }
    [Id(2)] public PaymentPlatform Platform { get; set; }
    [Id(3)] public string Currency { get; set; } = string.Empty;
    [Id(4)] public string PlatformPriceId { get; set; } = string.Empty;
}

/// <summary>
/// Event raised when platform price sync is completed.
/// </summary>
[GenerateSerializer]
public class PlatformPriceSyncCompletedEvent : PlatformPriceEventBase
{
    [Id(1)] public DateTime SyncedAt { get; set; }
}
