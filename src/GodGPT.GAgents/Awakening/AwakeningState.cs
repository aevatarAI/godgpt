using Aevatar.Core.Abstractions;
using GodGPT.GAgents.SpeechChat;
using GodGPT.GAgents.Awakening.Dtos;

namespace GodGPT.GAgents.Awakening;

/// <summary>
/// State class for awakening system
/// </summary>
[GenerateSerializer]
public class AwakeningState : StateBase
{
    [Id(0)] public long LastGeneratedTimestamp { get; set; } // Timestamp in seconds
    [Id(1)] public int AwakeningLevel { get; set; } // Awakening level 1-10
    [Id(2)] public string AwakeningMessage { get; set; } = string.Empty; // Awakening message
    [Id(3)] public VoiceLanguageEnum Language { get; set; } = VoiceLanguageEnum.Unset; // Language type
    [Id(4)] public string SessionId { get; set; } = string.Empty; // Based session ID
    [Id(5)] public DateTime CreatedAt { get; set; } // Creation time
    [Id(6)] public int GenerationAttempts { get; set; } = 0; // Generation attempt count
    [Id(7)] public AwakeningStatus Status { get; set; } = AwakeningStatus.NotStarted; // Generation status
}
