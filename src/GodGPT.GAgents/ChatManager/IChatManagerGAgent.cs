using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.Options;
using Aevatar.Application.Grains.Agents.ChatManager.Share;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Agents.ChatManager;

public interface IChatManagerGAgent : IGAgent
{
    Task<Guid> CreateSessionAsync(string systemLLM, string prompt, UserProfileDto? userProfile = null);
    Task<Tuple<string,string>> ChatWithSessionAsync(Guid sessionId, string sysmLLM, string content, ExecutionPromptSettings promptSettings = null);
    [ReadOnly]
    Task<List<SessionInfoDto>> GetSessionListAsync();
    [ReadOnly]
    Task<bool> IsUserSessionAsync(Guid sessionId);
    [ReadOnly]
    Task<List<ChatMessage>> GetSessionMessageListAsync(Guid sessionId);
    Task<Guid> DeleteSessionAsync(Guid sessionId);
    Task<Guid> RenameSessionAsync(Guid sessionId, string title);
    Task<UserProfileDto> GetUserProfileAsync();
    Task<Guid> SetUserProfileAsync(string gender, DateTime birthDate, string birthPlace, string fullName);
    Task<UserProfileDto> GetLastSessionUserProfileAsync();
    Task<Guid> ClearAllAsync();
    Task RenameChatTitleAsync(RenameChatTitleEvent @event);
    Task<Guid> GenerateChatShareContentAsync(Guid sessionId);
    [ReadOnly]
    Task<ShareLinkDto> GetChatShareContentAsync(Guid sessionId, Guid shareId);
    
    /// <summary>
    /// Get the speech agent ID for a session
    /// </summary>
    /// <param name="sessionId">Session ID</param>
    /// <param name="language">Language (default: zh-CN)</param>
    /// <param name="voiceName">Voice name (default: zh-CN-XiaoxiaoNeural)</param>
    /// <returns>Speech agent ID</returns>
    [ReadOnly]
    Task<Guid> GetSessionSpeechGAgentIdAsync(Guid sessionId, string language = "zh-CN", string voiceName = "zh-CN-XiaoxiaoNeural");
}