using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Lumen.SEvents;

/// <summary>
/// Event log for Lumen daily yearly history
/// </summary>
[GenerateSerializer]
public class LumenDailyYearlyHistoryEventLog : StateLogEventBase<LumenDailyYearlyHistoryEventLog>
{
}

/// <summary>
/// Event: Daily prediction added to yearly history
/// </summary>
[GenerateSerializer]
public class DailyPredictionAddedEvent : LumenDailyYearlyHistoryEventLog
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public int Year { get; set; }
    [Id(2)] public Guid PredictionId { get; set; }
    [Id(3)] public DateOnly Date { get; set; }
    [Id(4)] public DateTime CreatedAt { get; set; }
    [Id(5)] public Dictionary<string, Dictionary<string, string>> MultilingualResults { get; set; } = new();
    [Id(6)] public List<string> AvailableLanguages { get; set; } = new();
    [Id(7)] public DateTime AddedAt { get; set; }
}

