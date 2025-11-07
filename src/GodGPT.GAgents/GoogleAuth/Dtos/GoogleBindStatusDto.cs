namespace Aevatar.Application.Grains.GoogleAuth.Dtos;

/// <summary>
/// Google account binding status
/// </summary>
[GenerateSerializer]
public class GoogleBindStatusDto
{
    [Id(0)] public bool IsBound { get; set; }
    [Id(1)] public string GoogleUserId { get; set; }
    [Id(2)] public string Email { get; set; }
    [Id(3)] public string DisplayName { get; set; }
    [Id(4)] public bool CalendarSyncEnabled { get; set; }
}
