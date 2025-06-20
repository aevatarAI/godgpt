using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.MineAI.Events;

/// <summary>
/// Event log for MineAI awakening score calculation
/// </summary>
[GenerateSerializer]
public class MineAIAwakeningEventLog : StateLogEventBase<MineAIAwakeningEventLog>
{
}

public class MineAIAwakeningUpdateSessionidsEventLog : MineAIAwakeningEventLog
{
    [Id(0)] public List<Guid> SessionIds { get; set; }
}