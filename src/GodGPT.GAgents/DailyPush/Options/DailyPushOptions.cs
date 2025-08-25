using System;

namespace GodGPT.GAgents.DailyPush.Options;

/// <summary>
/// Daily Push configuration options
/// </summary>
public class DailyPushOptions
{
    /// <summary>
    /// Morning push time (local timezone)
    /// </summary>
    public TimeSpan MorningTime { get; set; } = new TimeSpan(8, 0, 0);
    
    /// <summary>
    /// Afternoon retry push time (local timezone)
    /// </summary>
    public TimeSpan AfternoonRetryTime { get; set; } = new TimeSpan(15, 0, 0);
    
    /// <summary>
    /// Reminder target ID for version control
    /// All timezone schedulers will use this ID for reminder execution control
    /// </summary>
    public Guid ReminderTargetId { get; set; } = Guid.Empty;
}
