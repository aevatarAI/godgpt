namespace Aevatar.Application.Grains.Common.Observability
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
        
        // New: User retention rate analysis metrics
        public const string ActiveByCohortEvents = "godgpt_user_lifecycle_active_by_cohort_total";
        
        // New: Anonymous user activity tracking for conversion funnel
        public const string AnonymousUserActivityEvents = "godgpt_user_lifecycle_anonymous_activity_total";
        
        // New: Credits exhausted tracking (critical conversion point)
        public const string CreditsExhaustedEvents = "godgpt_user_lifecycle_credits_exhausted_total";

        // Common tag names
        public const string UserIdTag = "user_id";
        
        // New: Retention analysis related tags
        public const string DaysSinceRegistrationTag = "days_since_registration";   // Days since user registration (0=today, 1=yesterday, etc.)
        public const string MembershipLevelTag = "membership_level";    // Membership level (9 levels: free, premium_day/week/month/year, ultimate_day/week/month/year)
        
        // New: Anonymous user tracking related tags
        public const string ChatCountTag = "chat_count";
        
        // New: Credits exhausted tracking related tags
        public const string ExhaustionDateTag = "exhaustion_date";          // Credits exhaustion date (YYYY-MM-DD)
    }
} 