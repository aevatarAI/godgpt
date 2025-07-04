using Orleans;
using Aevatar.Application.Grains.TwitterInteraction.Dtos;

namespace Aevatar.Application.Grains.TwitterInteraction;

/// <summary>
/// Twitter reward calculation Grain interface - responsible for daily reward calculation at 00:00 UTC
/// </summary>
public interface ITwitterRewardGrain : IGrainWithStringKey
{
    /// <summary>
    /// Start daily reward calculation scheduled task
    /// </summary>
    Task<TwitterApiResultDto<bool>> StartRewardCalculationAsync();

    /// <summary>
    /// Stop daily reward calculation scheduled task
    /// </summary>
    Task<TwitterApiResultDto<bool>> StopRewardCalculationAsync();

    /// <summary>
    /// Get reward calculation status
    /// </summary>
    Task<TwitterApiResultDto<RewardCalculationStatusDto>> GetRewardCalculationStatusAsync();

    /// <summary>
    /// Manually trigger reward calculation for specified date
    /// </summary>
    Task<TwitterApiResultDto<bool>>  TriggerRewardCalculationAsync(DateTime targetDate);

    /// <summary>
    /// Get reward calculation history
    /// </summary>
    Task<TwitterApiResultDto<List<RewardCalculationHistoryDto>>> GetRewardCalculationHistoryAsync(int days = 30);

    /// <summary>
    /// Query user reward records
    /// </summary>
    Task<TwitterApiResultDto<List<UserRewardRecordDto>>> GetUserRewardRecordsAsync(string userId, int days = 30);

    /// <summary>
    /// Get daily reward statistics
    /// </summary>
    Task<TwitterApiResultDto<DailyRewardStatisticsDto>> GetDailyRewardStatisticsAsync(DateTime targetDate);

    /// <summary>
    /// Update reward configuration
    /// </summary>
    Task<TwitterApiResultDto<bool>> UpdateRewardConfigAsync(RewardConfigDto config);

    /// <summary>
    /// Get current reward configuration
    /// </summary>
    Task<TwitterApiResultDto<RewardConfigDto>> GetRewardConfigAsync();

    /// <summary>
    /// Clear reward records for specified date (for testing purposes)
    /// </summary>
    Task<TwitterApiResultDto<bool>> ClearRewardByDayUtcSecondAsync(long utcSeconds);

    /// <summary>
    /// Check if user has received daily reward
    /// </summary>
    Task<TwitterApiResultDto<bool>> HasUserReceivedDailyRewardAsync(string userId, DateTime targetDate);

    /// <summary>
    /// Get system time status for precise time control
    /// </summary>
    Task<TwitterApiResultDto<TimeControlStatusDto>> GetTimeControlStatusAsync();
} 