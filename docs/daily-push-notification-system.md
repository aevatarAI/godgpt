# 每日推送通知系统设计方案

## 项目概述

本文档描述了一个基于Orleans分布式架构的每日推送通知系统，支持跨时区的精准定时推送，具备智能重试机制和夏令时自动处理能力。

### 核心需求

- **定时推送**: 每天按照用户所在时区早上8点准时推送
- **内容管理**: 从类似Excel的数据源中每天随机选择2条内容
- **智能重试**: 早上未读的用户在下午15点再次推送
- **多平台支持**: 支持iOS和Android，同一用户不同设备独立跟踪
- **时区感知**: 支持全球不同时区和夏令时自动处理

## 系统架构

### 整体架构图

```
┌─────────────────────────────────────────────────────────────┐
│                    API Gateway Layer                        │
│          (设备注册、时区更新、已读状态标记)                 │
└─────────────────────┬───────────────────────────────────────┘
                      │
┌─────────────────────┴───────────────────────────────────────┐
│                 Orleans Grain Layer                         │
│  ┌─────────────────┐ ┌──────────────┐ ┌─────────────────┐  │
│  │ Master Scheduler│ │   Timezone   │ │ChatManagerGAgent│  │
│  │     Grain       │ │ Scheduler    │ │  (扩展推送)     │  │
│  │                 │ │   Grains     │ │                 │  │
│  └─────────────────┘ └──────────────┘ └─────────────────┘  │
│  ┌─────────────────┐ ┌──────────────┐ ┌─────────────────┐  │
│  │  Daily Content  │ │ Timezone User│ │  Push Delivery  │  │
│  │     Grain       │ │ Index Grain  │ │    Service      │  │
│  └─────────────────┘ └──────────────┘ └─────────────────┘  │
└─────────────────────┬───────────────────────────────────────┘
                      │
┌─────────────────────┴───────────────────────────────────────┐
│                External Services Layer                      │
│     Firebase FCM        Excel Data Source      MongoDB      │
└─────────────────────────────────────────────────────────────┘
```

### Grain 分布和资源估算

```
系统Grain分布 (100万用户规模):
├── ChatManagerGAgent: 100万个 (扩展推送功能)
├── TimezoneSchedulerGrain: ~30个 (活跃时区)
├── TimezoneUserIndexGrain: ~30个 (时区用户索引)
├── DailyContentGrain: 1个 (全局内容管理)
├── MasterSchedulerGrain: 1个 (全局协调)
└── 总计: ~100万个 + 62个系统Grain

Orleans Reminders:
├── 时区调度Reminder: 60个 (每时区2个)
├── 内存占用: <1MB
└── CPU开销: 触发时才消耗，平时几乎为0
```

## 数据模型设计

### 1. 用户设备信息

```csharp
/// <summary>
/// 用户设备信息 (平台无关设计，以pushToken hash为key)
/// </summary>
[GenerateSerializer]
public class UserDeviceInfo
{
    [Id(0)] public string DeviceId { get; set; } = ""; // 前端生成的持久化设备ID (主键)
    [Id(1)] public string PushToken { get; set; } = ""; // Firebase推送令牌 (可变，token更新时修改)
    [Id(2)] public string TimeZoneId { get; set; } = ""; // IANA时区ID (设备级时区)
    [Id(3)] public string PushLanguage { get; set; } = "en"; // 该设备的推送语言设置
    [Id(4)] public bool PushEnabled { get; set; } = true; // 设备推送开关
    [Id(5)] public DateTime RegisteredAt { get; set; } // 设备注册时间 (便于排查问题)
    [Id(6)] public DateTime LastTokenUpdate { get; set; } // token最后更新时间
}
```

### 2. 每日推送内容 (多语言设计)

```csharp
[GenerateSerializer]
public class DailyNotificationContent
{
    [Id(0)] public string Id { get; set; } = ""; // 对应Excel中的contentKey (如: task01, task02)
    [Id(1)] public Dictionary<string, LocalizedContentData> LocalizedContents { get; set; } = new();
    [Id(2)] public DateTime CreatedAt { get; set; }
    [Id(3)] public bool IsActive { get; set; } = true;
    [Id(4)] public int Priority { get; set; } = 1;
    [Id(5)] public Dictionary<string, object> Metadata { get; set; } = new();
    [Id(6)] public List<string> SupportedLanguages => LocalizedContents.Keys.ToList();
}

[GenerateSerializer]
public class LocalizedContentData
{
    [Id(0)] public string Title { get; set; } = "";     // 对应Excel中的title_en/title_zh/title_es
    [Id(1)] public string Content { get; set; } = "";   // 对应Excel中的content_en/content_zh/content_es
    [Id(2)] public string Language { get; set; } = "";  // "en", "zh-tw", "es"
    [Id(3)] public DateTime LastUpdated { get; set; }
    [Id(4)] public string? AuthorId { get; set; } // 翻译者或创建者ID
    [Id(5)] public ContentStatus Status { get; set; } = ContentStatus.Active;
}

public enum ContentStatus
{
    Active = 0,
    Draft = 1,
    NeedsReview = 2,
    Archived = 3
}

/// <summary>
/// 推送类型枚举
/// </summary>
public enum PushType
{
    /// <summary>
    /// 每日推送
    /// </summary>
    DailyPush = 1
    
    // 🎯 后续可扩展其他类型：
    // SystemNotification = 2,    // 系统通知
    // PromotionalPush = 3,       // 推广推送
    // PersonalizedPush = 4       // 个性化推送
}

/// <summary>
/// 推送类型扩展方法
/// </summary>
public static class PushTypeExtensions
{
    /// <summary>
    /// 获取枚举的数字值用于传输
    /// </summary>
    public static string ToTransportValue(this PushType pushType)
    {
        return ((int)pushType).ToString();
    }
    
    /// <summary>
    /// 从数字字符串解析枚举
    /// </summary>
    public static PushType FromTransportValue(string value)
    {
        if (int.TryParse(value, out var intValue) && Enum.IsDefined(typeof(PushType), intValue))
        {
            return (PushType)intValue;
        }
        throw new ArgumentException($"Unknown push type value: {value}");
    }
}

/// <summary>
/// 多语言内容访问扩展方法
/// </summary>
public static class DailyNotificationContentExtensions
{
    /// <summary>
    /// 获取指定语言的内容，带智能降级
    /// </summary>
    public static LocalizedContentData GetLocalizedContent(this DailyNotificationContent content, string languageCode)
    {
        // 1. 优先使用请求的语言
        if (content.LocalizedContents.TryGetValue(languageCode, out var requestedContent) && 
            requestedContent.Status == ContentStatus.Active)
        {
            return requestedContent;
        }
        
        // 2. 降级到英语 (通用语言)
        if (content.LocalizedContents.TryGetValue("en", out var englishContent) && 
            englishContent.Status == ContentStatus.Active)
        {
            return englishContent;
        }
        
        // 3. 使用任何可用的活跃内容
        var fallbackContent = content.LocalizedContents.Values
            .FirstOrDefault(c => c.Status == ContentStatus.Active);
            
        if (fallbackContent != null)
        {
            return fallbackContent;
        }
        
        // 4. 最终降级：返回占位符内容
        return new LocalizedContentData
        {
            Title = "Daily Content",
            Content = "Content not available in your language",
            Language = "en",
            LastUpdated = DateTime.UtcNow,
            Status = ContentStatus.Active
        };
    }
    
    /// <summary>
    /// 获取指定语言的标题
    /// </summary>
    public static string GetLocalizedTitle(this DailyNotificationContent content, string languageCode)
    {
        return content.GetLocalizedContent(languageCode).Title;
    }
    
    /// <summary>
    /// 获取指定语言的正文
    /// </summary>
    public static string GetLocalizedBody(this DailyNotificationContent content, string languageCode)
    {
        return content.GetLocalizedContent(languageCode).Content;
    }
    
    /// <summary>
    /// 检查是否支持指定语言
    /// </summary>
    public static bool SupportsLanguage(this DailyNotificationContent content, string languageCode)
    {
        return content.LocalizedContents.ContainsKey(languageCode) &&
               content.LocalizedContents[languageCode].Status == ContentStatus.Active;
    }
}
```

### 3. 推送记录跟踪

```csharp
[GenerateSerializer]
public class DailyPushRecord
{
    [Id(0)] public DateTime PushDate { get; set; }
    [Id(1)] public List<Guid> ContentIds { get; set; } = new();
    [Id(2)] public DevicePushStatus? IOSPushStatus { get; set; }
    [Id(3)] public DevicePushStatus? AndroidPushStatus { get; set; }
}

[GenerateSerializer]
public class DevicePushStatus
{
    [Id(0)] public DateTime? MorningPushTime { get; set; }
    [Id(1)] public DateTime? AfternoonPushTime { get; set; }
    [Id(2)] public bool MorningRead { get; set; }
    [Id(3)] public bool AfternoonRead { get; set; }
    [Id(4)] public PushDeliveryStatus MorningStatus { get; set; }
    [Id(5)] public PushDeliveryStatus AfternoonStatus { get; set; }
}

public enum PushDeliveryStatus
{
    None = 0,
    Sent = 1,
    Failed = 2,
    Delivered = 3,
    Read = 4
}
```

### 4. 扩展的ChatManagerGAgent状态

```csharp
[GenerateSerializer]
public class ChatManagerGAgentState : AIGAgentStateBase
{
    // === 现有字段保持不变 ===
    [Id(0)] public List<SessionInfo> SessionInfoList { get; set; } = new();
    [Id(1)] public Guid UserId { get; set; }
    [Id(2)] public int MaxSession { get; set; }
    [Id(3)] public string Gender { get; set; }
    [Id(4)] public DateTime BirthDate { get; set; }
    [Id(5)] public string BirthPlace { get; set; }
    [Id(6)] public string FullName { get; set; }
    [Id(7)] public int MaxShareCount { get; set; }
    [Id(8)] public int CurrentShareCount { get; set; } = 0;
    [Id(9)] public bool? IsFirstConversation { get; set; }
    [Id(10)] public DateTime? RegisteredAtUtc { get; set; }
    [Id(11)] public Guid? InviterId { get; set; }
    [Id(12)] public VoiceLanguageEnum VoiceLanguage { get; set; } = VoiceLanguageEnum.Unset;

    // === 新增推送相关字段 (精简设计) ===
    [Id(13)] public Dictionary<string, UserDeviceInfo> UserDevices { get; set; } = new(); // key: deviceId
    [Id(14)] public Dictionary<string, string> TokenToDeviceMap { get; set; } = new(); // pushToken -> deviceId 映射 (token变化时更新)
    [Id(15)] public bool DailyPushEnabled { get; set; } = true; // 是否启用每日推送 (用户级开关)
}
```

### pushToken刷新处理策略

#### **简化策略：新token = 新设备**
```csharp
// 简化逻辑：pushToken变化时直接创建新设备记录
// 优点：
// 1. 逻辑简单，没有复杂的迁移逻辑
// 2. 前端无需管理oldToken
// 3. 不会因迁移失败导致推送中断
// 4. 自动清理过期的设备记录

var deviceKey = DeviceKeyGenerator.GenerateDeviceKey(request.PushToken);
var existingDevice = await GetDeviceInfoAsync(deviceKey);

if (existingDevice == null) {
    // 新设备或token已刷新，当作新设备处理
    await RegisterNewDeviceAsync(request);
}
```

#### **Firebase推送token刷新的影响**
- **应用重新安装** → 新token → 新设备记录 (用户需重新设置偏好)
- **应用数据清除** → 新token → 新设备记录 (用户需重新设置偏好)  
- **设备重置** → 新token → 新设备记录 (用户需重新设置偏好)
- **Firebase内部刷新** → 新token → 新设备记录 (影响最小，用户基本无感知)

### 平台无关设计说明

**移除deviceType的原因：**
- ✅ **Firebase自动识别**: pushToken已包含平台信息，Firebase自动识别iOS/Android
- ✅ **真正跨平台**: 无需区分平台，统一处理推送逻辑
- ✅ **简化前端**: 前端无需判断Platform.OS，减少条件逻辑
- ✅ **未来扩展**: 支持Web Push、桌面应用等新平台无需修改接口

### 字段精简说明

**移除的字段及原因：**
- ❌ `DeviceType` - Firebase自动识别平台，无需手动指定
- ❌ `LastActiveTime` - 推送系统无需跟踪设备活跃时间
- ❌ `IsActive` - 与`PushEnabled`功能重复，简化为单一开关
- ❌ `PushDisabledAt` - 审计信息，推送系统不需要
- ❌ `LastDeviceCleanup` - 从ChatManagerGAgentState移除，简化状态管理

**保留的字段及用途：**
- ✅ `PushToken` - Firebase推送核心标识 (自动包含平台信息)
- ✅ `TimeZoneId` - 设备级时区推送核心
- ✅ `PushLanguage` - 多语言推送必需
- ✅ `PushEnabled` - 推送开关控制
- ✅ `RegisteredAt` - 故障排查和统计需要

### 设备Key生成策略

