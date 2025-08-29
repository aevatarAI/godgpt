using System;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Device information in timezone for debugging - TODO: Remove before production
/// </summary>
[GenerateSerializer]
public class TimezoneDeviceInfo
{
    [Id(0)] public Guid UserId { get; set; }
    [Id(1)] public string DeviceId { get; set; } = "";
    [Id(2)] public string PushToken { get; set; } = "";
    [Id(3)] public string TimeZoneId { get; set; } = "";
    [Id(4)] public string PushLanguage { get; set; } = "";
    [Id(5)] public bool PushEnabled { get; set; }
    [Id(6)] public DateTime RegisteredAt { get; set; }
    [Id(7)] public DateTime LastTokenUpdate { get; set; }
    [Id(8)] public bool HasEnabledDeviceInTimezone { get; set; }
    [Id(9)] public int TotalDeviceCount { get; set; }
    [Id(10)] public int EnabledDeviceCount { get; set; }
}
