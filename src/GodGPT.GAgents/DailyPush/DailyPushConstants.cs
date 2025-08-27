using System;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Daily push notification constants
/// </summary>
public static class DailyPushConstants
{
    // === Test Mode Configuration ===
    public const bool IS_TEST_MODE = true; // Set to false for production
    
    // === Timing Configuration (Test Mode) ===
    public static readonly TimeSpan TEST_MORNING_DELAY = TimeSpan.FromMinutes(1);    // 1 minute after registration
    public static readonly TimeSpan TEST_AFTERNOON_DELAY = TimeSpan.FromMinutes(3);  // 3 minutes for retry
    public static readonly TimeSpan TEST_CYCLE_DURATION = TimeSpan.FromMinutes(5);   // 5 minute test cycle
    
    // === Timing Configuration (Production) ===
    public static readonly TimeSpan MORNING_TIME = TimeSpan.FromHours(8);    // 8:00 AM
    public static readonly TimeSpan AFTERNOON_TIME = TimeSpan.FromHours(15);  // 3:00 PM
    public static readonly TimeSpan DAILY_CYCLE = TimeSpan.FromHours(23);     // 23-hour cycle for DST compatibility
    
    // === Content Selection Configuration ===
    public const int DAILY_CONTENT_COUNT = 2;                               // 2 contents per day
    public const int CONTENT_HISTORY_DAYS = 7;                              // Avoid repeat for 7 days
    public const int MAX_RETRY_ATTEMPTS = 3;                                // Max push retry attempts
    
    // === Redis TTL Configuration ===
    public static readonly TimeSpan DAILY_READ_TTL = TimeSpan.FromHours(48);     // Read status 48 hours
    public static readonly TimeSpan CONTENT_USAGE_TTL = TimeSpan.FromDays(7);    // Content usage 7 days
    public const int REDIS_DATA_TTL_DAYS = 7;                                   // General Redis data TTL
    
    // === Firebase Configuration ===
    // In production, set via environment variable FIREBASE_SERVER_KEY
    public const string? FIREBASE_SERVER_KEY = null;
    
    // === Push Type Enumeration ===
    public enum PushType
    {
        DailyPush = 1,
        AfternoonRetry = 2,
        TestPush = 9
    }
    
    // === GAgent Instance Configuration ===
    /// <summary>
    /// Well-known GUID for the singleton DailyContentGAgent instance
    /// </summary>
    public static readonly Guid CONTENT_GAGENT_ID = new Guid("12345678-1234-5678-9abc-123456789012");
    
    /// <summary>
    /// Convert timezone string to deterministic GUID for TimezoneUserIndexGAgent and TimezoneSchedulerGAgent
    /// </summary>
    public static Guid TimezoneToGuid(string timezoneId)
    {
        using (var md5 = System.Security.Cryptography.MD5.Create())
        {
            var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"Timezone_{timezoneId}"));
            return new Guid(hash);
        }
    }
    
    /// <summary>
    /// Register timezone mapping when timezone GAgent is first created
    /// </summary>
    public static async Task RegisterTimezoneMapping(string timezoneId, IGrainFactory grainFactory)
    {
        var timezoneGuid = TimezoneToGuid(timezoneId);
        var contentGAgent = grainFactory.GetGrain<IDailyContentGAgent>(CONTENT_GAGENT_ID);
        await contentGAgent.RegisterTimezoneGuidMappingAsync(timezoneGuid, timezoneId);
    }
    
    /// <summary>
    /// Get timezone ID from GUID (reverse lookup from DailyContentGAgent)
    /// </summary>
    public static async Task<string?> GetTimezoneFromGuidAsync(Guid timezoneGuid, IGrainFactory grainFactory)
    {
        var contentGAgent = grainFactory.GetGrain<IDailyContentGAgent>(CONTENT_GAGENT_ID);
        return await contentGAgent.GetTimezoneFromGuidAsync(timezoneGuid);
    }
}
