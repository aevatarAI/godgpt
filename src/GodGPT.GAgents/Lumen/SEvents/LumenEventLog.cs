using Aevatar.Application.Grains.Lumen.Dtos;
using Aevatar.Core.Abstractions;
using Orleans;

namespace Aevatar.Application.Grains.Lumen.SEvents;

#region User Events

/// <summary>
/// Base event log for Lumen User GAgent
/// </summary>
[GenerateSerializer]
public abstract class LumenUserEventLog : StateLogEventBase<LumenUserEventLog>
{
}

/// <summary>
/// User registered event
/// </summary>
[GenerateSerializer]
public class UserRegisteredEvent : LumenUserEventLog
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string FirstName { get; set; } = string.Empty;
    [Id(2)] public string LastName { get; set; } = string.Empty;
    [Id(3)] public GenderEnum Gender { get; set; }
    [Id(4)] public DateOnly BirthDate { get; set; }
    [Id(5)] public TimeOnly BirthTime { get; set; }
    [Id(6)] public string? BirthCity { get; set; } // Optional
    [Id(7)] public string? LatLong { get; set; } // Optional - Latitude,Longitude for astrology calculations
    [Id(8)] public MbtiTypeEnum? MbtiType { get; set; } // Optional
    [Id(9)] public RelationshipStatusEnum? RelationshipStatus { get; set; } // Optional
    [Id(10)] public string? Interests { get; set; } // Optional
    [Id(11)] public CalendarTypeEnum CalendarType { get; set; }
    [Id(12)] public DateTime CreatedAt { get; set; }
    [Id(13)] public string? CurrentResidence { get; set; } // Optional
    [Id(14)] public string? Email { get; set; } // Optional
    [Id(15)] public string InitialLanguage { get; set; } = "en"; // Initial language from Accept-Language header
}

/// <summary>
/// User cleared event (for testing)
/// </summary>
[GenerateSerializer]
public class UserClearedEvent : LumenUserEventLog
{
    [Id(0)] public DateTime ClearedAt { get; set; }
}

/// <summary>
/// User actions updated event
/// </summary>
[GenerateSerializer]
public class UserActionsUpdatedEvent : LumenUserEventLog
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public List<string> Actions { get; set; } = new();
    [Id(2)] public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// LatLong inferred by LLM from BirthCity
/// </summary>
[GenerateSerializer]
public class LatLongInferredEvent : LumenUserEventLog
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string LatLongInferred { get; set; } = string.Empty; // Format: "latitude,longitude"
    [Id(2)] public string BirthCity { get; set; } = string.Empty; // Source city
    [Id(3)] public DateTime InferredAt { get; set; }
}

/// <summary>
/// Language switched event
/// </summary>
[GenerateSerializer]
public class LanguageSwitchedEvent : LumenUserEventLog
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string PreviousLanguage { get; set; } = string.Empty;
    [Id(2)] public string NewLanguage { get; set; } = string.Empty;
    [Id(3)] public DateTime SwitchedAt { get; set; }
    [Id(4)] public DateOnly SwitchDate { get; set; } // Date of the switch (for daily counting)
    [Id(5)] public int TodayCount { get; set; } // Count after this switch
}

#endregion

#region Prediction Events

/// <summary>
/// Base event log for Lumen Prediction GAgent
/// </summary>
[GenerateSerializer]
public abstract class LumenPredictionEventLog : StateLogEventBase<LumenPredictionEventLog>
{
}

/// <summary>
/// Prediction generated event (unified for Daily/Yearly/Lifetime)
/// </summary>
[GenerateSerializer]
public class PredictionGeneratedEvent : LumenPredictionEventLog
{
    [Id(0)] public Guid PredictionId { get; set; }
    [Id(1)] public string UserId { get; set; } = string.Empty;
    [Id(2)] public DateOnly PredictionDate { get; set; }
    [Id(3)] public DateTime CreatedAt { get; set; }
    [Id(4)] public DateTime? ProfileUpdatedAt { get; set; } // Track profile update time
    [Id(5)] public PredictionType Type { get; set; } // Daily/Yearly/Lifetime
    
    // Unified flattened results (key-value pairs with enum fields included)
    [Id(6)] public Dictionary<string, string> Results { get; set; } = new();
    
    // Multilingual cache (language -> flattened results)
    [Id(7)] public Dictionary<string, Dictionary<string, string>>? MultilingualResults { get; set; }
    
