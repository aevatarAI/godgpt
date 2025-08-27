using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Agents.UserStatistics.SEvents;

/// <summary>
/// Base event log for User Statistics GAgent
/// </summary>
[GenerateSerializer]
public class UserStatisticsEventLog : StateLogEventBase<UserStatisticsEventLog>
{
}
