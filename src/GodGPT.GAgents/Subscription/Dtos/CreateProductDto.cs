using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Aevatar.Application.Grains.Common.Constants;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.Dtos;

/// <summary>
/// DTO for creating a subscription product.
/// </summary>
[GenerateSerializer]
public class CreateProductDto
{
    [Id(0)]
    [Required]
    [MaxLength(256)]
    public string NameKey { get; set; } = string.Empty;
    
    [Id(1)]
    public Guid? LabelId { get; set; }
    
    [Id(2)]
    [Required]
    public PlanType PlanType { get; set; }
    
    [Id(3)]
    [Required]
    [MaxLength(256)]
    public string DescriptionKey { get; set; } = string.Empty;
    
    [Id(4)]
    [MaxLength(256)]
    public string? HighlightKey { get; set; }
    
    [Id(5)]
    public bool IsUltimate { get; set; }
    
    [Id(6)]
    public List<Guid> FeatureIds { get; set; } = new();
    
    [Id(7)]
    [Required]
    [MaxLength(256)]
    public string PlatformProductId { get; set; } = string.Empty;
    
    [Id(8)]
    [Required]
    public PaymentPlatform Platform { get; set; }
    
    /// <summary>
    /// Display order for page presentation (lower values appear first).
    /// </summary>
    [Id(9)]
    public int DisplayOrder { get; set; }
}
