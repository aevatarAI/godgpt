using Orleans;
using Aevatar.Application.Grains.TwitterInteraction.Dtos;

namespace Aevatar.Application.Grains.TwitterInteraction;

/// <summary>
/// 推文监控Grain接口 - 负责定时拉取推文数据
/// </summary>
public interface ITweetMonitorGrain : IGrainWithStringKey
{
    /// <summary>
    /// 启动推文监控定时任务
    /// </summary>
    Task<TwitterApiResultDto<bool>> StartMonitoringAsync();

    /// <summary>
    /// 停止推文监控定时任务
    /// </summary>
    Task<TwitterApiResultDto<bool>> StopMonitoringAsync();

    /// <summary>
    /// 获取监控状态
    /// </summary>
    Task<TwitterApiResultDto<TweetMonitorStatusDto>> GetMonitoringStatusAsync();

    /// <summary>
    /// 手动触发推文数据拉取
    /// </summary>
    Task<TwitterApiResultDto<TweetFetchResultDto>> FetchTweetsManuallyAsync();

    /// <summary>
    /// 查询指定时间区间内的推文数据
    /// </summary>
    Task<TwitterApiResultDto<List<TweetRecord>>> QueryTweetsByTimeRangeAsync(TimeRangeDto timeRange);

    /// <summary>
    /// 获取推文拉取历史记录
    /// </summary>
    Task<TwitterApiResultDto<List<TweetFetchHistoryDto>>> GetFetchHistoryAsync(int days = 7);

    /// <summary>
    /// 清理过期推文数据
    /// </summary>
    Task<TwitterApiResultDto<bool>> CleanupExpiredTweetsAsync();

    /// <summary>
    /// 更新监控配置
    /// </summary>
    Task<TwitterApiResultDto<bool>> UpdateMonitoringConfigAsync(TweetMonitorConfigDto config);

    /// <summary>
    /// 获取当前监控配置
    /// </summary>
    Task<TwitterApiResultDto<TweetMonitorConfigDto>> GetMonitoringConfigAsync();

    /// <summary>
    /// 重新拉取指定时间区间的推文数据（用于数据恢复）
    /// </summary>
    Task<TwitterApiResultDto<TweetFetchResultDto>> RefetchTweetsByTimeRangeAsync(TimeRangeDto timeRange);

    /// <summary>
    /// 获取推文统计信息
    /// </summary>
    Task<TwitterApiResultDto<TweetStatisticsDto>> GetTweetStatisticsAsync(TimeRangeDto timeRange);
} 