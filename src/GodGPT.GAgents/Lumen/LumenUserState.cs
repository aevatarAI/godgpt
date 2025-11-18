using Aevatar.Application.Grains.Lumen.Dtos;
using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Lumen;

/// <summary>
/// Lumen user state data
/// </summary>
[GenerateSerializer]
public class LumenUserState : StateBase
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string FirstName { get; set; } = string.Empty;
    [Id(2)] public string LastName { get; set; } = string.Empty;
    [Id(3)] public GenderEnum Gender { get; set; }
    [Id(4)] public DateOnly BirthDate { get; set; }
    [Id(5)] public TimeOnly BirthTime { get; set; }
    [Id(6)] public string? BirthCity { get; set; } // Optional
    [Id(7)] public string LatLong { get; set; } = string.Empty; // Latitude,Longitude for astrology calculations
    [Id(8)] public MbtiTypeEnum? MbtiType { get; set; } // Optional
    [Id(9)] public RelationshipStatusEnum? RelationshipStatus { get; set; } // Optional
    [Id(10)] public string? Interests { get; set; } // Optional
    [Id(11)] public CalendarTypeEnum CalendarType { get; set; }
    [Id(12)] public DateTime CreatedAt { get; set; }
    [Id(13)] public DateTime UpdatedAt { get; set; }
    [Id(14)] public List<string> Actions { get; set; } = new(); // User selected lumen prediction actions
    [Id(15)] public string? CurrentResidence { get; set; } // Optional
    [Id(16)] public string? Email { get; set; } // Optional
}

