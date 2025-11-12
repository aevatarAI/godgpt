namespace Aevatar.Application.Grains.Lumen.Dtos;

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

/// <summary>
/// Tarot Card enumeration (Major Arcana + Minor Arcana)
/// </summary>
[GenerateSerializer]
public enum TarotCardEnum
{
    // Major Arcana (0-21)
    [Id(0)] TheFool = 0,
    [Id(1)] TheMagician = 1,
    [Id(2)] TheHighPriestess = 2,
    [Id(3)] TheEmpress = 3,
    [Id(4)] TheEmperor = 4,
    [Id(5)] TheHierophant = 5,
    [Id(6)] TheLovers = 6,
    [Id(7)] TheChariot = 7,
    [Id(8)] Strength = 8,
    [Id(9)] TheHermit = 9,
    [Id(10)] WheelOfFortune = 10,
    [Id(11)] Justice = 11,
    [Id(12)] TheHangedMan = 12,
    [Id(13)] Death = 13,
    [Id(14)] Temperance = 14,
    [Id(15)] TheDevil = 15,
    [Id(16)] TheTower = 16,
    [Id(17)] TheStar = 17,
    [Id(18)] TheMoon = 18,
    [Id(19)] TheSun = 19,
    [Id(20)] Judgement = 20,
    [Id(21)] TheWorld = 21,
    
    // Wands (22-35)
    [Id(22)] AceOfWands = 22,
    [Id(23)] TwoOfWands = 23,
    [Id(24)] ThreeOfWands = 24,
    [Id(25)] FourOfWands = 25,
    [Id(26)] FiveOfWands = 26,
    [Id(27)] SixOfWands = 27,
    [Id(28)] SevenOfWands = 28,
    [Id(29)] EightOfWands = 29,
    [Id(30)] NineOfWands = 30,
    [Id(31)] TenOfWands = 31,
    [Id(32)] PageOfWands = 32,
    [Id(33)] KnightOfWands = 33,
    [Id(34)] QueenOfWands = 34,
    [Id(35)] KingOfWands = 35,
    
    // Cups (36-49)
    [Id(36)] AceOfCups = 36,
    [Id(37)] TwoOfCups = 37,
    [Id(38)] ThreeOfCups = 38,
    [Id(39)] FourOfCups = 39,
    [Id(40)] FiveOfCups = 40,
    [Id(41)] SixOfCups = 41,
    [Id(42)] SevenOfCups = 42,
    [Id(43)] EightOfCups = 43,
    [Id(44)] NineOfCups = 44,
    [Id(45)] TenOfCups = 45,
    [Id(46)] PageOfCups = 46,
    [Id(47)] KnightOfCups = 47,
    [Id(48)] QueenOfCups = 48,
    [Id(49)] KingOfCups = 49,
    
    // Swords (50-63)
    [Id(50)] AceOfSwords = 50,
    [Id(51)] TwoOfSwords = 51,
    [Id(52)] ThreeOfSwords = 52,
    [Id(53)] FourOfSwords = 53,
    [Id(54)] FiveOfSwords = 54,
    [Id(55)] SixOfSwords = 55,
    [Id(56)] SevenOfSwords = 56,
    [Id(57)] EightOfSwords = 57,
    [Id(58)] NineOfSwords = 58,
    [Id(59)] TenOfSwords = 59,
    [Id(60)] PageOfSwords = 60,
    [Id(61)] KnightOfSwords = 61,
    [Id(62)] QueenOfSwords = 62,
    [Id(63)] KingOfSwords = 63,
    
    // Pentacles (64-77)
    [Id(64)] AceOfPentacles = 64,
    [Id(65)] TwoOfPentacles = 65,
    [Id(66)] ThreeOfPentacles = 66,
    [Id(67)] FourOfPentacles = 67,
    [Id(68)] FiveOfPentacles = 68,
    [Id(69)] SixOfPentacles = 69,
    [Id(70)] SevenOfPentacles = 70,
    [Id(71)] EightOfPentacles = 71,
    [Id(72)] NineOfPentacles = 72,
    [Id(73)] TenOfPentacles = 73,
    [Id(74)] PageOfPentacles = 74,
    [Id(75)] KnightOfPentacles = 75,
    [Id(76)] QueenOfPentacles = 76,
    [Id(77)] KingOfPentacles = 77,
    
    [Id(999)] Unknown = 999
}

