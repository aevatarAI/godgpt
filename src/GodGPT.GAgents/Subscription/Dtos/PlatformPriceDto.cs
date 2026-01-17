using Orleans;

namespace Aevatar.Application.Grains.Subscription.Dtos;

/// <summary>
/// Platform price DTO.
/// </summary>
[GenerateSerializer]
public class PlatformPriceDto
{
    [Id(0)]
    public decimal Price { get; set; }
    
    /// <summary>
    /// ISO 4217 currency code.
    /// </summary>
    [Id(1)]
    public string Currency { get; set; } = "USD";
    
    /// <summary>
    /// Platform-specific price identifier (e.g., Stripe price_xxx).
    /// </summary>
    [Id(2)]
    public string PlatformPriceId { get; set; } = string.Empty;
    
    [Id(3)]
    public DateTime LastSyncedAt { get; set; }
}
