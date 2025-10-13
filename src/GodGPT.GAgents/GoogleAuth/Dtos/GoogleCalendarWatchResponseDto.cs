using Newtonsoft.Json;

namespace Aevatar.Application.Grains.GoogleAuth.Dtos;

/// <summary>
/// Google Calendar watch API response
/// </summary>
[GenerateSerializer]
public class GoogleCalendarWatchResponseDto
{
    [Id(0)]
    [JsonProperty("id")]
    public string Id { get; set; }

    [Id(1)]
    [JsonProperty("type")]
    public string Type { get; set; }

    [Id(2)]
    [JsonProperty("address")]
    public string Address { get; set; }

    [Id(3)]
    [JsonProperty("token")]
    public string Token { get; set; }

    [Id(4)]
    [JsonProperty("expiration")]
    public long Expiration { get; set; }

    [Id(5)]
    [JsonProperty("resourceId")]
    public string ResourceId { get; set; }

    [Id(6)]
    [JsonProperty("resourceUri")]
    public string ResourceUri { get; set; }
}
