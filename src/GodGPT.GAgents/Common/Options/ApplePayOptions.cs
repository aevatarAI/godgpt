namespace Aevatar.Application.Grains.Common.Options;

[GenerateSerializer]
public class ApplePayOptions
{
    [Id(0)] public string Environment { get; set; } = "Production"; //Production / Sandbox
    [Id(1)] public string IssuerId { get; set; }
    [Id(2)] public string BundleId { get; set; }
    [Id(3)] public string KeyId { get; set; }
    [Id(4)] public string PrivateKey { get; set; }
    [Id(5)] public string SharedSecret { get; set; }
    [Id(6)] public string NotificationToken { get; set; }
    [Id(7)] public List<AppleProduct> Products { get; set; } = new List<AppleProduct>();
    
}

[GenerateSerializer]
public class AppleProduct
{
    [Id(0)] public int PlanType { get; set; }
    [Id(1)] public bool IsUltimate { get; set; }
    [Id(2)] public string ProductId { get; set; }
    [Id(3)] public string Name { get; set; }
    [Id(4)] public string Description { get; set; }
    [Id(5)] public decimal Amount { get; set; }
    [Id(6)] public string Currency { get; set; } = "USD";
} 