namespace Aevatar.Application.Grains.Lumen.Dtos;

#region User Profile DTOs (V2 with FullName)

/// <summary>
/// Update user profile request (V2)
/// </summary>
[GenerateSerializer]
public class UpdateUserProfileRequest
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string FullName { get; set; } = string.Empty;
    [Id(2)] public GenderEnum Gender { get; set; }
    [Id(3)] public DateOnly BirthDate { get; set; } // Required
    [Id(4)] public TimeOnly BirthTime { get; set; } // Required for accurate Moon/Rising calculation
    [Id(5)] public string BirthCity { get; set; } = string.Empty; // Required - format: "Los Angeles, USA"
    [Id(6)] public string? LatLong { get; set; } // Optional - format: "34.0522, -118.2437" (latitude, longitude)
    [Id(7)] public MbtiTypeEnum? MbtiType { get; set; } // Optional
    [Id(8)] public RelationshipStatusEnum? RelationshipStatus { get; set; } // Optional
    [Id(9)] public string? Interests { get; set; } // Optional
    [Id(10)] public CalendarTypeEnum? CalendarType { get; set; } // Optional
    [Id(11)] public string? CurrentResidence { get; set; } // Optional
    [Id(12)] public string? Email { get; set; } // Optional
    [Id(13)] public string? Occupation { get; set; } // Optional
    [Id(14)] public string? Icon { get; set; } // Optional - User avatar/icon URL from blob storage
    [Id(15)] public string? CurrentTimeZone { get; set; } // Optional - IANA time zone ID (e.g., "America/New_York")
    [Id(16)] public List<InterestEnum>? InterestsList { get; set; } // Optional - V2: Interests as enum list
}

/// <summary>
/// Update user profile result (V2)
/// </summary>
[GenerateSerializer]
public class UpdateUserProfileResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Message { get; set; } = string.Empty;
    [Id(2)] public string? UserId { get; set; }
    [Id(3)] public DateTime? CreatedAt { get; set; }
    [Id(4)] public int? RemainingUpdates { get; set; } // Remaining profile updates this week
    [Id(5)] public DateTime? UpdatedAt { get; set; } // Actual update time from ProfileGAgent (critical for prediction regeneration)
}

/// <summary>
/// User profile DTO (V2)
/// </summary>
[GenerateSerializer]
public class LumenUserProfileDto
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string FullName { get; set; } = string.Empty;
    [Id(2)] public GenderEnum Gender { get; set; }
    [Id(3)] public DateOnly BirthDate { get; set; }
    [Id(4)] public TimeOnly BirthTime { get; set; } // Required
    [Id(5)] public string BirthCity { get; set; } = string.Empty; // Required - format: "Los Angeles, USA"
    [Id(6)] public string? LatLong { get; set; } // Optional - format: "34.0522, -118.2437" (latitude, longitude)
    [Id(7)] public CalendarTypeEnum? CalendarType { get; set; } // Optional
    [Id(8)] public DateTime CreatedAt { get; set; }
    [Id(9)] public string? CurrentResidence { get; set; }
    [Id(10)] public DateTime UpdatedAt { get; set; }
    [Id(11)] public Dictionary<string, string> WelcomeNote { get; set; } = new(); // Backend-calculated welcome note (zodiac, chineseZodiac, rhythm, essence)
    
    // Astrology information (backend-calculated)
    [Id(12)] public string ZodiacSign { get; set; } = string.Empty; // e.g., "Aries"
    [Id(13)] public ZodiacSignEnum ZodiacSignEnum { get; set; } = ZodiacSignEnum.Unknown;
    [Id(14)] public string ChineseZodiac { get; set; } = string.Empty; // e.g., "Fire Horse (火马)"
    [Id(15)] public ChineseZodiacEnum ChineseZodiacEnum { get; set; } = ChineseZodiacEnum.Unknown;
    [Id(16)] public string? Occupation { get; set; } // Optional
    [Id(17)] public MbtiTypeEnum? MbtiType { get; set; }
    [Id(18)] public RelationshipStatusEnum? RelationshipStatus { get; set; }
    [Id(19)] public string? Interests { get; set; }
    [Id(20)] public string? Email { get; set; }
    [Id(21)] public string? Icon { get; set; } // Optional - User avatar/icon URL from blob storage
    [Id(22)] public Dictionary<string, PredictionFeedbackSummary>? Feedbacks { get; set; } // User feedbacks (e.g., "settings")
    [Id(23)] public string? CurrentTimeZone { get; set; } // Optional - IANA time zone ID (e.g., "America/New_York"), defaults to UTC
    [Id(24)] public string CurrentLanguage { get; set; } = "en"; // Current active language (en, zh, zh-tw, es)
    [Id(25)] public List<InterestEnum>? InterestsList { get; set; } // Optional - V2: Interests as enum list
}

/// <summary>
/// Update icon result
/// </summary>
[GenerateSerializer]
public class UpdateIconResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Message { get; set; } = string.Empty;
    [Id(2)] public string? IconUrl { get; set; }
    [Id(3)] public int? RemainingUploads { get; set; } // Remaining uploads today
}

/// <summary>
/// Get user profile result (V2)
/// </summary>
[GenerateSerializer]
public class GetUserProfileResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Message { get; set; } = string.Empty;
    [Id(2)] public LumenUserProfileDto? UserProfile { get; set; }
}

#endregion

/// <summary>
/// Get remaining profile updates result
/// </summary>
[GenerateSerializer]
public class GetRemainingUpdatesResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public int UsedCount { get; set; }
    [Id(2)] public int MaxCount { get; set; }
    [Id(3)] public int RemainingCount { get; set; }
    [Id(4)] public DateTime? NextAvailableAt { get; set; } // When next update will be available if limit is reached
}

/// <summary>
/// Update time zone request (does NOT count as profile update)
/// </summary>
[GenerateSerializer]
public class UpdateTimeZoneRequest
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string TimeZoneId { get; set; } = string.Empty; // IANA time zone ID (e.g., "America/New_York", "Asia/Shanghai")
}

/// <summary>
/// Update time zone result
/// </summary>
[GenerateSerializer]
public class UpdateTimeZoneResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Message { get; set; } = string.Empty;
    [Id(2)] public string? TimeZoneId { get; set; } // The updated time zone ID
}
