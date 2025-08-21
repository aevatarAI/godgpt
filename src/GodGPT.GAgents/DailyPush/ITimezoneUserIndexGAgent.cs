using Aevatar.Core.Abstractions;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Timezone to user mapping index GAgent for efficient user lookup
/// </summary>
public interface ITimezoneUserIndexGAgent : IGAgent, IGrainWithStringKey
{
    /// <summary>
    /// Add user to timezone index
    /// </summary>
    Task AddUserToTimezoneAsync(Guid userId);
    
    /// <summary>
    /// Remove user from timezone index
    /// </summary>
    Task RemoveUserFromTimezoneAsync(Guid userId);
    
    /// <summary>
    /// Get paginated list of active users in this timezone
    /// </summary>
    Task<List<Guid>> GetActiveUsersInTimezoneAsync(int skip, int take);
    
    /// <summary>
    /// Get total user count in this timezone
    /// </summary>
    Task<int> GetActiveUserCountAsync();
    
    /// <summary>
    /// Check if user has active devices in this timezone
    /// </summary>
    Task<bool> HasActiveDeviceInTimezoneAsync(Guid userId);
    
    /// <summary>
    /// Batch update user timezone assignments (for performance)
    /// </summary>
    Task BatchUpdateUsersAsync(List<TimezoneUpdateRequest> updates);
}

/// <summary>
/// Timezone update request for batch operations
/// </summary>
[GenerateSerializer]
public class TimezoneUpdateRequest
{
    [Id(0)] public Guid UserId { get; set; }
    [Id(1)] public string SourceTimezone { get; set; } = "";
    [Id(2)] public string TargetTimezone { get; set; } = "";
    [Id(3)] public bool IsAdd { get; set; } = true; // true for add, false for remove
}
