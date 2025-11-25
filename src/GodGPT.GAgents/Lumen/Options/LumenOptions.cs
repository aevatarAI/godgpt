using System;
using System.Collections.Generic;

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
    /// Enable daily prediction auto-generation via scheduled reminders
    /// When disabled, daily predictions will only be generated on-demand
    /// Default: false (disabled)
    /// </summary>
    public bool EnableDailyAutoGeneration { get; set; } = false;
    
    /// <summary>
    /// Feature flags dictionary for frontend configuration
    /// Can be used to enable/disable features dynamically without code changes
    /// Example: { "showLifetimePrediction": "true", "maxFavorites": "10" }
    /// Default: empty dictionary
    /// </summary>
    public Dictionary<string, string> FeatureFlags { get; set; } = new Dictionary<string, string>();
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

