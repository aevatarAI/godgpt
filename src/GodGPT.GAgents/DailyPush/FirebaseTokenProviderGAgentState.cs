using Aevatar.Core.Abstractions;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// State for Firebase token provider GAgent
/// Manages cached access token and statistics for individual ChatManager
/// </summary>
[GenerateSerializer]
public class FirebaseTokenProviderGAgentState : StateBase
{
    /// <summary>
    /// Cached Firebase access token
    /// </summary>
    [Id(0)]
    public string? CachedAccessToken { get; set; }

    /// <summary>
    /// Token expiry time (UTC)
    /// </summary>
    [Id(1)]
    public DateTime TokenExpiry { get; set; } = DateTime.MinValue;

    /// <summary>
    /// Total number of token requests served
    /// </summary>
    [Id(2)]
    public int TotalRequests { get; set; }

    /// <summary>
    /// Number of successful token creations
    /// </summary>
    [Id(3)]
    public int SuccessfulCreations { get; set; }

    /// <summary>
    /// Number of failed token creation attempts
    /// </summary>
    [Id(4)]
    public int FailedAttempts { get; set; }

    /// <summary>
    /// Last successful token creation time
    /// </summary>
    [Id(5)]
    public DateTime? LastSuccessTime { get; set; }

    /// <summary>
    /// Last error message, if any
    /// </summary>
    [Id(6)]
    public string? LastError { get; set; }

    /// <summary>
    /// GAgent activation time for diagnostics
    /// </summary>
    [Id(7)]
    public DateTime ActivationTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Check if current token is still valid (with buffer time)
    /// </summary>
    public bool IsTokenValid(int bufferMinutes = 1)
    {
        return !string.IsNullOrEmpty(CachedAccessToken) && 
               DateTime.UtcNow < TokenExpiry.AddMinutes(-bufferMinutes);
    }

    /// <summary>
    /// Clear cached token data
    /// </summary>
    public void ClearToken()
    {
        CachedAccessToken = null;
        TokenExpiry = DateTime.MinValue;
    }

    /// <summary>
    /// Update token and expiry
    /// </summary>
    public void UpdateToken(string token, DateTime expiry)
    {
        CachedAccessToken = token;
        TokenExpiry = expiry;
        LastSuccessTime = DateTime.UtcNow;
        SuccessfulCreations++;
    }

    /// <summary>
    /// Record failed attempt
    /// </summary>
    public void RecordFailure(string error)
    {
        FailedAttempts++;
        LastError = error;
    }

    /// <summary>
    /// Increment request counter
    /// </summary>
    public void IncrementRequests()
    {
        TotalRequests++;
    }
}
