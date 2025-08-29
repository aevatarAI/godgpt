using Aevatar.Core.Abstractions;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// State for daily push coordinator
/// </summary>
[GenerateSerializer]
public class DailyPushCoordinatorState : StateBase
{
    /// <summary>
    /// Timezone ID (e.g., "Asia/Shanghai", "America/New_York")
    /// </summary>
    [Id(0)] public string TimeZoneId { get; set; } = "";
    
    /// <summary>
    /// Last successful morning push date
    /// </summary>
    [Id(1)] public DateTime? LastMorningPush { get; set; }
    
    /// <summary>
    /// Last successful afternoon retry date  
    /// </summary>
    [Id(2)] public DateTime? LastAfternoonRetry { get; set; }
    
    /// <summary>
    /// Number of users processed in last morning push
    /// </summary>
    [Id(3)] public int LastMorningUserCount { get; set; }
    
    /// <summary>
    /// Number of users that needed afternoon retry
    /// </summary>
    [Id(4)] public int LastAfternoonRetryCount { get; set; }
    
    /// <summary>
    /// Total push failures in last execution
    /// </summary>
    [Id(5)] public int LastExecutionFailures { get; set; }
    
    /// <summary>
    /// Scheduler status
    /// </summary>
    [Id(6)] public SchedulerStatus Status { get; set; } = SchedulerStatus.Active;
    
    /// <summary>
    /// Last status update timestamp
    /// </summary>
    [Id(7)] public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Reminder target ID for version control
    /// </summary>
    [Id(8)] public Guid ReminderTargetId { get; set; } = Guid.Empty;
    
    /// <summary>
    /// Test mode active flag
    /// </summary>
    [Id(9)] public bool TestModeActive { get; set; } = false;
    
    /// <summary>
    /// Test mode start time
    /// </summary>
    [Id(10)] public DateTime TestStartTime { get; set; } = DateTime.MinValue;
    
    /// <summary>
    /// Number of test rounds completed
    /// </summary>
    [Id(11)] public int TestRoundsCompleted { get; set; } = 0;
    
    /// <summary>
    /// Custom test interval in seconds (for configurable test mode)
    /// </summary>
    [Id(12)] public int TestCustomInterval { get; set; } = 600;
    
    /// <summary>
    /// Last known morning time from configuration (for change detection)
    /// </summary>
    [Id(13)] public TimeSpan? LastKnownMorningTime { get; set; }
    
    /// <summary>
    /// Last known afternoon time from configuration (for change detection)
    /// </summary>
    [Id(14)] public TimeSpan? LastKnownAfternoonTime { get; set; }
}

/// <summary>
/// Scheduler status enumeration
/// </summary>
public enum SchedulerStatus
{
    Active = 1,
    Paused = 2,
    Error = 3,
    Maintenance = 4
}
