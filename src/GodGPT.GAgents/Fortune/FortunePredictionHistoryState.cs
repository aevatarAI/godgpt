using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Fortune;

/// <summary>
/// Prediction history record (lightweight)
/// </summary>
[GenerateSerializer]
public class PredictionHistoryRecord
{
    [Id(0)] public Guid PredictionId { get; set; }
    [Id(1)] public DateOnly PredictionDate { get; set; }
    [Id(2)] public int Energy { get; set; }
    [Id(3)] public DateTime CreatedAt { get; set; }
    
    // Complete results stored as JSON for each record
    [Id(4)] public Dictionary<string, Dictionary<string, string>> Results { get; set; } = new();
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

