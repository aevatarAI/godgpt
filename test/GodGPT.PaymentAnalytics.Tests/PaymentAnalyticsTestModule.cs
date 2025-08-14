using Aevatar;
using Aevatar.Application.Grains;
using Aevatar.Application.Grains.PaymentAnalytics;
using Aevatar.Application.Grains.PaymentAnalytics.Dtos;
using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace GodGPT.PaymentAnalytics.Tests;

[DependsOn(
    typeof(AevatarOrleansTestBaseModule),
    typeof(GodGPTGAgentModule)
)]
public class PaymentAnalyticsTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var configuration = context.Services.GetConfiguration();

        // Configure GoogleAnalyticsOptions for testing
        services.Configure<GoogleAnalyticsOptions>(options =>
        {
            options.EnableAnalytics = true;
            options.MeasurementId = "G-TEST123456789";
            options.ApiSecret = "test-api-secret";
            options.ApiEndpoint = "https://www.google-analytics.com/mp/collect";
            options.TimeoutSeconds = 5;
        });

        // Configure LLMRegionOptions from configuration
        Configure<LLMRegionOptions>(configuration.GetSection("LLMRegion"));

        // Register HttpClient for testing
        services.AddHttpClient<PaymentAnalyticsGrain>();
    }
} 