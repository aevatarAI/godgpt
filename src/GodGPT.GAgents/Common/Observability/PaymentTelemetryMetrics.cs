using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Aevatar.Application.Grains.Common.Observability;

/// <summary>
/// Payment telemetry metrics collection following OpenTelemetry best practices
/// Provides independent, reusable metrics with minimal coupling to business logic
/// </summary>
public static class PaymentTelemetryMetrics
{
    private static readonly Meter Meter = new(PaymentTelemetryConstants.PaymentMeterName);

    // Counter for payment success events
    private static readonly Counter<long> PaymentSuccessCounter = Meter.CreateCounter<long>(
        PaymentTelemetryConstants.PaymentSuccessEvents,
        string.Empty,
        "Payment success events processed");

    /// <summary>
    /// Records a payment success event (separate from analytics reporting)
    /// </summary>
    /// <param name="paymentPlatform">Payment platform</param>
    /// <param name="purchaseType">Type of purchase</param>
    /// <param name="userId">User identifier (converted to tier for low cardinality)</param>
    /// <param name="transactionId">Transaction identifier</param>
    /// <param name="logger">Optional logger</param>
    public static void RecordPaymentSuccess(
        string paymentPlatform,
        string purchaseType,
        string userId,
        string transactionId,
        ILogger? logger = null)
    {
        try
        {
            PaymentSuccessCounter.Add(1,
                new KeyValuePair<string, object?>(PaymentTelemetryConstants.PaymentPlatformTag, paymentPlatform),
                new KeyValuePair<string, object?>(PaymentTelemetryConstants.PurchaseTypeTag, purchaseType),
                new KeyValuePair<string, object?>(PaymentTelemetryConstants.UserIdTag, userId),
                new KeyValuePair<string, object?>(PaymentTelemetryConstants.TransactionIdTag, transactionId));

            logger?.LogInformation(
                "[PaymentTelemetry] Payment success recorded: platform={PaymentPlatform} type={PurchaseType} tier={userId} transaction={TransactionId}",
                paymentPlatform, purchaseType, userId, transactionId);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[PaymentTelemetry] Failed to record payment success metric");
        }
    }
}