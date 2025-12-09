using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Lumen;

/// <summary>
/// Daily prediction record for yearly history
/// Compact structure optimized for storage
/// </summary>
[GenerateSerializer]
public class DailyPredictionRecord
{
    [Id(0)] public Guid PredictionId { get; set; }
    [Id(1)] public DateOnly Date { get; set; }
    [Id(2)] public DateTime CreatedAt { get; set; }
    
    // Multilingual results (language -> flattened key-value pairs)
    // Stores all 4 languages: en, zh, zh-tw, es
    [Id(3)] public Dictionary<string, Dictionary<string, string>> MultilingualResults { get; set; } = new();
    
    // Available languages for this prediction
    [Id(4)] public List<string> AvailableLanguages { get; set; } = new();
}

/// <summary>
/// Lumen daily prediction yearly history state
/// Stores all daily predictions for a specific year
/// GrainId format: {UserId}-{YYYY} (e.g., "user123-2025")
/// </summary>
[GenerateSerializer]
public class LumenDailyYearlyHistoryState : StateBase
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    
    [Id(1)] public int Year { get; set; }
    
    // All daily predictions for this year (keyed by date for fast lookup)
    // Max 366 entries (leap year)
    [Id(2)] public Dictionary<DateOnly, DailyPredictionRecord> Predictions { get; set; } = new();
    
    [Id(3)] public DateTime LastUpdatedAt { get; set; }
}

