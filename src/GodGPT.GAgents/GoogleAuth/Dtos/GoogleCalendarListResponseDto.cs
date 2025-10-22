using Newtonsoft.Json;

namespace Aevatar.Application.Grains.GoogleAuth.Dtos;

/// <summary>
/// Google Calendar List API response
/// </summary>
[GenerateSerializer]
public class GoogleCalendarListResponseDto
{
    [Id(0)]
    [JsonProperty("items")]
    public List<GoogleCalendarItemDto> Items { get; set; } = new();

    [Id(1)]
    [JsonProperty("nextPageToken")]
    public string NextPageToken { get; set; } = string.Empty;
}

/// <summary>
/// Google Calendar item from calendar list
/// </summary>
[GenerateSerializer]
public class GoogleCalendarItemDto
{
    [Id(0)]
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [Id(1)]
    [JsonProperty("summary")]
    public string Summary { get; set; } = string.Empty;

    [Id(2)]
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [Id(3)]
    [JsonProperty("timeZone")]
    public string TimeZone { get; set; } = string.Empty;

    [Id(4)]
    [JsonProperty("accessRole")]
    public string AccessRole { get; set; } = string.Empty;

    [Id(5)]
    [JsonProperty("primary")]
    public bool Primary { get; set; }

    [Id(6)]
    [JsonProperty("selected")]
    public bool Selected { get; set; }

    [Id(7)]
    [JsonProperty("hidden")]
    public bool Hidden { get; set; }

    [Id(8)]
    [JsonProperty("colorId")]
    public string ColorId { get; set; } = string.Empty;

    [Id(9)]
    [JsonProperty("backgroundColor")]
    public string BackgroundColor { get; set; } = string.Empty;

    [Id(10)]
    [JsonProperty("foregroundColor")]
    public string ForegroundColor { get; set; } = string.Empty;
}
