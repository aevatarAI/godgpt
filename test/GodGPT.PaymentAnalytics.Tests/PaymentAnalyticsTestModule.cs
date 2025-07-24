using Aevatar;
using Aevatar.Application.Grains;
using Aevatar.Application.Grains.PaymentAnalytics;
using Aevatar.Application.Grains.PaymentAnalytics.Dtos;
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

        // Configure GoogleAnalyticsOptions for testing
        services.Configure<GoogleAnalyticsOptions>(options =>
        {
            options.EnableAnalytics = true;
            options.MeasurementId = "G-TEST123456789";
            options.ApiSecret = "test-api-secret";
            options.TimeoutSeconds = 5;
        });

        // Register HttpClient for testing
        services.AddHttpClient<PaymentAnalyticsGrain>();
    }
} 