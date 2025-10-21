using Aevatar.Core;
using Orleans;

namespace Aevatar.Application.Grains.Fortune;

[GenerateSerializer]
public class MethodStats
{
    [Id(0)] public int TotalRating { get; set; }
    [Id(1)] public int Count { get; set; }
    [Id(2)] public double AvgRating { get; set; }
}

[GenerateSerializer]
public class FortuneStatsSnapshotState : StateBase
{
    [Id(0)] public Dictionary<string, MethodStats> GlobalStats { get; set; } = new();
    [Id(1)] public Dictionary<string, Dictionary<string, MethodStats>> UserStats { get; set; } = new();
    [Id(2)] public DateTime LastSnapshotAt { get; set; }
}

