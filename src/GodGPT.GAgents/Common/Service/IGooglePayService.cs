using Orleans;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.Common.Dtos;

namespace Aevatar.Application.Grains.Common.Service;

/// <summary>
/// Google Pay service interface for payment verification and subscription management
/// </summary>
public interface IGooglePayService
{
    /// <summary>
    /// Verify Google Play purchase token (internal method used by transaction verification and webhook)
    /// </summary>
    /// <param name="verificationDto">Google Play verification request</param>
    /// <returns>Payment verification result</returns>
    Task<PaymentVerificationResultDto> VerifyGooglePlayPurchaseAsync(GooglePlayVerificationDto verificationDto);
    
    /// <summary>
    /// Get subscription details from Google Play
    /// </summary>
    /// <param name="packageName">Application package name</param>
    /// <param name="subscriptionId">Subscription product ID</param>
    /// <param name="purchaseToken">Purchase token</param>
    /// <returns>Subscription details</returns>
    Task<GooglePlaySubscriptionDto> GetSubscriptionAsync(string packageName, string subscriptionId, string purchaseToken);
    
    /// <summary>
    /// Get product purchase details from Google Play
    /// </summary>
    /// <param name="packageName">Application package name</param>
    /// <param name="productId">Product ID</param>
    /// <param name="purchaseToken">Purchase token</param>
    /// <returns>Product purchase details</returns>
    Task<GooglePlayProductDto> GetProductAsync(string packageName, string productId, string purchaseToken);

    /// <summary>
    /// Acknowledge a purchase with Google Play
    /// </summary>
    /// <param name="packageName">Application package name</param>
    /// <param name="productId">Product ID</param>
    /// <param name="purchaseToken">Purchase token</param>
    /// <returns>True if acknowledged successfully</returns>
    Task<bool> AcknowledgePurchaseAsync(string packageName, string productId, string purchaseToken);
}

