namespace Aevatar.Application.Grains.Fortune.Dtos;

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
    [Id(12)] public string? Email { get; set; }
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
public class FortuneUserProfileDto
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
    [Id(11)] public DateTime CreatedAt { get; set; }
    [Id(12)] public List<string> Actions { get; set; } = new();
    [Id(13)] public string? CurrentResidence { get; set; }
    [Id(14)] public string? Email { get; set; }
}

/// <summary>
/// Get user profile result (V2)
/// </summary>
[GenerateSerializer]
public class GetUserProfileResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Message { get; set; } = string.Empty;
    [Id(2)] public FortuneUserProfileDto? UserProfile { get; set; }
}

#endregion

