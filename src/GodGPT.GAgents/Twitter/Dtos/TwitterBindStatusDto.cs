namespace Aevatar.Application.Grains.Twitter.Dtos;

/// <summary>
/// DTO for Twitter binding status
/// </summary>
[GenerateSerializer]
public class TwitterBindStatusDto
{
    /// <summary>
    /// Whether the Twitter account is bound
    /// </summary>
    [Id(0)]
    public bool IsBound { get; set; }

    /// <summary>
    /// Twitter user ID if bound
    /// </summary>
    [Id(1)]
    public string TwitterId { get; set; }

    /// <summary>
    /// Twitter username if bound
    /// </summary>
    [Id(2)]
    public string Username { get; set; }

    /// <summary>
    /// Twitter profile image URL if bound
    /// </summary>
    [Id(3)]
    public string ProfileImageUrl { get; set; }
}