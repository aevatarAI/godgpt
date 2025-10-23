namespace Aevatar.Application.Grains.Common.Options;

[GenerateSerializer]
public class StripeOptions
{
    [Id(0)] public string PublishableKey { get; set; }
    [Id(1)] public string SecretKey { get; set; }
    [Id(2)] public string WebhookSecret { get; set; }
    [Id(3)] public string BasicPrice { get; set; }
    [Id(4)] public string ProPrice { get; set; }
    [Id(5)] public string SuccessUrl { get; set; }
    [Id(6)] public string CancelUrl { get; set; }
    [Id(7)] public string ReturnUrl { get; set; }
    [Id(8)] public string WebhookHostName { get; set; }
    [Id(9)] public List<StripeProduct> Products { get; set; } = new List<StripeProduct>();
}

[GenerateSerializer]
public class StripeProduct
{
    [Id(0)] public int PlanType { get; set; }
    [Id(1)] public string PriceId { get; set; }
    [Id(2)] public string Mode { get; set; }
    [Id(3)] public decimal Amount { get; set; }
    [Id(4)] public string Currency { get; set; }
    [Id(5)] public bool IsUltimate { get; set; } = false;  // Configuration-driven Ultimate detection
    [Id(6)] public int Credits { get; set; } = 0;
}