using Aevatar.Application.Grains.Lumen.Dtos;
using Aevatar.Application.Grains.Lumen.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Lumen;

/// <summary>
/// Interface for Lumen Stats Snapshot GAgent - stores periodic snapshots from Redis
/// </summary>
public interface ILumenStatsSnapshotGAgent : IGAgent
{
    /// <summary>
    /// Save snapshot of all stats
    /// </summary>
    Task SnapshotAsync(Dictionary<string, MethodStats> globalStats, Dictionary<string, Dictionary<string, MethodStats>> userStats);
    
    [ReadOnly]
    Task<LumenStatsSnapshotState> GetSnapshotAsync();
}

[GAgent(nameof(LumenStatsSnapshotGAgent))]
[Reentrant]
public class LumenStatsSnapshotGAgent : GAgentBase<LumenStatsSnapshotState, LumenStatsSnapshotEventLog>,
    ILumenStatsSnapshotGAgent
{
    private readonly ILogger<LumenStatsSnapshotGAgent> _logger;

    public LumenStatsSnapshotGAgent(ILogger<LumenStatsSnapshotGAgent> logger)
    {
        _logger = logger;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Lumen stats snapshot management");
    }

    /// <summary>
    /// Event-driven state transition handler
    /// </summary>
    protected sealed override void GAgentTransitionState(LumenStatsSnapshotState state,
        StateLogEventBase<LumenStatsSnapshotEventLog> @event)
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
            _logger.LogInformation("[LumenStatsSnapshotGAgent][SnapshotAsync] Creating snapshot with {GlobalCount} global methods, {UserCount} users",
                globalStats.Count, userStats.Count);

            RaiseEvent(new StatsSnapshotEvent
            {
                GlobalStats = globalStats,
                UserStats = userStats,
                SnapshotAt = DateTime.UtcNow
            });

            await ConfirmEvents();

            _logger.LogInformation("[LumenStatsSnapshotGAgent][SnapshotAsync] Snapshot completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenStatsSnapshotGAgent][SnapshotAsync] Error creating snapshot");
            throw;
        }
    }

    public Task<LumenStatsSnapshotState> GetSnapshotAsync()
    {
        return Task.FromResult(State);
    }
}

