using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Lumen;

/// <summary>
/// Prediction history record (lightweight)
/// </summary>
[GenerateSerializer]
public class PredictionHistoryRecord
{
    [Id(0)] public Guid PredictionId { get; set; }
    [Id(1)] public DateOnly PredictionDate { get; set; }
    [Id(2)] public DateTime CreatedAt { get; set; }
    
    // Flat results structure (unified format)
    [Id(3)] public Dictionary<string, string> Results { get; set; } = new();
    
    // Prediction type (Daily/Yearly/Lifetime)
    [Id(4)] public PredictionType Type { get; set; }
}

/// <summary>
/// Fortune prediction history state - stores recent N days of predictions
/// </summary>
[GenerateSerializer]
public class FortunePredictionHistoryState : StateBase
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    
    // Store recent predictions (max 30 days)
    [Id(1)] public List<PredictionHistoryRecord> RecentPredictions { get; set; } = new();
    
    [Id(2)] public DateTime LastUpdatedAt { get; set; }
}

