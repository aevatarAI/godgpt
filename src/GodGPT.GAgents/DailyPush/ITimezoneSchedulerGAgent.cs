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
}
