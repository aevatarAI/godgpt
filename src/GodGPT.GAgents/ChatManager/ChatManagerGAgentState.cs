using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AIGAgent.State;
using GodGPT.GAgents.SpeechChat;

namespace Aevatar.Application.Grains.Agents.ChatManager;

[GenerateSerializer]
public class ChatManagerGAgentState : AIGAgentStateBase
{
    [Id(0)] public List<SessionInfo> SessionInfoList { get; set; } = new List<SessionInfo>();
    [Id(1)] public Guid UserId { get; set; }
    [Id(2)] public int MaxSession { get; set; }
    [Id(3)] public string Gender { get; set; }
    [Id(4)] public DateTime BirthDate { get; set; }
    [Id(5)] public string BirthPlace { get; set; }
    [Id(6)] public string FullName { get; set; }
    [Id(7)] public int MaxShareCount { get; set; }
    [Id(8)] public int CurrentShareCount { get; set; } = 0;
    [Id(9)] public bool? IsFirstConversation { get; set; }
    [Id(10)] public DateTime? RegisteredAtUtc { get; set; }
    [Id(11)] public Guid? InviterId { get; set; }
    [Id(12)] public VoiceLanguageEnum VoiceLanguage { get; set; } = VoiceLanguageEnum.Unset;

    public SessionInfo? GetSession(Guid sessionId)
    {
        return SessionInfoList.FirstOrDefault(f=>f.SessionId == sessionId);
    }
}