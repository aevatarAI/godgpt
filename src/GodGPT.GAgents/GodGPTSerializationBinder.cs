using System.Text.RegularExpressions;
using Newtonsoft.Json.Serialization;

namespace Aevatar.Application.Grains;

public class GodGPTSerializationBinder : DefaultSerializationBinder
{
    private static readonly HashSet<string> TypesToReplace = new HashSet<string>
    {
        "Aevatar.Application.Grains.Agents.ChatManager.ChatManagerGAgentState",
        "Aevatar.Application.Grains.Agents.ChatManager.SessionInfo",
        "Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent.ConfigurationState",
        "Aevatar.Application.Grains.Agents.ChatManager.Chat.GodChatState",
        "Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.AIAgentStatusProxyState",
        "Aevatar.Application.Grains.Agents.ChatManager.ChatManageEventLog",
        "Aevatar.Application.Grains.Agents.ChatManager.CreateSessionInfoEventLog",
        "Aevatar.Application.Grains.Agents.ChatManager.DeleteSessionEventLog",
        "Aevatar.Application.Grains.Agents.ChatManager.RenameTitleEventLog",
        "Aevatar.Application.Grains.Agents.ChatManager.ClearAllEventLog",
        "Aevatar.Application.Grains.Agents.ChatManager.SetUserProfileEventLog",
        "Aevatar.Application.Grains.Agents.ChatManager.GenerateChatShareContentLogEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.SetMaxShareCountLogEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.RequestCreateGodChatEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.ResponseCreateGod",
        "Aevatar.Application.Grains.Agents.ChatManager.RequestGodChatEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.ResponseGodChat",
        "Aevatar.Application.Grains.Agents.ChatManager.RequestStreamGodChatEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.ResponseStreamGodChat",
        "Aevatar.Application.Grains.Agents.ChatManager.RequestGodSessionListEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.ResponseGodSessionList",
        "Aevatar.Application.Grains.Agents.ChatManager.RequestSessionChatHistoryEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.ResponseSessionChatHistory",
        "Aevatar.Application.Grains.Agents.ChatManager.RequestDeleteSessionEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.ResponseDeleteSession",
        "Aevatar.Application.Grains.Agents.ChatManager.RequestRenameSessionEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.ResponseRenameSession",
        "Aevatar.Application.Grains.Agents.ChatManager.RequestClearAllEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.ResponseClearAll",
        "Aevatar.Application.Grains.Agents.ChatManager.RequestSetUserProfileEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.ResponseSetUserProfile",
        "Aevatar.Application.Grains.Agents.ChatManager.RequestGetUserProfileEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.ResponseGetUserProfile",
        "Aevatar.Application.Grains.Agents.ChatManager.UserProfileDto",
        "Aevatar.Application.Grains.Agents.ChatManager.ResponseType",
        "Aevatar.Application.Grains.Agents.ChatManager.Share.ShareLinkDto",
        "Aevatar.Application.Grains.Agents.ChatManager.Share.ShareState",
        "Aevatar.Application.Grains.Agents.ChatManager.ManagerConfigDto",
        "Aevatar.Application.Grains.Agents.ChatManager.SessionInfoDto",
        "Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent.SetLLMEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent.SetPromptEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent.SetStreamingModeEnabledEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent.SetUserProfilePromptEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent.ConfigurationLogEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent.InitLogEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent.SetSystemLLMLogEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent.SetPromptLogEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent.SetStreamingModeEnabledLogEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent.SetUserProfilePromptLogEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.Chat.RenameChatTitleEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.Chat.RequestStreamChatEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.Chat.GodChatEventLog",
        "Aevatar.Application.Grains.Agents.ChatManager.Chat.UpdateUserProfileGodChatEventLog",
        "Aevatar.Application.Grains.Agents.ChatManager.Chat.RenameChatTitleEventLog",
        "Aevatar.Application.Grains.Agents.ChatManager.Chat.SetChatManagerGuidEventLog",
        "Aevatar.Application.Grains.Agents.ChatManager.Chat.SetAIAgentIdLogEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.Chat.UpdateRegionProxiesLogEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.Chat.SetActiveSessionEventLog",
        "Aevatar.Application.Grains.Agents.ChatManager.Chat.InterruptSessionEventLog",
        "Aevatar.Application.Grains.Agents.ChatManager.Chat.ClearInterruptedSessionEventLog",
        "Aevatar.Application.Grains.Agents.ChatManager.Chat.UserProfile",
        "Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.Dtos.AIAgentStatusProxyConfig",
        "Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.ProxySEvents.AIAgentStatusProxyLogEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.ProxySEvents.SetAvailableLogEvent",
        "Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.ProxySEvents.SetStatusProxyConfigLogEvent"
    };
    
    private const string OldAssemblyName = "Aevatar.Application.Grains";
    private const string NewAssemblyName = "GodGPT.GAgents";

    public override Type BindToType(string assemblyName, string typeName)
    {
        if (assemblyName == OldAssemblyName && TypesToReplace.Contains(typeName))
        {
            assemblyName = NewAssemblyName;
        }
        else
        {
            if (TypesToReplace.Any(typeToReplace => typeName.Contains(typeToReplace)))
            {
                typeName = ReplaceAssemblyName(typeName, OldAssemblyName, NewAssemblyName);
            }
        }

        return base.BindToType(assemblyName, typeName);
    }

    public static string ReplaceAssemblyName(string input, string oldAssemblyName, string newAssemblyName)
    {
        var pattern = $@",\s*{Regex.Escape(oldAssemblyName)}";
        return Regex.Replace(input, pattern, $", {newAssemblyName}");
    }
}