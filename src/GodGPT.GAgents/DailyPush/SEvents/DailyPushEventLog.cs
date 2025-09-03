using System;
using System.Collections.Generic;
using Orleans;
using Aevatar.Core.Abstractions;

namespace GodGPT.GAgents.DailyPush.SEvents;

/// <summary>
/// Base log event for Daily Push operations
/// </summary>
[GenerateSerializer]
public class DailyPushLogEvent : StateLogEventBase<DailyPushLogEvent>
{
}

// === Daily Content Management Events ===

/// <summary>
/// Add content event
/// </summary>
[GenerateSerializer]
public class AddContentEventLog : DailyPushLogEvent
{
    [Id(0)] public DailyNotificationContent Content { get; set; } = null!;
}

/// <summary>
/// Update content event
/// </summary>
[GenerateSerializer]
public class UpdateContentEventLog : DailyPushLogEvent
{
    [Id(0)] public string ContentId { get; set; } = "";
    [Id(1)] public DailyNotificationContent Content { get; set; } = null!;
}

/// <summary>
/// Remove content event
/// </summary>
[GenerateSerializer]
public class RemoveContentEventLog : DailyPushLogEvent
{
    [Id(0)] public string ContentId { get; set; } = "";
}

/// <summary>
/// Content selection event
/// </summary>
[GenerateSerializer]
public class ContentSelectionEventLog : DailyPushLogEvent
{
    [Id(0)] public DateTime SelectionDate { get; set; }
    [Id(1)] public List<string> SelectedContentIds { get; set; } = new();
    [Id(2)] public int Count { get; set; }
}

