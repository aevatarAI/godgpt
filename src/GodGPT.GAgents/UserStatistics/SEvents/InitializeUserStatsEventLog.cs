namespace Aevatar.Application.Grains.UserStatistics.SEvents;

/// <summary>
/// Event log for initializing user statistics
/// </summary>
[GenerateSerializer]
public class InitializeUserStatsEventLog : UserStatisticsEventLog
{
    [Id(0)] public Guid UserId { get; set; }
}
