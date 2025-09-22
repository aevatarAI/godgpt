using Aevatar.Core.Abstractions;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// State for GlobalJwtProviderGAgent
/// Tracks global JWT creation and deduplication statistics
/// </summary>
[GenerateSerializer]
public class GlobalJwtProviderState : StateBase
{
    /// <summary>
    /// Total number of JWT token requests served globally
    /// </summary>
    [Id(0)]
    public int TotalTokenRequests { get; set; }
    
    /// <summary>
    /// Total number of deduplication checks performed
    /// </summary>
    [Id(1)]
    public int TotalDeduplicationChecks { get; set; }
    
    /// <summary>
    /// Number of duplicate pushes prevented
    /// </summary>
    [Id(2)]
    public int PreventedDuplicates { get; set; }
    
    /// <summary>
    /// Number of successful JWT token creations
    /// </summary>
    [Id(3)]
    public int SuccessfulTokenCreations { get; set; }
    
    /// <summary>
    /// Last successful JWT token creation time
    /// </summary>
    [Id(4)]
    public DateTime? LastTokenCreation { get; set; }
    
    /// <summary>
    /// Last cleanup operation time
    /// </summary>
    [Id(5)]
    public DateTime? LastCleanup { get; set; }
    
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
    /// Global deduplication tracking - stores device/token keys and their claim dates
    /// Key format: "device:{deviceId}:{date}:{hour}:{timezone}" or "token:{token}:{date}:{hour}"
    /// Value: DateOnly when the key was claimed
    /// CRITICAL: Must be in Orleans State for cross-silo consistency in clusters!
    /// </summary>
    [Id(8)]
    public Dictionary<string, DateOnly> LastPushDates { get; set; } = new();
    
    /// <summary>
    /// Increment token request counter
    /// </summary>
    public void IncrementTokenRequests()
    {
        TotalTokenRequests++;
    }
    
    /// <summary>
    /// Increment deduplication check counter
    /// </summary>
    public void IncrementDeduplicationChecks()
    {
        TotalDeduplicationChecks++;
    }
    
    /// <summary>
    /// Increment prevented duplicates counter
    /// </summary>
    public void IncrementPreventedDuplicates()
    {
        PreventedDuplicates++;
    }
    
    /// <summary>
    /// Record successful token creation
    /// </summary>
    public void RecordSuccessfulTokenCreation()
    {
        SuccessfulTokenCreations++;
        LastTokenCreation = DateTime.UtcNow;
        LastError = null; // Clear error on success
    }
    
    /// <summary>
    /// Record cleanup operation
    /// </summary>
    public void RecordCleanup()
    {
        LastCleanup = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Record error
    /// </summary>
    public void RecordError(string error)
    {
        LastError = error;
    }
}
