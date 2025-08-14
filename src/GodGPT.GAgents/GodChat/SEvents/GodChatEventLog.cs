using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Common;
using Aevatar.Application.Grains.Common.Constants;

namespace Aevatar.Application.Grains.Agents.ChatManager.Chat;

[GenerateSerializer]
public class GodChatEventLog : StateLogEventBase<GodChatEventLog>
{
}

[GenerateSerializer]
public class UpdateUserProfileGodChatEventLog : GodChatEventLog
{
    [Id(0)] public string Gender { get; set; }
    [Id(1)] public DateTime BirthDate { get; set; }
    [Id(2)] public string BirthPlace { get; set; }
    [Id(3)] public string FullName { get; set; }
}

[GenerateSerializer]
public class RenameChatTitleEventLog : GodChatEventLog
{
    [Id(0)] public string Title { get; set; }
}

[GenerateSerializer]
public class SetChatManagerGuidEventLog : GodChatEventLog
{
    [Id(0)] public Guid ChatManagerGuid { get; set; }
}

[GenerateSerializer]
[Obsolete("This class is deprecated and no longer in use.")]
public class SetAIAgentIdLogEvent : GodChatEventLog
{
    [Id(0)] public List<Guid> AIAgentIds { get; set; }
}

[GenerateSerializer]
public class UpdateRegionProxiesLogEvent : GodChatEventLog
{
    [Id(0)] public Dictionary<string, List<Guid>> RegionProxies;
}

[GenerateSerializer]
public class UpdateSingleRegionProxyLogEvent : GodChatEventLog
{
    [Id(0)] public string Region { get; set; }
    [Id(1)] public List<Guid> ProxyIds { get; set; }
}

[GenerateSerializer]
public class UpdateChatTimeEventLog : GodChatEventLog
{
    [Id(0)] public DateTime ChatTime { get; set; }
}

[GenerateSerializer]
public class AddChatMessageMetasLogEvent : GodChatEventLog
{
    [Id(0)] public List<ChatMessageMeta> ChatMessageMetas { get; set; } = new List<ChatMessageMeta>();
}

[GenerateSerializer]
public class AddPromptTemplateLogEvent : GodChatEventLog
{
    [Id(0)] public string PromptTemplate { get; set; } 
}

[GenerateSerializer]
public class UpdateProxyInitStatusLogEvent : GodChatEventLog
{
    [Id(0)] public Guid ProxyId { get; set; }
    [Id(1)] public ProxyInitStatus Status { get; set; }
}

[GenerateSerializer]
public class GodSetMaxHistoryCount : GodChatEventLog
{
    [Id(0)] public int MaxHistoryCount { get; set; }
}

[GenerateSerializer]
public class PerformConfigCombinedEventLog : GodChatEventLog
{
    [Id(0)] public string Region { get; set; }
    [Id(1)] public List<Guid> ProxyIds { get; set; }
    [Id(2)] public string PromptTemplate { get; set; }
    [Id(3)] public int MaxHistoryCount { get; set; }
}

[GenerateSerializer]
public class GodStreamChatCombinedEventLog : GodChatEventLog
{
    [Id(0)] public List<ChatMessage> ChatList { get; set; } = new List<ChatMessage>();
    [Id(1)] public DateTime ChatTime { get; set; }
    [Id(2)] public List<ChatMessageMeta> ChatMessageMetas { get; set; } = new List<ChatMessageMeta>();
}