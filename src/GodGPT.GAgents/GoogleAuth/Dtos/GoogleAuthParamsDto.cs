namespace Aevatar.Application.Grains.GoogleAuth.Dtos;

/// <summary>
/// Google OAuth2 authorization parameters
/// </summary>
[GenerateSerializer]
public class GoogleAuthParamsDto
{
    [Id(0)] public string ClientId { get; set; }
    [Id(1)] public string ResponseType { get; set; }
    [Id(2)] public List<string> Scope { get; set; }
    [Id(3)] public string State { get; set; }
    [Id(4)] public string RedirectUri { get; set; }
    [Id(5)] public string AccessType { get; set; }
    [Id(6)] public string Prompt { get; set; }
}
