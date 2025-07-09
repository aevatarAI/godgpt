namespace Aevatar.Application.Grains.Twitter.Dtos;

/// <summary>
/// Twitter user information result
/// </summary>
[GenerateSerializer]
public class TwitterUserInfoDto
{
    /// <summary>
    /// Whether the operation was successful
    /// </summary>
    [Id(0)] public bool Success { get; set; }

    /// <summary>
    /// Error message if operation failed
    /// </summary>
    [Id(1)] public string Error { get; set; }

    /// <summary>
    /// Twitter user ID
    /// </summary>
    [Id(2)] public string TwitterId { get; set; }

    /// <summary>
    /// Twitter username
    /// </summary>
    [Id(3)] public string Username { get; set; }

    /// <summary>
    /// Twitter user name
    /// </summary>
    [Id(4)] public string Name { get; set; }

    /// <summary>
    /// Twitter user profile image URL
    /// </summary>
    [Id(5)] public string ProfileImageUrl { get; set; }
}