    // Initial language generated in first stage
    [Id(8)] public string? InitialLanguage { get; set; }
    
    // Track generation date for daily reminder deduplication
    [Id(9)] public DateOnly? LastGeneratedDate { get; set; }
    
    // Prompt version used for this generation
    [Id(10)] public int PromptVersion { get; set; }
}

/// <summary>
/// Prediction cleared event (for user deletion or profile update)
/// </summary>
[GenerateSerializer]
public class PredictionClearedEvent : LumenPredictionEventLog
{
    [Id(0)] public DateTime ClearedAt { get; set; }
}

/// <summary>
/// Event raised when remaining languages are generated asynchronously (second stage)
/// </summary>
[GenerateSerializer]
public class LanguagesTranslatedEvent : LumenPredictionEventLog
{
    [Id(0)] public PredictionType Type { get; set; }
    [Id(1)] public DateOnly PredictionDate { get; set; }
    [Id(2)] public Dictionary<string, Dictionary<string, string>>? TranslatedLanguages { get; set; } // Key: language code, Value: content
    [Id(3)] public List<string>? AllGeneratedLanguages { get; set; } // All languages now available
    [Id(4)] public DateOnly? LastGeneratedDate { get; set; } // Track translation date for daily limit
}

/// <summary>
/// Generation lock set event - marks that generation has started
/// </summary>
[GenerateSerializer]
public class GenerationLockSetEvent : LumenPredictionEventLog
{
    [Id(0)] public PredictionType Type { get; set; }
    [Id(1)] public DateTime StartedAt { get; set; }
    [Id(2)] public int RetryCount { get; set; }
}

/// <summary>
/// Generation lock cleared event - marks that generation has completed or failed
/// </summary>
[GenerateSerializer]
public class GenerationLockClearedEvent : LumenPredictionEventLog
{
    [Id(0)] public PredictionType Type { get; set; }
}

#endregion

#region Feedback Events

/// <summary>
/// Base event log for Lumen Feedback GAgent
/// </summary>
[GenerateSerializer]
public abstract class LumenFeedbackEventLog : StateLogEventBase<LumenFeedbackEventLog>
{
}

/// <summary>
/// Feedback submitted event
/// </summary>
[GenerateSerializer]
public class FeedbackSubmittedEvent : LumenFeedbackEventLog
{
    [Id(0)] public string FeedbackId { get; set; } = string.Empty;
    [Id(1)] public string UserId { get; set; } = string.Empty;
    [Id(2)] public Guid PredictionId { get; set; }
    [Id(3)] public string PredictionMethod { get; set; } = string.Empty;
    [Id(4)] public FeedbackDetail FeedbackDetail { get; set; } = new();
    [Id(5)] public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Method rating updated event
/// </summary>
[GenerateSerializer]
public class MethodRatingUpdatedEvent : LumenFeedbackEventLog
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
/// Base event log for Lumen Stats Snapshot GAgent
/// </summary>
[GenerateSerializer]
public abstract class LumenStatsSnapshotEventLog : StateLogEventBase<LumenStatsSnapshotEventLog>
{
}

/// <summary>
/// Stats snapshot event
/// </summary>
[GenerateSerializer]
public class StatsSnapshotEvent : LumenStatsSnapshotEventLog
{
    [Id(0)] public Dictionary<string, MethodStats> GlobalStats { get; set; } = new();
    [Id(1)] public Dictionary<string, Dictionary<string, MethodStats>> UserStats { get; set; } = new();
    [Id(2)] public DateTime SnapshotAt { get; set; }
}

#endregion

#region User Profile Events (V2)

/// <summary>
/// Base event log for Lumen User Profile GAgent (V2)
/// </summary>
[GenerateSerializer]
public abstract class LumenUserProfileEventLog : StateLogEventBase<LumenUserProfileEventLog>
{
}

/// <summary>
/// User profile updated event (V2)
/// </summary>
[GenerateSerializer]
public class UserProfileUpdatedEvent : LumenUserProfileEventLog
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string FullName { get; set; } = string.Empty;
    [Id(2)] public GenderEnum Gender { get; set; }
    [Id(3)] public DateOnly BirthDate { get; set; }
    [Id(4)] public TimeOnly? BirthTime { get; set; } // Optional
    [Id(5)] public string? BirthCity { get; set; } // Format: "Los Angeles, USA"
    [Id(6)] public string? LatLong { get; set; } // Format: "34.0522, -118.2437"
    [Id(7)] public MbtiTypeEnum? MbtiType { get; set; }
    [Id(8)] public RelationshipStatusEnum? RelationshipStatus { get; set; }
    [Id(9)] public string? Interests { get; set; }
    [Id(10)] public CalendarTypeEnum? CalendarType { get; set; } // Optional
    [Id(11)] public DateTime UpdatedAt { get; set; }
    [Id(12)] public string? CurrentResidence { get; set; }
    [Id(13)] public string? Email { get; set; }
    [Id(14)] public string? Occupation { get; set; } // Optional
    [Id(15)] public string? Icon { get; set; } // Optional - User avatar/icon URL from blob storage
}

