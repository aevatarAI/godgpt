using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Lumen;

/// <summary>
/// Lumen prediction state data (unified for daily/yearly/lifetime predictions)
/// </summary>
[GenerateSerializer]
public class LumenPredictionState : StateBase
{
    // Metadata
    [Id(0)] public Guid PredictionId { get; set; }
    [Id(1)] public string UserId { get; set; } = string.Empty;
    [Id(2)] public DateOnly PredictionDate { get; set; }
    [Id(3)] public DateTime CreatedAt { get; set; }
    [Id(4)] public DateTime? ProfileUpdatedAt { get; set; } // Track profile update time for prediction regeneration
    
    // Unified prediction results (flattened key-value pairs)
    [Id(5)] public Dictionary<string, string> Results { get; set; } = new();
    
    // Multilingual cache (language -> flattened results)
    [Id(6)] public Dictionary<string, Dictionary<string, string>> MultilingualResults { get; set; } = new();
    
    // Language generation status
    [Id(7)] public List<string> GeneratedLanguages { get; set; } = new();
    
    // Idempotency protection - prevent concurrent generation
    [Id(8)] public Dictionary<PredictionType, GenerationLockInfo> GenerationLocks { get; set; } = new();
    
    // Prediction type
    [Id(9)] public PredictionType Type { get; set; }
    
    // Track on-demand translations in progress (to avoid duplicate triggers)
    [Id(10)] public HashSet<string> TranslationInProgress { get; set; } = new();
    
    // Daily reminder management (for auto-generation at UTC 00:00)
    [Id(11)] public DateTime LastActiveDate { get; set; } // Track user activity for auto-reminder management
    [Id(12)] public Guid DailyReminderTargetId { get; set; } // Used to manually enable/disable daily reminders
    [Id(13)] public bool IsDailyReminderEnabled { get; set; } // Whether daily auto-generation is enabled
    [Id(14)] public DateOnly? LastGeneratedDate { get; set; } // Track the last date when prediction was generated (for deduplication)
}

/// <summary>
/// Generation lock information for idempotency protection
/// </summary>
[GenerateSerializer]
public class GenerationLockInfo
{
    [Id(0)] public bool IsGenerating { get; set; }
    [Id(1)] public DateTime? StartedAt { get; set; }
}
