using Aevatar.Application.Grains.Lumen.Dtos;
using Aevatar.Application.Grains.Lumen.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Lumen;

/// <summary>
/// Interface for Fortune Stats Snapshot GAgent - stores periodic snapshots from Redis
/// </summary>
public interface IFortuneStatsSnapshotGAgent : IGAgent
{
    /// <summary>
    /// Save snapshot of all stats
    /// </summary>
    Task SnapshotAsync(Dictionary<string, MethodStats> globalStats, Dictionary<string, Dictionary<string, MethodStats>> userStats);
    
    [ReadOnly]
    Task<FortuneStatsSnapshotState> GetSnapshotAsync();
}

[GAgent(nameof(FortuneStatsSnapshotGAgent))]
[Reentrant]
public class FortuneStatsSnapshotGAgent : GAgentBase<FortuneStatsSnapshotState, FortuneStatsSnapshotEventLog>,
    IFortuneStatsSnapshotGAgent
{
    private readonly ILogger<FortuneStatsSnapshotGAgent> _logger;

    public FortuneStatsSnapshotGAgent(ILogger<FortuneStatsSnapshotGAgent> logger)
    {
        _logger = logger;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Fortune stats snapshot management");
    }

    /// <summary>
    /// Event-driven state transition handler
    /// </summary>
    protected sealed override void GAgentTransitionState(FortuneStatsSnapshotState state,
        StateLogEventBase<FortuneStatsSnapshotEventLog> @event)
    {
        switch (@event)
        {
            case StatsSnapshotEvent snapshotEvent:
                state.GlobalStats = snapshotEvent.GlobalStats;
                state.UserStats = snapshotEvent.UserStats;
                state.LastSnapshotAt = snapshotEvent.SnapshotAt;
                break;
        }
    }

    public async Task SnapshotAsync(Dictionary<string, MethodStats> globalStats, Dictionary<string, Dictionary<string, MethodStats>> userStats)
    {
        try
        {
            _logger.LogInformation("[FortuneStatsSnapshotGAgent][SnapshotAsync] Creating snapshot with {GlobalCount} global methods, {UserCount} users",
                globalStats.Count, userStats.Count);

            RaiseEvent(new StatsSnapshotEvent
            {
                GlobalStats = globalStats,
                UserStats = userStats,
                SnapshotAt = DateTime.UtcNow
            });

            await ConfirmEvents();

            _logger.LogInformation("[FortuneStatsSnapshotGAgent][SnapshotAsync] Snapshot completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortuneStatsSnapshotGAgent][SnapshotAsync] Error creating snapshot");
            throw;
        }
    }

    public Task<FortuneStatsSnapshotState> GetSnapshotAsync()
    {
        return Task.FromResult(State);
    }
}

