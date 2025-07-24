using Aevatar.Application.Grains.PaymentAnalytics.Dtos;

namespace Aevatar.Application.Grains.PaymentAnalytics;

/// <summary>
/// Payment analytics service interface
/// Responsible for reporting payment success events to Google Analytics
/// </summary>
public interface IPaymentAnalyticsGrain : IGrainWithStringKey
{
    /// <summary>
    /// Report a payment success event to Google Analytics
    /// Debug mode is controlled by configuration (UseDebugMode setting)
    /// </summary>
    /// <returns>Analytics result with success status</returns>
    Task<PaymentAnalyticsResultDto> ReportPaymentSuccessAsync();
}
