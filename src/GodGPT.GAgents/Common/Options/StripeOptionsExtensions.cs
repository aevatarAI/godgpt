using System.Collections.Generic;
using System.Linq;

namespace Aevatar.Application.Grains.Common.Options;

/// <summary>
/// Extension methods for StripeOptions
/// </summary>
public static class StripeOptionsExtensions
{
    /// <summary>
    /// Gets products filtered by environment
    /// </summary>
    /// <param name="options">The stripe options</param>
    /// <param name="environment">The target environment</param>
    /// <returns>A list of products for the specified environment or with no environment specified</returns>
    public static List<StripeProduct> GetProductsByEnvironment(
        this StripeOptions options, 
        StripeEnvironment environment)
    {
        if (options?.Products == null)
        {
            return new List<StripeProduct>();
        }
        
        return options.Products
            .Where(p => p.Environment == null || p.Environment == environment)
            .ToList();
    }
    
    /// <summary>
    /// Gets a product by price ID and environment
    /// </summary>
    /// <param name="options">The stripe options</param>
    /// <param name="priceId">The price ID to find</param>
    /// <param name="environment">The target environment</param>
    /// <returns>The matching product or null if not found</returns>
    public static StripeProduct GetProductByPriceIdAndEnvironment(
        this StripeOptions options,
        string priceId,
        StripeEnvironment environment)
    {
        if (options?.Products == null || string.IsNullOrEmpty(priceId))
        {
            return null;
        }
        
        // First try to find an exact match with the environment
        var product = options.Products
            .FirstOrDefault(p => p.PriceId == priceId && p.Environment == environment);
        
        // If not found and priceId exists in environment-agnostic products, use that
        if (product == null)
        {
            product = options.Products
                .FirstOrDefault(p => p.PriceId == priceId && p.Environment == null);
        }
        
        return product;
    }
    
    /// <summary>
    /// Gets the secret key for the specified environment
    /// </summary>
    /// <param name="options">The stripe options</param>
    /// <param name="environment">The target environment</param>
    /// <returns>The environment-specific secret key or the default key if not found</returns>
    public static string GetSecretKeyForEnvironment(
        this StripeOptions options,
        StripeEnvironment environment)
    {
        if (options?.EnvironmentSecretKeys == null || 
            !options.EnvironmentSecretKeys.TryGetValue(environment.ToString(), out var key) ||
            string.IsNullOrEmpty(key))
        {
            return options?.SecretKey;
        }
        
        return key;
    }
    
    /// <summary>
    /// Gets the webhook secret for the specified environment
    /// </summary>
    /// <param name="options">The stripe options</param>
    /// <param name="environment">The target environment</param>
    /// <returns>The environment-specific webhook secret or the default secret if not found</returns>
    public static string GetWebhookSecretForEnvironment(
        this StripeOptions options,
        StripeEnvironment environment)
    {
        if (options?.EnvironmentWebhookSecrets == null || 
            !options.EnvironmentWebhookSecrets.TryGetValue(environment.ToString(), out var secret) ||
            string.IsNullOrEmpty(secret))
        {
            return options?.WebhookSecret;
        }
        
        return secret;
    }
} 