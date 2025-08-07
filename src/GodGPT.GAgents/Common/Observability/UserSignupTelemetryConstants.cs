namespace GodGPT.GAgents.Common.Observability
{
    /// <summary>
    /// User lifecycle telemetry constants following OpenTelemetry best practices
    /// </summary>
    public static class UserLifecycleTelemetryConstants
    {
        // Meter name - use component namespace
        public const string UserLifecycleMeterName = "GodGPT.UserLifecycle";
        
        // Metric names - follow godgpt_user_lifecycle_* pattern
        public const string SignupSuccessEvents = "godgpt_user_lifecycle_signup_success_total";
        
        // Event categories for labeling
        public const string UserOnboardingCategory = "user_onboarding";
        
        // Common tag names
        public const string SignupSourceTag = "signup_source";
        public const string UserTierTag = "user_tier";
        public const string MethodNameTag = "method_name";
        public const string EventCategoryTag = "event_category";
    }
} 