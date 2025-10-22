using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.GoogleAuth.SEvents;

[GenerateSerializer]
public abstract class GoogleAuthLogEvent : StateLogEventBase<GoogleAuthLogEvent>
{
}

[GenerateSerializer]
public class GoogleAccountBoundLogEvent : GoogleAuthLogEvent
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string GoogleUserId { get; set; } = string.Empty;
    [Id(2)] public string Email { get; set; } = string.Empty;
    [Id(3)] public string DisplayName { get; set; } = string.Empty;
    [Id(4)] public string AccessToken { get; set; } = string.Empty;
    [Id(5)] public string RefreshToken { get; set; } = string.Empty;
    [Id(6)] public DateTime TokenExpiresAt { get; set; }
    [Id(7)] public string Platform { get; set; } = string.Empty;
}

[GenerateSerializer]
public class GoogleTokenRefreshedLogEvent : GoogleAuthLogEvent
{
    [Id(0)] public string AccessToken { get; set; } = string.Empty;
    [Id(1)] public DateTime TokenExpiresAt { get; set; }
    [Id(2)] public string RefreshToken { get; set; } = string.Empty;
}

[GenerateSerializer]
public class GoogleTokenRefreshedFailedLogEvent : GoogleAuthLogEvent
{
}

[GenerateSerializer]
public class GoogleCalendarSyncEnabledLogEvent : GoogleAuthLogEvent
{
    [Id(0)] public string WatchResourceId { get; set; } = string.Empty;
    [Id(1)] public DateTime WatchExpiresAt { get; set; }
    [Id(2)] public DateTime LastSyncAt { get; set; }
}

[GenerateSerializer]
public class GoogleCalendarSyncDisabledLogEvent : GoogleAuthLogEvent
{
    [Id(0)] public DateTime DisabledAt { get; set; }
}
