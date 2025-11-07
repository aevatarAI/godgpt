namespace Aevatar.Application.Grains.GoogleAuth.Dtos;

/// <summary>
/// Token exchange result
/// </summary>
[GenerateSerializer]
public class TokenResultDto
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string Error { get; set; }
    [Id(2)] public string AccessToken { get; set; }
    [Id(3)] public string TokenType { get; set; }
    [Id(4)] public int ExpiresIn { get; set; }
    [Id(5)] public string RefreshToken { get; set; }
    [Id(6)] public string IdToken { get; set; }
}