/// <summary>
/// Western Zodiac Sign enumeration
/// </summary>
[GenerateSerializer]
public enum ZodiacSignEnum
{
    [Id(0)] Aries = 0,
    [Id(1)] Taurus = 1,
    [Id(2)] Gemini = 2,
    [Id(3)] Cancer = 3,
    [Id(4)] Leo = 4,
    [Id(5)] Virgo = 5,
    [Id(6)] Libra = 6,
    [Id(7)] Scorpio = 7,
    [Id(8)] Sagittarius = 8,
    [Id(9)] Capricorn = 9,
    [Id(10)] Aquarius = 10,
    [Id(11)] Pisces = 11,
    [Id(999)] Unknown = 999
}

/// <summary>
/// Chinese Zodiac Animal enumeration
/// </summary>
[GenerateSerializer]
public enum ChineseZodiacEnum
{
    [Id(0)] Rat = 0,
    [Id(1)] Ox = 1,
    [Id(2)] Tiger = 2,
    [Id(3)] Rabbit = 3,
    [Id(4)] Dragon = 4,
    [Id(5)] Snake = 5,
    [Id(6)] Horse = 6,
    [Id(7)] Goat = 7,
    [Id(8)] Monkey = 8,
    [Id(9)] Rooster = 9,
    [Id(10)] Dog = 10,
    [Id(11)] Pig = 11,
    [Id(999)] Unknown = 999
}

/// <summary>
/// Crystal/Stone enumeration for lucky stones
/// </summary>
[GenerateSerializer]
public enum CrystalStoneEnum
{
    [Id(0)] Amethyst = 0,
    [Id(1)] RoseQuartz = 1,
    [Id(2)] ClearQuartz = 2,
    [Id(3)] Citrine = 3,
    [Id(4)] BlackTourmaline = 4,
    [Id(5)] Selenite = 5,
    [Id(6)] Labradorite = 6,
    [Id(7)] Moonstone = 7,
    [Id(8)] Carnelian = 8,
    [Id(9)] TigersEye = 9,
    [Id(10)] Jade = 10,
    [Id(11)] Turquoise = 11,
    [Id(12)] Lapis = 12,
    [Id(13)] Aquamarine = 13,
    [Id(14)] Emerald = 14,
    [Id(15)] Ruby = 15,
    [Id(16)] Sapphire = 16,
    [Id(17)] Garnet = 17,
    [Id(18)] Opal = 18,
    [Id(19)] Topaz = 19,
    [Id(20)] Peridot = 20,
    [Id(21)] Obsidian = 21,
    [Id(22)] Malachite = 22,
    [Id(23)] Hematite = 23,
    [Id(24)] Pyrite = 24,
    [Id(25)] Fluorite = 25,
    [Id(26)] Aventurine = 26,
    [Id(27)] Jasper = 27,
    [Id(28)] Agate = 28,
    [Id(29)] Bloodstone = 29,
    [Id(30)] Onyx = 30,
    [Id(999)] Unknown = 999
}

#endregion

#region User Management DTOs

/// <summary>
/// Register user request
/// </summary>
[GenerateSerializer]
public class UpdateUserInfoRequest
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string FirstName { get; set; } = string.Empty;
    [Id(2)] public string LastName { get; set; } = string.Empty;
    [Id(3)] public GenderEnum Gender { get; set; }
    [Id(4)] public DateOnly BirthDate { get; set; }
    [Id(5)] public TimeOnly BirthTime { get; set; } = default;
    [Id(6)] public string? BirthCountry { get; set; } // Optional
    [Id(7)] public string? BirthCity { get; set; } // Optional
    [Id(8)] public MbtiTypeEnum? MbtiType { get; set; } // Optional
    [Id(9)] public RelationshipStatusEnum? RelationshipStatus { get; set; } // Optional
    [Id(10)] public string? Interests { get; set; } // Optional
    [Id(11)] public CalendarTypeEnum CalendarType { get; set; } = CalendarTypeEnum.Solar;
    [Id(12)] public string? CurrentResidence { get; set; } // Optional
    [Id(13)] public string? Email { get; set; } // Optional
    [Id(14)] public string? Occupation { get; set; } // Optional
}

