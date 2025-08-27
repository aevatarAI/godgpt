namespace Aevatar.Application.Grains.UserStatistics.Dtos;

/// <summary>
/// Data transfer object for app rating record
/// </summary>
[GenerateSerializer]
public class AppRatingRecordDto
{
    /// <summary>
    /// Platform information (iOS/Android/Web)
    /// </summary>
    [Id(0)] public string Platform { get; set; } = string.Empty;
    
    /// <summary>
    /// Device information from frontend
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
