using Aevatar.Core.Abstractions;
// DailyPush types are in same namespace

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Timezone-specific push scheduler GAgent
/// </summary>
public interface ITimezoneSchedulerGAgent : IGAgent, IGrainWithStringKey
{
    /// <summary>
    /// Initialize scheduler for specific timezone
    /// </summary>
    Task InitializeAsync(string timeZoneId);
    
    /// <summary>
    /// Process morning push for this timezone (8:00 AM local time)
    /// </summary>
    Task ProcessMorningPushAsync(DateTime targetDate);
    
    /// <summary>
    /// Process afternoon retry push for this timezone (3:00 PM local time)
    /// </summary>
    Task ProcessAfternoonRetryAsync(DateTime targetDate);
    
    /// <summary>
    /// Get scheduler status and statistics
    /// </summary>
    Task<TimezoneSchedulerGAgentState> GetStatusAsync();
    
    /// <summary>
    /// Pause/resume scheduler
    /// </summary>
    Task SetStatusAsync(SchedulerStatus status);
    
    /// <summary>
    /// Set reminder target ID for version control
    /// </summary>
    Task SetReminderTargetIdAsync(Guid targetId);
    
    /// <summary>
    /// Start test mode with rapid push testing - TODO: Remove before production
    /// </summary>
    Task StartTestModeAsync();
    
    /// <summary>
    /// Stop test mode and cleanup test reminders - TODO: Remove before production
    /// </summary>
    Task StopTestModeAsync();
    
    /// <summary>
    /// Get test mode status - TODO: Remove before production
    /// </summary>
    Task<(bool IsActive, DateTime StartTime, int RoundsCompleted, int MaxRounds)> GetTestStatusAsync();
}
