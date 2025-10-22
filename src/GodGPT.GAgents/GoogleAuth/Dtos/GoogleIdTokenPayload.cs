using Newtonsoft.Json;

namespace Aevatar.Application.Grains.GoogleAuth.Dtos;

/// <summary>
/// Google ID Token JWT payload
/// </summary>
[GenerateSerializer]
public class GoogleIdTokenPayload
{
    /// <summary>
    /// Subject - Google User ID
    /// </summary>
    [Id(0)]
    [JsonProperty("sub")]
    public string Sub { get; set; } = string.Empty;

    /// <summary>
    /// Email address
    /// </summary>
    [Id(1)]
    [JsonProperty("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Display name
    /// </summary>
    [Id(2)]
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Profile picture URL
    /// </summary>
    [Id(3)]
    [JsonProperty("picture")]
    public string Picture { get; set; } = string.Empty;

    /// <summary>
    /// Given name (first name)
    /// </summary>
    [Id(4)]
    [JsonProperty("given_name")]
    public string GivenName { get; set; } = string.Empty;

    /// <summary>
    /// Family name (last name)
    /// </summary>
    [Id(5)]
    [JsonProperty("family_name")]
    public string FamilyName { get; set; } = string.Empty;

    /// <summary>
    /// Issuer
    /// </summary>
    [Id(6)]
    [JsonProperty("iss")]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Audience
    /// </summary>
    [Id(7)]
    [JsonProperty("aud")]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Expiration time (Unix timestamp)
    /// </summary>
    [Id(8)]
    [JsonProperty("exp")]
    public long ExpirationTime { get; set; }

    /// <summary>
    /// Issued at time (Unix timestamp)
    /// </summary>
    [Id(9)]
    [JsonProperty("iat")]
    public long IssuedAt { get; set; }

    /// <summary>
    /// Email verified
    /// </summary>
    [Id(10)]
    [JsonProperty("email_verified")]
    public bool EmailVerified { get; set; }
}