using System;
using System.Collections.Generic;
using Aevatar.Application.Grains.Common.Constants;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.Models;

/// <summary>
/// Subscription product model embedded in GAgent state.
/// </summary>
[GenerateSerializer]
public class SubscriptionProduct
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public string NameKey { get; set; } = string.Empty;
    [Id(2)] public Guid? LabelId { get; set; }
    [Id(3)] public PlanType PlanType { get; set; }
    [Id(4)] public string DescriptionKey { get; set; } = string.Empty;
    [Id(5)] public string? HighlightKey { get; set; }
    [Id(6)] public bool IsUltimate { get; set; }
    [Id(7)] public List<Guid> FeatureIds { get; set; } = new();
    [Id(8)] public string PlatformProductId { get; set; } = string.Empty;
    [Id(9)] public PaymentPlatform Platform { get; set; }
    [Id(10)] public bool? IsListed { get; set; }
    [Id(11)] public DateTime CreatedAt { get; set; }
    [Id(12)] public DateTime? UpdatedAt { get; set; }
    
    /// <summary>
    /// Display order for page presentation (lower values appear first).
    /// </summary>
    [Id(13)] public int DisplayOrder { get; set; }
}
