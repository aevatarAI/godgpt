using Aevatar.Application.Grains.TwitterInteraction.Dtos;
using Orleans;

namespace Aevatar.Application.Grains.TwitterInteraction;

/// <summary>
/// Twitter system management interface
/// Provides task control, configuration management and system monitoring capabilities
/// </summary>
public interface ITwitterSystemManagerGrain : IGrainWithStringKey
{
    #region Task Control
    
    /// <summary>
    /// Start a specific Twitter task
    /// </summary>
    /// <param name="taskName">Task name (e.g., "TweetMonitor", "RewardCalculation")</param>
    /// <param name="targetId">ReminderTargetId for version control</param>
    /// <returns>Success status</returns>
    Task<TwitterApiResultDto<bool>> StartTaskAsync(string taskName, string targetId);
    
    /// <summary>
    /// Stop a specific Twitter task
    /// </summary>
    /// <param name="taskName">Task name</param>
    /// <returns>Success status</returns>
    Task<TwitterApiResultDto<bool>> StopTaskAsync(string taskName);
    
    /// <summary>
    /// Get status of all Twitter tasks
    /// </summary>
    /// <returns>List of task statuses</returns>
    Task<TwitterApiResultDto<List<TaskExecutionStatusDto>>> GetAllTaskStatusAsync();
    
    #endregion
    
    #region System Monitoring
    
    /// <summary>
    /// Get overall system health status
    /// </summary>
    /// <returns>System health information</returns>
    Task<TwitterApiResultDto<SystemHealthDto>> GetSystemHealthAsync();
    
    #endregion

    /// <summary>
    /// 启动Twitter监听任务
    /// </summary>
    Task StartTweetMonitorAsync();

    /// <summary>
    /// 停止Twitter监听任务
    /// </summary>
    Task StopTweetMonitorAsync();

    /// <summary>
    /// 启动奖励计算任务
    /// </summary>
    Task StartRewardCalculationAsync();

    /// <summary>
    /// 停止奖励计算任务
    /// </summary>
    Task StopRewardCalculationAsync();

    /// <summary>
    /// 获取任务执行状态
    /// </summary>
    Task<TaskExecutionStatusDto> GetTaskStatusAsync();

    /// <summary>
    /// 获取当前配置
    /// </summary>
    Task<TwitterSystemConfigDto> GetCurrentConfigAsync();

    /// <summary>
    /// 设置配置
    /// </summary>
    Task SetConfigAsync(TwitterSystemConfigDto config);

    /// <summary>
    /// 更新时间配置
    /// </summary>
    Task UpdateTimeConfigAsync(TimeSpan monitorInterval, TimeSpan rewardInterval);

    /// <summary>
    /// 手动拉取推文
    /// </summary>
    Task ManualPullTweetsAsync();

    /// <summary>
    /// 手动计算奖励
    /// </summary>
    Task ManualCalculateRewardsAsync();

    /// <summary>
    /// 获取处理历史
    /// </summary>
    Task<List<ProcessingHistoryDto>> GetProcessingHistoryAsync();

    /// <summary>
    /// 获取系统指标
    /// </summary>
    Task<SystemMetricsDto> GetSystemMetricsAsync();
} 