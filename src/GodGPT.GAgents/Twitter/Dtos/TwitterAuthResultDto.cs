using System;

namespace Aevatar.Application.Grains.Twitter.Dtos;

/// <summary>
/// Twitter OAuth2 authentication result
/// </summary>
[GenerateSerializer]
public class TwitterAuthResultDto
{
    /// <summary>
    /// Whether the authentication was successful
    /// </summary>
    [Id(0)] public bool Success { get; set; }

    /// <summary>
    /// Error message if authentication failed
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
    /// Whether the Twitter account is bound
    /// </summary>
    [Id(4)] public bool BindStatus { get; set; }

    /// <summary>
    /// Twitter profile image URL
    /// </summary>
    [Id(5)] public string ProfileImageUrl { get; set; }
    [Id(6)] public string RedirectUri { get; set; }
}