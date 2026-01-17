namespace Aevatar.Application.Grains.Subscription.Providers;

/// <summary>
/// Platform price information returned by providers.
/// Contains essential price data: PriceId, Price, Currency, and product association.
/// </summary>
public class PlatformPriceInfo
{
    /// <summary>
    /// Platform-specific price ID (e.g., Stripe price_xxx, Apple product_xxx).
    /// </summary>
    public string PriceId { get; set; } = string.Empty;
    
    /// <summary>
    /// Platform-specific product ID that this price belongs to.
    /// </summary>
    public string PlatformProductId { get; set; } = string.Empty;
    
    /// <summary>
    /// Actual price amount (already converted from platform units).
    /// For Stripe: converted from cents to dollars.
    /// </summary>
    public decimal Price { get; set; }
    
    /// <summary>
    /// ISO 4217 currency code (uppercase, e.g., "USD", "JPY").
    /// </summary>
    public string Currency { get; set; } = "USD";
}
