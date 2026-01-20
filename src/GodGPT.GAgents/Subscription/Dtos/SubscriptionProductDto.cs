using System;
using System.Collections.Generic;
using Aevatar.Application.Grains.Common.Constants;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.Dtos;

/// <summary>
/// User-facing subscription product DTO with localized content.
/// </summary>
[GenerateSerializer]
public class SubscriptionProductDto
{
    [Id(0)]
    public Guid Id { get; set; }
    
    /// <summary>
    /// Raw key.
    /// </summary>
    [Id(1)]
    public string NameKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Localized product name.
    /// </summary>
    [Id(2)]
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Label key.
    /// </summary>
    [Id(3)]
    public string? LabelKey { get; set; }
    
    /// <summary>
    /// Localized label (e.g., "Most Popular").
    /// </summary>
    [Id(4)]
    public string? Label { get; set; }
    
    [Id(5)]
    public PlanType PlanType { get; set; }
    
    /// <summary>
    /// Localized product description.
    /// </summary>
    [Id(6)]
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Localized highlight/tagline.
    /// </summary>
    [Id(7)]
    public string? Highlight { get; set; }
    
    [Id(8)]
    public bool IsUltimate { get; set; }
    
    [Id(9)]
    public List<SubscriptionFeatureDto> Features { get; set; } = new();
    
    [Id(10)]
    public PaymentPlatform Platform { get; set; }
    
    /// <summary>
    /// Price information (only included for Web platform).
    /// iOS/Android clients should use native SDKs to fetch prices.
    /// </summary>
    [Id(11)]
    public PlatformPriceDto? Price { get; set; }
    
    /// <summary>
    /// Display order for page presentation (lower values appear first).
    /// </summary>
    [Id(12)]
    public int DisplayOrder { get; set; }
}
