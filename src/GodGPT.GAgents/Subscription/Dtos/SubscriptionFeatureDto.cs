using System;
using Aevatar.Application.Grains.Subscription.Enums;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.Dtos;

/// <summary>
/// Subscription feature DTO with localized content.
/// </summary>
[GenerateSerializer]
public class SubscriptionFeatureDto
{
    [Id(0)]
    public Guid Id { get; set; }
    
    /// <summary>
    /// Raw key
    /// </summary>
    [Id(1)]
    public string NameKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Localized feature name.
    /// </summary>
    [Id(2)]
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Localized feature description.
    /// </summary>
    [Id(3)]
    public string? Description { get; set; }
    
    [Id(4)]
    public SubscriptionFeatureType Type { get; set; }
    
    /// <summary>
    /// Localized type display name (e.g., "Core Feature").
    /// </summary>
    [Id(5)]
    public string TypeName { get; set; } = string.Empty;
    
    [Id(6)]
    public int DisplayOrder { get; set; }
    
    /// <summary>
    /// Usage context for this feature (Comparison or ProductDisplay).
    /// </summary>
    [Id(7)]
    public SubscriptionFeatureUsage Usage { get; set; }
}