/// <summary>
/// Content import event
/// </summary>
[GenerateSerializer]
public class ImportContentsEventLog : DailyPushLogEvent
{
    [Id(0)] public List<DailyNotificationContent> Contents { get; set; } = new();
    [Id(1)] public DateTime ImportTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Content refresh event
/// </summary>
[GenerateSerializer]
public class RefreshContentsEventLog : DailyPushLogEvent
{
    [Id(0)] public DateTime RefreshTime { get; set; } = DateTime.UtcNow;
}

// === Timezone User Index Events ===

/// <summary>
/// Add user to timezone event
/// </summary>
[GenerateSerializer]
public class AddUserToTimezoneEventLog : DailyPushLogEvent
{
    [Id(0)] public Guid UserId { get; set; }
    [Id(1)] public string TimeZoneId { get; set; } = "";
}

/// <summary>
/// Remove user from timezone event
/// </summary>
[GenerateSerializer]
public class RemoveUserFromTimezoneEventLog : DailyPushLogEvent
{
    [Id(0)] public Guid UserId { get; set; }
    [Id(1)] public string TimeZoneId { get; set; } = "";
}

/// <summary>
/// Batch update users event
/// </summary>
[GenerateSerializer]
public class BatchUpdateUsersEventLog : DailyPushLogEvent
{
    [Id(0)] public List<TimezoneUpdateRequest> Updates { get; set; } = new();
    [Id(1)] public int UpdatedCount { get; set; }
}

/// <summary>
/// Initialize timezone index event
/// </summary>
[GenerateSerializer]
public class InitializeTimezoneIndexEventLog : DailyPushLogEvent
{
    [Id(0)] public string TimeZoneId { get; set; } = "";
    [Id(1)] public DateTime InitTime { get; set; } = DateTime.UtcNow;
}

// === Timezone Scheduler Events ===

/// <summary>
/// Initialize scheduler event
/// </summary>
[GenerateSerializer]
public class InitializeSchedulerEventLog : DailyPushLogEvent
{
    [Id(0)] public string TimeZoneId { get; set; } = "";
    [Id(1)] public SchedulerStatus Status { get; set; }
    [Id(2)] public DateTime InitTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Morning push processed event
/// </summary>
[GenerateSerializer]
public class MorningPushProcessedEventLog : DailyPushLogEvent
{
    [Id(0)] public DateTime PushTime { get; set; }
    [Id(1)] public int UserCount { get; set; }
    [Id(2)] public int SuccessCount { get; set; }
    [Id(3)] public int FailureCount { get; set; }
}

/// <summary>
/// Afternoon retry processed event
/// </summary>
[GenerateSerializer]
public class AfternoonRetryProcessedEventLog : DailyPushLogEvent
{
    [Id(0)] public DateTime RetryTime { get; set; }
    [Id(1)] public int UserCount { get; set; }
    [Id(2)] public int SuccessCount { get; set; }
    [Id(3)] public int FailureCount { get; set; }
}

/// <summary>
/// Set scheduler status event
/// </summary>
[GenerateSerializer]
public class SetSchedulerStatusEventLog : DailyPushLogEvent
{
    [Id(0)] public SchedulerStatus OldStatus { get; set; }
    [Id(1)] public SchedulerStatus NewStatus { get; set; }
    [Id(2)] public DateTime ChangeTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Set reminder target ID event  
/// </summary>
[GenerateSerializer]
public class SetReminderTargetIdEventLog : DailyPushLogEvent
{
    [Id(0)] public Guid OldTargetId { get; set; }
    [Id(1)] public Guid NewTargetId { get; set; }
    [Id(2)] public DateTime ChangeTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Initialize coordinator event
/// </summary>
[GenerateSerializer]
public class InitializeCoordinatorEventLog : DailyPushLogEvent
{
    [Id(0)] public string TimeZoneId { get; set; } = "";
    [Id(1)] public SchedulerStatus Status { get; set; }
    [Id(2)] public DateTime InitTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Morning push completed event
/// </summary>
[GenerateSerializer]
public class MorningPushCompletedEventLog : DailyPushLogEvent
{
    [Id(0)] public DateTime PushDate { get; set; }
    [Id(1)] public int UserCount { get; set; }
    [Id(2)] public int FailureCount { get; set; }
    [Id(3)] public DateTime CompletionTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Afternoon retry completed event
/// </summary>
[GenerateSerializer]
public class AfternoonRetryCompletedEventLog : DailyPushLogEvent
{
    [Id(0)] public DateTime RetryDate { get; set; }
    [Id(1)] public int RetryUserCount { get; set; }
    [Id(2)] public int FailureCount { get; set; }
    [Id(3)] public DateTime CompletionTime { get; set; } = DateTime.UtcNow;
}

/// <summary>  
/// Test mode state change event
/// </summary>
[GenerateSerializer]
public class TestModeStateEventLog : DailyPushLogEvent
{
    [Id(0)] public bool IsActive { get; set; }
    [Id(1)] public DateTime StartTime { get; set; }
    [Id(2)] public int CustomInterval { get; set; }
    [Id(3)] public DateTime ChangeTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Configuration change tracking event
/// </summary>
[GenerateSerializer]
public class ConfigurationChangeEventLog : DailyPushLogEvent
{
    [Id(0)] public TimeSpan? OldMorningTime { get; set; }
    [Id(1)] public TimeSpan? NewMorningTime { get; set; }
    [Id(2)] public TimeSpan? OldAfternoonTime { get; set; }
    [Id(3)] public TimeSpan? NewAfternoonTime { get; set; }
    [Id(4)] public DateTime ChangeTime { get; set; } = DateTime.UtcNow;
}

// === Chat Manager Daily Push Events ===

/// <summary>
/// Register or update device event
/// </summary>
[GenerateSerializer]
public class RegisterOrUpdateDeviceEventLog : DailyPushLogEvent
{
    [Id(0)] public string DeviceId { get; set; } = "";
    [Id(1)] public UserDeviceInfo DeviceInfo { get; set; } = null!;
    [Id(2)] public bool IsNewDevice { get; set; }
    [Id(3)] public string? OldPushToken { get; set; }
}

/// <summary>
/// Mark push as read event
/// </summary>
[GenerateSerializer]
public class MarkPushAsReadEventLog : DailyPushLogEvent
{
    [Id(0)] public string PushToken { get; set; } = "";
    [Id(1)] public string DateKey { get; set; } = "";
    [Id(2)] public DateTime ReadTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Scheduler status change event
/// </summary>
[GenerateSerializer]
public class SchedulerStatusLogEvent : DailyPushLogEvent
{
    [Id(0)] public SchedulerStatus OldStatus { get; set; }
    [Id(1)] public SchedulerStatus NewStatus { get; set; }
    [Id(2)] public DateTime ChangeTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Scheduler error event
/// </summary>
[GenerateSerializer]
public class SchedulerErrorEventLog : DailyPushLogEvent
{
    [Id(0)] public string ErrorMessage { get; set; } = "";
    [Id(1)] public string ErrorType { get; set; } = "";
    [Id(2)] public DateTime ErrorTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Test round completed event
/// </summary>
[GenerateSerializer]
public class TestRoundCompletedEventLog : DailyPushLogEvent
{
    [Id(0)] public int CompletedRound { get; set; }
    [Id(1)] public DateTime CompletionTime { get; set; } = DateTime.UtcNow;
}

// === Firebase Token Provider Events ===

/// <summary>
/// Token creation success event
/// </summary>
[GenerateSerializer]
public class TokenCreationSuccessEventLog : DailyPushLogEvent
{
    [Id(0)] public DateTime CreationTime { get; set; } = DateTime.UtcNow;
    [Id(1)] public DateTime TokenExpiry { get; set; }
    [Id(2)] public int AttemptNumber { get; set; }
}

/// <summary>
/// Token creation failure event
/// </summary>
[GenerateSerializer]
public class TokenCreationFailureEventLog : DailyPushLogEvent
{
    [Id(0)] public DateTime FailureTime { get; set; } = DateTime.UtcNow;
    [Id(1)] public string ErrorMessage { get; set; } = "";
    [Id(2)] public string ErrorType { get; set; } = "";
    [Id(3)] public int AttemptNumber { get; set; }
}

/// <summary>
/// Token cache cleared event
/// </summary>
[GenerateSerializer]
public class TokenCacheClearedEventLog : DailyPushLogEvent
{
    [Id(0)] public DateTime ClearTime { get; set; } = DateTime.UtcNow;
    [Id(1)] public string Reason { get; set; } = "";
}

/// <summary>
/// Token provider activation event
/// </summary>
[GenerateSerializer]
public class TokenProviderActivationEventLog : DailyPushLogEvent
{
    [Id(0)] public DateTime ActivationTime { get; set; } = DateTime.UtcNow;
    [Id(1)] public long ChatManagerId { get; set; }
}

/// <summary>
/// Duplicate push prevention event - logged when a duplicate push is prevented by global deduplication
/// </summary>
[GenerateSerializer]
public class DuplicatePreventionEventLog : DailyPushLogEvent
{
    [Id(0)] public string PushTokenPrefix { get; set; } = "";
    [Id(1)] public string TimeZone { get; set; } = "";
    [Id(2)] public DateTime PreventionDate { get; set; }
    [Id(3)] public DateTime PreventionTime { get; set; } = DateTime.UtcNow;
}
