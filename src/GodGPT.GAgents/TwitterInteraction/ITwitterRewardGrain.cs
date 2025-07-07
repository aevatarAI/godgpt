using Orleans;
using Aevatar.Application.Grains.TwitterInteraction.Dtos;

namespace Aevatar.Application.Grains.TwitterInteraction;

/// <summary>
/// 推特奖励计算Grain接口 - 负责每日00:00 UTC执行奖励计算
/// </summary>
public interface ITwitterRewardGrain : IGrainWithStringKey
{
    /// <summary>
    /// 启动每日奖励计算定时任务
    /// </summary>
    Task<TwitterApiResultDto<bool>> StartRewardCalculationAsync();

    /// <summary>
    /// 停止每日奖励计算定时任务
    /// </summary>
    Task<TwitterApiResultDto<bool>> StopRewardCalculationAsync();

    /// <summary>
    /// 获取奖励计算状态
    /// </summary>
    Task<TwitterApiResultDto<RewardCalculationStatusDto>> GetRewardCalculationStatusAsync();

    /// <summary>
    /// 手动触发奖励计算（指定日期）
    /// </summary>
    Task<TwitterApiResultDto<RewardCalculationResultDto>> TriggerRewardCalculationAsync(DateTime targetDate);

    /// <summary>
    /// 获取奖励计算历史
    /// </summary>
    Task<TwitterApiResultDto<List<RewardCalculationHistoryDto>>> GetRewardCalculationHistoryAsync(int days = 30);

    /// <summary>
    /// 查询用户奖励记录
    /// </summary>
    Task<TwitterApiResultDto<List<UserRewardRecordDto>>> GetUserRewardRecordsAsync(string userId, int days = 30);

    /// <summary>
    /// 获取每日奖励统计
    /// </summary>
    Task<TwitterApiResultDto<DailyRewardStatisticsDto>> GetDailyRewardStatisticsAsync(DateTime targetDate);

    /// <summary>
    /// 更新奖励配置
    /// </summary>
    Task<TwitterApiResultDto<bool>> UpdateRewardConfigAsync(RewardConfigDto config);

    /// <summary>
    /// 获取当前奖励配置
    /// </summary>
    Task<TwitterApiResultDto<RewardConfigDto>> GetRewardConfigAsync();

    /// <summary>
    /// 重新计算指定日期的奖励（用于数据恢复）
    /// </summary>
    Task<TwitterApiResultDto<RewardCalculationResultDto>> RecalculateRewardsForDateAsync(DateTime targetDate, bool forceRecalculate = false);

    /// <summary>
    /// 检查用户是否已获得当日奖励
    /// </summary>
    Task<TwitterApiResultDto<bool>> HasUserReceivedDailyRewardAsync(string userId, DateTime targetDate);

    /// <summary>
    /// 获取系统时间状态（用于时间精确控制）
    /// </summary>
    Task<TwitterApiResultDto<TimeControlStatusDto>> GetTimeControlStatusAsync();
} 