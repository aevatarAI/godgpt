using Aevatar.Application.Grains.PaymentAnalytics;
using Aevatar;
using Microsoft.Extensions.Logging;

namespace GodGPT.PaymentAnalytics.Tests;

/// <summary>
/// Base class for PaymentAnalyticsGrain tests
/// </summary>
public class PaymentAnalyticsTestBase : AevatarOrleansTestBase<PaymentAnalyticsTestModule>
{
    protected ILogger Logger => GetService<ILogger<PaymentAnalyticsTestBase>>();

    /// <summary>
    /// Get PaymentAnalyticsGrain instance for testing
    /// </summary>
    /// <param name="grainKey">Optional grain key, uses default if not provided</param>
    /// <returns>PaymentAnalyticsGrain instance</returns>
    protected async Task<IPaymentAnalyticsGrain> GetPaymentAnalyticsGrainAsync(string? grainKey = null)
    {
        var key = grainKey ?? "test-payment-analytics";
        var grain = Cluster.GrainFactory.GetGrain<IPaymentAnalyticsGrain>(key);
        
        // Ensure grain is activated
        await Task.Delay(10); // Small delay to ensure activation
        
        return grain;
    }
}
