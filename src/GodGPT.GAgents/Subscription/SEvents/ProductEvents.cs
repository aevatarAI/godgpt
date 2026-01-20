using System;
using System.Collections.Generic;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Core.Abstractions;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.SEvents;

/// <summary>
/// Base event class for product events.
/// </summary>
[GenerateSerializer]
public class ProductEventBase : StateLogEventBase<ProductEventBase>
{
    [Id(0)] public override Guid Id { get; set; } = Guid.NewGuid();
}

/// <summary>
/// Event raised when a product is created.
/// </summary>
[GenerateSerializer]
public class ProductCreatedEvent : ProductEventBase
{
    [Id(1)] public Guid ProductId { get; set; }
    [Id(2)] public string NameKey { get; set; } = string.Empty;
    [Id(3)] public Guid? LabelId { get; set; }
    [Id(4)] public PlanType PlanType { get; set; }
    [Id(5)] public string DescriptionKey { get; set; } = string.Empty;
    [Id(6)] public string? HighlightKey { get; set; }
    [Id(7)] public bool IsUltimate { get; set; }
    [Id(8)] public List<Guid>? FeatureIds { get; set; }
    [Id(9)] public string PlatformProductId { get; set; } = string.Empty;
    [Id(10)] public PaymentPlatform Platform { get; set; }
    [Id(11)] public int DisplayOrder { get; set; }
}

/// <summary>
/// Event raised when a product is updated.
/// </summary>
[GenerateSerializer]
public class ProductUpdatedEvent : ProductEventBase
{
    [Id(1)] public Guid ProductId { get; set; }
    [Id(2)] public string? NameKey { get; set; }
    [Id(3)] public Guid? LabelId { get; set; }
    [Id(4)] public PlanType? PlanType { get; set; }
    [Id(5)] public string? DescriptionKey { get; set; }
    [Id(6)] public string? HighlightKey { get; set; }
    [Id(7)] public bool? IsUltimate { get; set; }
    [Id(8)] public List<Guid>? FeatureIds { get; set; }
    [Id(9)] public int? DisplayOrder { get; set; }
}

/// <summary>
/// Event raised when a product is deleted.
/// </summary>
[GenerateSerializer]
public class ProductDeletedEvent : ProductEventBase
{
    [Id(1)] public Guid ProductId { get; set; }
}

/// <summary>
/// Event raised when a product is listed or unlisted.
/// </summary>
[GenerateSerializer]
public class ProductListedEvent : ProductEventBase
{
    [Id(1)] public Guid ProductId { get; set; }
    [Id(2)] public bool IsListed { get; set; }
}
