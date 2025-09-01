using Orleans;

namespace Aevatar.Application.Grains.Common.Dtos;

/// <summary>
/// Google Play subscription details DTO
/// </summary>
[GenerateSerializer]
public class GooglePlaySubscriptionDto
{
    [Id(0)] public string SubscriptionId { get; set; } = string.Empty;
    [Id(1)] public long StartTimeMillis { get; set; }
    [Id(2)] public long ExpiryTimeMillis { get; set; }
    [Id(3)] public bool AutoRenewing { get; set; }
    [Id(4)] public int PaymentState { get; set; }
    [Id(5)] public string OrderId { get; set; } = string.Empty;
    [Id(6)] public string PriceAmountMicros { get; set; } = string.Empty;
    [Id(7)] public string PriceCurrencyCode { get; set; } = "USD";
    [Id(8)] public string PurchaseToken { get; set; } = string.Empty;
}

/// <summary>
/// Google Play product purchase details DTO
/// </summary>
[GenerateSerializer]
public class GooglePlayProductDto
{
    [Id(0)] public string ProductId { get; set; } = string.Empty;
    [Id(1)] public long PurchaseTimeMillis { get; set; }
    [Id(2)] public int PurchaseState { get; set; }
    [Id(3)] public int ConsumptionState { get; set; }
    [Id(4)] public string OrderId { get; set; } = string.Empty;
    [Id(5)] public string PurchaseToken { get; set; } = string.Empty;
    [Id(6)] public string DeveloperPayload { get; set; } = string.Empty;
}

