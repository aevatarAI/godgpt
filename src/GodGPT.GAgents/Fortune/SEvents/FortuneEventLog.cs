using Aevatar.Application.Grains.Fortune.Dtos;
using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Fortune.SEvents;

#region User Events

/// <summary>
/// Base event log for Fortune User GAgent
/// </summary>
[GenerateSerializer]
public abstract class FortuneUserEventLog : StateLogEventBase<FortuneUserEventLog>
{
}

/// <summary>
/// User registered event
/// </summary>
[GenerateSerializer]
public class UserRegisteredEvent : FortuneUserEventLog
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string FirstName { get; set; } = string.Empty;
    [Id(2)] public string LastName { get; set; } = string.Empty;
    [Id(3)] public GenderEnum Gender { get; set; }
    [Id(4)] public DateOnly BirthDate { get; set; }
    [Id(5)] public TimeOnly BirthTime { get; set; }
    [Id(6)] public string BirthCountry { get; set; } = string.Empty;
    [Id(7)] public string BirthCity { get; set; } = string.Empty;
    [Id(8)] public MbtiTypeEnum MbtiType { get; set; }
    [Id(9)] public RelationshipStatusEnum? RelationshipStatus { get; set; }
    [Id(10)] public string? Interests { get; set; }
    [Id(11)] public DateTime CreatedAt { get; set; }
}

#endregion

#region Prediction Events

/// <summary>
/// Base event log for Fortune Prediction GAgent
/// </summary>
[GenerateSerializer]
public abstract class FortunePredictionEventLog : StateLogEventBase<FortunePredictionEventLog>
{
}

/// <summary>
/// Prediction generated event
/// </summary>
[GenerateSerializer]
public class PredictionGeneratedEvent : FortunePredictionEventLog
{
    [Id(0)] public Guid PredictionId { get; set; }
    [Id(1)] public string UserId { get; set; } = string.Empty;
    [Id(2)] public DateOnly PredictionDate { get; set; }
    [Id(3)] public Dictionary<string, Dictionary<string, string>> Results { get; set; } = new();
    [Id(4)] public int OverallEnergy { get; set; }
    [Id(5)] public DateTime CreatedAt { get; set; }
}

#endregion

#region Feedback Events

/// <summary>
/// Base event log for Fortune Feedback GAgent
/// </summary>
[GenerateSerializer]
public abstract class FortuneFeedbackEventLog : StateLogEventBase<FortuneFeedbackEventLog>
{
}

/// <summary>
/// Feedback submitted event
/// </summary>
[GenerateSerializer]
public class FeedbackSubmittedEvent : FortuneFeedbackEventLog
{
    [Id(0)] public string FeedbackId { get; set; } = string.Empty;
    [Id(1)] public string UserId { get; set; } = string.Empty;
    [Id(2)] public Guid PredictionId { get; set; }
    [Id(3)] public int Score { get; set; }
    [Id(4)] public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Feedback updated event
/// </summary>
[GenerateSerializer]
public class FeedbackUpdatedEvent : FortuneFeedbackEventLog
{
    [Id(0)] public int Score { get; set; }
    [Id(1)] public DateTime UpdatedAt { get; set; }
}

#endregion