```csharp
/// <summary>
/// 设备Key生成工具类
/// </summary>
public static class DeviceKeyGenerator
{
    /// <summary>
    /// 根据pushToken生成短hash作为设备key
    /// </summary>
    /// <param name="pushToken">Firebase推送令牌</param>
    /// <returns>12字符的设备key</returns>
    public static string GenerateDeviceKey(string pushToken)
    {
        if (string.IsNullOrEmpty(pushToken))
            throw new ArgumentException("PushToken cannot be null or empty");
        
        // 使用SHA256 hash并取前12位
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(pushToken));
        var hashString = Convert.ToHexString(hashBytes);
        
        return hashString[..12].ToLowerInvariant(); // "abc123def456"
    }
    
    /// <summary>
    /// 验证设备key格式
    /// </summary>
    /// <param name="deviceKey">设备key</param>
    /// <returns>是否有效</returns>
    public static bool IsValidDeviceKey(string deviceKey)
    {
        return !string.IsNullOrEmpty(deviceKey) && 
               deviceKey.Length == 12 && 
               deviceKey.All(c => char.IsDigit(c) || (c >= 'a' && c <= 'f'));
    }
}
```

## Orleans GAgent 状态定义

### 1. TimezoneSchedulerGAgentState

```csharp
[GenerateSerializer]
public class TimezoneSchedulerGAgentState : AIGAgentStateBase
{
    [Id(0)] public string TimeZoneId { get; set; } = "";
    [Id(1)] public DateTime LastMorningPush { get; set; }
    [Id(2)] public DateTime LastAfternoonPush { get; set; }
    [Id(3)] public int TotalPushesProcessed { get; set; }
    [Id(4)] public DateTime NextScheduledMorning { get; set; }
    [Id(5)] public DateTime NextScheduledAfternoon { get; set; }
    [Id(6)] public bool IsActive { get; set; } = true;
}
```

### 2. TimezoneUserIndexGAgentState

```csharp
[GenerateSerializer]
public class TimezoneUserIndexGAgentState : AIGAgentStateBase
{
    [Id(0)] public string TimeZoneId { get; set; } = "";
    [Id(1)] public HashSet<Guid> ActiveUsers { get; set; } = new();
    [Id(2)] public DateTime LastUpdated { get; set; }
    [Id(3)] public int TotalUserCount { get; set; }
    [Id(4)] public Dictionary<DateTime, int> DailyUserStats { get; set; } = new();
}

/// <summary>
/// 时区更新请求 (批量操作用)
/// </summary>
[GenerateSerializer]
public class TimezoneUpdateRequest
{
    [Id(0)] public Guid UserId { get; set; }
    [Id(1)] public string SourceTimezone { get; set; } = "";
    [Id(2)] public string TargetTimezone { get; set; } = "";
    [Id(3)] public DateTime RequestTime { get; set; } = DateTime.UtcNow;
}
```

### 3. DailyContentGAgentState

```csharp
[GenerateSerializer]
public class DailyContentGAgentState : AIGAgentStateBase
{
    [Id(0)] public List<DailyNotificationContent> AllContents { get; set; } = new();
    [Id(1)] public Dictionary<DateTime, List<Guid>> DailySelectedContentIds { get; set; } = new();
    [Id(2)] public Dictionary<Guid, int> ContentUsageCount { get; set; } = new();
    [Id(3)] public DateTime LastContentUpdate { get; set; }
    [Id(4)] public int TotalContentCount { get; set; }
}
```

## Orleans GAgent 接口定义

### 1. IChatManagerGAgent (扩展推送功能)

```csharp
public interface IChatManagerGAgent : IGAgent
{
    // === 现有方法保持不变 ===
    Task<Guid> CreateSessionAsync(string systemLLM, string prompt, UserProfileDto? userProfile = null, string? guider = null);
    Task<Tuple<string,string>> ChatWithSessionAsync(Guid sessionId, string sysmLLM, string content, ExecutionPromptSettings promptSettings = null);
    Task<List<SessionInfoDto>> GetSessionListAsync();
    Task<UserProfileDto> GetUserProfileAsync();
    Task<Guid> SetUserProfileAsync(string gender, DateTime birthDate, string birthPlace, string fullName);
    Task<Guid> SetVoiceLanguageAsync(VoiceLanguageEnum voiceLanguage);

    // === 推送功能 - 仅保留核心接口 ===
    
    /// <summary>
    /// 注册或更新设备推送信息 (平台无关)
    /// 使用场景：APP首次安装、推送token变化、时区变化、推送开关切换
    /// </summary>
    /// <param name="deviceId">前端生成的持久化设备ID (主键，不变)</param>
    /// <param name="pushToken">Firebase推送令牌 (可选，token更新时传递)</param>
    /// <param name="timeZoneId">IANA时区ID (可选，时区变化时传递)</param>
    /// <param name="pushEnabled">推送开关 (可选，开关变化时传递)</param>
    Task RegisterOrUpdateDeviceAsync(string deviceId, string? pushToken = null, string? timeZoneId = null, bool? pushEnabled = null);
    
    /// <summary>
    /// 更新设备推送语言设置 (内部方法，从HTTP上下文获取语言)
    /// </summary>
    /// <param name="deviceId">设备ID</param>
    Task UpdateDevicePushLanguageFromContextAsync(string deviceId);
    
    /// <summary>
    /// 更新时区索引 (内部方法，设备注册时调用)
    /// </summary>
    /// <param name="oldTimeZone">旧时区 (可为null)</param>
    /// <param name="newTimeZone">新时区</param>
    Task UpdateTimezoneIndexAsync(string? oldTimeZone, string newTimeZone);
    
    /// <summary>
    /// 标记推送为已读 (简化逻辑)
    /// 使用场景：用户点击推送通知时调用
    /// </summary>
    /// <param name="pushToken">pushToken (用于查找设备)</param>
    Task MarkTodayAsReadAsync(string pushToken);
    
    // === 内部接口 - 仅供系统调用，不暴露HTTP API ===
    
    /// <summary>
    /// 处理每日推送 (由时区调度器调用)
    /// </summary>
    /// <param name="targetDate">推送日期</param>
    /// <param name="contents">推送内容列表</param>
    Task ProcessDailyPushAsync(DateTime targetDate, List<DailyNotificationContent> contents);
    
    /// <summary>
    /// 获取设备推送语言设置 (用于定时推送)
    /// </summary>
    /// <param name="deviceKey">设备key</param>
    /// <returns>语言代码 (en, zh-tw, es)</returns>
    Task<string> GetDevicePushLanguageAsync(string deviceKey);
    
    /// <summary>
    /// 获取设备时区 (用于时区索引更新)
    /// </summary>
    /// <param name="deviceKey">设备key</param>
    /// <returns>时区ID，如果设备不存在返回null</returns>
    Task<string?> GetDeviceTimezoneAsync(string deviceKey);
    
    /// <summary>
    /// 获取设备信息 (用于部分更新判断)
    /// </summary>
    /// <param name="deviceKey">设备key</param>
    /// <returns>设备信息，如果设备不存在返回null</returns>
    Task<UserDeviceInfo?> GetDeviceInfoAsync(string deviceKey);
    
    /// <summary>
    /// 更新设备时区 (部分更新专用)
    /// </summary>
    /// <param name="pushToken">pushToken</param>
    /// <param name="newTimeZoneId">新时区ID</param>
    Task UpdateDeviceTimezoneAsync(string pushToken, string newTimeZoneId);
    

    
    /// <summary>
    /// 设置设备推送开关
    /// </summary>
    /// <param name="pushToken">pushToken (用于生成设备key)</param>
    /// <param name="enabled">是否启用推送</param>
    Task SetDevicePushEnabledAsync(string pushToken, bool enabled);
    
    /// <summary>
    /// 获取用户所有启用推送的设备列表
    /// </summary>
    /// <returns>启用推送的设备列表</returns>
    Task<List<UserDeviceInfo>> GetEnabledDevicesAsync();
    
    /// <summary>
    /// 检查当日早晨推送是否已读 (下午重试判断用)
    /// </summary>
    /// <param name="targetDate">目标日期</param>
    /// <returns>是否需要下午重试推送</returns>
    Task<bool> ShouldSendAfternoonRetryAsync(DateTime targetDate);
    
    /// <summary>
    /// 处理下午重试推送 (由时区调度器调用)
    /// </summary>
    /// <param name="targetDate">推送日期</param>
    /// <param name="contents">推送内容列表</param>
    Task ProcessAfternoonRetryAsync(DateTime targetDate, List<DailyNotificationContent> contents);
}
```

### 2. ITimezoneSchedulerGAgent (时区调度器)

```csharp
public interface ITimezoneSchedulerGAgent : IGAgent
{
    /// <summary>
    /// 初始化时区调度器
    /// </summary>
    Task InitializeAsync();
    
    /// <summary>
    /// 确保调度器已初始化 (惰性初始化)
    /// </summary>
    Task EnsureInitializedAsync();
    
    /// <summary>
    /// 处理早上8点推送
    /// </summary>
    /// <param name="targetDate">目标日期 (用户本地时间)</param>
    Task ProcessMorningPushAsync(DateTime targetDate);
    
    /// <summary>
    /// 处理下午3点重试推送
    /// </summary>
    /// <param name="targetDate">目标日期 (用户本地时间)</param>
    Task ProcessAfternoonRetryAsync(DateTime targetDate);
    
    /// <summary>
    /// 获取下次调度时间
    /// </summary>
    /// <returns>下次调度的UTC时间</returns>
    Task<DateTime> GetNextScheduledTimeAsync();
    
    /// <summary>
    /// 重新校准调度时间 (夏令时切换后)
    /// </summary>
    Task RecalibrateSchedulingAsync();
    
    /// <summary>
    /// 获取调度统计信息
    /// </summary>
    /// <returns>统计信息</returns>
    Task<TimezoneSchedulerStatsDto> GetStatsAsync();
}
```

### 3. IDailyContentGAgent (内容管理)

```csharp
public interface IDailyContentGAgent : IGAgent
{
    /// <summary>
    /// 获取每日智能选择的内容 (带去重逻辑)
    /// </summary>
    /// <param name="count">内容数量</param>
    /// <param name="date">日期 (用于一致性随机选择)</param>
    /// <returns>智能选择的内容列表 (避免重复)</returns>
    Task<List<DailyNotificationContent>> GetSmartSelectedContentsAsync(int count, DateTime date);
    
    /// <summary>
    /// 获取个性化内容 (基于用户历史)
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="count">内容数量</param>
    /// <param name="date">日期</param>
    /// <returns>个性化内容列表</returns>
    Task<List<DailyNotificationContent>> GetPersonalizedContentsAsync(Guid userId, int count, DateTime date);
    
    /// <summary>
    /// 从多语言Excel文件导入内容 (支持多个sheet或多列)
    /// </summary>
    /// <param name="excelContent">Excel文件内容</param>
    /// <param name="importOptions">导入选项配置</param>
    Task ImportFromExcelAsync(byte[] excelContent, MultiLanguageImportOptions importOptions);
    
    /// <summary>
    /// 添加多语言内容
    /// </summary>
    /// <param name="contentKey">内容标识符</param>
    /// <param name="localizedContents">多语言内容字典</param>
    /// <param name="priority">优先级 (影响选择概率)</param>
    /// <param name="metadata">元数据</param>
    /// <returns>内容ID</returns>
    Task<Guid> AddMultiLanguageContentAsync(string contentKey, Dictionary<string, LocalizedContentData> localizedContents, int priority = 0, Dictionary<string, object>? metadata = null);
    
    /// <summary>
    /// 添加单语言内容 (便捷方法)
    /// </summary>
    /// <param name="contentKey">内容标识符</param>
    /// <param name="title">标题</param>
    /// <param name="content">内容</param>
    /// <param name="languageCode">语言代码 (en, zh-tw, es)</param>
    /// <param name="priority">优先级</param>
    /// <param name="metadata">元数据</param>
    /// <returns>内容ID</returns>
    Task<Guid> AddSingleLanguageContentAsync(string contentKey, string title, string content, string languageCode, int priority = 0, Dictionary<string, object>? metadata = null);
    
    /// <summary>
    /// 更新内容状态
    /// </summary>
    /// <param name="contentId">内容ID</param>
    /// <param name="isActive">是否活跃</param>
    Task UpdateContentStatusAsync(Guid contentId, bool isActive);
    
    /// <summary>
    /// 删除内容
    /// </summary>
    /// <param name="contentId">内容ID</param>
    Task DeleteContentAsync(Guid contentId);
    
    /// <summary>
    /// 获取所有活跃内容
    /// </summary>
    /// <returns>活跃内容列表</returns>
    Task<List<DailyNotificationContent>> GetAllActiveContentsAsync();
    
    /// <summary>
    /// 获取内容选择统计信息
    /// </summary>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <returns>内容选择统计</returns>
    Task<ContentSelectionStatsDto> GetSelectionStatsAsync(DateTime startDate, DateTime endDate);
    
    /// <summary>
    /// 获取内容统计信息
    /// </summary>
    /// <returns>统计信息</returns>
    Task<ContentStatsDto> GetContentStatsAsync();
}
```

### 4. ITimezoneUserIndexGAgent (时区用户索引)

