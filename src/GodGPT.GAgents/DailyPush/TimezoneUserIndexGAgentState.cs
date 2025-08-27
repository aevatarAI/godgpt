using System;
using System.Collections.Generic;
using Orleans;
using Aevatar.Core.Abstractions;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// State for timezone user index GAgent
/// </summary>
[GenerateSerializer]
public class TimezoneUserIndexGAgentState : StateBase
{
    /// <summary>
    /// Timezone ID (e.g., "Asia/Shanghai", "America/New_York")
    /// </summary>
    [Id(0)] public string TimeZoneId { get; set; } = "";
    
    /// <summary>
    /// Active users in this timezone (users with enabled push devices)
    /// </summary>
    [Id(1)] public HashSet<Guid> ActiveUsers { get; set; } = new();
    
    /// <summary>
    /// Last index update timestamp
    /// </summary>
    [Id(2)] public DateTime LastUpdated { get; set; }
    
    /// <summary>
    /// Total active user count for quick access
    /// </summary>
    [Id(3)] public int ActiveUserCount { get; set; }
}
