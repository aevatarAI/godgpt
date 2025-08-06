using Aevatar.Application.Grains.Common.Service;

namespace GodGPT.GooglePay.Tests;

/// <summary>
/// Mock implementation of IGooglePayService for testing
/// </summary>
public class MockGooglePayService : IGooglePayService
{
    private readonly ILogger<MockGooglePayService> _logger;

    public MockGooglePayService(ILogger<MockGooglePayService> logger)
    {
        _logger = logger;
    }

    public Task<GooglePlayPurchaseDto> VerifyPurchaseAsync(string purchaseToken, string productId, string packageName)
    {
        _logger.LogInformation("[MockGooglePayService] VerifyPurchaseAsync called with token: {Token}, productId: {ProductId}",
            purchaseToken?.Substring(0, Math.Min(10, purchaseToken.Length)) + "***", productId);

        // Return mock successful verification for test tokens
        if (purchaseToken?.StartsWith("test_") == true)
        {
            return Task.FromResult(new GooglePlayPurchaseDto
            {
                PurchaseToken = purchaseToken,
                ProductId = productId,
                PurchaseTimeMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                PurchaseState = 1, // Purchased
                OrderId = "GPA.TEST-1234-5678-9012",
                PackageName = packageName,
                AutoRenewing = true,
                DeveloperPayload = "test_payload"
            });
        }

        // Return null for invalid tokens
        return Task.FromResult<GooglePlayPurchaseDto>(null);
    }

    public Task<bool> VerifyWebPaymentAsync(string paymentToken, string orderId)
    {
        _logger.LogInformation("[MockGooglePayService] VerifyWebPaymentAsync called with orderId: {OrderId}", orderId);

        // Return true for test tokens
        return Task.FromResult(paymentToken?.StartsWith("test_") == true);
    }

    public Task<GooglePlaySubscriptionDto> GetSubscriptionAsync(string subscriptionId, string packageName)
    {
        _logger.LogInformation("[MockGooglePayService] GetSubscriptionAsync called with subscriptionId: {SubscriptionId}", subscriptionId);

        // Return mock subscription for test IDs
        if (subscriptionId?.Contains("test") == true)
        {
            return Task.FromResult(new GooglePlaySubscriptionDto
            {
                SubscriptionId = subscriptionId,
                StartTimeMillis = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeMilliseconds(),
                ExpiryTimeMillis = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeMilliseconds(),
                AutoRenewing = true,
                PaymentState = 1, // Payment received
                OrderId = "GPA.TEST-SUBSCRIPTION-1234",
                PriceAmountMicros = "9990000", // $9.99 in micros
                PriceCurrencyCode = "USD"
            });
        }

        return Task.FromResult<GooglePlaySubscriptionDto>(null);
    }

    public Task<bool> GetRefundStatusAsync(string purchaseToken, string packageName)
    {
        _logger.LogInformation("[MockGooglePayService] GetRefundStatusAsync called");

        // Return false (not refunded) for test tokens
        return Task.FromResult(purchaseToken?.Contains("refunded") == true);
    }

    public Task<bool> GetCancellationStatusAsync(string subscriptionId, string packageName)
    {
        _logger.LogInformation("[MockGooglePayService] GetCancellationStatusAsync called");

        // Return false (not canceled) for test subscriptions
        return Task.FromResult(subscriptionId?.Contains("canceled") == true);
    }

    public Task<bool> AcknowledgePurchaseAsync(string purchaseToken, string packageName)
    {
        _logger.LogInformation("[MockGooglePayService] AcknowledgePurchaseAsync called");

        // Return true for test tokens
        return Task.FromResult(purchaseToken?.StartsWith("test_") == true);
    }
}