```csharp
public interface ITimezoneUserIndexGAgent : IGAgent
{
    /// <summary>
    /// 添加用户到时区索引 (轻量级，无事件)
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="timeZoneId">时区ID</param>
    Task AddUserToTimezoneAsync(Guid userId, string timeZoneId);
    
    /// <summary>
    /// 从时区索引中移除用户 (轻量级，无事件)
    /// </summary>
    /// <param name="userId">用户ID</param>
    Task RemoveUserFromTimezoneAsync(Guid userId);
    
    /// <summary>
    /// 批量更新用户时区 (减少调用次数)
    /// </summary>
    /// <param name="updates">批量更新列表</param>
    Task BatchUpdateUsersAsync(List<TimezoneUpdateRequest> updates);
    
    /// <summary>
    /// 更新用户时区
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="oldTimeZone">旧时区ID</param>
    /// <param name="newTimeZone">新时区ID</param>
    Task UpdateUserTimezoneAsync(Guid userId, string oldTimeZone, string newTimeZone);
    
    /// <summary>
    /// 获取时区内的活跃用户列表 (分页)
    /// </summary>
    /// <param name="skip">跳过数量</param>
    /// <param name="take">获取数量</param>
    /// <returns>活跃用户ID列表 (有启用推送的设备)</returns>
    Task<List<Guid>> GetActiveUsersInTimezoneAsync(int skip, int take);
    
    /// <summary>
    /// 获取时区内的活跃用户总数
    /// </summary>
    /// <returns>活跃用户总数</returns>
    Task<int> GetActiveUserCountAsync();
    
    /// <summary>
    /// 检查用户是否在此时区有活跃设备
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>是否有活跃设备</returns>
    Task<bool> HasActiveDeviceInTimezoneAsync(Guid userId);
}
```

## HTTP API 接口

### 核心特性
- **平台无关设计**: Firebase自动识别iOS/Android，无需传递deviceType
- **部分更新支持**: 只传需要修改的字段，提高接口效率  
- **统一请求体**: 所有参数都在请求体中，接口设计一致
- **权限自动验证**: 通过pushToken自动识别用户和设备

### 1. 设备推送注册/更新 (平台无关)

```csharp
[ApiController]
[Route("api/push")]
public class PushController : ControllerBase
{
    /// <summary>
    /// 设备推送注册/更新 (支持部分更新)
    /// 调用频率：极低频 (每用户每月0-3次)
    /// 使用场景：首次注册、pushToken刷新(当新设备)、时区变化、推送开关切换、语言更新等
    /// 平台无关：Firebase自动识别iOS/Android，无需传递deviceType
    /// </summary>
    [HttpPost("device")]
    public async Task<IActionResult> RegisterDeviceAsync([FromBody] DeviceRequest request)
    {
        var userId = CurrentUser.GetId();
        var language = HttpContext.GetGodGPTLanguage();
        
        var chatManagerGAgent = _clusterClient.GetGrain<IChatManagerGAgent>(userId);
        RequestContext.Set("GodGPTLanguage", language.ToString());
        
        var deviceKey = DeviceKeyGenerator.GenerateDeviceKey(request.PushToken);
        
        // 检查设备是否存在
        var existingDevice = await chatManagerGAgent.GetDeviceInfoAsync(deviceKey);
        var isNewRegistration = existingDevice == null;
        
        // 新设备注册：必须提供TimeZoneId (Firebase自动识别平台)
        if (isNewRegistration)
        {
            if (string.IsNullOrEmpty(request.TimeZoneId))
            {
                return BadRequest("TimeZoneId is required for new device registration");
            }
            
            await chatManagerGAgent.RegisterOrUpdateDeviceAsync(
                request.PushToken,
                request.TimeZoneId);
                
            // 新设备默认启用推送
            await chatManagerGAgent.SetDevicePushEnabledAsync(request.PushToken, request.PushEnabled ?? true);
            
            // 更新时区索引 (无需deviceType，统一管理)
            await chatManagerGAgent.UpdateTimezoneIndexAsync(null, request.TimeZoneId);
        }
        else
        {
            // 现有设备更新：只更新提供的字段
            var hasChanges = false;
            
            // 更新时区 (如果提供)
            if (!string.IsNullOrEmpty(request.TimeZoneId) && request.TimeZoneId != existingDevice.TimeZoneId)
            {
                var oldTimeZone = existingDevice.TimeZoneId;
                await chatManagerGAgent.UpdateDeviceTimezoneAsync(request.PushToken, request.TimeZoneId);
                await chatManagerGAgent.UpdateTimezoneIndexAsync(oldTimeZone, request.TimeZoneId);
                hasChanges = true;
            }
            
            // 更新推送开关 (如果提供)
            if (request.PushEnabled.HasValue && request.PushEnabled.Value != existingDevice.PushEnabled)
            {
                await chatManagerGAgent.SetDevicePushEnabledAsync(request.PushToken, request.PushEnabled.Value);
                hasChanges = true;
            }
            
            if (!hasChanges)
            {
                return Ok(new { success = true, message = "No changes detected" });
            }
        }
        
        // 总是更新语言设置 (基于HTTP header)
        await chatManagerGAgent.UpdateDevicePushLanguageFromContextAsync(request.PushToken);
        
        return Ok(new { 
            success = true, 
            isNewRegistration = isNewRegistration
        });
    }
    
    /// <summary>
    /// 标记推送已读 (平台无关)
    /// 调用频率：低频 (每用户每天0-2次)
    /// 使用场景：用户点击推送通知时调用
    /// 权限验证：通过pushToken自动识别用户和设备
    /// </summary>
    [HttpPost("read")]
    public async Task<IActionResult> MarkAsReadAsync([FromBody] MarkReadRequest request)
    {
        var userId = CurrentUser.GetId();
        var language = HttpContext.GetGodGPTLanguage(); // 保持语言上下文一致性
        
        var chatManagerGAgent = _clusterClient.GetGrain<IChatManagerGAgent>(userId);
        
        // 传递语言上下文到Orleans
        RequestContext.Set("GodGPTLanguage", language.ToString());
        
        // pushToken自动识别设备，无需deviceType
        await chatManagerGAgent.MarkPushAsReadAsync(request.PushToken, request.PushId);
        return Ok(new { success = true });
}


}
```

### 2. 内容管理API (管理员) - 多语言支持

```csharp
[ApiController]
[Route("api/admin/daily-push/content")]
[Authorize(Roles = "Admin")]
public class DailyPushContentController : ControllerBase
{
    /// <summary>
    /// 上传多语言Excel内容文件
    /// </summary>
    /// <param name="file">Excel文件</param>
    /// <param name="request">导入配置</param>
    /// <returns>导入结果</returns>
    [HttpPost("import/excel")]
    public async Task<ActionResult<ApiResponse<ContentImportResponse>>> ImportExcelAsync(
        IFormFile file,
        [FromForm] MultiLanguageImportRequest request)
    {
        if (file == null || file.Length == 0)
            return BadRequest("File is required");
            
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        
        var importOptions = new MultiLanguageImportOptions
        {
            ImportMode = request.ImportMode,
            SheetName = request.SheetName ?? "Sheet1",
            LanguageSheetMapping = request.LanguageSheetMapping ?? new(),
            ColumnMapping = request.ColumnMapping ?? new(),
            StartRow = request.StartRow,
            ContentKeyColumn = request.ContentKeyColumn,
            DefaultPriority = request.DefaultPriority
        };
        
        var contentGrain = _clusterClient.GetGrain<IDailyContentGrain>("default");
        await contentGrain.ImportFromExcelAsync(stream.ToArray(), importOptions);
        
        var stats = await contentGrain.GetContentStatsAsync();
        
        return Ok(new ApiResponse<ContentImportResponse>
        {
            Success = true,
            Data = new ContentImportResponse
            {
                TotalContents = stats.TotalCount,
                ActiveContents = stats.ActiveCount,
                ImportedAt = DateTime.UtcNow,
                SupportedLanguages = stats.SupportedLanguages
            }
        });
    }
    
    /// <summary>
    /// 添加多语言内容
    /// </summary>
    /// <param name="request">多语言内容添加请求</param>
    /// <returns>添加结果</returns>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<ContentCreateResponse>>> AddMultiLanguageContentAsync(
        [FromBody] MultiLanguageContentCreateRequest request)
    {
        var contentGrain = _clusterClient.GetGrain<IDailyContentGrain>("default");
        var contentId = await contentGrain.AddMultiLanguageContentAsync(
            request.ContentKey,
            request.LocalizedContents,
            request.Priority,
            request.Metadata);
        
        return Ok(new ApiResponse<ContentCreateResponse>
        {
            Success = true,
            Data = new ContentCreateResponse
            {
                ContentId = contentId,
                ContentKey = request.ContentKey,
                SupportedLanguages = request.LocalizedContents.Keys.ToList(),
                CreatedAt = DateTime.UtcNow
            }
        });
    }
    
    /// <summary>
    /// 添加单语言内容 (便捷接口)
    /// </summary>
    /// <param name="request">单语言内容添加请求</param>
    /// <returns>添加结果</returns>
    [HttpPost("single-language")]
    public async Task<ActionResult<ApiResponse<ContentCreateResponse>>> AddSingleLanguageContentAsync(
        [FromBody] SingleLanguageContentCreateRequest request)
    {
        var contentGrain = _clusterClient.GetGrain<IDailyContentGrain>("default");
        var contentId = await contentGrain.AddSingleLanguageContentAsync(
            request.ContentKey,
            request.Title,
            request.Content,
            request.LanguageCode,
            request.Priority,
            request.Metadata);
        
        return Ok(new ApiResponse<ContentCreateResponse>
        {
            Success = true,
            Data = new ContentCreateResponse
            {
                ContentId = contentId,
                ContentKey = request.ContentKey,
                SupportedLanguages = new List<string> { request.LanguageCode },
                CreatedAt = DateTime.UtcNow
            }
        });
    }
    
    /// <summary>
    /// 获取所有内容
    /// </summary>
    /// <returns>内容列表</returns>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<DailyNotificationContent>>>> GetAllContentsAsync()
    {
        var contentGrain = _clusterClient.GetGrain<IDailyContentGrain>("default");
        var contents = await contentGrain.GetAllActiveContentsAsync();
        
        return Ok(new ApiResponse<List<DailyNotificationContent>>
        {
            Success = true,
            Data = contents
        });
    }
    
    /// <summary>
    /// 更新内容状态
    /// </summary>
    /// <param name="contentId">内容ID</param>
    /// <param name="request">状态更新请求</param>
    /// <returns>更新结果</returns>
    [HttpPut("{contentId}/status")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateContentStatusAsync(
        Guid contentId,
        [FromBody] ContentStatusUpdateRequest request)
    {
        var contentGrain = _clusterClient.GetGrain<IDailyContentGrain>("default");
        await contentGrain.UpdateContentStatusAsync(contentId, request.IsActive);
        
        return Ok(new ApiResponse<object>
        {
            Success = true,
            Message = "Content status updated successfully"
        });
    }
    
    /// <summary>
    /// 删除内容
    /// </summary>
    /// <param name="contentId">内容ID</param>
    /// <returns>删除结果</returns>
    [HttpDelete("{contentId}")]
    public async Task<ActionResult<ApiResponse<object>>> DeleteContentAsync(Guid contentId)
    {
        var contentGrain = _clusterClient.GetGrain<IDailyContentGrain>("default");
        await contentGrain.DeleteContentAsync(contentId);
        
        return Ok(new ApiResponse<object>
        {
            Success = true,
            Message = "Content deleted successfully"
        });
    }
}
```

### 接口总结

#### **对外HTTP API (2个接口)**

| 接口 | 方法 | URL | 用途 | 特点 |
|------|------|-----|------|------|
| **设备注册/更新** | POST | `/api/push/device` | 注册设备、更新设置 | 平台无关、部分更新 |
| **标记已读** | POST | `/api/push/read` | 标记推送已读 | 统一请求体、自动权限验证 |

#### **核心设计原则**
- ✅ **平台无关**: 无需区分iOS/Android，Firebase自动识别
- ✅ **参数精简**: 移除冗余的deviceType参数
- ✅ **部分更新**: 只传需要修改的字段
- ✅ **统一风格**: 所有参数都在请求体中
- ✅ **自动验证**: 通过pushToken自动识别用户和设备

## DTO 模型定义

### 1. 请求模型

