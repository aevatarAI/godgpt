using Newtonsoft.Json;

namespace Aevatar.Application.Grains.GoogleAuth.Dtos;

/// <summary>
/// Google Calendar events API response
/// </summary>
[GenerateSerializer]
public class GoogleCalendarEventsResponseDto
{
    [Id(0)]
    [JsonProperty("items")]
    public List<GoogleCalendarEventItemDto> Items { get; set; } = new();

    [Id(1)]
    [JsonProperty("nextPageToken")]
    public string NextPageToken { get; set; }
}

[GenerateSerializer]
public class GoogleCalendarEventItemDto
{
    [Id(0)]
    [JsonProperty("id")]
    public string Id { get; set; }

    [Id(1)]
    [JsonProperty("summary")]
    public string Summary { get; set; }

    [Id(2)]
    [JsonProperty("description")]
    public string Description { get; set; }

    [Id(3)]
    [JsonProperty("start")]
    public GoogleCalendarDateTimeDto Start { get; set; }

    [Id(4)]
    [JsonProperty("end")]
    public GoogleCalendarDateTimeDto End { get; set; }

    [Id(7)]
    [JsonProperty("status")]
    public string Status { get; set; }

    [Id(8)]
    [JsonProperty("created")]
    public DateTime Created { get; set; }

    [Id(9)]
    [JsonProperty("updated")]
    public DateTime Updated { get; set; }
}

[GenerateSerializer]
public class GoogleCalendarDateTimeDto
{
    [Id(0)]
    [JsonProperty("dateTime")]
    public DateTimeOffset? DateTime { get; set; }

    [Id(1)]
    [JsonProperty("timeZone")]
    public string? TimeZone { get; set; }
}
