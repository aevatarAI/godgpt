using Aevatar.Application.Grains.PaymentAnalytics;
using Aevatar.Application.Grains.PaymentAnalytics.Dtos;
using Aevatar.Application.Grains.Common.Constants;
using Xunit;

namespace GodGPT.PaymentAnalytics.Tests;

/// <summary>
/// Integration tests for PaymentAnalyticsGrain
/// </summary>
public class PaymentAnalyticsGrainTests : PaymentAnalyticsTestBase
{
    [Fact(Skip = "test")]
    public async Task ReportPaymentSuccessAsync_WithValidConfig_ShouldSucceed()
    {
        // Arrange
        var grain = await GetPaymentAnalyticsGrainAsync();
        
        // Act - Using standard method with retry mechanism
        var result = await grain.ReportPaymentSuccessAsync(PaymentPlatform.Stripe, "test-transaction-123", "user-456");
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(204, result.StatusCode); // Google Analytics GA4 returns 204 for successful requests
    }

    [Fact(Skip = "test")]
    public async Task ReportPaymentSuccessAsync_WithIdempotentTransactionId_ShouldHandleGracefully()
    {
        // Arrange
        var grain = await GetPaymentAnalyticsGrainAsync();
        var transactionId = $"idempotent-test-{DateTime.UtcNow:yyyyMMddHHmmss}";
        
        // Act - Send the same transaction multiple times to test GA4's built-in deduplication
        var result1 = await grain.ReportPaymentSuccessAsync(PaymentPlatform.Stripe, transactionId, "user-456");
        var result2 = await grain.ReportPaymentSuccessAsync(PaymentPlatform.Stripe, transactionId, "user-456");
        var result3 = await grain.ReportPaymentSuccessAsync(PaymentPlatform.Stripe, transactionId, "user-456");
        
        // Assert - All should succeed (GA4 handles deduplication server-side)
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.True(result3.IsSuccess);
        Assert.Equal(204, result1.StatusCode);
        Assert.Equal(204, result2.StatusCode);
        Assert.Equal(204, result3.StatusCode);
    }

    [Fact(Skip = "test")]
    public async Task ReportPaymentSuccessAsync_WithInvalidTransactionId_ShouldReturnError()
    {
        // Arrange
        var grain = await GetPaymentAnalyticsGrainAsync();
        
        // Act - Using empty transaction ID
        var result = await grain.ReportPaymentSuccessAsync(PaymentPlatform.AppStore, "", "user-123");
        
        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Transaction ID is required", result.ErrorMessage);
    }

    [Fact(Skip = "test")]
    public async Task ReportPaymentSuccessAsync_ConcurrentRequests_ShouldAllSucceed()
    {
        // Arrange
        var grain = await GetPaymentAnalyticsGrainAsync();
        var tasks = new List<Task<PaymentAnalyticsResultDto>>();
        
        // Act - Send multiple concurrent requests
        for (int i = 0; i < 5; i++)
        {
            var transactionId = $"concurrent-test-{DateTime.UtcNow:yyyyMMddHHmmss}-{i}";
            tasks.Add(grain.ReportPaymentSuccessAsync(PaymentPlatform.Stripe, transactionId, $"user-{i}"));
        }
        
        var results = await Task.WhenAll(tasks);
        
        // Assert - All should succeed
        foreach (var result in results)
        {
            Assert.True(result.IsSuccess);
            Assert.Equal(204, result.StatusCode);
        }
    }

    [Fact(Skip = "test")]
    public async Task ReportPaymentSuccessAsync_WithRetryMechanism_ShouldLogRetryAttempts()
    {
        // This test verifies that retry mechanism is in place
        // Since we're hitting real GA4 API, we can't easily simulate failures
        // But we can verify that successful requests work and are logged properly
        
        // Arrange
        var grain = await GetPaymentAnalyticsGrainAsync("test-retry-grain");
        var transactionId = $"retry-test-{DateTime.UtcNow:yyyyMMddHHmmss}";
        
        // Act - Test retry mechanism (normal operation should succeed on first attempt)
        var result = await grain.ReportPaymentSuccessAsync(PaymentPlatform.Stripe, transactionId, "retry-user");
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(204, result.StatusCode);
        
        // Note: The retry mechanism will be tested in scenarios where GA4 API is temporarily unavailable
        // In normal operation, the first attempt should succeed
    }
}