```csharp
public class DeviceRequest
{
    [Required]
    public string PushToken { get; set; } // 设备标识 (必须，Firebase自动识别平台)
    
    // === 以下字段都是可选的，支持部分更新 ===
    public string? TimeZoneId { get; set; } // 可选: IANA时区ID (首次注册必须)
    public bool? PushEnabled { get; set; } // 可选: 推送开关
}

public class MarkReadRequest
{
    [Required]
    public string PushToken { get; set; } // pushToken (设备标识，用于权限验证)
    
    [Required]
    public string PushId { get; set; } // 推送唯一标识 (标记哪条推送已读)
}



// === 多语言内容请求模型 ===

public class MultiLanguageContentCreateRequest
{
    public string ContentKey { get; set; }
    public Dictionary<string, LocalizedContentData> LocalizedContents { get; set; } = new();
    public int Priority { get; set; } = 0;
    public Dictionary<string, object>? Metadata { get; set; }
}

public class SingleLanguageContentCreateRequest
{
    public string ContentKey { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }
    public string LanguageCode { get; set; } // "en", "zh-tw", "es"
    public int Priority { get; set; } = 0;
    public Dictionary<string, object>? Metadata { get; set; }
}

// === 多语言Excel导入配置 ===

public class MultiLanguageImportRequest
{
    public ExcelImportMode ImportMode { get; set; } = ExcelImportMode.MultiColumn;
    public string? SheetName { get; set; } = "Sheet1";
    public Dictionary<string, string>? LanguageSheetMapping { get; set; } // 语言 -> Sheet名称映射
    public Dictionary<string, ExcelColumnConfig>? ColumnMapping { get; set; } // 语言 -> 列配置映射
    public int StartRow { get; set; } = 2; // 从第2行开始读取 (跳过标题行)
    public string ContentKeyColumn { get; set; } = "A"; // 内容标识符列
    public int DefaultPriority { get; set; } = 0;
}

public class MultiLanguageImportOptions
{
    public ExcelImportMode ImportMode { get; set; }
    public string SheetName { get; set; }
    public Dictionary<string, string> LanguageSheetMapping { get; set; } = new();
    public Dictionary<string, ExcelColumnConfig> ColumnMapping { get; set; } = new();
    public int StartRow { get; set; }
    public string ContentKeyColumn { get; set; }
    public int DefaultPriority { get; set; }
}

public class ExcelColumnConfig
{
    public string TitleColumn { get; set; } // 标题列 (如: "B")
    public string ContentColumn { get; set; } // 内容列 (如: "C")
    public string? PriorityColumn { get; set; } // 优先级列 (可选)
}

public enum ExcelImportMode
{
    /// <summary>
    /// 多列模式：一个Sheet，不同语言使用不同列
    /// 格式：| ContentKey | EN_Title | EN_Content | ZH_Title | ZH_Content | ES_Title | ES_Content |
    /// </summary>
    MultiColumn = 0,
    
    /// <summary>
    /// 多Sheet模式：不同语言使用不同Sheet
    /// 每个Sheet格式：| ContentKey | Title | Content |
    /// </summary>
    MultiSheet = 1,
    
    /// <summary>
    /// 混合模式：主要语言在主Sheet，其他语言在单独Sheet
    /// </summary>
    Hybrid = 2
}

public class ContentStatusUpdateRequest
{
    public bool IsActive { get; set; }
}

public enum DeviceType
{
    iOS = 1,
    Android = 2
}
```

### 2. 响应模型

```csharp
// === 多语言响应模型 ===

public class ContentImportResponse
{
    public int TotalContents { get; set; }
    public int ActiveContents { get; set; }
    public List<string> SupportedLanguages { get; set; } = new();
    public Dictionary<string, int> LanguageContentCounts { get; set; } = new(); // 各语言内容数量统计
    public DateTime ImportedAt { get; set; }
    public List<string>? ImportWarnings { get; set; } // 导入警告信息
}

public class ContentCreateResponse
{
    public Guid ContentId { get; set; }
    public string ContentKey { get; set; }
    public List<string> SupportedLanguages { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class TimezoneSchedulerStatsDto
{
    public string TimeZoneId { get; set; }
    public int UserCount { get; set; }
    public DateTime? LastMorningPush { get; set; }
    public DateTime? LastAfternoonPush { get; set; }
    public DateTime? NextScheduledPush { get; set; }
    public bool IsActive { get; set; }
}

public class ContentStatsDto
{
    public int TotalCount { get; set; }
    public int ActiveCount { get; set; }
    public int InactiveCount { get; set; }
    public List<string> SupportedLanguages { get; set; } = new();
    public Dictionary<string, int> LanguageContentCounts { get; set; } = new(); // 各语言内容数量
    public Dictionary<string, double> LanguageCompleteness { get; set; } = new(); // 各语言完整度百分比
    public DateTime? LastImportTime { get; set; }
    public DateTime? LastUpdateTime { get; set; }
}

public class ContentSelectionStatsDto
{
    public int TotalSelections { get; set; }
    public int UniqueContentsUsed { get; set; }
    public double AverageSelectionsPerDay { get; set; }
    public double ContentReuseRate { get; set; } // 内容重复使用率
    public Dictionary<Guid, int> ContentUsageFrequency { get; set; } = new(); // 各内容使用频率
}
```

## 🕐 **设备级时区设计说明**

### **时区归属原则**
- ✅ **时区属于设备**：每个设备有独立的时区设置
- ❌ ~~时区属于用户~~：用户的不同设备可能在不同时区

### **实际应用场景**
```typescript
// 同一用户，设备在不同时区
User张三 {
    iPhone → "Asia/Shanghai"      // 人在上海，iPhone收到 08:00 推送
    iPad  → "America/New_York"    // iPad放在纽约办公室，收到纽约时间 08:00 推送
    Android → "Europe/London"     // Android借给伦敦朋友，收到伦敦时间 08:00 推送
}

// 时区调度器分别处理
TimezoneSchedulerGAgent("Asia/Shanghai").ProcessMorningPush()      // 上海时间 08:00
TimezoneSchedulerGAgent("America/New_York").ProcessMorningPush()   // 纽约时间 08:00  
TimezoneSchedulerGAgent("Europe/London").ProcessMorningPush()      // 伦敦时间 08:00
```

### **时区更新场景**
1. **设备移动**：用户携带设备到其他时区
2. **设备借用**：设备临时给其他时区的人使用  
3. **设备固定**：设备长期放置在某个时区

### **前端时区获取**
```typescript
// 使用react-native-localize获取设备当前时区
import { getTimeZone } from 'react-native-localize';

const deviceTimeZone = getTimeZone(); // "Asia/Shanghai"
```

## 核心流程设计

### 1. 系统初始化流程

```mermaid
graph TD
    A[系统启动] --> B[Master Scheduler启动]
    B --> C[扫描活跃用户时区]
    C --> D[为活跃时区创建Scheduler Grain]
    D --> E[每个时区Scheduler注册Orleans Reminder]
    E --> F[设置本地时间8:00和15:00触发]
    F --> G[系统就绪，等待定时触发]
```

### 2. 每日早上推送流程 (带时区索引)

```mermaid
graph TD
    A[早上8:00 Reminder触发] --> B[TimezoneSchedulerGrain被唤醒]
    B --> C[从TimezoneUserIndexGrain获取活跃用户列表]
    C --> D[分页获取用户 batch=1000]
    D --> E[DailyContentGrain智能选择内容]
    E --> F[检查近7天推送历史去重]
    F --> G[加权随机选择2条内容]
    G --> H[记录今日内容选择到Redis]
    H --> I[并行调用用户ChatManagerGAgent]
    I --> J[每个用户检查该时区的设备]
    J --> K[设备存在且活跃 → Firebase推送]
    K --> L[记录推送状态到设备]
    L --> M[处理下一批用户]
    M --> N[重新调度下次8:00]
```

### 3. 下午重试推送流程

```mermaid
graph TD
    A[下午15:00 Reminder触发] --> B[TimezoneSchedulerGAgent被唤醒]
    B --> C[获取相同的每日内容]
    C --> D[从TimezoneUserIndexGAgent获取用户列表]
    D --> E[分页处理用户 batch=1000]
    E --> F[并行调用ChatManagerGAgent.ShouldSendAfternoonRetryAsync]
    F --> G{检查Redis中当日推送已读状态}
    G -->|已读| H[跳过该用户]
    G -->|未读| I[调用ProcessAfternoonRetryAsync]
    I --> J[发送重试推送到启用推送的设备]
    J --> K[记录重试推送状态]
    H --> L[处理下一批用户]
    K --> L
    L --> M[统计: X/Y用户需要重试]
    M --> N[重新调度明天8:00和15:00]
```

## 智能内容选择算法

### 1. 内容去重策略

系统采用多层去重机制，确保用户收到的内容始终保持新鲜度：

```csharp
public class SmartContentSelector
{
    // 核心去重规则
    public class DeduplicationRules
    {
        public string 当日去重 = "同一天推送的2条内容不能重复";
        public string 历史去重 = "不与近7天推送的内容重复";
        public string 全局一致 = "同一天所有用户收到相同内容";
        public string 智能降级 = "内容不足时的多级降级策略";
    }
    
    // 智能选择算法
    public async Task<List<DailyNotificationContent>> SelectDailyContentsAsync(
        DateTime targetDate, 
        int requestCount = 2)
    {
        // 1. 获取所有活跃内容
        var allActiveContents = await _contentGrain.GetAllActiveContentsAsync();
        
        // 2. 应用去重过滤 (排除近7天使用的内容)
        var recentlyUsedIds = GetRecentlyUsedContentIds(targetDate, days: 7);
        var availableContents = allActiveContents
            .Where(c => !recentlyUsedIds.Contains(c.Id))
            .ToList();
        
        // 3. 智能降级策略 (如果可用内容不足)
        if (availableContents.Count < requestCount)
        {
            availableContents = await ApplyFallbackStrategy(allActiveContents, targetDate);
        }
        
        // 4. 加权随机选择 (优先级高的内容有更高概率)
        var selected = SelectWithWeightedRandom(availableContents, requestCount, targetDate);
        
        // 5. 记录选择结果用于后续去重
        await RecordDailySelection(targetDate, selected.Select(c => c.Id).ToList());
        
        return selected;
    }
}
```

### 2. 全局内容使用跟踪

```csharp
public class GlobalContentUsageTracker
{
    // 使用Redis存储近期内容选择记录
    public static async Task RecordDailySelectionAsync(DateTime date, List<Guid> contentIds)
    {
        var cacheKey = $"daily_selection:{date:yyyy-MM-dd}";
        var expiry = TimeSpan.FromDays(30); // 保留30天历史
        
        var record = new DailySelectionRecord
        {
            Date = date,
            ContentIds = contentIds,
            RecordedAt = DateTime.UtcNow
        };
        
        // 存储到Redis (自动过期) + 内存缓存 (快速访问)
        await _distributedCache.SetStringAsync(cacheKey, JsonSerializer.Serialize(record), 
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiry });
        _memoryCache.Set(cacheKey, record, TimeSpan.FromHours(24));
    }
    
    // 获取近期使用的内容ID (支持多天查询)
    public static List<Guid> GetRecentlyUsedIds(DateTime targetDate, int days)
    {
        var recentlyUsedIds = new HashSet<Guid>();
        
        for (int i = 1; i <= days; i++)
        {
            var checkDate = targetDate.AddDays(-i);
            var record = GetDailySelectionRecord(checkDate);
            if (record != null)
            {
                recentlyUsedIds.UnionWith(record.ContentIds);
            }
        }
        
        return recentlyUsedIds.ToList();
    }
}
```

### 3. 内容不足时的降级策略

```csharp
public class ContentFallbackStrategy
{
    // 多级降级确保系统稳定运行
    public async Task<List<DailyNotificationContent>> ApplyFallbackStrategy(
        List<DailyNotificationContent> allContents,
        DateTime targetDate)
    {
        // 策略1: 缩短去重历史期间 (7天 -> 3天 -> 1天)
        for (int days = 3; days >= 1; days--)
        {
            var recentIds = GetRecentlyUsedContentIds(targetDate, days);
            var available = allContents.Where(c => !recentIds.Contains(c.Id)).ToList();
            
            if (available.Count >= 2) // 至少需要2条内容
            {
                _logger.LogInformation("Fallback: Using {Days} days deduplication", days);
                return available;
            }
        }
        
        // 策略2: 允许重复但优先选择较久未使用的
        _logger.LogWarning("Allowing content repetition with preference for less recent");
        var recentIds = GetRecentlyUsedContentIds(targetDate, 7);
        
        return allContents
            .OrderBy(c => recentIds.Contains(c.Id) ? 1 : 0) // 未使用的排前面
            .ThenBy(c => GetLastUsedDate(c.Id)) // 按最后使用时间排序
            .ToList();
    }
}
```

### 4. 性能优化机制

- **双层缓存**: 内存缓存(2小时) + Redis缓存(30天)
- **批量查询**: 一次性获取多天的历史记录
- **自动过期**: Redis TTL机制自动清理过期数据
- **懒加载**: 只在需要时加载历史数据

## 夏令时处理策略

### 1. 时区处理原则

- **使用IANA时区ID**: 如`America/New_York`、`Asia/Shanghai`等标准时区标识
- **依赖.NET TimeZoneInfo**: 让框架自动处理夏令时转换
- **动态重新计算**: 每次reminder触发后重新计算下次调度时间
- **容错窗口**: 允许±30分钟的执行时间偏差
- **自动校准**: 夏令时切换后自动验证和校准调度

### 2. 关键实现代码

```csharp
// 夏令时安全的时间计算
public static DateTime CalculateNext8AM(string ianaTimeZoneId)
{
    var timeZone = TimeZoneInfo.FindSystemTimeZoneById(ianaTimeZoneId);
    var userLocalNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
    
    var next8AM = userLocalNow.Date.AddHours(8);
    if (userLocalNow.TimeOfDay >= TimeSpan.FromHours(8))
    {
        next8AM = next8AM.AddDays(1);
    }
    
    // 转换回UTC，.NET自动处理夏令时
    return TimeZoneInfo.ConvertTimeToUtc(next8AM, timeZone);
}

// 执行窗口验证 (处理夏令时偏差)
public static bool IsWithinExecutionWindow(DateTime userLocalTime, TimeSpan targetTime, TimeSpan tolerance)
{
    var currentTime = userLocalTime.TimeOfDay;
    var timeDiff = Math.Abs((currentTime - targetTime).TotalMinutes);
    return timeDiff <= tolerance.TotalMinutes;
}
```

## 性能和扩展性

### 1. 性能指标

