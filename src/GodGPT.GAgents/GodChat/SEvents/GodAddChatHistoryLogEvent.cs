using Aevatar.GAgents.AI.Common;

namespace Aevatar.Application.Grains.Agents.ChatManager.Chat;


[GenerateSerializer]
public class GodAddChatHistoryLogEvent : GodChatEventLog
{
    [Id(0)]
    public List<ChatMessage> ChatList { get; set; }
}