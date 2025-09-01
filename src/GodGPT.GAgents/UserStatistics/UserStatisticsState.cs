using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.UserStatistics;

/// <summary>
/// State class for User Statistics GAgent
/// </summary>
[GenerateSerializer]
public class UserStatisticsState : StateBase
{
    [Id(0)] public Guid UserId { get; set; }
    [Id(1)] public bool IsInitialized { get; set; } = false;
    
    /// <summary>
    /// Dictionary of app rating information by platform
    /// Key: DeviceId
    /// Value: App rating information
    /// </summary>
    [Id(2)] public Dictionary<string, AppRatingInfo> AppRatings { get; set; } = new();
}

/// <summary>
/// App rating information for a specific platform
/// </summary>
[GenerateSerializer]
public class AppRatingInfo
{
    /// <summary>
    /// Platform name (iOS/Android/Web)
    /// </summary>
    [Id(0)] public string Platform { get; set; }
    
    /// <summary>
    /// Device ID
    /// </summary>
    [Id(1)] public string DeviceId { get; set; }

    /// <summary>
    /// First time user rated on this platform
    /// </summary>
    [Id(2)] public DateTime FirstRatingTime { get; set; }
    
    /// <summary>
    /// Last time user rated on this platform
    /// </summary>
    [Id(3)] public DateTime LastRatingTime { get; set; }
    
    /// <summary>
    /// Total number of ratings on this platform
    /// </summary>
    [Id(4)] public int RatingCount { get; set; }
}