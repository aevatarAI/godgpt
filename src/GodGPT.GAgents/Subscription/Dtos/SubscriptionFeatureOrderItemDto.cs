using System;
using System.ComponentModel.DataAnnotations;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.Dtos;

/// <summary>
/// Item representing feature ID and its display order for reordering.
/// </summary>
[GenerateSerializer]
public class SubscriptionFeatureOrderItemDto
{
    [Id(0)]
    [Required]
    public Guid FeatureId { get; set; }
    
    [Id(1)]
    [Required]
    public int DisplayOrder { get; set; }
}
