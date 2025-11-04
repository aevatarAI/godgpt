using Aevatar.Application.Grains.Fortune.Dtos;
using Aevatar.Core.Abstractions;
using Orleans;

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
    [Id(6)] public string? BirthCountry { get; set; } // Optional
    [Id(7)] public string? BirthCity { get; set; } // Optional
    [Id(8)] public MbtiTypeEnum? MbtiType { get; set; } // Optional
    [Id(9)] public RelationshipStatusEnum? RelationshipStatus { get; set; } // Optional
    [Id(10)] public string? Interests { get; set; } // Optional
    [Id(11)] public CalendarTypeEnum CalendarType { get; set; }
    [Id(12)] public DateTime CreatedAt { get; set; }
    [Id(13)] public string? CurrentResidence { get; set; } // Optional
    [Id(14)] public string? Email { get; set; } // Optional
}

/// <summary>
/// User cleared event (for testing)
/// </summary>
[GenerateSerializer]
public class UserClearedEvent : FortuneUserEventLog
{
    [Id(0)] public DateTime ClearedAt { get; set; }
}

/// <summary>
/// User actions updated event
/// </summary>
[GenerateSerializer]
public class UserActionsUpdatedEvent : FortuneUserEventLog
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public List<string> Actions { get; set; } = new();
    [Id(2)] public DateTime UpdatedAt { get; set; }
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
    [Id(4)] public int Energy { get; set; }
    [Id(5)] public DateTime CreatedAt { get; set; }
    [Id(6)] public Dictionary<string, string> LifetimeForecast { get; set; }
    [Id(7)] public Dictionary<string, string> WeeklyForecast { get; set; }
    [Id(8)] public DateTime? WeeklyGeneratedDate { get; set; }
    [Id(9)] public DateTime? ProfileUpdatedAt { get; set; }
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
    [Id(3)] public string PredictionMethod { get; set; } = string.Empty;
    [Id(4)] public FeedbackDetail FeedbackDetail { get; set; } = new();
    [Id(5)] public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Feedback updated event
/// </summary>
[GenerateSerializer]
public class FeedbackUpdatedEvent : FortuneFeedbackEventLog
{
    [Id(0)] public string? PredictionMethod { get; set; } // e.g., "opportunity", "bazi", "astrology", "tarot" (Obsolete event)
    [Id(1)] public int Rating { get; set; }
    [Id(2)] public List<string> FeedbackTypes { get; set; } = new();
    [Id(3)] public string? Comment { get; set; }
    [Id(4)] public string? Email { get; set; }
    [Id(5)] public bool AgreeToContact { get; set; }
    [Id(6)] public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Method rating updated event
/// </summary>
[GenerateSerializer]
public class MethodRatingUpdatedEvent : FortuneFeedbackEventLog
{
    [Id(0)] public string FeedbackId { get; set; } = string.Empty;
    [Id(1)] public string UserId { get; set; } = string.Empty;
    [Id(2)] public Guid PredictionId { get; set; }
    [Id(3)] public string PredictionMethod { get; set; } = string.Empty;
    [Id(4)] public FeedbackDetail FeedbackDetail { get; set; } = new();
    [Id(5)] public DateTime UpdatedAt { get; set; }
}

#endregion

#region Stats Snapshot Events

/// <summary>
/// Base event log for Fortune Stats Snapshot GAgent
/// </summary>
[GenerateSerializer]
public abstract class FortuneStatsSnapshotEventLog : StateLogEventBase<FortuneStatsSnapshotEventLog>
{
}

/// <summary>
/// Stats snapshot event
/// </summary>
[GenerateSerializer]
public class StatsSnapshotEvent : FortuneStatsSnapshotEventLog
{
    [Id(0)] public Dictionary<string, MethodStats> GlobalStats { get; set; } = new();
    [Id(1)] public Dictionary<string, Dictionary<string, MethodStats>> UserStats { get; set; } = new();
    [Id(2)] public DateTime SnapshotAt { get; set; }
}

#endregion

#region User Profile Events (V2)

/// <summary>
/// Base event log for Fortune User Profile GAgent (V2)
/// </summary>
[GenerateSerializer]
public abstract class FortuneUserProfileEventLog : StateLogEventBase<FortuneUserProfileEventLog>
{
}

/// <summary>
/// User profile updated event (V2)
/// </summary>
[GenerateSerializer]
public class UserProfileUpdatedEvent : FortuneUserProfileEventLog
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string FullName { get; set; } = string.Empty;
    [Id(2)] public GenderEnum Gender { get; set; }
    [Id(3)] public DateOnly BirthDate { get; set; }
    [Id(4)] public TimeOnly? BirthTime { get; set; } // Optional
    [Id(5)] public string? BirthCountry { get; set; }
    [Id(6)] public string? BirthCity { get; set; }
    [Id(7)] public MbtiTypeEnum? MbtiType { get; set; }
    [Id(8)] public RelationshipStatusEnum? RelationshipStatus { get; set; }
    [Id(9)] public string? Interests { get; set; }
    [Id(10)] public CalendarTypeEnum? CalendarType { get; set; } // Optional
    [Id(11)] public DateTime UpdatedAt { get; set; }
    [Id(12)] public string? CurrentResidence { get; set; }
    [Id(13)] public string? Email { get; set; }
}

