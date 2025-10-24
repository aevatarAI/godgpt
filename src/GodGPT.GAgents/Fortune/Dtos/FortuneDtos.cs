namespace Aevatar.Application.Grains.Fortune.Dtos;

#region Enums

/// <summary>
/// Gender enumeration
/// </summary>
[GenerateSerializer]
public enum GenderEnum
{
    [Id(0)] Male = 0,
    [Id(1)] Female = 1,
    [Id(2)] Other = 2
}

/// <summary>
/// MBTI Type enumeration (16 types) - Optional field
/// </summary>
[GenerateSerializer]
public enum MbtiTypeEnum
{
    // Analysts
    [Id(0)] INTJ = 0,
    [Id(1)] INTP = 1,
    [Id(2)] ENTJ = 2,
    [Id(3)] ENTP = 3,
    
    // Diplomats
    [Id(4)] INFJ = 4,
    [Id(5)] INFP = 5,
    [Id(6)] ENFJ = 6,
    [Id(7)] ENFP = 7,
    
    // Sentinels
    [Id(8)] ISTJ = 8,
    [Id(9)] ISFJ = 9,
    [Id(10)] ESTJ = 10,
    [Id(11)] ESFJ = 11,
    
    // Explorers
    [Id(12)] ISTP = 12,
    [Id(13)] ISFP = 13,
    [Id(14)] ESTP = 14,
    [Id(15)] ESFP = 15
}

/// <summary>
/// Relationship status enumeration
/// </summary>
[GenerateSerializer]
public enum RelationshipStatusEnum
{
    [Id(0)] Single = 0,
    [Id(1)] InRelationship = 1,
    [Id(2)] Married = 2,
    [Id(3)] Situationship = 3
}

/// <summary>
/// Calendar type enumeration
/// </summary>
[GenerateSerializer]
public enum CalendarTypeEnum
{
    [Id(0)] Solar = 0,  // Gregorian/Solar calendar
    [Id(1)] Lunar = 1   // Lunar calendar
}

/// <summary>
/// Feedback type enumeration
/// </summary>
[GenerateSerializer]
public enum FeedbackTypeEnum
{
    [Id(0)] Accuracy = 0,
    [Id(1)] Clarity = 1,
    [Id(2)] Tone = 2,
    [Id(3)] Length = 3,
    [Id(4)] Design = 4,
    [Id(5)] Bug = 5
}

#endregion

#region User Management DTOs

/// <summary>
/// Register user request
/// </summary>
[GenerateSerializer]
public class RegisterUserRequest
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string FirstName { get; set; } = string.Empty;
    [Id(2)] public string LastName { get; set; } = string.Empty;
    [Id(3)] public GenderEnum Gender { get; set; }
    [Id(4)] public DateOnly BirthDate { get; set; }
    [Id(5)] public TimeOnly BirthTime { get; set; } = default;
    [Id(6)] public string BirthCountry { get; set; } = string.Empty;
    [Id(7)] public string BirthCity { get; set; } = string.Empty;
    [Id(8)] public MbtiTypeEnum? MbtiType { get; set; } // Optional
    [Id(9)] public RelationshipStatusEnum? RelationshipStatus { get; set; }
    [Id(10)] public string? Interests { get; set; }
    [Id(11)] public CalendarTypeEnum CalendarType { get; set; } = CalendarTypeEnum.Solar;
}

/// <summary>
/// Register user result
/// </summary>
[GenerateSerializer]
public class RegisterUserResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Message { get; set; } = string.Empty;
    [Id(2)] public string? UserId { get; set; }
    [Id(3)] public DateTime? CreatedAt { get; set; }
}

/// <summary>
/// User info DTO
/// </summary>
[GenerateSerializer]
public class FortuneUserDto
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string FirstName { get; set; } = string.Empty;
    [Id(2)] public string LastName { get; set; } = string.Empty;
    [Id(3)] public GenderEnum Gender { get; set; }
    [Id(4)] public DateOnly BirthDate { get; set; }
    [Id(5)] public TimeOnly BirthTime { get; set; }
    [Id(6)] public string BirthCountry { get; set; } = string.Empty;
    [Id(7)] public string BirthCity { get; set; } = string.Empty;
    [Id(8)] public MbtiTypeEnum? MbtiType { get; set; } // Optional
    [Id(9)] public RelationshipStatusEnum? RelationshipStatus { get; set; }
    [Id(10)] public string? Interests { get; set; }
    [Id(11)] public CalendarTypeEnum CalendarType { get; set; } = CalendarTypeEnum.Solar;
    [Id(12)] public DateTime CreatedAt { get; set; }
    [Id(13)] public List<string> Actions { get; set; } = new(); // User selected fortune prediction actions
}

/// <summary>
/// Get user info result
/// </summary>
[GenerateSerializer]
public class GetUserInfoResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Message { get; set; } = string.Empty;
    [Id(2)] public FortuneUserDto? UserInfo { get; set; }
}

/// <summary>
/// Clear user result (for testing)
/// </summary>
[GenerateSerializer]
public class ClearUserResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Update user actions request
/// </summary>
[GenerateSerializer]
public class UpdateUserActionsRequest
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public List<string> Actions { get; set; } = new(); // Available actions: forecast, horoscope, bazi, ziwei, constellation, numerology, synastry, chineseZodiac, mayanTotem, humanFigure, tarot, zhengYu
}

/// <summary>
/// Update user actions result
/// </summary>
[GenerateSerializer]
public class UpdateUserActionsResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Message { get; set; } = string.Empty;
    [Id(2)] public List<string> UpdatedActions { get; set; } = new();
    [Id(3)] public DateTime UpdatedAt { get; set; }
}