/// <summary>
/// User profile actions updated event (V2)
/// </summary>
[GenerateSerializer]
public class UserProfileActionsUpdatedEvent : LumenUserProfileEventLog
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public List<string> Actions { get; set; } = new();
    [Id(2)] public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// User profile cleared event (for testing)
/// </summary>
[GenerateSerializer]
public class UserProfileClearedEvent : LumenUserProfileEventLog
{
    [Id(0)] public DateTime ClearedAt { get; set; }
}

/// <summary>
/// User icon updated event
/// </summary>
[GenerateSerializer]
public class IconUpdatedEvent : LumenUserProfileEventLog
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string IconUrl { get; set; } = string.Empty;
    [Id(2)] public DateTime UpdatedAt { get; set; }
    [Id(3)] public DateTime UploadTimestamp { get; set; } // For daily limit tracking
}

/// <summary>
/// Language switched event (for UserProfile)
/// </summary>
[GenerateSerializer]
public class UserProfileLanguageSwitchedEvent : LumenUserProfileEventLog
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string PreviousLanguage { get; set; } = string.Empty;
    [Id(2)] public string NewLanguage { get; set; } = string.Empty;
    [Id(3)] public DateTime SwitchedAt { get; set; }
    [Id(4)] public DateOnly SwitchDate { get; set; } // Date of the switch (for daily counting)
    [Id(5)] public int TodayCount { get; set; } // Count after this switch
}

#endregion

#region Prediction History Events

/// <summary>
/// Base event log for Lumen Prediction History GAgent
/// </summary>
[GenerateSerializer]
public abstract class LumenPredictionHistoryEventLog : StateLogEventBase<LumenPredictionHistoryEventLog>
{
}

/// <summary>
/// Prediction added to history event
/// </summary>
[GenerateSerializer]
public class PredictionAddedToHistoryEvent : LumenPredictionHistoryEventLog
{
    [Id(0)] public Guid PredictionId { get; set; }
    [Id(1)] public DateOnly PredictionDate { get; set; }
    [Id(2)] public DateTime CreatedAt { get; set; }
    [Id(3)] public Dictionary<string, string> Results { get; set; } = new();
    [Id(4)] public PredictionType Type { get; set; }
    // Additional fields for complete prediction data
    [Id(5)] public string UserId { get; set; } = string.Empty;
    [Id(6)] public List<string> AvailableLanguages { get; set; } = new();
    [Id(7)] public string RequestedLanguage { get; set; } = "en";
    [Id(8)] public string ReturnedLanguage { get; set; } = "en";
    [Id(9)] public bool FromCache { get; set; }
    [Id(10)] public bool AllLanguagesGenerated { get; set; }
    [Id(11)] public bool IsFallback { get; set; }
}

/// <summary>
/// Prediction history cleared event
/// </summary>
[GenerateSerializer]
public class PredictionHistoryClearedEvent : LumenPredictionHistoryEventLog
{
    [Id(0)] public DateTime ClearedAt { get; set; }
}

#endregion

#region Favourite Events

/// <summary>
/// Base event log for Lumen Favourite GAgent
/// </summary>
[GenerateSerializer]
public abstract class LumenFavouriteEventLog : StateLogEventBase<LumenFavouriteEventLog>
{
}

/// <summary>
/// Prediction favourited event
/// </summary>
[GenerateSerializer]
public class PredictionFavouritedEvent : LumenFavouriteEventLog
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
public class PredictionUnfavouritedEvent : LumenFavouriteEventLog
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public Guid PredictionId { get; set; }
    [Id(2)] public DateTime UnfavouritedAt { get; set; }
}

#endregion