/// <summary>
/// User profile actions updated event (V2)
/// </summary>
[GenerateSerializer]
public class UserProfileActionsUpdatedEvent : FortuneUserProfileEventLog
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public List<string> Actions { get; set; } = new();
    [Id(2)] public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Profile insights generated event (Astrology, Bazi, Zodiac)
/// </summary>
[GenerateSerializer]
public class ProfileInsightsGeneratedEvent : FortuneUserProfileEventLog
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public Dictionary<string, string> Astrology { get; set; } = new();
    [Id(2)] public Dictionary<string, string> Bazi { get; set; } = new();
    [Id(3)] public Dictionary<string, string> Zodiac { get; set; } = new();
    [Id(4)] public DateTime GeneratedAt { get; set; }
}

#endregion

#region Prediction History Events

/// <summary>
/// Base event log for Fortune Prediction History GAgent
/// </summary>
[GenerateSerializer]
public abstract class FortunePredictionHistoryEventLog : StateLogEventBase<FortunePredictionHistoryEventLog>
{
}

/// <summary>
/// Prediction added to history event
/// </summary>
[GenerateSerializer]
public class PredictionAddedToHistoryEvent : FortunePredictionHistoryEventLog
{
    [Id(0)] public Guid PredictionId { get; set; }
    [Id(1)] public DateOnly PredictionDate { get; set; }
    [Id(2)] public int Energy { get; set; }
    [Id(3)] public Dictionary<string, Dictionary<string, string>> Results { get; set; } = new();
    [Id(4)] public DateTime CreatedAt { get; set; }
}

#endregion

#region Favourite Events

/// <summary>
/// Base event log for Fortune Favourite GAgent
/// </summary>
[GenerateSerializer]
public abstract class FortuneFavouriteEventLog : StateLogEventBase<FortuneFavouriteEventLog>
{
}

/// <summary>
/// Prediction favourited event
/// </summary>
[GenerateSerializer]
public class PredictionFavouritedEvent : FortuneFavouriteEventLog
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public DateOnly Date { get; set; }
    [Id(2)] public Guid PredictionId { get; set; }
    [Id(3)] public FavouriteDetail FavouriteDetail { get; set; } = new();
    [Id(4)] public DateTime FavouritedAt { get; set; }
}

/// <summary>
/// Prediction unfavourited event
/// </summary>
[GenerateSerializer]
public class PredictionUnfavouritedEvent : FortuneFavouriteEventLog
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public Guid PredictionId { get; set; }
    [Id(2)] public DateTime UnfavouritedAt { get; set; }
}

#endregion

