
using Aevatar.Application.Grains.UserInfo.Enums;

namespace Aevatar.Application.Grains.UserInfo.Dtos;

/// <summary>
/// Main DTO for user information collection during onboarding process
/// </summary>
[GenerateSerializer]
public class UserInfoCollectionDto
{
    [Id(0)] public Guid UserId { get; set; }
    [Id(1)] public UserNameInfoDto NameInfo { get; set; }
    [Id(2)] public UserLocationInfoDto LocationInfo { get; set; }
    [Id(3)] public UserBirthDateInfoDto BirthDateInfo { get; set; }
    [Id(4)] public UserBirthTimeInfoDto BirthTimeInfo { get; set; }
    [Id(5)] public List<string> SeekingInterests { get; set; } = new List<string>();
    [Id(6)] public List<string> SourceChannels { get; set; } = new List<string>();
    [Id(7)] public DateTime CreatedAt { get; set; }
    [Id(8)] public DateTime UpdatedAt { get; set; }
    [Id(9)] public bool IsCompleted { get; set; }
    [Id(10)] public bool IsInitialized { get; set; }
    [Id(11)] public List<int> SeekingInterestsCode { get; set; } = new List<int>();
    
    // Source channels codes (Step 7) - Enum values for statistics
    [Id(12)] public List<int> SourceChannelsCode { get; set; } = new List<int>();

}

/// <summary>
/// User name information (Step 2)
/// </summary>
[GenerateSerializer]
public class UserNameInfoDto
{
    [Id(0)] public string Gender { get; set; } // Required, single selection
    [Id(1)] public string FirstName { get; set; } // Required
    [Id(2)] public string LastName { get; set; } // Required
}

/// <summary>
/// User location information (Step 3)
/// </summary>
[GenerateSerializer]
public class UserLocationInfoDto
{
    [Id(0)] public string Country { get; set; } // Required
    [Id(1)] public string City { get; set; } // Required
}

/// <summary>
/// User birth date information (Step 4)
/// </summary>
[GenerateSerializer]
public class UserBirthDateInfoDto
{
    [Id(0)] public int? Day { get; set; } // Required, but nullable to distinguish from default 0
    [Id(1)] public int? Month { get; set; } // Required, but nullable to distinguish from default 0
    [Id(2)] public int? Year { get; set; } // Required, but nullable to distinguish from default 0
}

/// <summary>
/// User birth time information (Step 5) - Optional
/// </summary>
[GenerateSerializer]
public class UserBirthTimeInfoDto
{
    [Id(0)] public int? Hour { get; set; } // Optional
    [Id(1)] public int? Minute { get; set; } // Optional
}

/// <summary>
/// Request DTO for updating user information collection
/// </summary>
[GenerateSerializer]
public class UpdateUserInfoCollectionDto
{
    [Id(0)] public Guid UserId { get; set; }
    [Id(1)] public UserNameInfoDto NameInfo { get; set; }
    [Id(2)] public UserLocationInfoDto LocationInfo { get; set; }
    [Id(3)] public UserBirthDateInfoDto BirthDateInfo { get; set; }
    [Id(4)] public UserBirthTimeInfoDto BirthTimeInfo { get; set; }
    [Id(5)] public List<SeekingInterestEnum> SeekingInterests { get; set; }
    [Id(6)] public List<SourceChannelEnum> SourceChannels { get; set; }
}

/// <summary>
/// Response DTO for user information collection
/// </summary>
[GenerateSerializer]
public class UserInfoCollectionResponseDto
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Message { get; set; }
    [Id(2)] public UserInfoCollectionDto Data { get; set; }
}

/// <summary>
/// DTO for displaying collected user information (Step 8)
/// </summary>
[GenerateSerializer]
public class UserInfoDisplayDto
{
    [Id(0)] public string FirstName { get; set; }
    [Id(1)] public string LastName { get; set; }
    [Id(2)] public string Gender { get; set; }
    [Id(3)] public int Day { get; set; }
    [Id(4)] public int Month { get; set; }
    [Id(5)] public int Year { get; set; }
    
    // Birth time information (Step 5) - Optional
    [Id(6)] public int? Hour { get; set; }
    [Id(7)] public int? Minute { get; set; }
    [Id(8)] public string Country { get; set; }
    [Id(9)] public string City { get; set; }
    [Id(10)] public List<string> SeekingInterests { get; set; }
    [Id(11)] public List<string> SourceChannels { get; set; }
}
