using Aevatar.Application.Grains.Lumen.Dtos;
using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Lumen;

/// <summary>
/// Lumen user profile state data (V2 with FullName)
/// </summary>
[GenerateSerializer]
public class LumenUserProfileState : StateBase
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
    [Id(11)] public List<string> Actions { get; set; } = new();
    [Id(12)] public string? CurrentResidence { get; set; }
    [Id(13)] public string? Email { get; set; }
    [Id(14)] public DateTime CreatedAt { get; set; }
    [Id(15)] public DateTime UpdatedAt { get; set; }
    [Id(17)] public string? Occupation { get; set; } // Optional
    [Id(19)] public string? Icon { get; set; } // Optional - User avatar/icon URL from blob storage
    [Id(20)] public bool IsDeleted { get; set; } // Track if user was deleted (to prevent auto-migration after deletion)
    
    // Multilingual welcome note (language -> {rhythm, essence, ...})
    [Id(16)] public Dictionary<string, Dictionary<string, string>> MultilingualWelcomeNote { get; set; } = new();
    
    // Profile update history (timestamps) for rate limiting
    [Id(18)] public List<DateTime> UpdateHistory { get; set; } = new();
}

