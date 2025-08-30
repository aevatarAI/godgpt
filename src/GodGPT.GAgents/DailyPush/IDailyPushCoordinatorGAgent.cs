using Aevatar.Core.Abstractions;
// DailyPush types are in same namespace

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Daily push coordinator GAgent
/// </summary>
public interface IDailyPushCoordinatorGAgent : IGAgent, IGrainWithGuidKey
{
    /// <summary>
    /// Initialize scheduler for specific timezone
    /// </summary>
    Task InitializeAsync(string timeZoneId);
    
    /// <summary>
    /// Process morning push for this timezone (8:00 AM local time)
    /// </summary>
    Task ProcessMorningPushAsync(DateTime targetDate, bool isManualTrigger = false);
    
    /// <summary>
    /// Process afternoon retry push for this timezone (3:00 PM local time)
    /// </summary>
    Task ProcessAfternoonRetryAsync(DateTime targetDate, bool isManualTrigger = false);
    
    /// <summary>
    /// Get scheduler status and statistics
    /// </summary>
    Task<DailyPushCoordinatorState> GetStatusAsync();
    
    /// <summary>
    /// Pause/resume scheduler
    /// </summary>
    Task SetStatusAsync(SchedulerStatus status);
    
    /// <summary>
    /// Set reminder target ID for version control
    /// </summary>
    Task SetReminderTargetIdAsync(Guid targetId);
    
    /// <summary>
    /// Force initialize this grain with specific timezone (admin/debugging use)
    /// </summary>
    Task ForceInitializeAsync(string timeZoneId);
    
}
