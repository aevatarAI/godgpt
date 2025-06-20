using Aevatar.Application.Grains.MineAI.Events;
using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.MineAI;

/// <summary>
/// State for MineAI awakening score calculation
/// </summary>
[GenerateSerializer]
public class MineAIAwakeningState : StateBase
{
    [Id(0)] public List<Guid> SessionIds { get; set; } = new List<Guid>();
    
    /// <summary>
    /// The last calculation event
    /// </summary>
    [Id(1)] public MineAIAwakeningEventLog LastCalculation { get; set; }

    /// <summary>
    /// Total number of requests processed
    /// </summary>
    [Id(2)] public int TotalRequests { get; set; }

    /// <summary>
    /// Average score across all calculations
    /// </summary>
    [Id(3)] public double AverageScore { get; set; }

    /// <summary>
    /// Timestamp of the last request
    /// </summary>
    [Id(4)] public DateTime LastRequestTime { get; set; }
}