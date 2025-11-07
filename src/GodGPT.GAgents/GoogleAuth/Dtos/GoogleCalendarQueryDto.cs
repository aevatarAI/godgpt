using System;

namespace Aevatar.Application.Grains.GoogleAuth.Dtos;

/// <summary>
/// Google Calendar query parameters
/// </summary>
[GenerateSerializer]
public class GoogleCalendarQueryDto
{
    /// <summary>
    /// Start time for calendar events query (optional, defaults to now)
    /// </summary>
    [Id(0)]
    public string StartTime { get; set; } = string.Empty;

    /// <summary>
    /// End time for calendar events query (optional, defaults to StartTime + configured range)
    /// </summary>
    [Id(1)]
    public string EndTime { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of events to return (optional, uses configured default)
    /// </summary>
    [Id(2)]
    public int MaxResults { get; set; } = 200;

    /// <summary>
    /// Calendar ID to query (optional, defaults to empty string which means query all calendars)
    /// Use "primary" to query only the primary calendar
    /// </summary>
    [Id(3)] public string CalendarId { get; set; } = string.Empty;

    /// <summary>
    /// Whether to expand recurring events into individual instances
    /// </summary>
    [Id(4)] public bool SingleEvents { get; set; } = true;

    /// <summary>
    /// Order by field (startTime, updated)
    /// </summary>
    [Id(5)] public string OrderBy { get; set; } = "startTime";

    /// <summary>
    /// Time zone for the query (optional, defaults to UTC)
    /// </summary>
    [Id(6)] public string TimeZone { get; set; } = "UTC";

    /// <summary>
    /// Show deleted events
    /// </summary>
    [Id(7)] public bool ShowDeleted { get; set; } = false;

    /// <summary>
    /// Page token for pagination (optional)
    /// </summary>
    [Id(8)] public string PageToken { get; set; } = string.Empty;

    /// <summary>
    /// Event types to include (default, birthday, focusTime, fromGmail, outOfOffice, workingLocation)
    /// If null or empty, includes all event types
    /// </summary>
    [Id(9)] public List<string>? EventTypes { get; set; }
}
