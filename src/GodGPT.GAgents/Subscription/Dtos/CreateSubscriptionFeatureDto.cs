using System.ComponentModel.DataAnnotations;
using Aevatar.Application.Grains.Subscription.Enums;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.Dtos;

/// <summary>
/// DTO for creating a subscription feature.
/// </summary>
[GenerateSerializer]
public class CreateSubscriptionFeatureDto
{
    /// <summary>
    /// Localization resource key for feature name.
    /// Admin must ensure corresponding localization entries exist.
    /// </summary>
    [Id(0)]
    [Required]
    [MaxLength(256)]
    public string NameKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Localization resource key for feature description.
    /// </summary>
    [Id(1)]
    [MaxLength(256)]
    public string? DescriptionKey { get; set; }
    
    [Id(2)]
    [Required]
    public SubscriptionFeatureType Type { get; set; }
    
    /// <summary>
    /// Display order (lower = higher priority).
    /// </summary>
    [Id(3)]
    public int DisplayOrder { get; set; }
    
    /// <summary>
    /// Usage context for this feature (Comparison or ProductDisplay).
    /// </summary>
    [Id(4)]
    public SubscriptionFeatureUsage Usage { get; set; } = SubscriptionFeatureUsage.Comparison;
}
