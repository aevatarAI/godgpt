using Aevatar.Application.Grains.ChatManager.UserBilling;

namespace Aevatar.Application.Grains.Common.Service;

/// <summary>
/// Google Pay service for integrating with Google Play Developer API and Google Pay API
/// </summary>
public interface IGooglePayService
{
    /// <summary>
    /// Verify Google Play purchase token
    /// </summary>
    /// <param name="purchaseToken">Purchase token from Google Play</param>
    /// <param name="productId">Product ID</param>
    /// <param name="packageName">App package name</param>
    /// <returns>Purchase verification details</returns>
    Task<GooglePlayPurchaseDto> VerifyPurchaseAsync(string purchaseToken, string productId, string packageName);
    
    /// <summary>
    /// Verify Google Pay Web payment token
    /// </summary>
    /// <param name="paymentToken">Payment token from Google Pay</param>
    /// <param name="orderId">Order ID</param>
    /// <returns>Payment verification result</returns>
    Task<bool> VerifyWebPaymentAsync(string paymentToken, string orderId);
    
    /// <summary>
    /// Get Google Play subscription details
    /// </summary>
    /// <param name="subscriptionId">Subscription ID</param>
    /// <param name="packageName">App package name</param>
    /// <returns>Subscription details</returns>
    Task<GooglePlaySubscriptionDto> GetSubscriptionAsync(string subscriptionId, string packageName);
    
    /// <summary>
    /// Refund a purchase (note: actual refund must be done through Play Console)
    /// This method queries the refund status
    /// </summary>
    /// <param name="purchaseToken">Purchase token</param>
    /// <param name="packageName">App package name</param>
    /// <returns>Refund status</returns>
    Task<bool> GetRefundStatusAsync(string purchaseToken, string packageName);
    
    /// <summary>
    /// Cancel a subscription (note: actual cancellation must be done by user)
    /// This method queries the subscription status
    /// </summary>
    /// <param name="subscriptionId">Subscription ID</param>
    /// <param name="packageName">App package name</param>
    /// <returns>Cancellation status</returns>
    Task<bool> GetCancellationStatusAsync(string subscriptionId, string packageName);
    
    /// <summary>
    /// Acknowledge a purchase (required for subscriptions)
    /// </summary>
    /// <param name="purchaseToken">Purchase token</param>
    /// <param name="packageName">App package name</param>
    /// <returns>Acknowledgment success</returns>
    Task<bool> AcknowledgePurchaseAsync(string purchaseToken, string packageName);
}