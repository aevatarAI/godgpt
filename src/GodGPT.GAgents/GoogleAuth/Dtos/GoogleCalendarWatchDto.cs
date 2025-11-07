namespace Aevatar.Application.Grains.GoogleAuth.Dtos;

/// <summary>
/// Google Calendar watch request/response
/// </summary>
[GenerateSerializer]
public class GoogleCalendarWatchDto
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Error { get; set; }
    [Id(2)] public string Id { get; set; }
    [Id(3)] public string Type { get; set; }
    [Id(4)] public string Address { get; set; }
    [Id(5)] public string Token { get; set; }
    [Id(6)] public DateTime Expiration { get; set; }
    [Id(7)] public string ResourceId { get; set; }
    [Id(8)] public string ResourceUri { get; set; }
}
