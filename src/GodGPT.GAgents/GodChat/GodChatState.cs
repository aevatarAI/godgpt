using Aevatar.GAgents.ChatAgent.GAgent.State;

namespace Aevatar.Application.Grains.Agents.ChatManager.Chat;

[GenerateSerializer]
public class GodChatState:ChatGAgentState
{
    [Id(0)] public UserProfile? UserProfile { get; set; }
    [Id(1)] public string? Title { get; set; }
    [Id(2)] public Guid ChatManagerGuid { get; set; }
    [Obsolete("This class is deprecated and no longer in use.")]
    [Id(3)] public List<Guid> AIAgentIds { get; set; } = new List<Guid>();
    [Id(4)] public Dictionary<string, List<Guid>> RegionProxies = new ();
    [Id(5)] public DateTime? FirstChatTime { get; set; }
    [Id(6)] public DateTime? LastChatTime { get; set; }
    
    /// <summary>
    /// Metadata for chat messages including voice-related information
    /// Parallel list to ChatHistory from base class
    /// </summary>
    [Id(7)] public List<ChatMessageMeta> ChatMessageMetas { get; set; } = new List<Aevatar.Application.Grains.Agents.ChatManager.ChatMessageMeta>();
    
    /// <summary>
    /// Temporary storage for accumulated content during suggestion filtering
    /// </summary>
    [Id(8)] public string AccumulatedSuggestionContent { get; set; } = "";
}

[GenerateSerializer]
public class UserProfile
{
    [Id(0)] public string Gender { get; set; }
    [Id(1)] public DateTime BirthDate { get; set; }
    [Id(2)] public string BirthPlace { get; set; }
    [Id(3)] public string FullName { get; set; }
}