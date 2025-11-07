namespace Aevatar.Application.Grains.GoogleAuth.Dtos;

/// <summary>
/// Google user information
/// </summary>
[GenerateSerializer]
public class GoogleUserInfoDto
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Error { get; set; }
    [Id(2)] public string GoogleUserId { get; set; }
    [Id(3)] public string Email { get; set; }
    [Id(4)] public string DisplayName { get; set; }
}
