namespace Aevatar.Application.Grains.GoogleAuth.Dtos;

/// <summary>
/// Google OAuth2 authentication result
/// </summary>
[GenerateSerializer]
public class GoogleAuthResultDto
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
    /// Google user ID
    /// </summary>
    [Id(2)] public string GoogleUserId { get; set; }

    /// <summary>
    /// Google email
    /// </summary>
    [Id(3)] public string Email { get; set; }

    /// <summary>
    /// Google display name
    /// </summary>
    [Id(4)] public string DisplayName { get; set; }

    /// <summary>
    /// Whether the Google account is bound
    /// </summary>
    [Id(5)] public bool BindStatus { get; set; }

    /// <summary>
    /// Redirect URI after authentication
    /// </summary>
    [Id(6)] public string RedirectUri { get; set; }
}
