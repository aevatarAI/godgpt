using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AIGAgent.State;

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
    [Id(9)] public Dictionary<Guid, Dictionary<string, Guid>> SessionSpeechAgents { get; set; } = new Dictionary<Guid, Dictionary<string, Guid>>();

    public SessionInfo? GetSession(Guid sessionId)
    {
        return SessionInfoList.FirstOrDefault(f=>f.SessionId == sessionId);
    }
    
    public Guid? GetSessionSpeechGAgentId(Guid sessionId, string language)
    {
        if (SessionSpeechAgents == null)
        {
            SessionSpeechAgents = new Dictionary<Guid, Dictionary<string, Guid>>();
            return null;
        }
        
        if (SessionSpeechAgents.TryGetValue(sessionId, out var languageMap))
        {
            if (languageMap != null && languageMap.TryGetValue(language, out var speechAgentId))
            {
                return speechAgentId;
            }
        }
        return null;
    }
    
    public void SetSessionSpeechGAgentId(Guid sessionId, string language, Guid speechAgentId)
    {
        if (SessionSpeechAgents == null)
        {
            SessionSpeechAgents = new Dictionary<Guid, Dictionary<string, Guid>>();
        }
        
        if (!SessionSpeechAgents.TryGetValue(sessionId, out var languageMap))
        {
            languageMap = new Dictionary<string, Guid>();
            SessionSpeechAgents[sessionId] = languageMap;
        }
        else if (languageMap == null)
        {
            languageMap = new Dictionary<string, Guid>();
            SessionSpeechAgents[sessionId] = languageMap;
        }
        
        languageMap[language] = speechAgentId;
    }
}