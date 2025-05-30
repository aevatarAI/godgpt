using Aevatar.Application.Grains.Common.Options;
using System.Collections.Generic;

namespace Aevatar.Application.Grains.ChatManager.UserBilling.Payment;

/// <summary>
/// Service for managing Stripe environment context
/// </summary>
public interface IStripeEnvironmentService
{
    /// <summary>
    /// Gets the current active environment
    /// </summary>
    StripeEnvironment CurrentEnvironment { get; }
    
    /// <summary>
    /// Switches the current active environment
    /// </summary>
    /// <param name="environment">The environment to switch to</param>
    void SwitchEnvironment(StripeEnvironment environment);
    
    /// <summary>
    /// Gets the environment from metadata
    /// </summary>
    /// <param name="metadata">The metadata dictionary</param>
    /// <returns>The detected environment or default if not found</returns>
    StripeEnvironment GetEnvironmentFromMetadata(IDictionary<string, string> metadata);
    
    /// <summary>
    /// Gets the secret key for the specified environment
    /// </summary>
    /// <param name="environment">The target environment</param>
    /// <returns>The environment-specific secret key</returns>
    string GetSecretKeyForEnvironment(StripeEnvironment environment);
    
    /// <summary>
    /// Gets the webhook secret for the specified environment
    /// </summary>
    /// <param name="environment">The target environment</param>
    /// <returns>The environment-specific webhook secret</returns>
    string GetWebhookSecretForEnvironment(StripeEnvironment environment);
} 