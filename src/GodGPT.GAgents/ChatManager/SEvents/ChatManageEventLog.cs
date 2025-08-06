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
/// 初始化用户首次访问状态的事件
/// 该事件用于合并多个相关字段的设置，减少事件发送次数
/// 适用于新用户的完整初始化和历史用户的状态标记
/// </summary>
[GenerateSerializer]
public class InitializeNewUserStatusLogEvent : ChatManageEventLog
{
    /// <summary>
    /// 标记是否首次访问ChatManagerGAgent（复用字段，实际用于首次访问标记）
    /// true: 新用户首次访问
    /// false: 历史用户非首次访问
    /// </summary>
    [Id(0)] public bool IsFirstConversation { get; set; }
    
    /// <summary>
    /// 用户唯一标识符
    /// 从Grain的PrimaryKey获取
    /// </summary>
    [Id(1)] public Guid UserId { get; set; }
    
    /// <summary>
    /// 注册时间（UTC时间）
    /// 新用户：设置为当前时间
    /// 历史用户：可能为null（保持历史兼容性）
    /// </summary>
    [Id(2)] public DateTime? RegisteredAtUtc { get; set; }
    
    /// <summary>
    /// 最大分享次数限制
    /// 新用户：设置默认值（如10000）
    /// 历史用户：根据需要设置，避免覆盖现有值
    /// </summary>
    [Id(3)] public int MaxShareCount { get; set; }
}