using Aevatar.Application.Grains.PaymentAnalytics;
using Aevatar.Application.Grains.PaymentAnalytics.Dtos;
using Aevatar;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GodGPT.PaymentAnalytics.Tests;

/// <summary>
/// Base class for payment analytics integration tests
/// </summary>
public abstract class PaymentAnalyticsTestBase : AevatarOrleansTestBase<PaymentAnalyticsTestModule>
{
    protected readonly ILogger<PaymentAnalyticsTestBase> Logger;
    protected readonly GoogleAnalyticsOptions TestOptions;

    protected PaymentAnalyticsTestBase()
    {
        Logger = GetRequiredService<ILogger<PaymentAnalyticsTestBase>>();
        TestOptions = GetTestAnalyticsOptions();
    }

    /// <summary>
    /// Get test analytics options
    /// </summary>
    protected GoogleAnalyticsOptions GetTestAnalyticsOptions()
    {
        return new GoogleAnalyticsOptions
        {
            EnableAnalytics = true,
            MeasurementId = "G-TEST123456789",
            ApiSecret = "test-api-secret",
            TimeoutSeconds = 5
        };
    }

    /// <summary>
    /// Get PaymentAnalyticsGrain for testing
    /// </summary>
    protected async Task<IPaymentAnalyticsGrain> GetPaymentAnalyticsGrainAsync()
    {
        return await Task.FromResult(Cluster.GrainFactory.GetGrain<IPaymentAnalyticsGrain>("test-grain"));
    }

    /// <summary>
    /// Wait for grain to be ready
    /// </summary>
    protected async Task WaitForGrainAsync(IPaymentAnalyticsGrain grain)
    {
        // Make a simple call to ensure grain is activated
        await grain.ReportPaymentSuccessAsync();
        await Task.Delay(100); // Small delay to ensure activation is complete
    }
}