| 指标 | 目标值 | 备注 |
|------|--------|------|
| 推送完成时间 | < 5分钟/时区 | 单时区内所有用户推送完成 |
| Reminder触发延迟 | < 30秒 | Orleans reminder精度 |
| Firebase推送成功率 | > 95% | 排除设备离线等情况 |
| Grain激活时间 | < 100ms | 用户Grain激活响应时间 |
| 数据库查询延迟 | < 50ms | MongoDB查询响应时间 |

### 2. 扩展策略

- **水平扩展**: Orleans自动分布Grain到多个节点
- **分片策略**: 按时区和用户ID进行自然分片
- **缓存优化**: 内容和时区信息适度缓存
- **批量处理**: Firebase支持批量推送，提高吞吐量
- **异步处理**: 推送和状态更新异步执行

## 监控和运维

### 1. 关键监控指标

```csharp
public class DailyPushMetrics
{
    // 业务指标
    public int DailyPushCount { get; set; }          // 每日推送总数
    public double PushSuccessRate { get; set; }      // 推送成功率
    public double ReadRate { get; set; }             // 阅读率
    public int RetryPushCount { get; set; }          // 重试推送数量
    
    // 系统指标
    public int ActiveGrainCount { get; set; }        // 活跃Grain数量
    public double MemoryUsage { get; set; }          // 内存使用率
    public double CpuUsage { get; set; }             // CPU使用率
    public int PendingReminderCount { get; set; }    // 待处理Reminder数量
    
    // 时区指标
    public Dictionary<string, int> TimezoneUserCount { get; set; }  // 各时区用户数
    public Dictionary<string, DateTime> LastPushTime { get; set; }  // 各时区最后推送时间
}
```

### 2. 告警策略

- **推送失败率 > 10%**: 检查Firebase服务状态
- **Reminder延迟 > 5分钟**: 检查Orleans集群健康状态
- **内存使用率 > 80%**: 考虑扩容或优化
- **夏令时切换期间**: 增加监控频率，验证调度正确性

## 部署和配置

### 1. 环境要求

- **.NET 8.0+**: 支持最新的Orleans和TimeZoneInfo功能
- **Orleans 8.0+**: 分布式Grain和Reminder支持
- **MongoDB 6.0+**: 数据持久化存储
- **Firebase FCM**: 推送通知服务
- **Redis**: 可选的分布式缓存

### 2. 配置示例

```json
{
  // === Firebase配置 (只需要这个) ===
  "Firebase": {
    "ProjectId": "your-firebase-project-id",
    "PrivateKey": "-----BEGIN PRIVATE KEY-----\n...your-private-key...\n-----END PRIVATE KEY-----\n",
    "ClientEmail": "firebase-adminsdk-xxxxx@your-project.iam.gserviceaccount.com"
  },
  
  // ❌ DailyPush配置暂时不需要 - 全部使用硬编码常量
  "Orleans": {
    "ClusterId": "daily-push-cluster",
    "ServiceId": "daily-push-service",
    "ConnectionString": "mongodb://localhost:27017/daily-push"
  }
}
```

## PushId 管理策略

### 1. PushId 生命周期设计

```csharp
public class PushIdLifecycleManagement
{
    // ✅ 临时存储策略 (非永久存储)
    public class PushIdStorageStrategy
    {
        public string 存储位置 = "Redis分布式缓存 (临时存储)";
        public TimeSpan 存储时长 = TimeSpan.FromHours(48); // 48小时TTL
        public string 存储目的 = "仅用于48小时内的已读状态验证";
        public bool 永久存储 = false; // ❌ 不永久存储任何pushId
        public string 清理机制 = "Redis TTL自动过期删除，无需手动清理";
        public string 数据规模 = "最大800MB稳定存储，不会无限增长";
    }
    
    // 🎯 PushId 格式规范
    public class PushIdFormat
    {
        public string 格式模板 = "daily_push_{userId}_{date:yyyyMMdd}_{timeSlot}_{deviceType}";
        public string 示例值 = "daily_push_123e4567-e89b-12d3-a456-426614174000_20240315_morning_ios";
        public string 唯一性保证 = "通过业务逻辑确保组合唯一，理论冲突概率为0";
        public string 冲突处理 = "添加毫秒时间戳后缀 (防御性编程)";
        public bool 人类可读 = true; // 便于调试和日志分析
    }
}
```

### 2. PushId管理器实现

```csharp
public class PushIdManager
{
    private readonly IDistributedCache _distributedCache; // Redis
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<PushIdManager> _logger;
    
    /// <summary>
    /// 生成并临时存储pushId (48小时内有效)
    /// </summary>
    public async Task<string> CreatePushIdAsync(Guid userId, DateTime pushDate, bool isMorning, DeviceType deviceType)
    {
        // 1. 生成结构化pushId
        var timeSlot = isMorning ? "morning" : "afternoon";
        var deviceStr = deviceType.ToString().ToLower();
        var pushId = $"daily_push_{userId}_{pushDate:yyyyMMdd}_{timeSlot}_{deviceStr}";
        
        // 2. 理论冲突检查 (防御性编程，实际几乎不会发生)
        var existingMetadata = await GetPushMetadataAsync(pushId);
        if (existingMetadata != null)
        {
            // 添加毫秒时间戳确保唯一性
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            pushId = $"{pushId}_{timestamp}";
            _logger.LogWarning("Unexpected pushId collision resolved: {PushId}", pushId);
        }
        
        // 3. 创建临时元数据
        var metadata = new PushMetadata
        {
            UserId = userId,
            DeviceType = deviceType,
            PushTime = DateTime.UtcNow,
            IsMorning = isMorning,
            IsRead = false,
            ExpiresAt = DateTime.UtcNow.AddHours(48) // 48小时后自动失效
        };
        
        // 4. 存储到Redis (48小时TTL自动清理)
        var cacheKey = $"push_id:{pushId}";
        await _distributedCache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(metadata),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(48)
            });
        
        // 5. 同时存储到内存缓存 (2小时，更快访问)
        _memoryCache.Set(cacheKey, metadata, TimeSpan.FromHours(2));
        
        _logger.LogDebug("Created temporary pushId: {PushId}, expires in 48 hours", pushId);
        return pushId;
    }
    
    /// <summary>
    /// 获取pushId元数据 (可能返回null，表示不存在或已过期)
    /// </summary>
    public async Task<PushMetadata?> GetPushMetadataAsync(string pushId)
    {
        var cacheKey = $"push_id:{pushId}";
        
        // 1. 优先从内存缓存查找 (最快路径)
        if (_memoryCache.TryGetValue(cacheKey, out PushMetadata? cachedMetadata))
        {
            if (cachedMetadata.ExpiresAt > DateTime.UtcNow)
            {
                return cachedMetadata;
            }
            else
            {
                // 已过期，从内存移除
                _memoryCache.Remove(cacheKey);
            }
        }
        
        // 2. 从Redis查找
        var cachedData = await _distributedCache.GetStringAsync(cacheKey);
        if (cachedData != null)
        {
            var metadata = JsonSerializer.Deserialize<PushMetadata>(cachedData);
            
            // 双重过期检查
            if (metadata.ExpiresAt > DateTime.UtcNow)
            {
                // 回填到内存缓存
                _memoryCache.Set(cacheKey, metadata, TimeSpan.FromHours(2));
                return metadata;
            }
            else
            {
                // 已过期，从Redis删除
                await _distributedCache.RemoveAsync(cacheKey);
            }
        }
        
        // 3. pushId不存在或已过期 (正常情况)
        _logger.LogDebug("PushId {PushId} not found or expired", pushId);
        return null;
    }
    
    /// <summary>
    /// 更新pushId元数据 (标记已读等操作)
    /// </summary>
    public async Task UpdatePushMetadataAsync(string pushId, PushMetadata metadata)
    {
        var cacheKey = $"push_id:{pushId}";
        
        // 计算剩余TTL时间
        var remainingTtl = metadata.ExpiresAt - DateTime.UtcNow;
        if (remainingTtl <= TimeSpan.Zero)
        {
            _logger.LogWarning("Attempted to update expired pushId: {PushId}", pushId);
            return;
        }
        
        // 更新Redis
        await _distributedCache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(metadata),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = remainingTtl
            });
        
        // 更新内存缓存
        var memoryCacheTtl = TimeSpan.FromMinutes(Math.Min(remainingTtl.TotalMinutes, 120)); // 最多2小时
        _memoryCache.Set(cacheKey, metadata, memoryCacheTtl);
    }
}

[GenerateSerializer]
public class PushMetadata
{
    [Id(0)] public Guid UserId { get; set; }
    [Id(1)] public DeviceType DeviceType { get; set; }
    [Id(2)] public DateTime PushTime { get; set; }
    [Id(3)] public bool IsMorning { get; set; }
    [Id(4)] public bool IsRead { get; set; }
    [Id(5)] public DateTime? ReadTime { get; set; }
    [Id(6)] public DateTime ExpiresAt { get; set; }
}
```

### 3. 优化的已读标记实现

```csharp
public async Task<bool> MarkPushAsReadAsync(string pushToken, string pushId)
{
    // 1. 生成设备key
    var deviceKey = DeviceKeyGenerator.GenerateDeviceKey(pushToken);
    
    // 2. 快速本地缓存检查 (避免重复网络调用)
    var localCacheKey = $"read_local:{pushId}:{deviceKey}";
    if (_memoryCache.TryGetValue(localCacheKey, out bool alreadyRead) && alreadyRead)
    {
        _logger.LogDebug("Push {PushId} already marked as read (local cache)", pushId);
        return true; // 幂等返回成功
    }
    
    // 2. 获取pushId元数据 (可能返回null)
    var pushMetadata = await _pushIdManager.GetPushMetadataAsync(pushId);
    
    if (pushMetadata == null)
    {
        // pushId不存在或已过期 (48小时后的正常情况)
        _logger.LogInformation("Push {PushId} not found or expired", pushId);
        
        // 缓存"不存在"状态，避免重复查询过期pushId
        _memoryCache.Set(localCacheKey, true, TimeSpan.FromHours(1));
        return false; // 返回false表示pushId无效
    }
    
    // 3. 验证设备类型匹配
    if (pushMetadata.DeviceType != deviceType)
    {
        _logger.LogWarning("Push {PushId} device type mismatch: expected {Expected}, got {Actual}", 
            pushId, pushMetadata.DeviceType, deviceType);
        return false;
    }
    
    // 4. 幂等性检查 (避免重复标记)
    if (pushMetadata.IsRead)
    {
        _logger.LogDebug("Push {PushId} already marked as read", pushId);
        _memoryCache.Set(localCacheKey, true, TimeSpan.FromHours(1));
        return true; // 幂等返回成功
    }
    
    // 5. 标记为已读并更新元数据
    pushMetadata.IsRead = true;
    pushMetadata.ReadTime = DateTime.UtcNow;
    
    await _pushIdManager.UpdatePushMetadataAsync(pushId, pushMetadata);
    
    // 🆕 6. 记录当日推送已读状态 (用于下午重试判断)
    var todayKey = DateTime.UtcNow.ToString("yyyy-MM-dd");
    var dailyReadKey = $"daily_push_read:{pushMetadata.UserId}:{todayKey}";
    await _distributedCache.SetStringAsync(dailyReadKey, deviceType.ToString(), TimeSpan.FromHours(24));
    
    // 7. 缓存已读状态 (避免后续重复查询)
    _memoryCache.Set(localCacheKey, true, TimeSpan.FromHours(1));
    
    _logger.LogInformation("Push {PushId} marked as read for {DeviceType}, daily read status recorded", pushId, deviceType);
    return true;
}
```

### 4. 存储空间评估

```csharp
public class PushIdStorageAnalysis
{
    // 📊 存储容量分析
    public class StorageCapacityAnalysis
    {
        public int 每日推送数量 = 2_000_000; // 100万用户 × 2次推送
        public int 单个pushId数据大小 = 200; // pushId字符串 + 元数据 ≈ 200字节
        public long 每日新增存储 = 2_000_000 * 200; // 400MB/天
        public long 最大存储占用 = 400 * 2; // 800MB (48小时滚动窗口)
        public string 存储增长模式 = "稳定800MB，不会持续增长";
        public string 清理机制 = "Redis TTL自动删除，无需人工维护";
    }
    
    // 🔄 数据流转过程
    public class DataFlowLifecycle  
    {
        public string 阶段1_创建 = "发送推送时: 生成pushId → 存储Redis (48h TTL)";
        public string 阶段2_使用 = "48小时内: 支持幂等已读标记 → 内存+Redis双层缓存";
        public string 阶段3_过期 = "48小时后: Redis自动删除 → 无残留数据";
        public string 阶段4_统计 = "聚合数据: 推送统计写入MongoDB (不含pushId)";
        public string 存储特点 = "临时存储，自动清理，不影响长期容量规划";
    }
}
```

## 接口调用频率分析

