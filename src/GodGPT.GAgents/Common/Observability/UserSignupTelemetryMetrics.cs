using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace GodGPT.GAgents.Common.Observability
{
    /// <summary>
    /// User lifecycle telemetry metrics collection following OpenTelemetry best practices
    /// Provides independent, reusable metrics with minimal coupling to business logic
    /// </summary>
    public static class UserLifecycleTelemetryMetrics
    {
        private static readonly Meter Meter = new(UserLifecycleTelemetryConstants.UserLifecycleMeterName);
        
        // Counter for signup success events
        private static readonly Counter<long> SignupSuccessCounter = Meter.CreateCounter<long>(
            UserLifecycleTelemetryConstants.SignupSuccessEvents, 
            "events", 
            "User signup success events processed");

        /// <summary>
        /// Records a user signup success event
        /// </summary>
        /// <param name="signupSource">Source of signup (web, mobile, api, etc.)</param>
        /// <param name="userId">User identifier (converted to tier for low cardinality)</param>
        /// <param name="logger">Optional logger</param>
        /// <param name="methodName">Automatically captured calling method name</param>
        /// <param name="filePath">Automatically captured file path</param>
        public static void RecordSignupSuccess(
            string signupSource, 
            string userId,
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
                
                SignupSuccessCounter.Add(1,
                    new KeyValuePair<string, object?>(UserLifecycleTelemetryConstants.SignupSourceTag, signupSource),
                    new KeyValuePair<string, object?>(UserLifecycleTelemetryConstants.UserTierTag, userTier),
                    new KeyValuePair<string, object?>(UserLifecycleTelemetryConstants.MethodNameTag, fullMethodName),
                    new KeyValuePair<string, object?>(UserLifecycleTelemetryConstants.EventCategoryTag, UserLifecycleTelemetryConstants.UserOnboardingCategory));
                
                logger?.LogInformation(
                    "[UserLifecycleTelemetry] Signup success recorded: source={SignupSource} tier={UserTier} user={UserId} method={MethodName}",
                    signupSource, userTier, userId, fullMethodName);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "[UserLifecycleTelemetry] Failed to record signup success metric");
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