using System.Collections.Generic;

namespace Aevatar.Application.Grains.Common.Options;

/// <summary>
/// Google OAuth2 authentication options
/// </summary>
[GenerateSerializer]
public class GoogleAuthOptions
{
    /// <summary>
    /// Google OAuth2 token endpoint
    /// </summary>
    [Id(0)] public string TokenEndpoint { get; set; } = "https://oauth2.googleapis.com/token";

    /// <summary>
    /// Google Calendar API endpoint
    /// </summary>
    [Id(1)] public string CalendarApiEndpoint { get; set; } = "https://www.googleapis.com/calendar/v3";
    
    /// <summary>
    /// Google Tasks API endpoint
    /// </summary>
    [Id(2)] public string TasksApiEndpoint { get; set; } = "https://tasks.googleapis.com/tasks/v1";
    
    /// <summary>
    /// Default OAuth scopes (can be overridden per platform)
    /// </summary>
    [Id(3)] public List<string> Scopes { get; set; } = new() {};
    
    /// <summary>
    /// Webhook base URL for calendar notifications
    /// </summary>
    [Id(4)] public string WebhookBaseUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// Token refresh threshold in minutes (refresh when token expires within this time)
    /// </summary>
    [Id(5)] public int TokenRefreshThresholdMinutes { get; set; } = 10;
    
    /// <summary>
    /// Calendar watch expiration in hours
    /// </summary>
    [Id(6)] public int CalendarWatchExpirationHours { get; set; } = 24;

    /// <summary>
    /// Maximum allowed calendar events per request (hard limit)
    /// </summary>
    [Id(7)] public int MaxCalendarResultsLimit { get; set; } = 200;
    
    /// <summary>
    /// Default calendar query time range in days (from now)
    /// </summary>
    [Id(8)] public int DefaultCalendarQueryRangeDays { get; set; } = 30;

    /// <summary>
    /// Maximum allowed tasks per request (hard limit)
    /// </summary>
    [Id(9)] public int MaxTaskListIdResultsLimit { get; set; } = 10;
    
    /// <summary>
    /// Maximum allowed tasks per request (hard limit)
    /// </summary>
    [Id(10)] public int MaxTasksResultsLimit { get; set; } = 200;
    
    /// <summary>
    /// Platform-specific client configurations
    /// Key: platform name (e.g., "web", "ios", "android")
    /// Value: platform configuration
    /// </summary>
    [Id(11)] public Dictionary<string, GooglePlatformConfig?> PlatformConfigs { get; set; } = new Dictionary<string, GooglePlatformConfig?>();
}

/// <summary>
/// Platform-specific Google OAuth configuration
/// </summary>
[GenerateSerializer]
public class GooglePlatformConfig
{
    /// <summary>
    /// Platform-specific client ID
    /// </summary>
    [Id(0)] public string ClientId { get; set; } = string.Empty;
    
    /// <summary>
    /// Client secret (optional for iOS, required for web)
    /// </summary>
    [Id(1)] public string ClientSecret { get; set; } = string.Empty;
    
    /// <summary>
    /// Redirect URI for this platform
    /// </summary>
    [Id(2)] public string RedirectUri { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether this platform requires client secret
    /// iOS typically uses ID tokens without client secret
    /// </summary>
    [Id(3)] public bool RequiresClientSecret { get; set; } = true;

    /// <summary>
    /// Custom scopes for this platform (optional)
    /// </summary>
    [Id(4)] public List<string>? Scopes { get; set; }
}
