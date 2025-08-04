using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace GodGPT.GAgents.Common.Observability
{
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
            "ms", 
            "Payment success events processed");

        /// <summary>
        /// Records a payment success event (separate from analytics reporting)
        /// </summary>
        /// <param name="paymentPlatform">Payment platform</param>
        /// <param name="purchaseType">Type of purchase</param>
        /// <param name="userId">User identifier (converted to tier for low cardinality)</param>
        /// <param name="transactionId">Transaction identifier</param>
        /// <param name="logger">Optional logger</param>
        /// <param name="methodName">Automatically captured calling method name</param>
        /// <param name="filePath">Automatically captured file path</param>
        public static void RecordPaymentSuccess(
            string paymentPlatform, 
            string purchaseType, 
            string userId,
            string transactionId,
            ILogger? logger = null,
            [CallerMemberName] string methodName = "", 
            [CallerFilePath] string? filePath = null)
        {
            try
            {
                var className = GetClassNameFromFilePath(filePath);
                var fullMethodName = className != null ? $"{className}.{methodName}" : methodName;
                
                // Use user tier instead of user ID to avoid high cardinality
                var userTier = GetUserTier(userId);
                
                PaymentSuccessCounter.Add(1,
                    new KeyValuePair<string, object?>(PaymentTelemetryConstants.PaymentPlatformTag, paymentPlatform),
                    new KeyValuePair<string, object?>(PaymentTelemetryConstants.PurchaseTypeTag, purchaseType),
                    new KeyValuePair<string, object?>("user_tier", userTier),
                    new KeyValuePair<string, object?>(PaymentTelemetryConstants.MethodNameTag, fullMethodName),
                    new KeyValuePair<string, object?>(PaymentTelemetryConstants.EventCategoryTag, PaymentTelemetryConstants.PaymentProcessingCategory));
                
                logger?.LogInformation(
                    "[PaymentTelemetry] Payment success recorded: platform={PaymentPlatform} type={PurchaseType} tier={UserTier} transaction={TransactionId} method={MethodName}",
                    paymentPlatform, purchaseType, userTier, transactionId, fullMethodName);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "[PaymentTelemetry] Failed to record payment success metric");
            }
        }

        /// <summary>
        /// Gets class name from file path for context
        /// </summary>
        private static string? GetClassNameFromFilePath(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;

            var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
            return fileName;
        }

        /// <summary>
        /// Converts user ID to tier for low cardinality metrics
        /// </summary>
        private static string GetUserTier(string userId)
        {
            // Simple tier classification to avoid high cardinality
            // This can be enhanced based on business requirements
            if (string.IsNullOrEmpty(userId))
                return "unknown";
                
            var hash = userId.GetHashCode();
            var tier = Math.Abs(hash) % 4;
            
            return tier switch
            {
                0 => "tier_a",
                1 => "tier_b", 
                2 => "tier_c",
                _ => "tier_d"
            };
        }
    }
} 