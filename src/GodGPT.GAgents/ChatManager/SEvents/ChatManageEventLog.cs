using Aevatar.Core.Abstractions;
using GodGPT.GAgents.SpeechChat;

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