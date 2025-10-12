using Newtonsoft.Json;

namespace Aevatar.Application.Grains.GoogleAuth.Dtos;

/// <summary>
/// Response from Google OAuth2 token endpoint
/// </summary>
[GenerateSerializer]
public class GoogleTokenResponseDto
{
    /// <summary>
    /// Access token
    /// </summary>
    [Id(0)]
    [JsonProperty("access_token")]
    public string AccessToken { get; set; }

    /// <summary>
    /// Token type (usually "Bearer")
    /// </summary>
    [Id(1)]
    [JsonProperty("token_type")]
    public string TokenType { get; set; }

    /// <summary>
    /// Expires in (seconds)
    /// </summary>
    [Id(2)]
    [JsonProperty("expires_in")]
    public int ExpiresIn { get; set; }

    /// <summary>
    /// Refresh token
    /// </summary>
    [Id(3)]
    [JsonProperty("refresh_token")]
    public string RefreshToken { get; set; }

    /// <summary>
    /// Scope
    /// </summary>
    [Id(4)]
    [JsonProperty("scope")]
    public string Scope { get; set; }
    
    [Id(5)]
    [JsonProperty("id_token")]
    public string IdToken { get; set; }
}
