using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Agents.UserStatistics.SEvents;

/// <summary>
/// Event log for initializing user statistics
/// </summary>
[GenerateSerializer]
public class InitializeUserStatsEventLog : UserStatisticsEventLog
{
    [Id(0)] public Guid UserId { get; set; }
}
