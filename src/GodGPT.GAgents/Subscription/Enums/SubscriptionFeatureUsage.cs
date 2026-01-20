using Orleans;

namespace Aevatar.Application.Grains.Subscription.Enums;

/// <summary>
/// Usage context for subscription features.
/// </summary>
[GenerateSerializer]
public enum SubscriptionFeatureUsage
{
    /// <summary>
    /// Used for plan comparison display (Core vs Advanced features)
    /// </summary>
    Comparison = 0,
    
    /// <summary>
    /// Used for product list/card display
    /// </summary>
    ProductDisplay = 1
}
