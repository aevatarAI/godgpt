using Aevatar;
using Aevatar.Application.Grains;
using Aevatar.Application.Grains.Common.Service;
using GodGPT.Webhook.Http;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace GodGPT.GooglePay.Tests;

[DependsOn(
    typeof(AevatarOrleansTestBaseModule),
    typeof(GodGPTGAgentModule)
)]
public class GooglePayTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // Configure GooglePayOptions for testing
        services.Configure<GooglePayOptions>(options =>
        {
            options.PackageName = "com.godgpt.app.test";
            options.ServiceAccountEmail = "test-service@godgpt-test.iam.gserviceaccount.com";
            options.ServiceAccountKeyPath = "/test/path/to/service-account-key.json";
            options.WebhookEndpoint = "/api/webhooks/godgpt-googleplay-payment";
            options.ApplicationName = "GodGPT-Test";
            options.TimeoutSeconds = 30;
            options.EnableSandboxTesting = true;
            options.PubSubTopicName = "projects/godgpt-test/topics/play-billing-test";
            options.WebMerchantId = "test-merchant-id";
            options.WebGatewayMerchantId = "test-gateway-merchant-id";
            options.Products = new List<GooglePayProduct>
            {
                new GooglePayProduct
                {
                    PlanType = 1,
                    ProductId = "premium_monthly_test",
                    SubscriptionId = "premium_monthly_test",
                    Amount = 9.99m,
                    Currency = "USD",
                    IsUltimate = false,
                    BasePlanId = "monthly-autorenewing",
                    OfferId = ""
                }
            };
        });

        // Register HttpClient for testing
        services.AddHttpClient();
        
        // Register GooglePayService with mock implementation for testing
        services.AddTransient<IGooglePayService, MockGooglePayService>();
        
        // Register webhook handler for testing
        services.AddTransient<GooglePayWebhookHandler>();
        
        // Register other services needed for testing
        services.AddLogging();
    }
}