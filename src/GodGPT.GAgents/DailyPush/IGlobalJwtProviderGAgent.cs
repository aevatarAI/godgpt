using Aevatar.Core.Abstractions;
using Orleans;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Global JWT Provider GAgent - singleton for entire system
/// Manages JWT creation and caching
/// </summary>
public interface IGlobalJwtProviderGAgent : IGAgent
{
    /// <summary>
    /// Get Firebase access token (cached globally for 24 hours)
    /// Thread-safe JWT creation with proper RSA lifecycle management
    /// </summary>
    Task<string?> GetFirebaseAccessTokenAsync();


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
    [Id(0)]
    public bool IsReady { get; set; }
    
    [Id(1)]
    public bool HasCachedToken { get; set; }
    
    [Id(2)]
    public DateTime? TokenExpiry { get; set; }
    
    [Id(3)]
    public int TotalTokenRequests { get; set; }
    
    [Id(4)]
    public DateTime? LastTokenCreation { get; set; }
    
    [Id(5)]
    public string? LastError { get; set; }
}