using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.UserInfo.SEvents;

[GenerateSerializer]
public class UserInfoCollectionLogEvent : StateLogEventBase<UserInfoCollectionLogEvent>
{
}

/// <summary>
/// Event for initializing user info collection
/// </summary>
[GenerateSerializer]
public class InitializeUserInfoCollectionLogEvent : UserInfoCollectionLogEvent
{
    [Id(0)] public Guid UserId { get; set; }
    [Id(1)] public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Event for updating user info collection
/// </summary>
[GenerateSerializer]
public class UpdateUserInfoCollectionLogEvent : UserInfoCollectionLogEvent
{
    [Id(0)] public int? Gender { get; set; }
    [Id(1)] public string FirstName { get; set; }
    [Id(2)] public string LastName { get; set; }
    [Id(3)] public string Country { get; set; }
    [Id(4)] public string City { get; set; }
    [Id(5)] public int? Day { get; set; }
    [Id(6)] public int? Month { get; set; }
    [Id(7)] public int? Year { get; set; }
    [Id(8)] public int? Hour { get; set; }
    [Id(9)] public int? Minute { get; set; }
    [Id(10)] public List<string> SeekingInterests { get; set; }
    [Id(11)] public List<string> SourceChannels { get; set; }
    [Id(12)] public List<int> SeekingInterestsCode { get; set; } = new List<int>();
    
    // Source channels codes (Step 7) - Enum values for statistics
    [Id(13)] public List<int> SourceChannelsCode { get; set; } = new List<int>();
    [Id(14)] public DateTime UpdatedAt { get; set; }
    [Id(15)] public Guid UserId { get; set; }

}

/// <summary>
/// Event for clearing all user info collection data
/// </summary>
[GenerateSerializer]
public class ClearUserInfoCollectionLogEvent : UserInfoCollectionLogEvent
{
}
