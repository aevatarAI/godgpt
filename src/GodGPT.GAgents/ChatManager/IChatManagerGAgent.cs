using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.Agents.ChatManager.Share;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using GodGPT.GAgents.DailyPush;
using GodGPT.GAgents.SpeechChat;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Agents.ChatManager;

public interface IChatManagerGAgent : IGAgent
{
    Task<Guid> CreateSessionAsync(string systemLLM, string prompt, UserProfileDto? userProfile = null,
        string? guider = null, DateTime? userLocalTime = null);
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
    
    // === Daily Push Notification Methods ===
    
    /// <summary>
    /// Register or update device using V2 structure (enhanced version)
    /// All new device registrations should use this method
    /// </summary>
    Task<bool> RegisterOrUpdateDeviceV2Async(string deviceId, string pushToken, string timeZoneId, 
        bool? pushEnabled, string pushLanguage, string? platform = null, string? appVersion = null);
    
    /// <summary>
    /// Mark daily push as read for today
    /// </summary>
    Task MarkPushAsReadAsync(string deviceId);
    
    /// <summary>
    /// Process daily push for this user (called by timezone scheduler)
    /// </summary>
    Task ProcessDailyPushAsync(DateTime targetDate, List<DailyNotificationContent> contents, string timeZoneId, bool bypassReadStatusCheck = false, bool isRetryPush = false, bool isTestPush = false);
    
    /// <summary>
    /// Check if user should receive afternoon retry push
    /// </summary>
    Task<bool> ShouldSendAfternoonRetryAsync(DateTime targetDate);
    
    /// <summary>
    /// Get push debug information for a specific device (for troubleshooting)
    /// </summary>
    [ReadOnly]
    Task<object> GetPushDebugInfoAsync(string deviceId, DateOnly date, string timeZoneId);
    
    /// <summary>
    /// Get all V2 devices for this user
    /// </summary>
    [ReadOnly]
    Task<List<UserDeviceInfoV2>> GetAllDevicesV2Async();
    
    /// <summary>
    /// Log detailed information for all devices registered under this user
    /// </summary>
    
    /// <summary>
    /// Check if user has enabled devices in specific timezone (performance optimization)
    /// </summary>
    Task<bool> HasEnabledDeviceInTimezoneAsync(string timeZoneId);

    /// <summary>
    /// Get device status for query API
    /// </summary>
    Task<UserDeviceInfo?> GetDeviceStatusAsync(string deviceId);
    
    // === Coordinated Push Methods ===
    
    /// <summary>
    /// Get devices for coordinated push (called by DailyPushCoordinatorGAgent)
    /// Returns device information without executing push
    /// </summary>
    [ReadOnly]
    Task<List<UserDeviceInfo>> GetDevicesForCoordinatedPushAsync(string timeZoneId, DateTime targetDate);
    
    /// <summary>
    /// Execute coordinated push for a specific device (called by coordinator after device selection)
    /// Uses existing Redis deduplication logic
    /// </summary>
    Task<bool> ExecuteCoordinatedPushAsync(UserDeviceInfo device, DateTime targetDate, List<DailyNotificationContent> contents, bool isRetryPush = false, bool isTestPush = false);
    
}