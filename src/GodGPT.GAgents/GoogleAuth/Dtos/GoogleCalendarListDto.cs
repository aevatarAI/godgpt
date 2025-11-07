namespace Aevatar.Application.Grains.GoogleAuth.Dtos;

/// <summary>
/// Google Calendar list response
/// </summary>
[GenerateSerializer]
public class GoogleCalendarListDto
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Error { get; set; } = string.Empty;
    [Id(2)] public List<GoogleCalendarEventDto> Events { get; set; } = new();
    [Id(3)] public string NextPageToken { get; set; } = string.Empty;
    
    /// <summary>
    /// Total number of calendars queried (when querying all calendars)
    /// </summary>
    [Id(4)] public int TotalCalendarsQueried { get; set; }
    
    /// <summary>
    /// List of calendar IDs that were successfully queried
    /// </summary>
    [Id(5)] public List<string> QueriedCalendarIds { get; set; } = new();
    
    /// <summary>
    /// List of calendar IDs that failed to query with error messages
    /// </summary>
    [Id(6)] public Dictionary<string, string> FailedCalendarIds { get; set; } = new();
}
