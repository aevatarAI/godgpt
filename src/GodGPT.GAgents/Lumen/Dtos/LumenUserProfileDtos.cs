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
    [Id(3)] public DateOnly BirthDate { get; set; }
    [Id(4)] public TimeOnly? BirthTime { get; set; } // Optional
    [Id(5)] public string? BirthCountry { get; set; }
    [Id(6)] public string? BirthCity { get; set; }
    [Id(7)] public MbtiTypeEnum? MbtiType { get; set; }
    [Id(8)] public RelationshipStatusEnum? RelationshipStatus { get; set; }
    [Id(9)] public string? Interests { get; set; }
    [Id(10)] public CalendarTypeEnum? CalendarType { get; set; } // Optional
    [Id(11)] public string? CurrentResidence { get; set; }
    [Id(12)] public string? Email { get; set; } // Deprecated, kept in state only
    [Id(13)] public string? Occupation { get; set; }
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
    [Id(4)] public TimeOnly? BirthTime { get; set; } // Optional
    [Id(5)] public string? BirthCountry { get; set; }
    [Id(6)] public string? BirthCity { get; set; }
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

