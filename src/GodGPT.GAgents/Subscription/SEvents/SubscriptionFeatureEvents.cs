using System;
using System.Collections.Generic;
using Aevatar.Application.Grains.Subscription.Enums;
using Aevatar.Core.Abstractions;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.SEvents;

/// <summary>
/// Base event class for feature events.
/// </summary>
[GenerateSerializer]
public class SubscriptionFeatureEventBase : StateLogEventBase<SubscriptionFeatureEventBase>
{
    [Id(0)] public override Guid Id { get; set; } = Guid.NewGuid();
}

/// <summary>
/// Event raised when a feature is created.
/// </summary>
[GenerateSerializer]
public class SubscriptionFeatureCreatedEvent : SubscriptionFeatureEventBase
{
    [Id(1)] public Guid FeatureId { get; set; }
    [Id(2)] public string NameKey { get; set; } = string.Empty;
    [Id(3)] public string? DescriptionKey { get; set; }
    [Id(4)] public SubscriptionFeatureType Type { get; set; }
    [Id(5)] public int DisplayOrder { get; set; }
    [Id(6)] public SubscriptionFeatureUsage Usage { get; set; }
}

/// <summary>
/// Event raised when a feature is updated.
/// </summary>
[GenerateSerializer]
public class SubscriptionFeatureUpdatedEvent : SubscriptionFeatureEventBase
{
    [Id(1)] public Guid FeatureId { get; set; }
    [Id(2)] public string? NameKey { get; set; }
    [Id(3)] public string? DescriptionKey { get; set; }
    [Id(4)] public SubscriptionFeatureType? Type { get; set; }
    [Id(5)] public int? DisplayOrder { get; set; }
    [Id(6)] public SubscriptionFeatureUsage? Usage { get; set; }
}

/// <summary>
/// Event raised when a feature is deleted.
/// </summary>
[GenerateSerializer]
public class SubscriptionFeatureDeletedEvent : SubscriptionFeatureEventBase
{
    [Id(1)] public Guid FeatureId { get; set; }
}

/// <summary>
/// Event raised when features are reordered.
/// </summary>
[GenerateSerializer]
public class SubscriptionFeaturesReorderedEvent : SubscriptionFeatureEventBase
{
    [Id(1)] public List<SubscriptionFeatureOrderItem> Orders { get; set; } = new();
}

/// <summary>
/// Item representing feature ID and its display order.
/// </summary>
[GenerateSerializer]
public class SubscriptionFeatureOrderItem
{
    [Id(0)] public Guid FeatureId { get; set; }
    [Id(1)] public int DisplayOrder { get; set; }
}
