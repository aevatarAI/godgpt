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
    /// Check if push token can receive push notification (UTC-based deduplication)
    /// Prevents same device from receiving duplicate pushes at the same UTC hour
    /// </summary>
    /// <param name="pushToken">Firebase push token</param>
    /// <param name="timeZoneId">Target timezone (e.g., "Asia/Shanghai")</param>
    /// <param name="isRetryPush">Whether this is a retry push (bypasses UTC hour check)</param>
    /// <returns>True if push can be sent, false if duplicate at this UTC hour</returns>
    Task<bool> CanSendPushAsync(string pushToken, string timeZoneId, bool isRetryPush = false);

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
public class GlobalJwtProviderStatus
{
    public bool IsReady { get; set; }
    public bool HasCachedToken { get; set; }
    public DateTime? TokenExpiry { get; set; }
    public int TotalTokenRequests { get; set; }
    public int TotalDeduplicationChecks { get; set; }
    public int PreventedDuplicates { get; set; }
    public int TrackedPushTokens { get; set; }
    public DateTime? LastTokenCreation { get; set; }
    public DateTime? LastCleanup { get; set; }
    public string? LastError { get; set; }
}
