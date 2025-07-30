using Aevatar.Core.Abstractions;
using GodGPT.GAgents.SpeechChat;
using GodGPT.GAgents.Awakening.Dtos;

namespace GodGPT.GAgents.Awakening.SEvents;

/// <summary>
/// Base awakening log event
/// </summary>
[GenerateSerializer]
public class AwakeningLogEvent : StateLogEventBase<AwakeningLogEvent>
{
}

/// <summary>
/// Event for successful awakening generation
/// </summary>
[GenerateSerializer]
public class GenerateAwakeningLogEvent : AwakeningLogEvent
{
    [Id(0)] public long Timestamp { get; set; }
    [Id(1)] public int AwakeningLevel { get; set; }
    [Id(2)] public string AwakeningMessage { get; set; } = string.Empty;
    [Id(3)] public VoiceLanguageEnum Language { get; set; }
    [Id(4)] public string SessionId { get; set; } = string.Empty;
    [Id(5)] public bool IsSuccess { get; set; }
    [Id(6)] public int AttemptCount { get; set; }
}



/// <summary>
/// Event for locking generation timestamp to prevent concurrent generation
/// </summary>
[GenerateSerializer]
public class LockGenerationTimestampLogEvent : AwakeningLogEvent
{
    [Id(0)] public long Timestamp { get; set; }
}

/// <summary>
/// Event for updating awakening status
/// </summary>
[GenerateSerializer]
public class UpdateAwakeningStatusLogEvent : AwakeningLogEvent
{
    [Id(0)] public AwakeningStatus Status { get; set; }
}

/// <summary>
/// Event for resetting awakening content for new day
/// </summary>
[GenerateSerializer]
public class ResetAwakeningContentLogEvent : AwakeningLogEvent
{
    [Id(0)] public long Timestamp { get; set; }
    [Id(1)] public VoiceLanguageEnum Language { get; set; }
}
