using System;
using Orleans;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Result for instant push operation
/// </summary>
[GenerateSerializer]
public class InstantPushResult
{
    [Id(0)] public string Timezone { get; set; } = "";
    [Id(1)] public int TotalUsers { get; set; }
    [Id(2)] public int TotalDevices { get; set; }
    [Id(3)] public int SuccessfulPushes { get; set; }
    [Id(4)] public int FailedPushes { get; set; }
    [Id(5)] public int NotificationsPerDevice { get; set; }
    [Id(6)] public DateTime Timestamp { get; set; }
    [Id(7)] public string Message { get; set; } = "";
    [Id(8)] public string? Error { get; set; }
}
