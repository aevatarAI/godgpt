namespace Aevatar.Application.Grains.UserStatistics.Dtos;

/// <summary>
/// Data transfer object for complete user statistics
/// </summary>
[GenerateSerializer]
public class UserStatisticsDto
{
    /// <summary>
    /// User identifier
    /// </summary>
    [Id(0)] public Guid UserId { get; set; }
    
    /// <summary>
    /// List of app rating records for all platforms
    /// </summary>
    [Id(1)] public List<AppRatingRecordDto> AppRatings { get; set; } = new();
}
