namespace Aevatar.Application.Grains.Common.Options;

[GenerateSerializer]
public class ApplePayOptions
{
    [Id(0)] public string Environment { get; set; } = "Production";
    [Id(1)] public string SharedSecret { get; set; }
    [Id(2)] public string NotificationToken { get; set; }
    [Id(3)] public string BundleId { get; set; }
    [Id(4)] public List<AppleProduct> Products { get; set; } = new List<AppleProduct>();
}

[GenerateSerializer]
public class AppleProduct
{
    [Id(0)] public int PlanType { get; set; }
    [Id(1)] public bool Ultimate { get; set; }
    [Id(2)] public string ProductId { get; set; }
    [Id(3)] public string Name { get; set; }
    [Id(4)] public string Description { get; set; }
    [Id(5)] public decimal Amount { get; set; }
    [Id(6)] public string Currency { get; set; } = "USD";
} 