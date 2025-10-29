using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.Common.Options;

namespace Aevatar.Application.Grains.Common.Service;

/// <summary>
/// Apple payment service interface for handling Apple Pay and App Store specific payment operations
/// </summary>
public interface IApplePaymentService
{
    /// <summary>
    /// Get available Apple products
    /// </summary>
    /// <param name="options">Apple Pay configuration options</param>
    /// <returns>List of Apple products</returns>
    Task<List<AppleProductDto>> GetProductsAsync(ApplePayOptions options);

    /// <summary>
    /// Create App Store subscription
    /// </summary>
    /// <param name="dto">App Store subscription creation parameters</param>
    /// <param name="options">Apple Pay configuration options</param>
    /// <returns>App Store subscription response</returns>
    Task<AppStoreSubscriptionResponseDto> CreateAppStoreSubscriptionAsync(CreateAppStoreSubscriptionDto dto, ApplePayOptions options);

    /// <summary>
    /// Process App Store notification
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="jsonPayload">Notification JSON payload</param>
    /// <param name="options">Apple Pay configuration options</param>
    /// <returns>Payment verification result</returns>
    Task<PaymentVerificationResultDto> ProcessAppStoreNotificationAsync(Guid userId, string jsonPayload, ApplePayOptions options);

    /// <summary>
    /// Get App Store transaction information
    /// </summary>
    /// <param name="transactionId">Transaction ID</param>
    /// <param name="environment">App Store environment (sandbox/production)</param>
    /// <param name="options">Apple Pay configuration options</param>
    /// <returns>Transaction decoded payload</returns>
    Task<AppStoreJWSTransactionDecodedPayload> GetAppStoreTransactionInfoAsync(string transactionId, string environment, ApplePayOptions options);

    /// <summary>
    /// Verify App Store receipt
    /// </summary>
    /// <param name="receiptData">Receipt data</param>
    /// <param name="options">Apple Pay configuration options</param>
    /// <returns>Payment verification result</returns>
    Task<PaymentVerificationResultDto> VerifyAppStoreReceiptAsync(string receiptData, ApplePayOptions options);

    /// <summary>
    /// Process App Store subscription renewal
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="transactionInfo">Transaction information</param>
    /// <param name="options">Apple Pay configuration options</param>
    /// <returns>Processing success</returns>
    Task<bool> ProcessSubscriptionRenewalAsync(Guid userId, AppStoreJWSTransactionDecodedPayload transactionInfo, ApplePayOptions options);
}
