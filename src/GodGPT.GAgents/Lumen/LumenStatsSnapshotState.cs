using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Orleans;

namespace Aevatar.Application.Grains.Lumen;

[GenerateSerializer]
public class MethodStats
{
    [Id(0)] public int TotalRating { get; set; }
    [Id(1)] public int Count { get; set; }
    [Id(2)] public double AvgRating { get; set; }
    [Id(3)] public int PositiveCount { get; set; } // Count of ratings >= 3 (3-5 stars)
    [Id(4)] public double PositiveRate { get; set; } // PositiveCount / Count
}

[GenerateSerializer]
public class LumenStatsSnapshotState : StateBase
{
    [Id(0)] public Dictionary<string, MethodStats> GlobalStats { get; set; } = new();
    [Id(1)] public Dictionary<string, Dictionary<string, MethodStats>> UserStats { get; set; } = new();
    [Id(2)] public DateTime LastSnapshotAt { get; set; }
}

