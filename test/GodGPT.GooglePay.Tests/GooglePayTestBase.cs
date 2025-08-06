using Aevatar.Application.Grains.UserBilling;
using Aevatar;

namespace GodGPT.GooglePay.Tests;

/// <summary>
/// Base class for Google Pay integration tests
/// </summary>
public class GooglePayTestBase : AevatarOrleansTestBase<GooglePayTestModule>
{
    protected ILogger Logger => GetService<ILogger<GooglePayTestBase>>();

    /// <summary>
    /// Get UserBillingGAgent instance for testing
    /// </summary>
    /// <param name="userId">User ID for the grain</param>
    /// <returns>UserBillingGAgent instance</returns>
    protected async Task<IUserBillingGAgent> GetUserBillingGAgentAsync(Guid? userId = null)
    {
        var id = userId ?? Guid.NewGuid();
        var grain = Cluster.GrainFactory.GetGrain<IUserBillingGAgent>(id);
        
        // Ensure grain is activated
        await Task.Delay(10); // Small delay to ensure activation
        
        return grain;
    }

    /// <summary>
    /// Get GooglePlayEventProcessingGrain instance for testing
    /// </summary>
    /// <param name="grainKey">Optional grain key, uses default if not provided</param>
    /// <returns>GooglePlayEventProcessingGrain instance</returns>
    protected async Task<IGooglePlayEventProcessingGrain> GetGooglePlayEventProcessingGrainAsync(string? grainKey = null)
    {
        var key = grainKey ?? "test-google-play-event-processing";
        var grain = Cluster.GrainFactory.GetGrain<IGooglePlayEventProcessingGrain>(key);
        
        // Ensure grain is activated
        await Task.Delay(10); // Small delay to ensure activation
        
        return grain;
    }

    /// <summary>
    /// Get UserPurchaseTokenMappingGrain instance for testing
    /// </summary>
    /// <param name="purchaseToken">Purchase token as grain key</param>
    /// <returns>UserPurchaseTokenMappingGrain instance</returns>
    protected async Task<IUserPurchaseTokenMappingGrain> GetUserPurchaseTokenMappingGrainAsync(string purchaseToken)
    {
        var grain = Cluster.GrainFactory.GetGrain<IUserPurchaseTokenMappingGrain>(purchaseToken);
        
        // Ensure grain is activated
        await Task.Delay(10); // Small delay to ensure activation
        
        return grain;
    }

    /// <summary>
    /// Create a test Google Play purchase verification request
    /// </summary>
    /// <returns>Test GooglePlayVerificationDto</returns>
    protected GooglePlayVerificationDto CreateTestGooglePlayVerificationDto()
    {
        return new GooglePlayVerificationDto
        {
            PurchaseToken = "test_purchase_token_12345",
            ProductId = "premium_monthly_test",
            PackageName = "com.godgpt.app.test",
            OrderId = "GPA.1234-5678-9012-34567",
            UserId = Guid.NewGuid().ToString()
        };
    }

    /// <summary>
    /// Create a test Google Pay web verification request
    /// </summary>
    /// <returns>Test GooglePayVerificationDto</returns>
    protected GooglePayVerificationDto CreateTestGooglePayVerificationDto()
    {
        return new GooglePayVerificationDto
        {
            PaymentToken = "test_payment_token_web_12345",
            ProductId = "premium_monthly_test",
            OrderId = "12999763169054705758.1371079406387615",
            UserId = Guid.NewGuid().ToString(),
            Environment = "TEST"
        };
    }

    /// <summary>
    /// Create a test RTDN notification JSON
    /// </summary>
    /// <param name="notificationType">Notification type</param>
    /// <param name="purchaseToken">Purchase token</param>
    /// <returns>RTDN notification JSON string</returns>
    protected string CreateTestRTDNNotification(int notificationType = 4, string? purchaseToken = null)
    {
        var token = purchaseToken ?? "test_purchase_token_12345";
        
        return $$"""
        {
          "version": "1.0",
          "packageName": "com.godgpt.app.test",
          "eventTimeMillis": {{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}},
          "subscriptionNotification": {
            "version": "1.0",
            "notificationType": {{notificationType}},
            "purchaseToken": "{{token}}",
            "subscriptionId": "premium_monthly_test"
          }
        }
        """;
    }
}