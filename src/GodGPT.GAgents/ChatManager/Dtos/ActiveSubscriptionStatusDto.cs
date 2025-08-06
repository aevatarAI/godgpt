using Aevatar.Application.Grains.Common.Constants;

namespace Aevatar.Application.Grains.ChatManager.Dtos;

/// <summary>
/// Active subscription status information for different payment platforms
/// </summary>
[GenerateSerializer]
public class ActiveSubscriptionStatusDto
{
    /// <summary>
    /// Whether there is an active Apple App Store subscription
    /// </summary>
    [Id(0)] public bool HasActiveAppleSubscription { get; set; } = false;
    
    /// <summary>
    /// Whether there is an active Stripe subscription
    /// </summary>
    [Id(1)] public bool HasActiveStripeSubscription { get; set; } = false;
    
    /// <summary>
    /// Whether there is any active subscription (Apple, Stripe, or Google Play)
    /// </summary>
    [Id(2)] public bool HasActiveSubscription { get; set; } = false;
    
    /// <summary>
    /// Whether there is an active Google Play subscription
    /// </summary>
    [Id(3)] public bool HasActiveGooglePlaySubscription { get; set; } = false;
} 