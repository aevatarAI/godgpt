using System;
using Aevatar.Application.Grains.Subscription.Enums;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.Models;

/// <summary>
/// Subscription feature model embedded in GAgent state.
/// </summary>
[GenerateSerializer]
public class SubscriptionFeature
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public string NameKey { get; set; } = string.Empty;
    [Id(2)] public string? DescriptionKey { get; set; }
    [Id(3)] public SubscriptionFeatureType Type { get; set; }
    [Id(4)] public int DisplayOrder { get; set; }
    [Id(5)] public DateTime CreatedAt { get; set; }
    [Id(6)] public DateTime? UpdatedAt { get; set; }
    
    /// <summary>
    /// Usage context for this feature (Comparison or ProductDisplay).
    /// </summary>
    [Id(7)] public SubscriptionFeatureUsage Usage { get; set; } = SubscriptionFeatureUsage.Comparison;
}
