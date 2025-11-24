using System;

namespace Aevatar.Application.Grains.Lumen.Options;

/// <summary>
/// Lumen prediction configuration options
/// </summary>
public class LumenPredictionOptions
{
    /// <summary>
    /// Current prompt version for prediction generation
    /// When this version changes, all predictions will be regenerated on next access
    /// Default: 28
    /// </summary>
    public int PromptVersion { get; set; } = 28;
    
    /// <summary>
    /// Reminder target ID for daily prediction auto-generation
    /// Change this GUID to invalidate all existing reminders (e.g., when switching from UTC to user timezone)
    /// Default: 00000000-0000-0000-0000-000000000001
    /// </summary>
    public Guid ReminderTargetId { get; set; } = new Guid("00000000-0000-0000-0000-000000000001");
    
    /// <summary>
    /// Maximum retry count for prediction generation failures
    /// Default: 3
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;
    
    /// <summary>
    /// Generation timeout threshold in minutes
    /// If generation takes longer than this, it's considered timed out
    /// Default: 5 minutes
    /// </summary>
    public int GenerationTimeoutMinutes { get; set; } = 5;
    
    /// <summary>
    /// Total number of supported languages
    /// Used to determine if all languages have been generated
    /// Default: 4 (en, zh, zh-tw, es)
    /// </summary>
    public int TotalLanguageCount { get; set; } = 4;
    
    /// <summary>
    /// Daily reminder interval
    /// Default: 24 hours
    /// </summary>
    public TimeSpan DailyReminderInterval { get; set; } = TimeSpan.FromHours(24);
    
    /// <summary>
    /// Daily reminder name
    /// Default: "LumenDailyPredictionReminder"
    /// </summary>
    public string DailyReminderName { get; set; } = "LumenDailyPredictionReminder";
}

/// <summary>
/// Lumen user profile configuration options
/// </summary>
public class LumenUserProfileOptions
{
    /// <summary>
    /// Maximum number of profile updates allowed per week
    /// Default: 100 (for testing)
    /// </summary>
    public int MaxProfileUpdatesPerWeek { get; set; } = 100;
    
    /// <summary>
    /// Valid lumen prediction actions
    /// </summary>
    public string[] ValidActions { get; set; } = new[]
    {
        "forecast", "horoscope", "bazi", "ziwei", "constellation",
        "numerology", "synastry", "chineseZodiac", "mayanTotem",
        "humanFigure", "tarot", "zhengYu"
    };
}

/// <summary>
/// Lumen prediction history configuration options
/// </summary>
public class LumenPredictionHistoryOptions
{
    /// <summary>
    /// Maximum number of days to keep in prediction history
    /// Default: 30 days
    /// </summary>
    public int MaxHistoryDays { get; set; } = 30;
}

/// <summary>
/// Lumen favourite configuration options
/// </summary>
public class LumenFavouriteOptions
{
    /// <summary>
    /// Maximum number of favourites per user
    /// Default: 100
    /// </summary>
    public int MaxFavourites { get; set; } = 100;
}

/// <summary>
/// Solar term data configuration options
/// </summary>
public class LumenSolarTermOptions
{
    /// <summary>
    /// File path to solar term data JSON file
    /// Default: /app/lumen/solar-terms-full.json
    /// </summary>
    public string DataFilePath { get; set; } = "/app/lumen/solar-terms-full.json";
}

