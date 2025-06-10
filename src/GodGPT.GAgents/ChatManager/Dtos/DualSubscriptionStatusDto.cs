using Orleans;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.ChatManager.UserQuota;

namespace Aevatar.Application.Grains.ChatManager.Dtos;

/// <summary>
/// Internal DTO for tracking dual subscription status in unified interface design
/// </summary>
[GenerateSerializer]
public class DualSubscriptionStatusDto
{
    /// <summary>
    /// Ultimate subscription information
    /// </summary>
    [Id(0)] public SubscriptionInfoDto UltimateSubscription { get; set; } = new();
    
    /// <summary>
    /// Standard subscription information (using legacy field for backward compatibility)
    /// </summary>
    [Id(1)] public SubscriptionInfoDto StandardSubscription { get; set; } = new();
    
    /// <summary>
    /// Whether Ultimate subscription is currently active
    /// </summary>
    [Id(2)] public bool UltimateActive { get; set; }
    
    /// <summary>
    /// Whether Standard subscription is currently active
    /// </summary>
    [Id(3)] public bool StandardActive { get; set; }
} 