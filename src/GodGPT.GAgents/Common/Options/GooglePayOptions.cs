namespace Aevatar.Application.Grains.Common.Options;

[GenerateSerializer]
public class GooglePayOptions
{
    [Id(0)] public string PackageName { get; set; }
    [Id(1)] public string ServiceAccountEmail { get; set; }
    [Id(2)] public string ServiceAccountKeyPath { get; set; }
    [Id(3)] public string WebhookEndpoint { get; set; }
    [Id(4)] public string ApplicationName { get; set; }
    [Id(5)] public int TimeoutSeconds { get; set; } = 30;
    [Id(6)] public bool EnableSandboxTesting { get; set; }
    [Id(7)] public string PubSubTopicName { get; set; }
    [Id(8)] public string WebMerchantId { get; set; }         // For Google Pay Web
    [Id(9)] public string WebGatewayMerchantId { get; set; }  // For Google Pay Web Gateway
    [Id(10)] public List<GooglePayProduct> Products { get; set; } = new List<GooglePayProduct>();
}

[GenerateSerializer]
public class GooglePayProduct
{
    [Id(0)] public int PlanType { get; set; }
    [Id(1)] public string ProductId { get; set; }      // Google Play product ID
    [Id(2)] public string SubscriptionId { get; set; } // Google Play subscription ID
    [Id(3)] public decimal Amount { get; set; }
    [Id(4)] public string Currency { get; set; }
    [Id(5)] public bool IsUltimate { get; set; } = false;
    [Id(6)] public string BasePlanId { get; set; }     // Google Play base plan ID
    [Id(7)] public string OfferId { get; set; }        // Google Play offer ID (optional)
}