using Orleans;

namespace Aevatar.Application.Grains.Subscription.Enums;

/// <summary>
/// Types of subscription features (used for comparison display).
/// </summary>
[GenerateSerializer]
public enum SubscriptionFeatureType
{
    /// <summary>
    /// Not applicable (for ProductDisplay usage)
    /// </summary>
    None = -1,
    
    /// <summary>
    /// Core feature included in basic plans
    /// </summary>
    Core = 0,
    
    /// <summary>
    /// Advanced feature for premium plans
    /// </summary>
    Advanced = 1
}
