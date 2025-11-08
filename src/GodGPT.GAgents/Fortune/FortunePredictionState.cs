using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Fortune;

/// <summary>
/// Fortune prediction state data (Legacy - deprecated, use LumenPredictionState instead)
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
    [Id(8)] public DateTime? WeeklyGeneratedDate { get; set; }
    [Id(9)] public DateTime? ProfileUpdatedAt { get; set; }
    [Id(10)] public Dictionary<string, Dictionary<string, Dictionary<string, string>>>? MultilingualResults { get; set; }
    [Id(11)] public Dictionary<string, Dictionary<string, string>>? MultilingualLifetime { get; set; }
    [Id(12)] public Dictionary<string, Dictionary<string, string>>? MultilingualWeekly { get; set; }
}

/// <summary>
/// Lumen prediction state data (New version with daily/yearly/lifetime support)
/// </summary>
[GenerateSerializer]
public class LumenPredictionState : StateBase
{
    [Id(0)] public Guid PredictionId { get; set; }
    [Id(1)] public string UserId { get; set; } = string.Empty;
    [Id(2)] public DateOnly PredictionDate { get; set; }
    [Id(3)] public Dictionary<string, Dictionary<string, string>> Results { get; set; } = new(); // Daily results
    [Id(4)] public DateTime CreatedAt { get; set; }
    [Id(5)] public Dictionary<string, string> LifetimeForecast { get; set; } = new Dictionary<string, string>(); // Lifetime prediction (never expires unless profile changes)
    [Id(6)] public DateTime? ProfileUpdatedAt { get; set; } // Track profile update time to detect when prediction needs regeneration
    
    // Multilingual support - cache all language versions (en, zh-tw, zh, es) to avoid regeneration on language switch
    [Id(7)] public Dictionary<string, Dictionary<string, Dictionary<string, string>>>? MultilingualResults { get; set; } // Daily multilingual
    [Id(8)] public Dictionary<string, Dictionary<string, string>>? MultilingualLifetime { get; set; } // Lifetime multilingual
    
    // Yearly prediction (expires after 1 year)
    [Id(9)] public Dictionary<string, string> YearlyForecast { get; set; } = new Dictionary<string, string>(); // Yearly prediction
    [Id(10)] public DateTime? YearlyGeneratedDate { get; set; } // Track when yearly was generated for expiration check
    [Id(11)] public Dictionary<string, Dictionary<string, string>>? MultilingualYearly { get; set; } // Yearly multilingual
}

