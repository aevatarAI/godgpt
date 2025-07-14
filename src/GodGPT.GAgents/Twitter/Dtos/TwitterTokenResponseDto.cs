using Newtonsoft.Json;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Twitter.Dtos;

/// <summary>
/// Response from Twitter OAuth2 token endpoint
/// </summary>
[GenerateSerializer]
public class TwitterTokenResponseDto
{
    /// <summary>
    /// Access token
    /// </summary>
    [Id(0)]
    [JsonProperty("access_token")]
    public string AccessToken { get; set; }

    /// <summary>
    /// Token type (usually "bearer")
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
    /// Scope
    /// </summary>
    [Id(3)]
    [JsonProperty("scope")]
    public string Scope { get; set; }

    /// <summary>
    /// Refresh token
    /// </summary>
    [Id(4)]
    [JsonProperty("refresh_token")]
    public string RefreshToken { get; set; }
}