using Orleans;

namespace Aevatar.Application.Grains.Subscription.Enums;

/// <summary>
/// Types of subscription features.
/// </summary>
[GenerateSerializer]
public enum SubscriptionFeatureType
{
    /// <summary>
    /// Core feature included in basic plans
    /// </summary>
    Core = 0,
    
    /// <summary>
    /// Advanced feature for premium plans
    /// </summary>
    Advanced = 1
}
