using Aevatar.Application.Grains.Common.Service;

namespace GodGPT.GooglePay.Tests;

/// <summary>
/// Tests for GooglePayService (using mock implementation)
/// </summary>
public class GooglePayServiceTests : GooglePayTestBase
{
    [Fact]
    public async Task VerifyPurchaseAsync_ValidTestToken_ReturnsValidPurchase()
    {
        // Arrange
        var service = GetService<IGooglePayService>();
        var testToken = "test_purchase_token_12345";
        var productId = "premium_monthly_test";
        var packageName = "com.godgpt.app.test";

        // Act
        var result = await service.VerifyPurchaseAsync(testToken, productId, packageName);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(testToken, result.PurchaseToken);
        Assert.Equal(productId, result.ProductId);
        Assert.Equal(packageName, result.PackageName);
        Assert.Equal(1, result.PurchaseState); // Purchased
        Assert.True(result.AutoRenewing);
    }

    [Fact]
    public async Task VerifyPurchaseAsync_InvalidToken_ReturnsNull()
    {
        // Arrange
        var service = GetService<IGooglePayService>();
        var invalidToken = "invalid_purchase_token";
        var productId = "premium_monthly_test";
        var packageName = "com.godgpt.app.test";

        // Act
        var result = await service.VerifyPurchaseAsync(invalidToken, productId, packageName);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyWebPaymentAsync_ValidTestToken_ReturnsTrue()
    {
        // Arrange
        var service = GetService<IGooglePayService>();
        var testToken = "test_payment_token_web_12345";
        var orderId = "12999763169054705758.1371079406387615";

        // Act
        var result = await service.VerifyWebPaymentAsync(testToken, orderId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task VerifyWebPaymentAsync_InvalidToken_ReturnsFalse()
    {
        // Arrange
        var service = GetService<IGooglePayService>();
        var invalidToken = "invalid_payment_token";
        var orderId = "12999763169054705758.1371079406387615";

        // Act
        var result = await service.VerifyWebPaymentAsync(invalidToken, orderId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetSubscriptionAsync_ValidTestSubscription_ReturnsSubscription()
    {
        // Arrange
        var service = GetService<IGooglePayService>();
        var subscriptionId = "test_subscription_12345";
        var packageName = "com.godgpt.app.test";

        // Act
        var result = await service.GetSubscriptionAsync(subscriptionId, packageName);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(subscriptionId, result.SubscriptionId);
        Assert.True(result.AutoRenewing);
        Assert.Equal(1, result.PaymentState); // Payment received
        Assert.Equal("USD", result.PriceCurrencyCode);
    }

    [Fact]
    public async Task GetSubscriptionAsync_InvalidSubscription_ReturnsNull()
    {
        // Arrange
        var service = GetService<IGooglePayService>();
        var invalidSubscriptionId = "invalid_subscription";
        var packageName = "com.godgpt.app.test";

        // Act
        var result = await service.GetSubscriptionAsync(invalidSubscriptionId, packageName);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetRefundStatusAsync_NormalToken_ReturnsFalse()
    {
        // Arrange
        var service = GetService<IGooglePayService>();
        var token = "test_purchase_token";
        var packageName = "com.godgpt.app.test";

        // Act
        var result = await service.GetRefundStatusAsync(token, packageName);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetRefundStatusAsync_RefundedToken_ReturnsTrue()
    {
        // Arrange
        var service = GetService<IGooglePayService>();
        var refundedToken = "test_refunded_token";
        var packageName = "com.godgpt.app.test";

        // Act
        var result = await service.GetRefundStatusAsync(refundedToken, packageName);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetCancellationStatusAsync_ActiveSubscription_ReturnsFalse()
    {
        // Arrange
        var service = GetService<IGooglePayService>();
        var subscriptionId = "test_subscription";
        var packageName = "com.godgpt.app.test";

        // Act
        var result = await service.GetCancellationStatusAsync(subscriptionId, packageName);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetCancellationStatusAsync_CanceledSubscription_ReturnsTrue()
    {
        // Arrange
        var service = GetService<IGooglePayService>();
        var canceledSubscriptionId = "test_canceled_subscription";
        var packageName = "com.godgpt.app.test";

        // Act
        var result = await service.GetCancellationStatusAsync(canceledSubscriptionId, packageName);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task AcknowledgePurchaseAsync_ValidTestToken_ReturnsTrue()
    {
        // Arrange
        var service = GetService<IGooglePayService>();
        var testToken = "test_purchase_token";
        var packageName = "com.godgpt.app.test";

        // Act
        var result = await service.AcknowledgePurchaseAsync(testToken, packageName);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task AcknowledgePurchaseAsync_InvalidToken_ReturnsFalse()
    {
        // Arrange
        var service = GetService<IGooglePayService>();
        var invalidToken = "invalid_token";
        var packageName = "com.godgpt.app.test";

        // Act
        var result = await service.AcknowledgePurchaseAsync(invalidToken, packageName);

        // Assert
        Assert.False(result);
    }
}