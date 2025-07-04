using System;
using Aevatar.Core.Abstractions;

namespace GodGPT.GAgents.Twitter;

/// <summary>
/// Twitter authentication state
/// </summary>
[GenerateSerializer]
public class TwitterAuthState : StateBase
{
    /// <summary>
    /// User ID
    /// </summary>
    [Id(0)]
    public string UserId { get; set; }

    /// <summary>
    /// Twitter user ID
    /// </summary>
    [Id(1)]
    public string TwitterUserId { get; set; }

    /// <summary>
    /// Twitter username
    /// </summary>
    [Id(2)]
    public string Username { get; set; }

    /// <summary>
    /// Whether the Twitter account is bound
    /// </summary>
    [Id(3)]
    public bool IsBound { get; set; }

    /// <summary>
    /// Access token
    /// </summary>
    [Id(4)]
    public string AccessToken { get; set; }

    /// <summary>
    /// Refresh token
    /// </summary>
    [Id(5)]
    public string RefreshToken { get; set; }

    /// <summary>
    /// Token expiration time
    /// </summary>
    [Id(6)]
    public DateTime? TokenExpiresAt { get; set; }

    /// <summary>
    /// PKCE code verifier
    /// </summary>
    [Id(7)]
    public string CodeVerifier { get; set; }

    /// <summary>
    /// PKCE code challenge
    /// </summary>
    [Id(8)]
    public string CodeChallenge { get; set; }

    /// <summary>
    /// OAuth2 state parameter
    /// </summary>
    [Id(9)]
    public string State { get; set; }

    [Id(10)] public string ProfileImageUrl { get; set; }
}