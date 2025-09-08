using Aevatar.Core.Abstractions;
using Orleans;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Global JWT Provider GAgent - singleton for entire system
/// Manages JWT creation, caching, and global push token deduplication
/// </summary>
public interface IGlobalJwtProviderGAgent : IGAgent
{
    /// <summary>
    /// Get Firebase access token (cached globally for 24 hours)
    /// Thread-safe JWT creation with proper RSA lifecycle management
    /// </summary>
    Task<string?> GetFirebaseAccessTokenAsync();

    /// <summary>
    /// Check if push token can receive push notification (global deduplication)
    /// Prevents same device from receiving duplicate pushes across different users
    /// </summary>
    /// <param name="pushToken">Firebase push token</param>
    /// <param name="timeZoneId">Target timezone (e.g., "Asia/Shanghai")</param>
    /// <param name="isRetryPush">Whether this is a retry push (bypasses same-day check)</param>
    /// <param name="isFirstContent">Whether this is first content of multi-content push</param>
    /// <returns>True if push can be sent, false if duplicate</returns>
    Task<bool> CanSendPushAsync(string pushToken, string timeZoneId, bool isRetryPush = false, bool isFirstContent = true);

    /// <summary>
    /// Mark push as sent for deduplication tracking
    /// Records successful push to prevent same-day duplicates
    /// </summary>
    /// <param name="pushToken">Firebase push token</param>
    /// <param name="timeZoneId">Target timezone</param>
    /// <param name="isRetryPush">Whether this was a retry push</param>
    /// <param name="isFirstContent">Whether this was first content of multi-content push</param>
    Task MarkPushSentAsync(string pushToken, string timeZoneId, bool isRetryPush = false, bool isFirstContent = true);

    /// <summary>
    /// Get current status of the global JWT provider
    /// </summary>
    Task<GlobalJwtProviderStatus> GetStatusAsync();

    /// <summary>
    /// Force refresh the cached JWT token (for testing/debugging)
    /// </summary>
    Task RefreshTokenAsync();
}

/// <summary>
/// Status information for GlobalJwtProviderGAgent
/// </summary>
[GenerateSerializer]
public class GlobalJwtProviderStatus
{
    [Id(0)] public bool IsReady { get; set; }
    [Id(1)] public bool HasCachedToken { get; set; }
    [Id(2)] public DateTime? TokenExpiry { get; set; }
    [Id(3)] public int TotalTokenRequests { get; set; }
    [Id(4)] public int TotalDeduplicationChecks { get; set; }
    [Id(5)] public int PreventedDuplicates { get; set; }
    [Id(6)] public int TrackedPushTokens { get; set; }
    [Id(7)] public DateTime? LastTokenCreation { get; set; }
    [Id(8)] public DateTime? LastCleanup { get; set; }
    [Id(9)] public string? LastError { get; set; }
}