```
简化后的接口调用频率评估：

📱 POST /api/push/device (设备注册/更新)：
├── 频率：极低频 (每用户每月0-3次)
├── 场景：应用安装、系统升级、时区变化、推送token刷新
├── 峰值：新版本发布时可能有波峰
├── 幂等策略：服务端比较数据变化，无变化时仅更新LastActiveTime
└── 优化：客户端智能缓存 + 服务端防重复 + 1小时频率限制

📊 POST /api/push/read/{pushId} (标记已读)：  
├── 频率：低频 (每用户每天0-2次)
├── 场景：用户点击推送通知时
├── 峰值：早上8点和下午3点推送后
├── 幂等策略：多层缓存检查 + 元数据状态验证
├── pushId有效期：48小时 (Redis TTL自动清理)
└── 优化：双层缓存 + 异步处理 + 批量统计

🔗 PushId管理特点：
├── 格式：daily_push_{userId}_{date}_{timeSlot}_{deviceType}
├── 存储：Redis临时存储 (48小时TTL)
├── 大小：最大800MB稳定存储，不会持续增长
├── 清理：自动过期删除，无需人工维护
└── 用途：仅用于48小时内的已读状态验证

🚫 简化掉的接口：
├── 设备时区单独更新：合并到设备注册接口
├── 推送开关设置：用户在系统设置中控制，无需应用层控制
├── 推送历史查询：查询价值低，增加复杂度
├── 设备列表查询：内部状态管理，无需暴露
└── 设备停用接口：通过注册接口的状态控制
```

## 总结

本系统设计提供了一个完整的、可扩展的每日推送通知解决方案，具有以下核心优势：

1. **架构简洁**: 扩展现有ChatManagerGAgent，避免引入新的复杂性
2. **接口精简**: 仅保留2个核心HTTP接口，覆盖所有使用场景
3. **时区智能**: 自动处理全球时区和夏令时变化
4. **高可用性**: 基于Orleans的分布式架构，支持故障恢复
5. **性能优化**: 分批处理、异步执行、智能缓存
6. **监控完善**: 全面的性能监控和告警机制
7. **易于维护**: 清晰的接口设计和文档

系统可以支持百万级用户规模，同时保持高性能和可靠性。通过渐进式的实施策略，可以平滑地集成到现有系统中，为用户提供优质的推送通知体验。

## 语言获取机制优化

### 复用现有的HTTP语言头机制

基于现有系统架构，推送系统直接复用 `HttpContext.GetGodGPTLanguage()` 机制：

```csharp
// 前端发送HTTP请求时包含语言头
Headers: {
    "GodgptLanguage": "zh-tw"  // 或 "en", "es"
}

// API层自动获取语言并传递给Orleans
var language = HttpContext.GetGodGPTLanguage();
RequestContext.Set("GodGPTLanguage", language.ToString());

// Grain层使用保存的语言偏好进行推送
var userLanguage = await GetDevicePushLanguageAsync(DeviceType.iOS);
```

### 语言获取优先级

```
设备级语言管理流程：
1. 设备注册场景: HTTP请求头 "GodgptLanguage" → 更新该设备的 PushLanguage
2. 定时推送场景: 分别读取iOS/Android设备的 PushLanguage 设置
3. 多设备支持: 同一用户的iOS和Android可以有不同语言设置
4. 默认语言: 新设备默认英语
```

### 设备级语言策略

- **设备注册时**: 从HTTP请求头获取 `"GodgptLanguage"` → 保存到对应设备的 `PushLanguage`
- **定时推送时**: 分别获取每个设备的 `PushLanguage` 设置
- **独立管理**: iOS设备和Android设备的语言设置完全独立
- **真实场景**: 支持用户iPad设置英语、iPhone设置中文的场景

## 设备级语言管理实际场景

### 真实多设备语言场景

```csharp
// 用户同时拥有iPhone和iPad的典型场景
用户张三的设备设置：
├── iPhone (中国版iOS)
│   ├── 系统语言: 简体中文  
│   ├── APP语言: 繁体中文 (手动设置)
│   └── PushLanguage: "zh-tw" (从APP语言头获取)
│
└── iPad (国际版iOS)  
    ├── 系统语言: English
    ├── APP语言: English (默认)
    └── PushLanguage: "en" (从APP语言头获取)

推送结果：
- iPhone: 收到繁体中文推送 📱🇹🇼
- iPad: 收到英语推送 📱🇺🇸
```

### 设备注册时序图

```
iPhone注册:
Client → POST /api/push/device 
       → Headers: {"GodgptLanguage": "zh-tw"}
       → UpdateDevicePushLanguageFromContextAsync(iOS)
       → State.IOSDevice.PushLanguage = "zh-tw"

iPad注册:  
Client → POST /api/push/device
       → Headers: {"GodgptLanguage": "en"}
       → UpdateDevicePushLanguageFromContextAsync(iOS) 
       → State.IOSDevice.PushLanguage = "en" (覆盖更新)

Android手机注册:
Client → POST /api/push/device
       → Headers: {"GodgptLanguage": "es"}  
       → UpdateDevicePushLanguageFromContextAsync(Android)
       → State.AndroidDevice.PushLanguage = "es"

定时推送时:
- iOS设备: GetDevicePushLanguageAsync(iOS) → "en" (最后注册的iPad语言)
- Android设备: GetDevicePushLanguageAsync(Android) → "es"
```

### 语言更新机制

```csharp
public class DeviceLanguageUpdateScenarios
{
    // 场景1: 用户在iPhone上切换APP语言
    public async Task UpdateiPhoneLanguage()
    {
        // iPhone发起设备注册 (语言已切换为西班牙语)
        POST /api/push/device
        Headers: { "GodgptLanguage": "es" }
        DeviceType: iOS
        
        // 结果: State.IOSDevice.PushLanguage = "es"
        // iPad不受影响，仍然是之前的语言设置
    }
    
    // 场景2: 用户仅在Android设备上使用APP
    public async Task AndroidOnlyUser()
    {
        // 只有Android设备注册
        State.IOSDevice = null;
        State.AndroidDevice.PushLanguage = "zh-tw";
        
        // 推送时只向Android发送繁体中文推送
        // iOS推送跳过 (设备不存在)
    }
    
    // 场景3: 跨平台语言偏好
    public async Task CrossPlatformLanguagePreference()
    {
        // 业务场景: 用户工作设备用英语，个人设备用中文
        State.IOSDevice.PushLanguage = "zh-tw";    // 个人iPhone
        State.AndroidDevice.PushLanguage = "en";   // 工作Android
        
        // 定时推送: 两个设备收到不同语言的相同内容
    }
}
```

### 5. TimezoneSchedulerGAgent 推送触发实现

```csharp
public class TimezoneSchedulerGAgent : GAgent<TimezoneSchedulerGAgentState>, ITimezoneSchedulerGAgent
{
    private readonly string _timeZoneId;
    private readonly ILogger<TimezoneSchedulerGAgent> _logger;
    
    public TimezoneSchedulerGAgent(ILogger<TimezoneSchedulerGAgent> logger)
    {
        _timeZoneId = this.GetPrimaryKeyString(); // "Asia/Shanghai", "America/New_York"
        _logger = logger;
    }
    
    public async Task ProcessMorningPushAsync(DateTime targetDate)
    {
        _logger.LogInformation("Processing morning push for timezone {TimeZone} on {Date}", 
            _timeZoneId, targetDate);
        
        // 1. 获取每日内容
        var contentGAgent = GrainFactory.GetGrain<IDailyContentGAgent>("default");
        var dailyContents = await contentGAgent.GetSmartSelectedContentsAsync(2, targetDate);
        
        if (!dailyContents.Any())
        {
            _logger.LogWarning("No daily content available for {Date}", targetDate);
            return;
        }
        
        // 2. 获取该时区的用户索引
        var timezoneIndexGAgent = GrainFactory.GetGrain<ITimezoneUserIndexGAgent>(_timeZoneId);
        
        // 3. 分批处理用户 (避免一次性加载太多用户)
        const int batchSize = 1000;
        int skip = 0;
        int processedUsers = 0;
        List<Guid> userBatch;
        
        do
        {
            // 获取一批活跃用户
            userBatch = await timezoneIndexGAgent.GetActiveUsersInTimezoneAsync(skip, batchSize);
            
            if (userBatch.Any())
            {
                // 并行处理这批用户的推送
                await ProcessUserBatchAsync(userBatch, dailyContents, targetDate);
                
                processedUsers += userBatch.Count;
                skip += batchSize;
                
                _logger.LogDebug("Processed {Count} users in timezone {TimeZone}, total: {Total}", 
                    userBatch.Count, _timeZoneId, processedUsers);
            }
            
        } while (userBatch.Count == batchSize); // 继续直到没有更多用户
        
        _logger.LogInformation("Completed morning push for timezone {TimeZone}: {Total} users processed", 
            _timeZoneId, processedUsers);
    }
    
    private async Task ProcessUserBatchAsync(List<Guid> userIds, List<DailyNotificationContent> contents, DateTime targetDate)
    {
        // 并行处理用户推送 (限制并发数避免过载)
        var semaphore = new SemaphoreSlim(50); // 最多50个并发
        var pushTasks = userIds.Select(async userId =>
        {
            await semaphore.WaitAsync();
            try
            {
                await ProcessSingleUserPushAsync(userId, contents, targetDate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process push for user {UserId}", userId);
            }
            finally
            {
                semaphore.Release();
            }
        });
        
        await Task.WhenAll(pushTasks);
    }
    
    private async Task ProcessSingleUserPushAsync(Guid userId, List<DailyNotificationContent> contents, DateTime targetDate)
    {
        var chatManagerGAgent = GrainFactory.GetGrain<IChatManagerGAgent>(userId);
        
        // 验证用户在此时区确实有活跃设备 (双重检查)
        if (await HasActiveDeviceInThisTimezone(chatManagerGAgent))
        {
            await chatManagerGAgent.ProcessDailyPushAsync(targetDate, contents);
        }
        else
        {
            _logger.LogDebug("User {UserId} has no active devices in timezone {TimeZone}, skipping", 
                userId, _timeZoneId);
        }
    }
    
    private async Task<bool> HasActiveDeviceInThisTimezone(IChatManagerGAgent chatManager)
    {
        // 检查用户是否在当前时区有活跃设备
        // 这是一个安全检查，防止索引数据不同步
        var iosTimezone = await chatManager.GetCurrentDeviceTimezoneAsync(DeviceType.iOS);
        var androidTimezone = await chatManager.GetCurrentDeviceTimezoneAsync(DeviceType.Android);
        
        return iosTimezone == _timeZoneId || androidTimezone == _timeZoneId;
    }
    
    public async Task ProcessAfternoonRetryAsync(DateTime targetDate)
    {
        _logger.LogInformation("Processing afternoon retry push for timezone {TimeZone} on {Date}", 
            _timeZoneId, targetDate);
        
        // 获取相同的每日内容 (与早晨推送保持一致)
        var contentGAgent = GrainFactory.GetGrain<IDailyContentGAgent>("default");
        var dailyContents = await contentGAgent.GetSmartSelectedContentsAsync(2, targetDate);
        
        if (!dailyContents.Any())
        {
            _logger.LogWarning("No daily content available for afternoon retry on {Date}", targetDate);
            return;
        }
        
        // 获取该时区的用户索引
        var timezoneIndexGAgent = GrainFactory.GetGrain<ITimezoneUserIndexGAgent>(_timeZoneId);
        
        // 分批处理用户 (只对未读早晨推送的用户进行重试)
        const int batchSize = 1000;
        int skip = 0;
        int processedUsers = 0;
        int retryUsers = 0;
        List<Guid> userBatch;
        
        do
        {
            userBatch = await timezoneIndexGAgent.GetActiveUsersInTimezoneAsync(skip, batchSize);
            
            if (userBatch.Any())
            {
                var batchRetryCount = await ProcessAfternoonRetryBatchAsync(userBatch, dailyContents, targetDate);
                
                processedUsers += userBatch.Count;
                retryUsers += batchRetryCount;
                skip += batchSize;
            }
            
        } while (userBatch.Count == batchSize);
        
        _logger.LogInformation("Completed afternoon retry for timezone {TimeZone}: {RetryUsers}/{TotalUsers} users needed retry", 
            _timeZoneId, retryUsers, processedUsers);
    }
    
    private async Task<int> ProcessAfternoonRetryBatchAsync(List<Guid> userIds, List<DailyNotificationContent> contents, DateTime targetDate)
    {
        var semaphore = new SemaphoreSlim(50);
        var retryCount = 0;
        var retryTasks = userIds.Select(async userId =>
        {
            await semaphore.WaitAsync();
            try
            {
                var chatManagerGAgent = GrainFactory.GetGrain<IChatManagerGAgent>(userId);
                
                // 检查是否需要下午重试
                if (await chatManagerGAgent.ShouldSendAfternoonRetryAsync(targetDate))
                {
                    await chatManagerGAgent.ProcessAfternoonRetryAsync(targetDate, contents);
                    Interlocked.Increment(ref retryCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process afternoon retry for user {UserId}", userId);
            }
            finally
            {
                semaphore.Release();
            }
        });
        
        await Task.WhenAll(retryTasks);
        return retryCount;
    }
}
```

### 6. TimezoneUserIndexGAgent 索引实现

