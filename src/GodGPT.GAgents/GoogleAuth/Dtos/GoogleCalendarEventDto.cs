namespace Aevatar.Application.Grains.GoogleAuth.Dtos;

/// <summary>
/// Google Calendar event
/// </summary>
[GenerateSerializer]
public class GoogleCalendarEventDto
{
    [Id(0)] public string Id { get; set; }
    [Id(1)] public string Summary { get; set; }
    [Id(2)] public string Description { get; set; }
    [Id(3)] public GoogleCalendarDateTimeDto? StartTime { get; set; }
    [Id(4)] public GoogleCalendarDateTimeDto? EndTime { get; set; }
    [Id(7)] public string Status { get; set; }
    [Id(8)] public DateTime Created { get; set; }
    [Id(9)] public DateTime Updated { get; set; }
}
