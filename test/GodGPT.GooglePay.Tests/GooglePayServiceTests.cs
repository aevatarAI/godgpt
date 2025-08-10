using Aevatar.Application.Grains.Common.Service;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Xunit;
using System.Threading.Tasks;
using Aevatar.Application.Grains.Common.Dtos;

namespace GodGPT.GooglePay.Tests;

/// <summary>
/// Tests for GooglePayService (using a mocked HttpClient)
/// </summary>
public class GooglePayServiceTests : GooglePayTestBase
{
    private readonly IGooglePayService _googlePayService;

    public GooglePayServiceTests()
    {
        // The service is retrieved from the DI container which is configured
        // in GooglePayTestModule to use a mocked HttpMessageHandler.
        _googlePayService = GetService<IGooglePayService>();
    }

    [Fact]
    public async Task VerifyGooglePlayPurchaseAsync_ValidSubscriptionToken_ReturnsValidResult()
    {
        // Arrange
        var request = CreateTestGooglePlayVerificationDto("test_user"); 
        request.PurchaseToken = "valid_subscription_token"; 
    // This token will be directed to the mock handler for successful subscription.
     
    
        // Act
        var result = await _googlePayService.VerifyGooglePlayPurchaseAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsValid, "Verification should be valid for a subscription purchase.");
        Assert.Equal("Subscription verified successfully", result.Message);
    }
    
    [Fact]
    public async Task VerifyGooglePlayPurchaseAsync_ValidProductToken_ReturnsValidResult()
    {
        // Arrange
        var request = CreateTestGooglePlayVerificationDto("test_user"); 
        request.PurchaseToken = "valid_product_token";
        // This token will be directed to the mock handler for successful product purchase.
        // The service first tries to validate as a subscription. The mock for subscriptions is generic,
        // so we rely on the product mock being hit as a fallback.
         

        // Act
        var result = await _googlePayService.VerifyGooglePlayPurchaseAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsValid, "Verification should be valid for a product purchase.");
        Assert.Equal("Product purchase verified successfully", result.Message);
    }

    [Fact]
    public async Task VerifyGooglePlayPurchaseAsync_NotFoundToken_ReturnsInvalidResult()
    {
        // Arrange
        var request = CreateTestGooglePlayVerificationDto("test_user");
        // This token is configured in the mock to return a 404 Not Found status.
        request.PurchaseToken = "not_found_token"; 

        // Act
        var result = await _googlePayService.VerifyGooglePlayPurchaseAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Equal("INVALID_PURCHASE_TOKEN", result.ErrorCode);
    }

    [Fact]
    public async Task VerifyGooglePlayPurchaseAsync_ApiErrorToken_ReturnsInvalidResult()
    {
        // Arrange
        var request = CreateTestGooglePlayVerificationDto("test_user");
        // This token is configured in the mock to return a 500 Internal Server Error status.
        request.PurchaseToken = "api_error_token";

        // Act
        var result = await _googlePayService.VerifyGooglePlayPurchaseAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Equal("API_ERROR", result.ErrorCode);
    }

    [Fact]
    public async Task VerifyGooglePayPaymentAsync_NotImplemented_ReturnsInvalid()
    {
        // This test remains valid as the web payment part is indeed not implemented in the service.
        // Arrange
        var request = new GooglePayVerificationDto();

        // Act
        var result = await _googlePayService.VerifyGooglePayPaymentAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Equal("NOT_IMPLEMENTED", result.ErrorCode);
    }
}
