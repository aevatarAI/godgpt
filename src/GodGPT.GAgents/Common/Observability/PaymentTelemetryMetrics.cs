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
    /// <param name="logger">Optional logger</param>
    public static void RecordPaymentSuccess(
        string paymentPlatform,
        string purchaseType,
        string userId,
        string productId,
        ILogger? logger = null)
    {
        try
        {
            PaymentSuccessCounter.Add(1,
                new KeyValuePair<string, object?>(PaymentTelemetryConstants.PaymentPlatformTag, paymentPlatform),
                new KeyValuePair<string, object?>(PaymentTelemetryConstants.PurchaseTypeTag, purchaseType),
                new KeyValuePair<string, object?>(PaymentTelemetryConstants.ProductIdTag, productId));

            logger?.LogDebug(
                "[PaymentTelemetry] Payment success recorded: platform={PaymentPlatform} type={PurchaseType} userId={UserId} productId={TransactionId}",
                paymentPlatform, purchaseType, userId, productId);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[PaymentTelemetry] Failed to record payment success metric");
        }
    }
}