/// <summary>
/// Register user result
/// </summary>
[GenerateSerializer]
public class UpdateUserInfoResult
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
public class LumenUserDto
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string FirstName { get; set; } = string.Empty;
    [Id(2)] public string LastName { get; set; } = string.Empty;
    [Id(3)] public GenderEnum Gender { get; set; }
    [Id(4)] public DateOnly BirthDate { get; set; }
    [Id(5)] public TimeOnly? BirthTime { get; set; } // Optional
    [Id(6)] public string? BirthCountry { get; set; } // Optional
    [Id(7)] public string? BirthCity { get; set; } // Optional
    [Id(8)] public MbtiTypeEnum? MbtiType { get; set; } // Optional
    [Id(9)] public RelationshipStatusEnum? RelationshipStatus { get; set; } // Optional
    [Id(10)] public string? Interests { get; set; } // Optional
    [Id(11)] public CalendarTypeEnum? CalendarType { get; set; } // Optional
    [Id(12)] public DateTime CreatedAt { get; set; }
    [Id(13)] public List<string> Actions { get; set; } = new(); // User selected lumen prediction actions
    [Id(14)] public string? CurrentResidence { get; set; } // Optional
    [Id(15)] public string? Email { get; set; } // Optional
    [Id(16)] public DateTime UpdatedAt { get; set; } // Track profile updates for prediction regeneration
    [Id(17)] public string? Occupation { get; set; } // Optional
}

/// <summary>
/// Get user info result
/// </summary>
[GenerateSerializer]
public class GetUserInfoResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Message { get; set; } = string.Empty;
    [Id(2)] public LumenUserDto? UserInfo { get; set; }
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

/// <summary>
/// Update method rating request
/// </summary>
[GenerateSerializer]
public class UpdateMethodRatingRequest
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public Guid PredictionId { get; set; }
    [Id(2)] public string PredictionMethod { get; set; } = string.Empty;
    [Id(3)] public int Rating { get; set; } // 0 or 1 (0=dislike, 1=like)
}

/// <summary>
/// Update method rating result
/// </summary>
[GenerateSerializer]
public class UpdateMethodRatingResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Message { get; set; } = string.Empty;
    [Id(2)] public string? PredictionMethod { get; set; }
    [Id(3)] public int UpdatedRating { get; set; }
    [Id(4)] public DateTime UpdatedAt { get; set; }
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
/// Prediction result DTO (unified for Daily/Yearly/Lifetime)
/// </summary>
[GenerateSerializer]
public class PredictionResultDto
{
    // Metadata
    [Id(0)] public Guid PredictionId { get; set; }
    [Id(1)] public string UserId { get; set; } = string.Empty;
    [Id(2)] public DateOnly PredictionDate { get; set; }
    [Id(3)] public DateTime CreatedAt { get; set; }
    [Id(4)] public bool FromCache { get; set; }
    [Id(5)] public PredictionType Type { get; set; } // Daily/Yearly/Lifetime
    
    // Unified flattened results (key-value pairs, includes enum fields like "tarotCard_enum": "32")
    [Id(6)] public Dictionary<string, string> Results { get; set; } = new();
    
    // Language generation status (indicates which languages are available for switching)
    [Id(7)] public List<string> AvailableLanguages { get; set; } = new();
    [Id(8)] public bool AllLanguagesGenerated { get; set; } // True if all 4 languages (en, zh-tw, zh, es) are generated
    
    // Minimal language metadata (for content query interface)
    [Id(10)] public string RequestedLanguage { get; set; } = "en"; // Language user requested
    [Id(11)] public string ReturnedLanguage { get; set; } = "en";  // Language actually returned
    [Id(12)] public bool IsFallback { get; set; } = false;         // Whether returned language is a fallback
    
