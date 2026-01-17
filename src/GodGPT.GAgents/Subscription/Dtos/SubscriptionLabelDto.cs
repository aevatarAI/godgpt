using System;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.Dtos;

/// <summary>
/// Subscription label DTO with localized content.
/// </summary>
[GenerateSerializer]
public class SubscriptionLabelDto
{
    [Id(0)]
    public Guid Id { get; set; }
    
    /// <summary>
    /// Label NameKey used for localization lookup.
    /// </summary>
    [Id(1)]
    public string NameKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Localized label name.
    /// </summary>
    [Id(2)]
    public string Name { get; set; } = string.Empty;
    
    [Id(3)]
    public DateTime CreatedAt { get; set; }
}
