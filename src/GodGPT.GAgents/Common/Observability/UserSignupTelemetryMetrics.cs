using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Aevatar.Application.Grains.Common.Observability;

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
        string.Empty, 
        "User signup success events processed");

    // New: Counter for user activity events by cohort
    private static readonly Counter<long> ActiveByCohortCounter = Meter.CreateCounter<long>(
        UserLifecycleTelemetryConstants.ActiveByCohortEvents,
        string.Empty,
        "User activity events grouped by registration cohort for retention analysis");

    // New: Counter for anonymous user activity events
    private static readonly Counter<long> AnonymousUserActivityCounter = Meter.CreateCounter<long>(
        UserLifecycleTelemetryConstants.AnonymousUserActivityEvents,
        string.Empty,
        "Anonymous user daily activity events for conversion funnel tracking");

    // New: Counter for credits exhausted events
    private static readonly Counter<long> CreditsExhaustedCounter = Meter.CreateCounter<long>(
        UserLifecycleTelemetryConstants.CreditsExhaustedEvents,
        string.Empty,
        "User credits exhausted events for conversion funnel analysis");

    /// <summary>
    /// Records a user signup success event
    /// </summary>
    /// <param name="userId">User identifier (converted to tier for low cardinality)</param>
    /// <param name="logger">Optional logger</param>
    public static void RecordSignupSuccess(string userId, ILogger? logger = null)
    {
        try
        {
            // Use user tier instead of user ID to avoid high cardinality
            SignupSuccessCounter.Add(1,
                new KeyValuePair<string, object?>(UserLifecycleTelemetryConstants.UserIdTag, "ChatManager"));
                
            logger?.LogDebug(
                "[UserLifecycleTelemetry] Signup success recorded: user={UserId}", userId);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[UserLifecycleTelemetry] Failed to record signup success metric");
        }
    }

    /// <summary>
    /// Records user activity event for retention tracking
    /// This single metric supports both new user registration and user retention analysis
    /// Application-level deduplication ensures each user is counted only once per day
    /// </summary>
    /// <param name="daysSinceRegistration">Days since user registration (0=registered today, 1=registered yesterday, etc.)</param>
    /// <param name="membershipLevel">Membership level (9 levels: see UserMembershipTier constants)</param>
    /// <param name="logger">Optional logger</param>
    public static void RecordUserActivityByCohort(
        int daysSinceRegistration,
        string membershipLevel,
        ILogger? logger = null)
    {
        try
        {
            ActiveByCohortCounter.Add(1,
                new KeyValuePair<string, object?>(UserLifecycleTelemetryConstants.DaysSinceRegistrationTag, daysSinceRegistration),
                new KeyValuePair<string, object?>(UserLifecycleTelemetryConstants.MembershipLevelTag, membershipLevel));

            logger?.LogDebug(
                "[UserLifecycleTelemetry] User activity recorded: daysSinceRegistration={DaysSinceRegistration} membership={MembershipLevel}",
                daysSinceRegistration, membershipLevel);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[UserLifecycleTelemetry] Failed to record user activity by cohort");
        }
    }

    /// <summary>
    /// Records anonymous user daily activity event (part of user lifecycle/conversion tracking)
    /// This method ensures each anonymous user is counted only once per day
    /// </summary>
    /// <param name="activityDate">Activity date (YYYY-MM-DD format)</param>
    /// <param name="chatCount">Current chat count for the user</param>
    /// <param name="logger">Optional logger</param>
    public static void RecordAnonymousUserActivity(
        int chatCount,
        ILogger? logger = null)
    {
        try
        {
            AnonymousUserActivityCounter.Add(1,
                new KeyValuePair<string, object?>(UserLifecycleTelemetryConstants.ChatCountTag, chatCount));

            logger?.LogDebug(
                "[UserLifecycleTelemetry] Anonymous user activity recorded: chatCount={ChatCount}", chatCount);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[UserLifecycleTelemetry] Failed to record anonymous user activity");
        }
    }

    /// <summary>
    /// Records a credits exhausted event (critical conversion point in user lifecycle)
    /// This method tracks when users run out of credits, indicating potential conversion opportunity
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="daysSinceSignup">Days since user registration</param>
    /// <param name="logger">Optional logger</param>
    public static void RecordCreditsExhausted(
        string userId,
        int daysSinceSignup,
        ILogger? logger = null)
    {
        try
        {
            var exhaustionDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
            
            var tags = new TagList
            {
                { UserLifecycleTelemetryConstants.ExhaustionDateTag, exhaustionDate },
            };

            CreditsExhaustedCounter.Add(1, tags);
            
            logger?.LogDebug(
                "[UserLifecycleTelemetry] Credits exhausted event recorded for user: {UserId}, days: {DaysSinceSignup}",
                userId, daysSinceSignup);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[UserLifecycleTelemetry] Failed to record credits exhausted event for user: {UserId}", userId);
        }
    }
}