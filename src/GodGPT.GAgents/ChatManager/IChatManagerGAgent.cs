using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.Agents.ChatManager.Share;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using GodGPT.GAgents.SpeechChat;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Agents.ChatManager;

public interface IChatManagerGAgent : IGAgent
{
    Task<Guid> CreateSessionAsync(string systemLLM, string prompt, UserProfileDto? userProfile = null, string? guider = null);
    Task<Tuple<string,string>> ChatWithSessionAsync(Guid sessionId, string sysmLLM, string content, ExecutionPromptSettings promptSettings = null);
    [ReadOnly]
    Task<List<SessionInfoDto>> GetSessionListAsync();
    [ReadOnly]
    Task<bool> IsUserSessionAsync(Guid sessionId);
    [ReadOnly]
    Task<List<ChatMessage>> GetSessionMessageListAsync(Guid sessionId);
    [ReadOnly]
    Task<List<ChatMessageWithMetaDto>> GetSessionMessageListWithMetaAsync(Guid sessionId);
    [ReadOnly]
    Task<SessionCreationInfoDto?> GetSessionCreationInfoAsync(Guid sessionId);
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
    /// Sets the voice language preference for the user.
    /// </summary>
    /// <param name="voiceLanguage">The voice language to set</param>
    /// <returns>The user ID</returns>
    Task<Guid> SetVoiceLanguageAsync(VoiceLanguageEnum voiceLanguage);

    /// <summary>
    /// Generates a unique invitation code for the current user.
    /// </summary>
    /// <returns>The invitation code.</returns>
    Task<string> GenerateInviteCodeAsync();

    /// <summary>
    /// Redeems an invitation code for the current user.
    /// </summary>
    /// <param name="inviteCode">The invitation code to redeem.</param>
    /// <returns>True if redemption is successful, otherwise false.</returns>
    Task<bool> RedeemInviteCodeAsync(string inviteCode);

    Task<Guid?> GetInviterAsync();

    /// <summary>
    /// Search sessions by keyword with fuzzy matching
    /// </summary>
    /// <param name="keyword">Search keyword</param>
    /// <param name="maxResults">Maximum number of results to return (default: 1000)</param>
    /// <returns>List of matching sessions with content preview</returns>
    [ReadOnly]
    Task<List<SessionInfoDto>> SearchSessionsAsync(string keyword, int maxResults = 1000);
}