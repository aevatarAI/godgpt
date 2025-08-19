using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;
using System.Net.Http;
using Aevatar;
using Aevatar.Application.Grains;

namespace GodGPT.RevenueCatVerification.Tests;

[DependsOn(
    typeof(AevatarOrleansTestBaseModule),
    typeof(GodGPTGAgentModule)
)]
public class RevenueCatVerificationTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // Configure GooglePayOptions for testing (only non-sensitive defaults)
        services.Configure<GooglePayOptions>(options =>
        {
            // Only set defaults that are safe to override
            // RevenueCatApiKey should be read from configuration files
            options.PackageName = "com.aevatar.godgpt.test";
            options.Products = new List<GooglePayProduct>
            {
                new GooglePayProduct
                {
                    ProductId = "premium_weekly_test1",
                    PlanType = 1,
                    Amount = 48.0m,
                    Currency = "HKD",
                    IsSubscription = true,
                    IsUltimate = false
                }
            };
        });

        // Configure HttpClientFactory for testing
        services.AddHttpClient();
        
        // Mock services can be added here as needed for specific tests
        // For example, mock external API responses
    }
}
