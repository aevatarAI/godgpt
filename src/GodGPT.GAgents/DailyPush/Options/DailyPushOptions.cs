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
    
    /// <summary>
    /// Global push notification switch
    /// When false, all push notifications are disabled regardless of other settings
    /// Default: false (disabled)
    /// </summary>
    public bool PushEnabled { get; set; } = false;
    
    /// <summary>
    /// Device registration and read status switch
    /// When false, device registration and read status APIs return success with mock data
    /// but skip actual business logic processing
    /// Default: false (disabled)
    /// </summary>
    public bool DeviceRegistrationEnabled { get; set; } = false;
    
    /// <summary>
    /// File paths configuration
    /// </summary>
    public FilePathsOptions FilePaths { get; set; } = new();
}

/// <summary>
/// File paths configuration for Daily Push system
/// </summary>
public class FilePathsOptions
{
    /// <summary>
    /// Local file path for CSV dictionary file containing push content
    /// </summary>
    public string CsvDictionaryPath { get; set; } = "/app/bright-mission/dailyPush.csv";
    
    /// <summary>
    /// Local file path to Firebase service account key JSON file
    /// </summary>
    public string FirebaseKeyPath { get; set; } = "/app/firebase/godgpt-test-firebase-adminsdk.json";
}
