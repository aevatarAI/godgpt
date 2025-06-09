using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.ChatManager.UserQuota;

namespace Aevatar.Application.Grains.ChatManager.Dtos;

[GenerateSerializer]
public class DualSubscriptionStatusDto
{
    /// <summary>
    /// Currently active subscription (Ultimate takes priority)
    /// </summary>
    [Id(0)] public SubscriptionInfoDto ActiveSubscription { get; set; }
    
    /// <summary>
    /// Standard subscription details
    /// </summary>
    [Id(1)] public SubscriptionInfoDto StandardSubscription { get; set; }
    
    /// <summary>
    /// Ultimate subscription details
    /// </summary>
    [Id(2)] public SubscriptionInfoDto UltimateSubscription { get; set; }
    
    /// <summary>
    /// Whether standard subscription is currently frozen
    /// </summary>
    [Id(3)] public bool IsStandardFrozen { get; set; }
    
    /// <summary>
    /// When standard subscription was frozen
    /// </summary>
    [Id(4)] public DateTime? FrozenAt { get; set; }
    
    /// <summary>
    /// Total accumulated frozen time
    /// </summary>
    [Id(5)] public TimeSpan AccumulatedFrozenTime { get; set; }
    
    /// <summary>
    /// Whether user has unlimited access (Ultimate active)
    /// </summary>
    [Id(6)] public bool HasUnlimitedAccess { get; set; }
} 