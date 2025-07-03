using Orleans;
using Aevatar.Application.Grains.TwitterInteraction.Dtos;

namespace Aevatar.Application.Grains.TwitterInteraction;

/// <summary>
/// Tweet monitoring Grain interface - responsible for scheduled tweet data fetching
/// </summary>
public interface ITweetMonitorGrain : IGrainWithStringKey
{
    /// <summary>
    /// Start tweet monitoring scheduled task
    /// </summary>
    Task<TwitterApiResultDto<bool>> StartMonitoringAsync();

    /// <summary>
    /// Stop tweet monitoring scheduled task
    /// </summary>
    Task<TwitterApiResultDto<bool>> StopMonitoringAsync();

    /// <summary>
    /// Get monitoring status
    /// </summary>
    Task<TwitterApiResultDto<TweetMonitorStatusDto>> GetMonitoringStatusAsync();

    /// <summary>
    /// Manually trigger tweet data fetching
    /// </summary>
    Task<TwitterApiResultDto<TweetFetchResultDto>> FetchTweetsManuallyAsync();

    /// <summary>
    /// Query tweet data within specified time range
    /// </summary>
    Task<TwitterApiResultDto<List<TweetRecord>>> QueryTweetsByTimeRangeAsync(TimeRangeDto timeRange);

    /// <summary>
    /// Get tweet fetch history records
    /// </summary>
    Task<TwitterApiResultDto<List<TweetFetchHistoryDto>>> GetFetchHistoryAsync(int days = 7);

    /// <summary>
    /// Clean up expired tweet data
    /// </summary>
    Task<TwitterApiResultDto<bool>> CleanupExpiredTweetsAsync();

    // Configuration update method removed - all configuration now managed through appsettings.json

    /// <summary>
    /// Get current monitoring configuration
    /// </summary>
    Task<TwitterApiResultDto<TweetMonitorConfigDto>> GetMonitoringConfigAsync();

    /// <summary>
    /// Re-fetch tweet data for specified time range (for data recovery)
    /// </summary>
    Task<TwitterApiResultDto<TweetFetchResultDto>> RefetchTweetsByTimeRangeAsync(TimeRangeDto timeRange);

    /// <summary>
    /// Get tweet statistics for specified time range
    /// </summary>
    Task<TwitterApiResultDto<TweetStatisticsDto>> GetTweetStatisticsAsync(TimeRangeDto timeRange);
} 