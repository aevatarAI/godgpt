using System;

namespace Aevatar.Application.Grains.Subscription;

/// <summary>
/// Constant keys for Subscription GAgents.
/// </summary>
public static class SubscriptionGAgentKeys
{
    /// <summary>
    /// Singleton key for SubscriptionProductGAgent.
    /// </summary>
    public static readonly Guid ProductGAgentKey = new("00000000-0000-0000-0000-000000000001");
    
    /// <summary>
    /// Singleton key for SubscriptionFeatureGAgent.
    /// </summary>
    public static readonly Guid FeatureGAgentKey = new("00000000-0000-0000-0000-000000000002");
    
    /// <summary>
    /// Singleton key for SubscriptionLabelGAgent.
    /// </summary>
    public static readonly Guid LabelGAgentKey = new("00000000-0000-0000-0000-000000000003");
    
    /// <summary>
    /// Singleton key for PlatformPriceGAgent.
    /// </summary>
    public static readonly Guid PriceGAgentKey = new("00000000-0000-0000-0000-000000000004");
    
    /// <summary>
    /// Singleton key for StripePriceGAgent.
    /// </summary>
    public static readonly Guid StripePriceGAgentKey = new("00000000-0000-0000-0000-000000000005");
}
