using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Aevatar.Application.Grains.Common.Constants;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.Dtos;

/// <summary>
/// DTO for updating a subscription product.
/// </summary>
[GenerateSerializer]
public class UpdateProductDto
{
    [Id(0)]
    [MaxLength(256)]
    public string? NameKey { get; set; }
    
    [Id(1)]
    public Guid? LabelId { get; set; }
    
    [Id(2)]
    public PlanType? PlanType { get; set; }
    
    [Id(3)]
    [MaxLength(256)]
    public string? DescriptionKey { get; set; }
    
    [Id(4)]
    [MaxLength(256)]
    public string? HighlightKey { get; set; }
    
    [Id(5)]
    public bool? IsUltimate { get; set; }
    
    [Id(6)]
    public List<Guid>? FeatureIds { get; set; }
    
    /// <summary>
    /// Display order for page presentation (lower values appear first).
    /// </summary>
    [Id(7)]
    public int? DisplayOrder { get; set; }
}