```csharp
public class TimezoneUserIndexGAgent : GAgent<TimezoneUserIndexGAgentState>, ITimezoneUserIndexGAgent
{
    public TimezoneUserIndexGAgent()
    {
        // 初始化State
        if (string.IsNullOrEmpty(State.TimeZoneId))
        {
            State.TimeZoneId = this.GetPrimaryKeyString();
        }
    }
    
    public async Task AddUserToTimezoneAsync(Guid userId, string timeZoneId)
    {
        if (timeZoneId != State.TimeZoneId)
        {
            throw new ArgumentException($"TimeZone mismatch: expected {State.TimeZoneId}, got {timeZoneId}");
        }
        
        if (State.ActiveUsers.Add(userId))
        {
            // 简单状态更新，无事件，GAgent自动持久化
            State.LastUpdated = DateTime.UtcNow;
            State.TotalUserCount = State.ActiveUsers.Count;
            
            _logger.LogInformation("Added user {UserId} to timezone {TimeZone} index", 
                userId, State.TimeZoneId);
        }
    }
    
    public async Task RemoveUserFromTimezoneAsync(Guid userId)
    {
        if (State.ActiveUsers.Remove(userId))
        {
            // 简单状态更新，无事件，GAgent自动持久化
            State.LastUpdated = DateTime.UtcNow;
            State.TotalUserCount = State.ActiveUsers.Count;
            
            _logger.LogInformation("Removed user {UserId} from timezone {TimeZone} index", 
                userId, State.TimeZoneId);
        }
    }
    
    public async Task BatchUpdateUsersAsync(List<TimezoneUpdateRequest> updates)
    {
        // 批量操作，减少GAgent之间的调用次数
        var changesCount = 0;
        
        foreach (var update in updates)
        {
            if (update.TargetTimezone == State.TimeZoneId)
            {
                if (State.ActiveUsers.Add(update.UserId))
                {
                    changesCount++;
                }
            }
            else if (update.SourceTimezone == State.TimeZoneId)
            {
                if (State.ActiveUsers.Remove(update.UserId))
                {
                    changesCount++;
                }
            }
        }
        
        if (changesCount > 0)
        {
            State.LastUpdated = DateTime.UtcNow;
            State.TotalUserCount = State.ActiveUsers.Count;
            
            _logger.LogInformation("Batch updated {Count} users in timezone {TimeZone}", 
                changesCount, State.TimeZoneId);
        }
    }
    
    public async Task<List<Guid>> GetActiveUsersInTimezoneAsync(int skip, int take)
    {
        return State.ActiveUsers.Skip(skip).Take(take).ToList();
    }
    
    public async Task<int> GetActiveUserCountAsync()
    {
        return State.ActiveUsers.Count;
    }
    
    public async Task<bool> HasActiveDeviceInTimezoneAsync(Guid userId)
    {
        return State.ActiveUsers.Contains(userId);
    }
}
```

## GAgent性能优化策略

### 🚀 减少ConfirmAsync和RaiseEventAsync调用

#### **1. 移除不必要的事件**
```csharp
// ❌ 避免：频繁的事件调用
await RaiseEventAsync(new UserAddedToTimezoneEvent(userId, timeZoneId));
await RaiseEventAsync(new UserRemovedFromTimezoneEvent(userId, State.TimeZoneId));

// ✅ 推荐：简单状态变更无需事件
State.ActiveUsers.Add(userId);               // 直接状态修改
State.HasPendingChanges = true;              // 标记待持久化
ScheduleDelayedPersistence();                // 延迟批量持久化
```

#### **2. GAgent自动持久化**
```csharp
// ✅ 推荐：依赖GAgent框架的自动持久化
State.ActiveUsers.Add(userId);          // 状态变更
State.LastUpdated = DateTime.UtcNow;    // 更新时间戳
State.TotalUserCount = State.ActiveUsers.Count; // 计数更新
// GAgent框架自动检测状态变更并持久化到MongoDB
```

#### **3. 批量操作减少调用**
```csharp
// ❌ 避免：单用户循环调用
foreach (var userId in userIds)
{
    await timezoneIndexGAgent.AddUserToTimezoneAsync(userId, timezone);  // N次调用
}

// ✅ 推荐：批量操作
var updates = userIds.Select(id => new TimezoneUpdateRequest 
{ 
    UserId = id, 
    TargetTimezone = timezone 
}).ToList();
await timezoneIndexGAgent.BatchUpdateUsersAsync(updates);  // 1次调用
```

#### **4. 性能收益分析**

| 优化前 | 优化后 | 性能提升 |
|--------|--------|----------|
| 每次时区切换2次RaiseEventAsync | 0次RaiseEventAsync | **100%减少事件** |
| 单用户操作 | 批量操作 | **90%减少网络调用** |
| 复杂事件处理 | 简单状态更新 | **80%减少CPU开销** |

#### **5. 简化设计原则**
```csharp
// 内存优先策略：高频读取，依赖GAgent自动持久化
public Task<List<Guid>> GetActiveUsersInTimezoneAsync(int skip, int take)
{
    return Task.FromResult(State.ActiveUsers.Skip(skip).Take(take).ToList());
    // 直接从内存返回，无异步开销
}

// 简单状态管理：让GAgent框架处理持久化复杂性
State.ActiveUsers.Add(userId);      // 状态变更
State.LastUpdated = DateTime.UtcNow; // 时间戳
// Orleans GAgent自动检测变更并适时持久化
```

## 多语言内容管理实现示例

### 1. Excel文件格式示例

#### 多列模式 (推荐)
```
| ContentKey    | EN_Title        | EN_Content           | ZH_Title    | ZH_Content       | ES_Title        | ES_Content           | Priority |
|---------------|-----------------|----------------------|-------------|------------------|-----------------|----------------------|----------|
| daily_tip_001 | Good Morning!   | Start your day...    | 早安！      | 开始美好的一天... | ¡Buenos días!   | Comienza tu día...   | 5        |
| daily_tip_002 | Health Tip      | Drink more water...  | 健康小贴士  | 多喝水...        | Consejo salud   | Bebe más agua...     | 3        |
```

#### 多Sheet模式
```
Sheet "EN":
| ContentKey    | Title          | Content              | Priority |
|---------------|----------------|----------------------|----------|
| daily_tip_001 | Good Morning!  | Start your day...    | 5        |

Sheet "ZH-TW":
| ContentKey    | Title    | Content         | Priority |
|---------------|----------|-----------------|----------|
| daily_tip_001 | 早安！   | 开始美好的一天... | 5        |
```

### 2. 推送处理中的语言选择逻辑

```csharp
public class ChatManagerGAgent : IChatManagerGAgent
{
    public async Task ProcessDailyPushAsync(DateTime targetDate, List<DailyNotificationContent> contents)
    {
        // 为每个设备发送本地化推送 (每个设备独立语言设置)
        var pushTasks = new List<Task>();
        
        // 检查iOS设备推送开关
        if (State.IOSDevice?.IsActive == true && State.IOSDevice.PushEnabled)
        {
            var iosLanguage = await GetDevicePushLanguageAsync(DeviceType.iOS);
            pushTasks.Add(SendLocalizedPushToDevice(State.IOSDevice, contents, iosLanguage, targetDate, DeviceType.iOS));
        }
        
        // 检查Android设备推送开关
        if (State.AndroidDevice?.IsActive == true && State.AndroidDevice.PushEnabled)
        {
            var androidLanguage = await GetDevicePushLanguageAsync(DeviceType.Android);
            pushTasks.Add(SendLocalizedPushToDevice(State.AndroidDevice, contents, androidLanguage, targetDate, DeviceType.Android));
        }
        
        if (pushTasks.Count == 0)
        {
            _logger.LogDebug("User {UserId} has no devices with push enabled, skipping daily push", 
                this.GetPrimaryKey());
        }
        else
        {
            await Task.WhenAll(pushTasks);
        }
    }
    
    public async Task<bool> ShouldSendAfternoonRetryAsync(DateTime targetDate)
    {
        // 检查当日是否有任何设备已读早晨推送
        var todayKey = targetDate.ToString("yyyy-MM-dd");
        
        // 检查Redis中的已读记录
        var cacheKey = $"daily_push_read:{this.GetPrimaryKey()}:{todayKey}";
        var readStatus = await _distributedCache.GetStringAsync(cacheKey);
        
        // 如果没有已读记录，说明需要下午重试
        return string.IsNullOrEmpty(readStatus);
    }
    
    public async Task ProcessAfternoonRetryAsync(DateTime targetDate, List<DailyNotificationContent> contents)
    {
        // 只对未读早晨推送的用户发送下午重试
        if (await ShouldSendAfternoonRetryAsync(targetDate))
        {
            _logger.LogInformation("Sending afternoon retry push for user {UserId} on {Date}", 
                this.GetPrimaryKey(), targetDate);
            
            await ProcessDailyPushAsync(targetDate, contents);
        }
        else
        {
            _logger.LogDebug("User {UserId} already read morning push on {Date}, skipping afternoon retry", 
                this.GetPrimaryKey(), targetDate);
        }
    }
    
    public async Task<string?> GetCurrentDeviceTimezoneAsync(DeviceType deviceType)
    {
        var targetDevice = deviceType == DeviceType.iOS ? State.IOSDevice : State.AndroidDevice;
        return targetDevice?.TimeZoneId;
    }
    
    public async Task UpdateTimezoneIndexAsync(DeviceType deviceType, string? oldTimeZone, string newTimeZone)
    {
        var userId = this.GetPrimaryKey();
        
        // 1. 如果有旧时区，从旧时区索引中移除
        if (!string.IsNullOrEmpty(oldTimeZone) && oldTimeZone != newTimeZone)
        {
                    var oldIndexGAgent = GrainFactory.GetGrain<ITimezoneUserIndexGAgent>(oldTimeZone);
        await oldIndexGAgent.RemoveUserFromTimezoneAsync(userId);
    }
    
    // 2. 添加到新时区索引
    var newIndexGAgent = GrainFactory.GetGrain<ITimezoneUserIndexGAgent>(newTimeZone);
        await newIndexGAgent.AddUserToTimezoneAsync(userId, newTimeZone);
        
        _logger.LogInformation("Updated timezone index for user {UserId}: {OldTz} → {NewTz}", 
            userId, oldTimeZone, newTimeZone);
    }

    public async Task UpdateDevicePushLanguageFromContextAsync(DeviceType deviceType)
    {
        // 从RequestContext获取语言 (来自HTTP请求头)
        var contextLanguage = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
        var languageCode = contextLanguage switch
        {
            GodGPTLanguage.English => "en",
            GodGPTLanguage.TraditionalChinese => "zh-tw",
            GodGPTLanguage.Spanish => "es",
            _ => "en"
        };
        
        // 获取目标设备并更新语言设置
        var targetDevice = deviceType == DeviceType.iOS ? State.IOSDevice : State.AndroidDevice;
        if (targetDevice != null && targetDevice.PushLanguage != languageCode)
        {
            targetDevice.PushLanguage = languageCode;
            await WriteStateAsync();
            _logger.LogInformation("Updated {DeviceType} push language to: {Language} for user: {UserId}", 
                deviceType, languageCode, this.GetPrimaryKey());
        }
    }
    
    /// <summary>
    /// 获取设备推送语言设置 (用于定时推送，无HTTP上下文时)
    /// </summary>
    public async Task<string> GetDevicePushLanguageAsync(DeviceType deviceType)
    {
        var targetDevice = deviceType == DeviceType.iOS ? State.IOSDevice : State.AndroidDevice;
        
        // 返回设备的语言设置，如果设备不存在则返回默认英语
        return targetDevice?.PushLanguage ?? "en";
    }
    
    public async Task SetDevicePushEnabledAsync(DeviceType deviceType, bool enabled)
    {
        var targetDevice = deviceType == DeviceType.iOS ? State.IOSDevice : State.AndroidDevice;
        
        if (targetDevice != null)
        {
            var oldEnabled = targetDevice.PushEnabled;
            targetDevice.PushEnabled = enabled;
            
            if (!enabled)
            {
                targetDevice.PushDisabledAt = DateTime.UtcNow;
            }
            else if (!oldEnabled)
            {
                targetDevice.PushDisabledAt = null; // 重新启用时清除关闭时间
            }
            
            _logger.LogInformation("Device push settings updated: User {UserId}, Device {DeviceType}, Enabled: {Enabled}", 
                this.GetPrimaryKey(), deviceType, enabled);
        }
        else
        {
            _logger.LogWarning("Cannot set push settings: Device {DeviceType} not found for user {UserId}", 
                deviceType, this.GetPrimaryKey());
        }
    }
    
    public async Task<bool> IsDevicePushEnabledAsync(DeviceType deviceType)
    {
        var targetDevice = deviceType == DeviceType.iOS ? State.IOSDevice : State.AndroidDevice;
        return targetDevice?.PushEnabled == true && targetDevice.IsActive;
    }
    
    private async Task SendLocalizedPushToDevice(
        UserDeviceInfo device, 
        List<DailyNotificationContent> contents, 
        string languageCode,
        DateTime targetDate,
        DeviceType deviceType)
    {
        foreach (var content in contents)
        {
            // 获取本地化内容
            var localizedContent = content.GetLocalizedContent(languageCode);
            
            // 生成pushId
            var pushId = await _pushIdManager.CreatePushIdAsync(
                State.UserId, 
                targetDate, 
                isMorning: DateTime.UtcNow.Hour < 12, 
                deviceType);
            
            // 构建Firebase消息
            var message = new Message
            {
                Token = device.PushToken,
                Notification = new Notification
                {
                    Title = localizedContent.Title,
                    Body = localizedContent.Content,
                    ImageUrl = GetNotificationImageUrl(content) // 可选：推送大图
                },
                Data = new Dictionary<string, string>
                {
                    ["pushType"] = ((int)PushType.DailyPush).ToString()
                },
                Android = new AndroidConfig
                {
                    Priority = Priority.High
                    // ❌ 暂时不需要自定义视觉配置，使用Firebase默认
                },
                Apns = new ApnsConfig
                {
                    Headers = new Dictionary<string, string>
                    {
                        ["apns-priority"] = "10"
                        // ❌ 暂时不需要collapse-id等自定义配置
                    }
                    // ❌ 暂时不需要自定义Payload，使用Firebase默认
                }
            };
            
            // 发送推送
            try
            {
                var response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
                _logger.LogInformation("Push sent successfully: {PushId}, Response: {Response}", pushId, response);
                
                // 记录推送状态
                await RecordPushStatus(targetDate, deviceType, pushId, PushDeliveryStatus.Sent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send push: {PushId}", pushId);
                await RecordPushStatus(targetDate, deviceType, pushId, PushDeliveryStatus.Failed);
            }
        }
    }
    
    // 删除时区推断逻辑，因为现在直接从HTTP请求头获取语言
}
```

