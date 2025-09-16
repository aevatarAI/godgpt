using Aevatar.Core.Abstractions;
using GodGPT.GAgents.SpeechChat;
using GodGPT.GAgents.DailyPush;

namespace Aevatar.Application.Grains.Agents.ChatManager;

[GenerateSerializer]
public class ChatManageEventLog : StateLogEventBase<ChatManageEventLog>
{
}

[GenerateSerializer]
public class CleanExpiredSessionsEventLog : ChatManageEventLog
{
    [Id(0)] public DateTime CleanBefore { get; set; }
}

[GenerateSerializer]
public class CreateSessionInfoEventLog : ChatManageEventLog
{
    [Id(0)] public Guid SessionId { get; set; }
    [Id(1)] public string Title { get; set; }
    [Id(2)] public DateTime CreateAt { get; set; }
    [Id(3)] public string? Guider { get; set; } // Role information for the conversation
}

[GenerateSerializer]
public class DeleteSessionEventLog : ChatManageEventLog
{
    [Id(0)] public Guid SessionId { get; set; }
}

[GenerateSerializer]
public class RenameTitleEventLog : ChatManageEventLog
{
    [Id(0)] public Guid SessionId { get; set; }
    [Id(1)] public string Title { get; set; }
}

[GenerateSerializer]
public class ClearAllEventLog : ChatManageEventLog
{
}

[GenerateSerializer]
public class SetUserProfileEventLog : ChatManageEventLog
{
    [Id(0)] public string Gender { get; set; }
    [Id(1)] public DateTime BirthDate { get; set; }
    [Id(2)] public string BirthPlace { get; set; }
    [Id(3)] public string FullName { get; set; }
}

[GenerateSerializer]
public class SetVoiceLanguageEventLog : ChatManageEventLog
{
    [Id(0)] public VoiceLanguageEnum VoiceLanguage { get; set; }
}

[GenerateSerializer]
public class GenerateChatShareContentLogEvent : ChatManageEventLog
{
    [Id(0)] public Guid SessionId { get; set; }
    [Id(1)] public Guid ShareId { get; set; }
}

[GenerateSerializer]
public class SetMaxShareCountLogEvent : ChatManageEventLog
{
    [Id(0)] public int MaxShareCount { get; set; }
}

[GenerateSerializer]
public class SetInviterEventLog : ChatManageEventLog
{
    [Id(0)] public Guid InviterId { get; set; }
}

[GenerateSerializer]
public class SetRegisteredAtUtcEventLog : ChatManageEventLog
{
    [Id(0)] public DateTime RegisteredAtUtc { get; set; }
}

/// <summary>
/// Event for initializing user first access status
/// This event is used to consolidate multiple related field settings to reduce event sending frequency
/// Applicable for complete initialization of new users and status marking of existing users
/// </summary>
[GenerateSerializer]
public class InitializeNewUserStatusLogEvent : ChatManageEventLog
{
    /// <summary>
    /// Marks whether this is the first access to ChatManagerGAgent (reused field, actually used for first access marking)
    /// true: New user's first access
    /// false: Existing user's non-first access
    /// </summary>
    [Id(0)] public bool IsFirstConversation { get; set; }
    
    /// <summary>
    /// User unique identifier
    /// Obtained from Grain's PrimaryKey
    /// </summary>
    [Id(1)] public Guid UserId { get; set; }
    
    /// <summary>
    /// Registration time (UTC time)
    /// New users: Set to current time
    /// Existing users: May be null (maintain historical compatibility)
    /// </summary>
    [Id(2)] public DateTime? RegisteredAtUtc { get; set; }
    
    /// <summary>
    /// Maximum share count limit
    /// New users: Set to default value (e.g., 10000)
    /// Existing users: Set as needed, avoid overwriting existing values
    /// </summary>
    [Id(3)] public int MaxShareCount { get; set; }
}

/// <summary>
/// Register or update device for daily push notifications
/// </summary>
[GenerateSerializer]
public class RegisterOrUpdateDeviceEventLog : ChatManageEventLog
{
    [Id(0)] public string DeviceId { get; set; } = "";
    [Id(1)] public GodGPT.GAgents.DailyPush.UserDeviceInfo DeviceInfo { get; set; } = null!;
    [Id(2)] public bool IsNewDevice { get; set; }
    [Id(3)] public string? OldPushToken { get; set; }
}

/// <summary>
/// Mark daily push as read
/// </summary>
[GenerateSerializer]
public class MarkDailyPushReadEventLog : ChatManageEventLog
{
    [Id(0)] public string DateKey { get; set; } = "";
    [Id(1)] public DateTime ReadTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Clean expired and duplicate devices for daily push notifications
/// </summary>
[GenerateSerializer]
public class CleanExpiredDevicesEventLog : ChatManageEventLog
{
    [Id(0)] public List<string> DeviceIdsToRemove { get; set; } = new();
    [Id(1)] public DateTime CleanupTime { get; set; } = DateTime.UtcNow;
    [Id(2)] public string CleanupReason { get; set; } = "";
    [Id(3)] public int RemovedCount { get; set; }
}

// === Enhanced Device Management V2 Events ===

/// <summary>
/// Register or update device for daily push notifications (V2)
/// </summary>
[GenerateSerializer]
public class RegisterOrUpdateDeviceV2EventLog : ChatManageEventLog
{
    [Id(0)] public string DeviceId { get; set; } = "";
    [Id(1)] public UserDeviceInfoV2 DeviceInfo { get; set; } = null!;
    [Id(2)] public bool IsNewDevice { get; set; }
    [Id(3)] public string? OldPushToken { get; set; }
    [Id(4)] public bool IsMigration { get; set; } = false; // Track if this is a V1->V2 migration
}

/// <summary>
/// Migrate device from V1 to V2 structure
/// </summary>
[GenerateSerializer]
public class MigrateDeviceToV2EventLog : ChatManageEventLog
{
    [Id(0)] public string DeviceId { get; set; } = "";
    [Id(1)] public UserDeviceInfo OldDeviceInfo { get; set; } = null!;
    [Id(2)] public UserDeviceInfoV2 NewDeviceInfo { get; set; } = null!;
    [Id(3)] public DateTime MigrationTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Cleanup V2 devices based on enhanced criteria
/// </summary>
[GenerateSerializer]
public class CleanupDevicesV2EventLog : ChatManageEventLog
{
    [Id(0)] public List<string> DeviceIdsToRemove { get; set; } = new();
    [Id(1)] public int RemovedCount { get; set; }
    [Id(2)] public string CleanupReason { get; set; } = ""; // "expired", "token_expired", "consecutive_failures", "status_cleanup"
    [Id(3)] public Dictionary<string, string> CleanupDetails { get; set; } = new(); // Additional cleanup metadata
    [Id(4)] public DateTime CleanupTime { get; set; } = DateTime.UtcNow;
}