using Aevatar.Application.Grains.Fortune.Dtos;
using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Fortune;

/// <summary>
/// Fortune user state data
/// </summary>
[GenerateSerializer]
public class FortuneUserState : StateBase
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
    [Id(12)] public DateTime UpdatedAt { get; set; }
}

