namespace Aevatar.Application.Grains.GoogleAuth.Dtos;

/// <summary>
/// Google Calendar list response
/// </summary>
[GenerateSerializer]
public class GoogleCalendarListDto
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Error { get; set; }
    [Id(2)] public List<GoogleCalendarEventDto> Events { get; set; } = new();
    [Id(3)] public string NextPageToken { get; set; }
}
