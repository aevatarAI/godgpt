using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AIGAgent.State;
using GodGPT.GAgents.SpeechChat;
using GodGPT.GAgents.DailyPush;

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
    
    // === Daily Push Notification Fields ===
    /// <summary>
    /// User devices for daily push notifications (key: deviceId)
    /// </summary>
    [Id(13)] public Dictionary<string, UserDeviceInfo> UserDevices { get; set; } = new();

    /// <summary>
    /// Mapping from pushToken to deviceId for efficient lookup when token changes
    /// </summary>
    [Id(14)] public Dictionary<string, string> TokenToDeviceMap { get; set; } = new();

    /// <summary>
    /// Daily push read status for current user (key: yyyy-MM-dd)
    /// </summary>
    [Id(15)] public Dictionary<string, bool> DailyPushReadStatus { get; set; } = new();

    public SessionInfo? GetSession(Guid sessionId)
    {
        return SessionInfoList.FirstOrDefault(f=>f.SessionId == sessionId);
    }
}