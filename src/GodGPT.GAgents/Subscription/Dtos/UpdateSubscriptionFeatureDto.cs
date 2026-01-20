using System.ComponentModel.DataAnnotations;
using Aevatar.Application.Grains.Subscription.Enums;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.Dtos;

/// <summary>
/// DTO for updating a subscription feature.
/// </summary>
[GenerateSerializer]
public class UpdateSubscriptionFeatureDto
{
    [Id(0)]
    [MaxLength(256)]
    public string? NameKey { get; set; }
    
    [Id(1)]
    [MaxLength(256)]
    public string? DescriptionKey { get; set; }
    
    [Id(2)]
    public SubscriptionFeatureType? Type { get; set; }
    
    [Id(3)]
    public int? DisplayOrder { get; set; }
    
    /// <summary>
    /// Usage context for this feature (Comparison or ProductDisplay).
    /// </summary>
    [Id(4)]
    public SubscriptionFeatureUsage? Usage { get; set; }
}
