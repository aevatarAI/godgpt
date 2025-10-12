using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.GoogleAuth;

/// <summary>
/// Google authentication state
/// </summary>
[GenerateSerializer]
public class GoogleAuthState : StateBase
{
    /// <summary>
    /// User ID
    /// </summary>
    [Id(0)]
    public string UserId { get; set; }

    /// <summary>
    /// Google user ID
    /// </summary>
    [Id(1)]
    public string GoogleUserId { get; set; }

    /// <summary>
    /// Google email
    /// </summary>
    [Id(2)]
    public string Email { get; set; }

    /// <summary>
    /// Google display name
    /// </summary>
    [Id(3)]
    public string DisplayName { get; set; }

    /// <summary>
    /// Whether the Google account is bound
    /// </summary>
    [Id(4)]
    public bool IsBound { get; set; }

    /// <summary>
    /// Access token
    /// </summary>
    [Id(5)]
    public string AccessToken { get; set; }

    /// <summary>
    /// Refresh token
    /// </summary>
    [Id(6)]
    public string RefreshToken { get; set; }

    /// <summary>
    /// Token expiration time
    /// </summary>
    [Id(7)]
    public DateTime? TokenExpiresAt { get; set; }

    /// <summary>
    /// OAuth2 state parameter
    /// </summary>
    [Id(8)]
    public string State { get; set; }
    
    /// <summary>
    /// Platform used for authentication (web, ios, android, etc.)
    /// Used for token refresh
    /// </summary>
    [Id(9)]
    public string Platform { get; set; }

    /// <summary>
    /// Calendar sync enabled status
    /// </summary>
    [Id(10)]
    public bool CalendarSyncEnabled { get; set; }

    /// <summary>
    /// Calendar watch resource ID for change notifications
    /// </summary>
    [Id(11)]
    public string CalendarWatchResourceId { get; set; }

    /// <summary>
    /// Calendar watch expiration time
    /// </summary>
    [Id(12)]
    public DateTime? CalendarWatchExpiresAt { get; set; }

    /// <summary>
    /// Last calendar sync time
    /// </summary>
    [Id(13)]
    public DateTime? LastCalendarSyncAt { get; set; }
}
