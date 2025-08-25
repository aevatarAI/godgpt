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
    /// Path to CSV dictionary file for push content
    /// </summary>
    public string CsvDictionaryPath { get; set; } = "/app/dailyPush/dailyPush.csv";
    
    /// <summary>
    /// Path to Firebase service account key JSON file
    /// </summary>
    public string FirebaseKeyPath { get; set; } = "/app/firebase/godgpt-test-66b04-firebase-adminsdk-fbsvc-249aa74f77.json";
    
    /// <summary>
    /// Base directory for file paths (if relative paths are used)
    /// </summary>
    public string BaseDirectory { get; set; } = "";
}