### 3. 内容导入实现示例

```csharp
public class DailyContentGrain : IDailyContentGrain
{
    public async Task ImportFromExcelAsync(byte[] excelContent, MultiLanguageImportOptions options)
    {
        using var stream = new MemoryStream(excelContent);
        using var package = new ExcelPackage(stream);
        
        switch (options.ImportMode)
        {
            case ExcelImportMode.MultiColumn:
                await ImportMultiColumnExcel(package, options);
                break;
                
            case ExcelImportMode.MultiSheet:
                await ImportMultiSheetExcel(package, options);
                break;
                
            case ExcelImportMode.Hybrid:
                await ImportHybridExcel(package, options);
                break;
        }
        
        await WriteStateAsync();
    }
    
    private async Task ImportMultiColumnExcel(ExcelPackage package, MultiLanguageImportOptions options)
    {
        var worksheet = package.Workbook.Worksheets[options.SheetName];
        var rowCount = worksheet.Dimension.Rows;
        
        for (int row = options.StartRow; row <= rowCount; row++)
        {
            // 读取ContentKey
            var contentKey = worksheet.Cells[$"{options.ContentKeyColumn}{row}"].Value?.ToString();
            if (string.IsNullOrEmpty(contentKey)) continue;
            
            var localizedContents = new Dictionary<string, LocalizedContentData>();
            
            // 读取各语言内容
            foreach (var (languageCode, columnConfig) in options.ColumnMapping)
            {
                var title = worksheet.Cells[$"{columnConfig.TitleColumn}{row}"].Value?.ToString();
                var content = worksheet.Cells[$"{columnConfig.ContentColumn}{row}"].Value?.ToString();
                
                if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(content))
                {
                    localizedContents[languageCode] = new LocalizedContentData
                    {
                        Title = title,
                        Content = content,
                        Language = languageCode,
                        LastUpdated = DateTime.UtcNow,
                        Status = ContentStatus.Active
                    };
                }
            }
            
            // 读取优先级
            var priority = options.DefaultPriority;
            if (options.ColumnMapping.Values.FirstOrDefault()?.PriorityColumn != null)
            {
                var priorityCell = worksheet.Cells[$"{options.ColumnMapping.Values.First().PriorityColumn}{row}"];
                if (int.TryParse(priorityCell.Value?.ToString(), out var parsedPriority))
                {
                    priority = parsedPriority;
                }
            }
            
            // 创建多语言内容
            if (localizedContents.Any())
            {
                await AddMultiLanguageContentAsync(contentKey, localizedContents, priority);
            }
        }
    }
    
    /// <summary>
    /// 导入标准Excel格式 (适配用户提供的格式)
    /// Excel列结构: A=contentKey, B=title_en, C=content_en, D=title_zh, E=content_zh, F=title_es, G=content_es
    /// </summary>
    private async Task ImportStandardExcel(ExcelPackage package)
    {
        var worksheet = package.Workbook.Worksheets[0]; // 第一个sheet
        var rowCount = worksheet.Dimension.Rows;
        
        for (int row = 2; row <= rowCount; row++) // 从第2行开始，第1行是标题
        {
            var contentKey = worksheet.Cells[$"A{row}"].Value?.ToString();
            if (string.IsNullOrEmpty(contentKey)) continue;
            
            var localizedContents = new Dictionary<string, LocalizedContentData>();
            
            // ✅ 英文内容 (B列=title_en, C列=content_en)
            var titleEn = worksheet.Cells[$"B{row}"].Value?.ToString();
            var contentEn = worksheet.Cells[$"C{row}"].Value?.ToString();
            if (!string.IsNullOrEmpty(titleEn) && !string.IsNullOrEmpty(contentEn))
            {
                localizedContents["en"] = new LocalizedContentData
                {
                    Title = titleEn,
                    Content = contentEn,
                    Language = "en",
                    LastUpdated = DateTime.UtcNow,
                    Status = ContentStatus.Active
                };
            }
            
            // ✅ 繁体中文内容 (D列=title_zh, E列=content_zh)
            var titleZh = worksheet.Cells[$"D{row}"].Value?.ToString();
            var contentZh = worksheet.Cells[$"E{row}"].Value?.ToString();
            if (!string.IsNullOrEmpty(titleZh) && !string.IsNullOrEmpty(contentZh))
            {
                localizedContents["zh-tw"] = new LocalizedContentData
                {
                    Title = titleZh,
                    Content = contentZh,
                    Language = "zh-tw",
                    LastUpdated = DateTime.UtcNow,
                    Status = ContentStatus.Active
                };
            }
            
            // ✅ 西班牙语内容 (F列=title_es, G列=content_es)
            var titleEs = worksheet.Cells[$"F{row}"].Value?.ToString();
            var contentEs = worksheet.Cells[$"G{row}"].Value?.ToString();
            if (!string.IsNullOrEmpty(titleEs) && !string.IsNullOrEmpty(contentEs))
            {
                localizedContents["es"] = new LocalizedContentData
                {
                    Title = titleEs,
                    Content = contentEs,
                    Language = "es",
                    LastUpdated = DateTime.UtcNow,
                    Status = ContentStatus.Active
                };
            }
            
            // 只有至少有一种语言的内容才创建记录
            if (localizedContents.Any())
            {
                var priority = CalculatePriority(localizedContents.Count); // 根据语言数量计算优先级
                await AddMultiLanguageContentAsync(contentKey, localizedContents, priority);
            }
        }
    }
}
```

### 4. 语言覆盖率监控

```csharp
public class ContentLanguageAnalytics
{
    public static ContentStatsDto AnalyzeLanguageCompleteness(List<DailyNotificationContent> contents)
    {
        var supportedLanguages = new HashSet<string>();
        var languageContentCounts = new Dictionary<string, int>();
        
        foreach (var content in contents.Where(c => c.IsActive))
        {
            foreach (var (lang, data) in content.LocalizedContents.Where(kv => kv.Value.Status == ContentStatus.Active))
            {
                supportedLanguages.Add(lang);
                languageContentCounts[lang] = languageContentCounts.GetValueOrDefault(lang, 0) + 1;
            }
        }
        
        var totalContents = contents.Count(c => c.IsActive);
        var languageCompleteness = supportedLanguages.ToDictionary(
            lang => lang,
            lang => totalContents > 0 ? (double)languageContentCounts[lang] / totalContents * 100 : 0
        );
        
        return new ContentStatsDto
        {
            TotalCount = contents.Count,
            ActiveCount = totalContents,
            SupportedLanguages = supportedLanguages.ToList(),
            LanguageContentCounts = languageContentCounts,
            LanguageCompleteness = languageCompleteness
        };
    }
}
```

## 🧪 QA测试支持

### 1. 生产配置常量

```csharp
/// <summary>
/// 每日推送系统常量配置
/// </summary>
public static class DailyPushConstants
{
    // === 推送时间配置 ===
    public const int MORNING_HOUR = 8;              // 早上推送时间
    public const int AFTERNOON_HOUR = 15;           // 下午重试时间
    public const int SECOND_PUSH_DELAY_SECONDS = 180; // 第二条推送延迟(3分钟=180秒)
    
    // === 批量处理配置 ===
    public const int BATCH_SIZE = 100;              // 批量推送大小
    
    // === Redis TTL配置 ===
    public static readonly TimeSpan DAILY_READ_TTL = TimeSpan.FromHours(48);     // 已读状态48小时
    public static readonly TimeSpan CONTENT_USAGE_TTL = TimeSpan.FromDays(7);    // 内容使用记录7天
    
    // ❌ 推送视觉配置暂时不需要 - 使用Firebase和前端默认配置
}

/// <summary>
/// 每日推送配置模型 (极简版)
/// </summary>
public class DailyPushConfig
{
    // ❌ 暂时不需要任何配置项 - 全部使用默认值和硬编码常量
}

/// <summary>
/// 推送服务类构造器示例 (极简版)
/// </summary>
public class PushNotificationService
{
    // ❌ 暂时不需要任何推送相关配置注入
    // 所有配置都使用硬编码常量或Firebase默认值
    
    public PushNotificationService()
    {
        // 无需配置注入
    }
}
```

### 2. 测试常量配置

```csharp
/// <summary>
/// 🧪 测试专用常量类 - 上线前修改IS_TEST_MODE为false
/// </summary>
public static class TestConstants
{
    // 🎯 测试开关 - 上线前改为 false
    public const bool IS_TEST_MODE = true;
    
    // 🎯 测试时间间隔 - 上线前这些都不会生效
    public const int TEST_CYCLE_MINUTES = 2;           // 2分钟一个完整周期
    public const int SECOND_PUSH_DELAY_SECONDS = 30;   // 第二条推送延迟30秒
    public const int AFTERNOON_RETRY_DELAY_SECONDS = 90; // 下午重试延迟1.5分钟
    
    // 🎯 正式环境时间（测试模式下不使用）
    public const int PRODUCTION_MORNING_HOUR = 8;
    public const int PRODUCTION_AFTERNOON_HOUR = 15;
    public const int PRODUCTION_SECOND_PUSH_DELAY_MINUTES = 3;
}
```

### 2. 测试时间线 (2分钟验证)

```
🧪 测试模式时间轴:
00:00 - 启动应用，触发第一轮测试
00:00 - 发送第一条推送 🧪 [TEST] 每日精選內容 (1/2)
00:30 - 发送第二条推送 🧪 [TEST] 每日精選內容 (2/2)  
01:30 - 发送下午重试（如果未读）
02:00 - 开始下一轮测试循环

🎯 生产模式:
08:00 - 发送第一条推送
08:03 - 发送第二条推送
15:00 - 下午重试（如果未读）
```

### 3. 测试内容生成 (适配Excel格式)

```csharp
// 🧪 测试用的虚拟内容 (使用Excel格式的contentKey)
private List<DailyNotificationContent> CreateDummyTestContent()
{
    return new List<DailyNotificationContent>
    {
        new DailyNotificationContent
        {
            Id = "test_task01", // 对应Excel的contentKey格式
            LocalizedContents = new Dictionary<string, LocalizedContentData>
            {
                ["en"] = new LocalizedContentData 
                { 
                    Title = "🧪 Test: Three-minute breath return", 
                    Content = "This is test content for QA testing daily push feature",
                    Language = "en"
                },
                ["zh-tw"] = new LocalizedContentData 
                { 
                    Title = "🧪 測試：三分鐘回歸呼吸", 
                    Content = "這是QA測試每日推送功能用的內容",
                    Language = "zh-tw"
                },
                ["es"] = new LocalizedContentData 
                { 
                    Title = "🧪 Prueba: Regreso de tres minutos a la respiración", 
                    Content = "Este es contenido de prueba para QA de notificaciones diarias",
                    Language = "es"
                }
            },
            Priority = 1,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        },
        new DailyNotificationContent
        {
            Id = "test_task03", // 对应Excel的contentKey格式
            LocalizedContents = new Dictionary<string, LocalizedContentData>
            {
                ["en"] = new LocalizedContentData 
                { 
                    Title = "🧪 Test: Listen to the silent voice", 
                    Content = "This is second test content for QA testing daily push feature",
                    Language = "en"
                },
                ["zh-tw"] = new LocalizedContentData 
                { 
                    Title = "🧪 測試：靜聽無聲的音韻", 
                    Content = "這是第二個QA測試每日推送功能用的內容",
                    Language = "zh-tw"
                },
                ["es"] = new LocalizedContentData 
                { 
                    Title = "🧪 Prueba: Escucha la voz silenciosa", 
                    Content = "Este es el segundo contenido de prueba para QA de notificaciones diarias",
                    Language = "es"
                }
            },
            Priority = 2,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        }
    };
}
```

### 4. 上线前清理

```csharp
// 🎯 上线前只需要改这一行：
public const bool IS_TEST_MODE = false; // ✅ 改为 false

// ✅ 其他测试常量会自动失效，测试日志和接口也会停止工作
// ✅ 生产环境会自动使用正常的8:00和15:00时间
// ✅ 推送标题不再显示🧪 [TEST]前缀
// ✅ payload中isTestMode字段会变为false
```

---

*文档版本: v2.2 (简化已读逻辑 + pushType枚举 + 测试支持)*  
*最后更新: 2024年*  
*作者: HyperEcho 系统架构团队*
