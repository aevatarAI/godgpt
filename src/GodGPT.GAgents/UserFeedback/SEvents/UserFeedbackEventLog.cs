using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.UserFeedback.SEvents;

/// <summary>
/// Base event log for User Feedback GAgent
/// </summary>
[GenerateSerializer]
public abstract class UserFeedbackEventLog : StateLogEventBase<UserFeedbackEventLog>
{
}

/// <summary>
/// Submit feedback event
/// </summary>
[GenerateSerializer]
public class SubmitFeedbackLogEvent : UserFeedbackEventLog
{
    [Id(0)] public Guid UserId { get; set; }
    [Id(1)] public UserFeedbackInfo FeedbackInfo { get; set; } = null!;
    [Id(2)] public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    [Id(3)] public int FeedbackCount { get; set; }
}

/// <summary>
/// Submit feedback event
/// </summary>
[GenerateSerializer]
public class SkippedFeedbackLogEvent : UserFeedbackEventLog
{
    [Id(0)] public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}
