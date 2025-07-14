namespace Aevatar.Application.Grains.Twitter.Dtos;

/// <summary>
/// OAuth2 token exchange result
/// </summary>
[GenerateSerializer]
public class TokenResultDto
{
    /// <summary>
    /// Whether the token exchange was successful
    /// </summary>
    [Id(0)] public bool Success { get; set; }

    /// <summary>
    /// Error message if token exchange failed
    /// </summary>
    [Id(1)] public string Error { get; set; }

    /// <summary>
    /// Token type (e.g., "Bearer")
    /// </summary>
    [Id(2)] public string TokenType { get; set; }

    /// <summary>
    /// Access token expiration time in seconds
    /// </summary>
    [Id(3)] public int ExpiresIn { get; set; }

    /// <summary>
    /// Access token
    /// </summary>
    [Id(4)] public string AccessToken { get; set; }

    /// <summary>
    /// Refresh token
    /// </summary>
    [Id(5)] public string Scope { get; set; }
}