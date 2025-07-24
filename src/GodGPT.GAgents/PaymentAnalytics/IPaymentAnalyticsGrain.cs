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
}
