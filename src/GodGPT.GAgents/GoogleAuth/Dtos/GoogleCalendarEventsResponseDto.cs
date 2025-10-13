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
    public DateTime? DateTime { get; set; }

    [Id(1)]
    [JsonProperty("timeZone")]
    public string? TimeZone { get; set; }

    /// <summary>
    /// Convert DateTime to UTC based on specified timezone
    /// </summary>
    /// <returns>DateTime in UTC, or null if conversion fails</returns>
    public DateTime? ToUtc()
    {
        if (!DateTime.HasValue)
        {
            return null;
        }

        return ConvertToUtc(DateTime.Value, TimeZone);
    }

    /// <summary>
    /// Convert DateTime to UTC based on specified timezone
    /// </summary>
    /// <param name="dateTime">The DateTime to convert</param>
    /// <param name="timeZoneId">The timezone ID (e.g., "America/New_York", "Asia/Shanghai", "UTC")</param>
    /// <returns>DateTime in UTC, or null if conversion fails</returns>
    private static DateTime? ConvertToUtc(DateTime dateTime, string? timeZoneId)
    {
        if (string.IsNullOrEmpty(timeZoneId) || timeZoneId.Equals("UTC", StringComparison.OrdinalIgnoreCase))
        {
            // Already UTC or no timezone specified
            return System.DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        }

        try
        {
            TimeZoneInfo timeZone;
            
            // Handle common timezone formats
            if (timeZoneId.StartsWith("GMT") || timeZoneId.StartsWith("UTC"))
            {
                // Handle GMT+8, UTC-5, etc.
                if (TryParseGmtOffset(timeZoneId, out var offset))
                {
                    var utcTime = dateTime.AddHours(-offset);
                    return System.DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);
                }
                else
                {
                    return System.DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
                }
            }
            
            // Try to find the timezone by ID
            try
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try alternative timezone ID formats
                timeZone = TryFindAlternativeTimeZone(timeZoneId);
                if (timeZone == null)
                {
                    // Fallback: treat as UTC
                    return System.DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
                }
            }

            // Convert to UTC
            var localDateTime = System.DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
            var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(localDateTime, timeZone);
            
            return utcDateTime;
        }
        catch (Exception)
        {
            // Fallback: treat as UTC
            return System.DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        }
    }

    /// <summary>
    /// Try to parse GMT/UTC offset format (e.g., "GMT+8", "UTC-5")
    /// </summary>
    private static bool TryParseGmtOffset(string timeZoneId, out double offsetHours)
    {
        offsetHours = 0;
        
        if (string.IsNullOrEmpty(timeZoneId))
            return false;

        // Remove GMT/UTC prefix
        var offsetString = timeZoneId.Replace("GMT", "").Replace("UTC", "").Trim();
        
        if (string.IsNullOrEmpty(offsetString))
            return true; // GMT or UTC without offset means UTC

        // Parse offset (e.g., "+8", "-5", "+5:30")
        if (offsetString.Contains(':'))
        {
            // Handle formats like "+5:30", "-9:30"
            var parts = offsetString.Split(':');
            if (parts.Length == 2)
            {
                if (double.TryParse(parts[0], out var hours) && double.TryParse(parts[1], out var minutes))
                {
                    offsetHours = hours + (Math.Sign(hours) * minutes / 60.0);
                    return true;
                }
            }
        }
        else
        {
            // Handle simple formats like "+8", "-5"
            return double.TryParse(offsetString, out offsetHours);
        }

        return false;
    }

    /// <summary>
    /// Try to find alternative timezone representations
    /// </summary>
    private static TimeZoneInfo? TryFindAlternativeTimeZone(string timeZoneId)
    {
        // Common timezone ID mappings
        var timeZoneMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Asia/Shanghai", "China Standard Time" },
            { "Asia/Beijing", "China Standard Time" },
            { "America/New_York", "Eastern Standard Time" },
            { "America/Los_Angeles", "Pacific Standard Time" },
            { "Europe/London", "GMT Standard Time" },
            { "Europe/Paris", "W. Europe Standard Time" },
            { "Asia/Tokyo", "Tokyo Standard Time" },
            { "Australia/Sydney", "AUS Eastern Standard Time" },
            { "Asia/Kolkata", "India Standard Time" },
            { "Europe/Berlin", "W. Europe Standard Time" },
            { "America/Chicago", "Central Standard Time" },
            { "America/Denver", "Mountain Standard Time" }
        };

        if (timeZoneMappings.TryGetValue(timeZoneId, out var windowsTimeZoneId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(windowsTimeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                // Continue to other attempts
            }
        }

        // Try to find by display name or standard name
        foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
        {
            if (tz.Id.Equals(timeZoneId, StringComparison.OrdinalIgnoreCase) ||
                tz.StandardName.Equals(timeZoneId, StringComparison.OrdinalIgnoreCase) ||
                tz.DisplayName.Contains(timeZoneId, StringComparison.OrdinalIgnoreCase))
            {
                return tz;
            }
        }

        return null;
    }
}
