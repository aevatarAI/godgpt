using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Fortune;

/// <summary>
/// Fortune prediction state data
/// </summary>
[GenerateSerializer]
public class FortunePredictionState : StateBase
{
    [Id(0)] public Guid PredictionId { get; set; }
    [Id(1)] public string UserId { get; set; } = string.Empty;
    [Id(2)] public DateOnly PredictionDate { get; set; }
    [Id(3)] public Dictionary<string, Dictionary<string, string>> Results { get; set; } = new();
    [Id(4)] public int Energy { get; set; }
    [Id(5)] public DateTime CreatedAt { get; set; }
    [Id(6)] public Dictionary<string, string> LifetimeForecast { get; set; } = new Dictionary<string, string>();
    [Id(7)] public Dictionary<string, string> WeeklyForecast { get; set; } = new Dictionary<string, string>();
    [Id(8)] public DateTime? WeeklyGeneratedDate { get; set; } // Track when weekly was generated for expiration check
    [Id(9)] public DateTime? ProfileUpdatedAt { get; set; } // Track profile update time to detect when prediction needs regeneration
}