    // User feedbacks (if exist)
    [Id(9)] public Dictionary<string, PredictionFeedbackSummary>? Feedbacks { get; set; } = null;
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
/// Feedback summary within prediction history
/// </summary>
[GenerateSerializer]
public class PredictionFeedbackSummary
{
    [Id(0)] public string PredictionMethod { get; set; }
    [Id(1)] public int Rating { get; set; } // 0 or 1 (0=dislike, 1=like)
    [Id(2)] public List<string> FeedbackTypes { get; set; } = new();
    [Id(3)] public string? Comment { get; set; }
    [Id(4)] public DateTime CreatedAt { get; set; }
    [Id(5)] public string? Email { get; set; }
    [Id(6)] public bool AgreeToContact { get; set; }
}

/// <summary>
/// Prediction summary DTO (for history)
/// </summary>
[GenerateSerializer]
public class PredictionSummaryDto
{
    [Id(0)] public Guid PredictionId { get; set; }
    [Id(1)] public DateOnly PredictionDate { get; set; }
    [Id(2)] public Dictionary<string, string> Results { get; set; } = new(); // Flattened prediction results
    [Id(3)] public PredictionType Type { get; set; } // Daily/Yearly/Lifetime
    [Id(4)] public Dictionary<string, PredictionFeedbackSummary>? Feedbacks { get; set; } // User feedbacks if exist
    [Id(5)] public DateTime CreatedAt { get; set; }
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
    [Id(2)] public string? PredictionMethod { get; set; } // Profile: "fourPillars", "westernOverview", "strengths", "challenges", "destiny", "zodiacCycle", "lifePlot", "activationSteps"; Daily/Yearly: any section name
    [Id(3)] public int Rating { get; set; } // 0 or 1 (0=dislike, 1=like)
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
    [Id(3)] public Dictionary<string, FeedbackDetail> MethodFeedbacks { get; set; } = new();
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

#region Favourite DTOs

/// <summary>
/// Toggle favourite request
/// </summary>
[GenerateSerializer]
public class ToggleFavouriteRequest
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public DateOnly Date { get; set; }
    [Id(2)] public Guid PredictionId { get; set; }
    [Id(3)] public bool IsFavourite { get; set; }  // true = add, false = remove
}

/// <summary>
/// Toggle favourite result
/// </summary>
[GenerateSerializer]
public class ToggleFavouriteResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Message { get; set; } = string.Empty;
    [Id(2)] public bool IsFavourite { get; set; }
    [Id(3)] public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Get favourites result
/// </summary>
[GenerateSerializer]
public class GetFavouritesResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Message { get; set; } = string.Empty;
    [Id(2)] public List<FavouriteItemDto> Favourites { get; set; } = new();
}

/// <summary>
/// Favourite item DTO
/// </summary>
[GenerateSerializer]
public class FavouriteItemDto
{
    [Id(0)] public DateOnly Date { get; set; }
    [Id(1)] public Guid PredictionId { get; set; }
    [Id(2)] public DateTime FavouritedAt { get; set; }
}

#endregion

#region Prediction Status

/// <summary>
/// Prediction generation status for a single prediction type
/// </summary>
[GenerateSerializer]
public class PredictionStatusDto
{
    [Id(0)] public PredictionType Type { get; set; }
    [Id(1)] public bool IsGenerated { get; set; } // Whether prediction has been generated
    [Id(2)] public bool IsGenerating { get; set; } // Whether prediction is currently being generated
    [Id(3)] public DateTime? GeneratedAt { get; set; } // When was it generated
    [Id(4)] public DateTime? GenerationStartedAt { get; set; } // When generation started (for in-progress)
    [Id(5)] public DateOnly? PredictionDate { get; set; } // The date this prediction is for
    [Id(6)] public List<string> AvailableLanguages { get; set; } = new(); // Which languages are available (e.g., ["en", "zh-tw"])
    [Id(7)] public bool NeedsRegeneration { get; set; } // Whether prediction needs regeneration (profile updated)
    [Id(8)] public TranslationStatusInfo? TranslationStatus { get; set; } // Detailed translation status (only in /status endpoint)
}

/// <summary>
/// Translation status information (detailed, only in /status endpoint)
/// </summary>
[GenerateSerializer]
public class TranslationStatusInfo
{
    [Id(0)] public bool IsTranslating { get; set; } // Whether translation is in progress
    [Id(1)] public DateTime? StartedAt { get; set; } // When translation started
    [Id(2)] public List<string> TargetLanguages { get; set; } = new(); // Languages being translated (e.g., ["zh-tw", "zh", "es"])
    [Id(3)] public DateTime? EstimatedCompletion { get; set; } // Estimated completion time (optional)
}

/// <summary>
/// Overall prediction status result
/// </summary>
[GenerateSerializer]
public class GetPredictionStatusResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Message { get; set; } = string.Empty;
    [Id(2)] public PredictionStatusDto? Daily { get; set; }
    [Id(3)] public PredictionStatusDto? Yearly { get; set; }
    [Id(4)] public PredictionStatusDto? Lifetime { get; set; }
    [Id(5)] public DateTime? ProfileUpdatedAt { get; set; } // User profile last update time
}

#endregion

