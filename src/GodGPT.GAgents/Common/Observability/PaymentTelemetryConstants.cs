namespace GodGPT.GAgents.Common.Observability
{
    /// <summary>
    /// Payment telemetry constants following OpenTelemetry best practices
    /// </summary>
    public static class PaymentTelemetryConstants
    {
        // Meter name - use component namespace
        public const string PaymentMeterName = "GodGPT.Payment";
        
        // Metric names - follow godgpt_payment_* pattern
        public const string PaymentSuccessEvents = "godgpt_payment_success_events_total";
        
        // Event categories for labeling
        public const string PaymentProcessingCategory = "payment_processing";
        
        // Common tag names
        public const string PaymentPlatformTag = "payment_platform";
        public const string PurchaseTypeTag = "purchase_type";
        public const string MethodNameTag = "method_name";
        public const string EventCategoryTag = "event_category";
    }
} 