using Aevatar.Core.Abstractions;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Firebase access token provider GAgent for individual ChatManager
/// Provides JWT token management without concurrency conflicts
/// </summary>
public interface IFirebaseTokenProviderGAgent : IGAgent, IGrainWithIntegerKey
{
    /// <summary>
    /// Get Firebase access token for FCM API v1
    /// Creates and caches token with proper lifecycle management
    /// </summary>
    Task<string?> GetAccessTokenAsync();

    /// <summary>
    /// Check if current cached token is still valid
    /// </summary>
    Task<bool> IsTokenValidAsync();

    /// <summary>
    /// Clear cached token and force refresh on next request
    /// </summary>
    Task ClearTokenCacheAsync();

    /// <summary>
    /// Get provider status and statistics
    /// </summary>
    Task<TokenProviderStatus> GetStatusAsync();
}

/// <summary>
/// Firebase token provider status information
/// </summary>
[GenerateSerializer]
public class TokenProviderStatus
{
    /// <summary>
    /// Whether the provider is ready to serve tokens
    /// </summary>
    [Id(0)] public bool IsReady { get; set; }

    /// <summary>
    /// Whether a token is currently cached
    /// </summary>
    [Id(1)] public bool HasCachedToken { get; set; }

    /// <summary>
    /// Token expiry time (UTC), null if no token
    /// </summary>
    [Id(2)] public DateTime? TokenExpiry { get; set; }

    /// <summary>
    /// Total number of token requests served
    /// </summary>
    [Id(3)] public int TotalRequests { get; set; }

    /// <summary>
    /// Number of successful token creations
    /// </summary>
    [Id(4)] public int SuccessfulCreations { get; set; }

    /// <summary>
    /// Number of failed token creation attempts
    /// </summary>
    [Id(5)] public int FailedAttempts { get; set; }

    /// <summary>
    /// Last successful token creation time
    /// </summary>
    [Id(6)] public DateTime? LastSuccessTime { get; set; }

    /// <summary>
    /// Last error message, if any
    /// </summary>
    [Id(7)] public string? LastError { get; set; }
}
