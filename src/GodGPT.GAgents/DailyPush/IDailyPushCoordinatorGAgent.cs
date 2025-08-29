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
    Task ProcessMorningPushAsync(DateTime targetDate);
    
    /// <summary>
    /// Process afternoon retry push for this timezone (3:00 PM local time)
    /// </summary>
    Task ProcessAfternoonRetryAsync(DateTime targetDate);
    
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
    /// Start test mode with rapid push testing - TODO: Remove before production
    /// </summary>
    /// <param name="intervalSeconds">Push interval in seconds (default: 600 seconds = 10 minutes)</param>
    Task StartTestModeAsync(int intervalSeconds = 600);
    
    /// <summary>
    /// Stop test mode and cleanup test reminders - TODO: Remove before production
    /// </summary>
    Task StopTestModeAsync();
    
    /// <summary>
    /// Get test mode status - TODO: Remove before production
    /// </summary>
    Task<(bool IsActive, DateTime StartTime, int RoundsCompleted, int MaxRounds)> GetTestStatusAsync();
    
    /// <summary>
    /// Get all devices registered in this timezone with detailed information - TODO: Remove before production
    /// </summary>
    Task<List<TimezoneDeviceInfo>> GetDevicesInTimezoneAsync();
    
    /// <summary>
    /// Send instant push notification to all devices in this timezone
    /// Each device will receive two identical notifications
    /// </summary>
    Task<InstantPushResult> SendInstantPushAsync();
    
    /// <summary>
    /// Diagnose and attempt to fix orphaned grain issues
    /// Returns diagnostic information and whether a fix was attempted
    /// </summary>
    Task<(bool IsOrphaned, string DiagnosticInfo, bool FixAttempted)> DiagnoseAndFixOrphanedGrainAsync();
}
