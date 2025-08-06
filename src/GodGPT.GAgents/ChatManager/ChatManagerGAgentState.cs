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
    
    /// <summary>
    /// Marks whether this is the first access to ChatManagerGAgent (Note: not the first conversation)
    /// This field is a repurposed field, its actual purpose is to mark whether the user is accessing ChatManagerGAgent for the first time
    /// null: Not initialized (need to determine if new user or historical user through Version)
    /// true: First access (new user)
    /// false: Not first access (historical user)
    /// </summary>
    [Id(9)] public bool? IsFirstConversation { get; set; }
    
    /// <summary>
    /// User registration time (UTC time)
    /// For new users: Set to current time on first access
    /// For historical users: May be null (indicating historical data did not set this field)
    /// </summary>
    [Id(10)] public DateTime? RegisteredAtUtc { get; set; }
    
    [Id(11)] public Guid? InviterId { get; set; }
    [Id(12)] public VoiceLanguageEnum VoiceLanguage { get; set; } = VoiceLanguageEnum.Unset;

    public SessionInfo? GetSession(Guid sessionId)
    {
        return SessionInfoList.FirstOrDefault(f=>f.SessionId == sessionId);
    }
}