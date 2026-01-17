using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Application.Grains.Common.Constants;

namespace Aevatar.Application.Grains.Subscription.Providers;

/// <summary>
/// Strategy interface for fetching prices from payment platforms.
/// Each payment platform (Stripe, Apple, Google) implements this interface.
/// </summary>
public interface IPlatformPriceProvider
{
    /// <summary>
    /// The payment platform this provider handles.
    /// </summary>
    PaymentPlatform Platform { get; }
    
    /// <summary>
    /// Get all active prices for a product from the platform.
    /// </summary>
    /// <param name="platformProductId">Platform-specific product ID</param>
    /// <returns>List of prices for the product</returns>
    Task<List<PlatformPriceInfo>> GetPricesAsync(string platformProductId);
    
    /// <summary>
    /// Get a specific price by its platform ID.
    /// </summary>
    /// <param name="platformPriceId">Platform-specific price ID</param>
    /// <returns>Price info or null if not found</returns>
    Task<PlatformPriceInfo?> GetPriceByIdAsync(string platformPriceId);

    /// <summary>
    /// Get all prices from the platform.
    /// </summary>
    /// <returns>List of prices</returns>
    Task<List<PlatformPriceInfo>> GetAllPricesAsync();
}
