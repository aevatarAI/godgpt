namespace Aevatar.Application.Grains.GoogleAuth.Dtos;

/// <summary>
/// Google Calendar event
/// </summary>
[GenerateSerializer]
public class GoogleCalendarEventDto
{
    [Id(0)] public string Id { get; set; } = string.Empty;
    [Id(1)] public string Summary { get; set; } = string.Empty;
    [Id(2)] public string Description { get; set; } = string.Empty;
    [Id(3)] public GoogleCalendarDateTimeDto? StartTime { get; set; }
    [Id(4)] public GoogleCalendarDateTimeDto? EndTime { get; set; }
    [Id(7)] public string Status { get; set; } = string.Empty;
    [Id(8)] public DateTime Created { get; set; }
    [Id(9)] public DateTime Updated { get; set; }
    
    /// <summary>
    /// Calendar ID this event belongs to
    /// </summary>
    [Id(10)] public string CalendarId { get; set; } = string.Empty;
    
    /// <summary>
    /// Calendar name/summary this event belongs to
    /// </summary>
    [Id(11)] public string CalendarName { get; set; } = string.Empty;
}
