using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.PaymentAnalytics.Dtos;

namespace Aevatar.Application.Grains.PaymentAnalytics;

/// <summary>
/// Payment analytics service interface
/// Responsible for reporting payment success events to Google Analytics with idempotent support and retry mechanism
/// </summary>
public interface IPaymentAnalyticsGrain : IGrainWithStringKey
{
    /// <summary>
    /// Report a payment success event to Google Analytics with retry mechanism and idempotent support
    /// Uses Google Analytics 4's built-in transaction_id deduplication mechanism
    /// </summary>
    /// <param name="paymentPlatform">Payment platform used</param>
    /// <param name="transactionId">Unique transaction/order ID for deduplication</param>
    /// <param name="userId">User ID</param>
    /// <returns>Analytics result with success status</returns>
    Task<PaymentAnalyticsResultDto> ReportPaymentSuccessAsync(
        PaymentPlatform paymentPlatform,
        string transactionId, 
        string userId);

    /// <summary>
    /// Report a payment success event to Google Analytics with detailed purchase type information
    /// Uses Google Analytics 4's built-in transaction_id deduplication mechanism
    /// </summary>
    /// <param name="paymentPlatform">Payment platform used</param>
    /// <param name="transactionId">Unique transaction/order ID for deduplication</param>
    /// <param name="userId">User ID</param>
    /// <param name="purchaseType">Type of purchase (Subscription/Renewal)</param>
    /// <param name="currency">Currency code</param>
    /// <param name="amount">Purchase amount</param>
    /// <returns>Analytics result with success status</returns>
    Task<PaymentAnalyticsResultDto> ReportPaymentSuccessAsync(
        PaymentPlatform paymentPlatform,
        string transactionId, 
        string userId,
        PurchaseType purchaseType,
        string currency,
        decimal amount);

    /// <summary>
    /// Report a refund event to Google Analytics
    /// </summary>
    /// <param name="paymentPlatform">Payment platform used</param>
    /// <param name="transactionId">Original transaction/order ID for the refunded purchase</param>
    /// <param name="userId">User ID</param>
    /// <param name="refundReason">Reason for the refund</param>
    /// <param name="currency">Currency code</param>
    /// <param name="refundAmount">Refund amount (positive value)</param>
    /// <returns>Analytics result with success status</returns>
    Task<PaymentAnalyticsResultDto> ReportRefundEventAsync(
        PaymentPlatform paymentPlatform,
        string transactionId, 
        string userId,
        string refundReason,
        string currency,
        decimal refundAmount);
}