#endregion

#region Prediction DTOs

/// <summary>
/// Get today's prediction request
/// </summary>
[GenerateSerializer]
public class GetTodayPredictionRequest
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
}

/// <summary>
/// Prediction result DTO
/// </summary>
[GenerateSerializer]
public class PredictionResultDto
{
    [Id(0)] public Guid PredictionId { get; set; }
    [Id(1)] public string UserId { get; set; } = string.Empty;
    [Id(2)] public DateOnly PredictionDate { get; set; }
    [Id(3)] public int Energy { get; set; }
    [Id(4)] public Dictionary<string, Dictionary<string, string>> Results { get; set; } = new();
    [Id(5)] public DateTime CreatedAt { get; set; }
    [Id(6)] public bool FromCache { get; set; }
}

/// <summary>
/// Get today's prediction result
/// </summary>
[GenerateSerializer]
public class GetTodayPredictionResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Message { get; set; } = string.Empty;
    [Id(2)] public PredictionResultDto? Prediction { get; set; }
}

/// <summary>
/// Prediction method result with feedback
/// </summary>
[GenerateSerializer]
public class PredictionMethodResult
{
    [Id(0)] public Dictionary<string, string> Content { get; set; } = new(); // Prediction content (summary, description, detail, etc.)
    [Id(1)] public PredictionFeedbackSummary? Feedback { get; set; } // Associated feedback if exists
}

/// <summary>
/// Feedback summary within prediction history
/// </summary>
[GenerateSerializer]
public class PredictionFeedbackSummary
{
    [Id(0)] public int Rating { get; set; } // 1-5 emoji rating
    [Id(1)] public List<string> FeedbackTypes { get; set; } = new();
    [Id(2)] public string? Comment { get; set; }
    [Id(3)] public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Prediction summary DTO (for history)
/// </summary>
[GenerateSerializer]
public class PredictionSummaryDto
{
    [Id(0)] public Guid PredictionId { get; set; }
    [Id(1)] public DateOnly PredictionDate { get; set; }
    [Id(2)] public int Energy { get; set; }
    [Id(3)] public string? ForecastSummary { get; set; } // Brief summary from forecast
    [Id(4)] public Dictionary<string, PredictionMethodResult> Results { get; set; } = new(); // Prediction results with feedbacks merged
    [Id(5)] public bool HasFeedback { get; set; }
    [Id(6)] public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Get prediction history result
/// </summary>
[GenerateSerializer]
public class GetPredictionHistoryResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Message { get; set; } = string.Empty;
    [Id(2)] public List<PredictionSummaryDto> History { get; set; } = new();
}

#endregion

#region Feedback DTOs

/// <summary>
/// Submit feedback request
/// </summary>
[GenerateSerializer]
public class SubmitFeedbackRequest
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public Guid PredictionId { get; set; }
    [Id(2)] public string? PredictionMethod { get; set; } // e.g., "horoscope", "bazi", null for overall
    [Id(3)] public int Rating { get; set; } // 0-5 (emoji rating)
    [Id(4)] public List<string> FeedbackTypes { get; set; } = new();
    [Id(5)] public string? Comment { get; set; }
    [Id(6)] public string? Email { get; set; }
    [Id(7)] public bool AgreeToContact { get; set; }
}

/// <summary>
/// Submit feedback result
/// </summary>
[GenerateSerializer]
public class SubmitFeedbackResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Message { get; set; } = string.Empty;
    [Id(2)] public string? FeedbackId { get; set; }
}

/// <summary>
/// Feedback info DTO
/// </summary>
[GenerateSerializer]
public class FeedbackDto
{
    [Id(0)] public string FeedbackId { get; set; } = string.Empty;
    [Id(1)] public string UserId { get; set; } = string.Empty;
    [Id(2)] public Guid PredictionId { get; set; }
    [Id(3)] public string? PredictionMethod { get; set; } // e.g., "horoscope", "bazi", null for overall
    [Id(4)] public int Rating { get; set; } // 1-5 (emoji rating)
    [Id(5)] public List<string> FeedbackTypes { get; set; } = new();
    [Id(6)] public string? Comment { get; set; }
    [Id(7)] public string? Email { get; set; }
    [Id(8)] public bool AgreeToContact { get; set; }
    [Id(9)] public DateTime CreatedAt { get; set; }
    [Id(10)] public DateTime UpdatedAt { get; set; }
}

#endregion

#region Stats & Recommendations

/// <summary>
/// Method statistics DTO
/// </summary>
[GenerateSerializer]
public class MethodStatsDto
{
    [Id(0)] public string Method { get; set; } = string.Empty;
    [Id(1)] public double AvgRating { get; set; }
    [Id(2)] public int TotalCount { get; set; }
    [Id(3)] public double PositiveRate { get; set; } // Positive feedback rate (3-5 stars / total), 0-100
}

/// <summary>
/// Recommendation item DTO
/// </summary>
[GenerateSerializer]
public class RecommendationItemDto
{
    [Id(0)] public int Rank { get; set; }
    [Id(1)] public string Method { get; set; } = string.Empty;
    [Id(2)] public string Source { get; set; } = string.Empty; // "global" or "personal" or "default"
    [Id(3)] public double AvgRating { get; set; }
    [Id(4)] public int TotalCount { get; set; }
    [Id(5)] public double PositiveRate { get; set; } // Positive feedback rate (3-5 stars / total), 0-100
}

/// <summary>
/// Get recommendations result
/// </summary>
[GenerateSerializer]
public class GetRecommendationsResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Message { get; set; } = string.Empty;
    [Id(2)] public List<RecommendationItemDto> Recommendations { get; set; } = new();
}

#endregion

