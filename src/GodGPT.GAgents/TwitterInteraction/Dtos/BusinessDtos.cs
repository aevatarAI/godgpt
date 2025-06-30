using System;
using System.Collections.Generic;
using Orleans;

namespace Aevatar.Application.Grains.TwitterInteraction.Dtos;

/// <summary>
/// 任务信息
/// </summary>
[GenerateSerializer]
public class TaskInfo
{
    [Id(0)] public string Name { get; set; } = string.Empty;
    [Id(1)] public bool IsRunning { get; set; }
    [Id(2)] public DateTime LastStartTime { get; set; }
    [Id(3)] public DateTime LastStopTime { get; set; }
    [Id(4)] public string Status { get; set; } = string.Empty;
    [Id(5)] public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Twitter系统配置DTO
/// </summary>
[GenerateSerializer]
public class TwitterSystemConfigDto
{
    [Id(0)] public TimeSpan MonitorInterval { get; set; }
    [Id(1)] public TimeSpan RewardInterval { get; set; }
    [Id(2)] public int MaxTweetsPerRequest { get; set; }
    [Id(3)] public bool AutoStartMonitoring { get; set; }
    [Id(4)] public bool EnableRewardCalculation { get; set; }
    [Id(5)] public Dictionary<string, object> CustomSettings { get; set; } = new();
}

/// <summary>
/// 处理历史DTO
/// </summary>
[GenerateSerializer]
public class ProcessingHistoryDto
{
    [Id(0)] public DateTime Timestamp { get; set; }
    [Id(1)] public string Operation { get; set; } = string.Empty;
    [Id(2)] public string Status { get; set; } = string.Empty;
    [Id(3)] public string Details { get; set; } = string.Empty;
    [Id(4)] public int ProcessedCount { get; set; }
    [Id(5)] public TimeSpan Duration { get; set; }
}

/// <summary>
/// 推文处理结果DTO
/// </summary>
[GenerateSerializer]
public class TweetProcessResultDto
{
    [Id(0)] public string TweetId { get; set; } = string.Empty;
    [Id(1)] public string AuthorId { get; set; } = string.Empty;
    [Id(2)] public string AuthorHandle { get; set; } = string.Empty;
    [Id(3)] public string AuthorName { get; set; } = string.Empty;
    [Id(4)] public DateTime CreatedAt { get; set; }
    [Id(5)] public TweetType Type { get; set; }
    [Id(6)] public int ViewCount { get; set; }
    [Id(7)] public int FollowerCount { get; set; }
    [Id(8)] public bool HasValidShareLink { get; set; }
    [Id(9)] public string ShareLinkUrl { get; set; } = string.Empty;
    [Id(10)] public bool IsProcessed { get; set; }
    [Id(11)] public int RewardCredits { get; set; }
    [Id(12)] public double ShareLinkMultiplier { get; set; } = 1.0;
}

/// <summary>
/// 推文详情DTO
/// </summary>
[GenerateSerializer]
public class TweetDetailsDto
{
    [Id(0)] public string TweetId { get; set; } = string.Empty;
    [Id(1)] public string Text { get; set; } = string.Empty;
    [Id(2)] public string AuthorId { get; set; } = string.Empty;
    [Id(3)] public string AuthorHandle { get; set; } = string.Empty;
    [Id(4)] public DateTime CreatedAt { get; set; }
    [Id(5)] public TweetType Type { get; set; }
    [Id(6)] public int ViewCount { get; set; }
    [Id(7)] public int RetweetCount { get; set; }
    [Id(8)] public int LikeCount { get; set; }
    [Id(9)] public int ReplyCount { get; set; }
    [Id(10)] public int QuoteCount { get; set; }
    [Id(11)] public bool HasValidShareLink { get; set; }
    [Id(12)] public string ShareLinkUrl { get; set; } = string.Empty;
    [Id(13)] public List<string> ExtractedUrls { get; set; } = new();
}

/// <summary>
/// 用户信息DTO
/// </summary>
[GenerateSerializer]
public class UserInfoDto
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string Username { get; set; } = string.Empty;
    [Id(2)] public string Name { get; set; } = string.Empty;
    [Id(3)] public int FollowersCount { get; set; }
    [Id(4)] public int FollowingCount { get; set; }
    [Id(5)] public int TweetCount { get; set; }
    [Id(6)] public bool IsVerified { get; set; }
    [Id(7)] public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 分享链接验证结果DTO
/// </summary>
[GenerateSerializer]
public class ShareLinkValidationDto
{
    [Id(0)] public bool IsValid { get; set; }
    [Id(1)] public string Url { get; set; } = string.Empty;
    [Id(2)] public string ValidationMessage { get; set; } = string.Empty;
    [Id(3)] public string ExtractedShareId { get; set; } = string.Empty;
    [Id(4)] public bool IsAccessible { get; set; }
}

/// <summary>
/// 批量推文处理请求DTO
/// </summary>
[GenerateSerializer]
public class BatchTweetProcessRequestDto
{
    [Id(0)] public List<string> TweetIds { get; set; } = new();
    [Id(1)] public bool IncludeUserInfo { get; set; } = true;
    [Id(2)] public bool ValidateShareLinks { get; set; } = true;
    [Id(3)] public bool FilterOriginalOnly { get; set; } = true;
}

/// <summary>
/// 批量推文处理响应DTO
/// </summary>
[GenerateSerializer]
public class BatchTweetProcessResponseDto
{
    [Id(0)] public List<TweetProcessResultDto> ProcessedTweets { get; set; } = new();
    [Id(1)] public List<string> FailedTweetIds { get; set; } = new();
    [Id(2)] public int TotalProcessed { get; set; }
    [Id(3)] public int SuccessCount { get; set; }
    [Id(4)] public int FailedCount { get; set; }
    [Id(5)] public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Twitter API配额信息DTO
/// </summary>
[GenerateSerializer]
public class TwitterApiQuotaDto
{
    [Id(0)] public int Limit { get; set; }
    [Id(1)] public int Remaining { get; set; }
    [Id(2)] public DateTime ResetTime { get; set; }
    [Id(3)] public int UsedCount { get; set; }
    [Id(4)] public double UsagePercentage { get; set; }
}

/// <summary>
/// 推文记录 - 存储在TweetMonitorGrain中的推文数据结构
/// </summary>
[GenerateSerializer]
public class TweetRecord
{
    [Id(0)] public string TweetId { get; set; } = string.Empty;
    [Id(1)] public string AuthorId { get; set; } = string.Empty;
    [Id(2)] public string AuthorHandle { get; set; } = string.Empty;
    [Id(3)] public string AuthorName { get; set; } = string.Empty;
    [Id(4)] public DateTime CreatedAt { get; set; }
    [Id(5)] public string Text { get; set; } = string.Empty;
    [Id(6)] public TweetType Type { get; set; }
    [Id(7)] public int ViewCount { get; set; }
    [Id(8)] public int FollowerCount { get; set; }
    [Id(9)] public bool HasValidShareLink { get; set; }
    [Id(10)] public string ShareLinkUrl { get; set; } = string.Empty;
    [Id(11)] public bool IsProcessed { get; set; }
    [Id(12)] public DateTime FetchedAt { get; set; }
    [Id(13)] public long CreatedAtUtc { get; set; } // UTC timestamp in seconds
}

/// <summary>
/// 时间区间DTO
/// </summary>
[GenerateSerializer]
public class TimeRangeDto
{
    [Id(0)] public DateTime StartTime { get; set; }
    [Id(1)] public DateTime EndTime { get; set; }
    [Id(2)] public long StartTimeUtc { get; set; } // UTC timestamp in seconds
    [Id(3)] public long EndTimeUtc { get; set; } // UTC timestamp in seconds
}

/// <summary>
/// 推文监控状态DTO
/// </summary>
[GenerateSerializer]
public class TweetMonitorStatusDto
{
    [Id(0)] public bool IsRunning { get; set; }
    [Id(1)] public DateTime? LastFetchTime { get; set; }
    [Id(2)] public long LastFetchTimeUtc { get; set; }
    [Id(3)] public int TotalTweetsStored { get; set; }
    [Id(4)] public int TweetsFetchedToday { get; set; }
    [Id(5)] public string LastError { get; set; } = string.Empty;
    [Id(6)] public TweetMonitorConfigDto Config { get; set; } = new();
    [Id(7)] public DateTime NextScheduledFetch { get; set; }
    [Id(8)] public long NextScheduledFetchUtc { get; set; }
}

/// <summary>
/// 推文拉取结果DTO
/// </summary>
[GenerateSerializer]
public class TweetFetchResultDto
{
    [Id(0)] public int TotalFetched { get; set; }
    [Id(1)] public int NewTweets { get; set; }
    [Id(2)] public int DuplicateSkipped { get; set; }
    [Id(3)] public int FilteredOut { get; set; }
    [Id(4)] public DateTime FetchStartTime { get; set; }
    [Id(5)] public DateTime FetchEndTime { get; set; }
    [Id(6)] public long FetchStartTimeUtc { get; set; }
    [Id(7)] public long FetchEndTimeUtc { get; set; }
    [Id(8)] public List<string> NewTweetIds { get; set; } = new();
    [Id(9)] public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// 推文拉取历史记录DTO
/// </summary>
[GenerateSerializer]
public class TweetFetchHistoryDto
{
    [Id(0)] public DateTime FetchTime { get; set; }
    [Id(1)] public long FetchTimeUtc { get; set; }
    [Id(2)] public int TweetsFetched { get; set; }
    [Id(3)] public int NewTweets { get; set; }
    [Id(4)] public bool IsSuccess { get; set; }
    [Id(5)] public string ErrorMessage { get; set; } = string.Empty;
    [Id(6)] public TimeSpan Duration { get; set; }
}

/// <summary>
/// 推文监控配置DTO
/// </summary>
[GenerateSerializer]
public class TweetMonitorConfigDto
{
    [Id(0)] public int FetchIntervalMinutes { get; set; } = 30;
    [Id(1)] public int MaxTweetsPerFetch { get; set; } = 100;
    [Id(2)] public int DataRetentionDays { get; set; } = 5;
    [Id(3)] public string SearchQuery { get; set; } = "@GodGPT_";
    [Id(4)] public bool FilterOriginalOnly { get; set; } = true;
    [Id(5)] public bool EnableAutoCleanup { get; set; } = true;
    [Id(6)] public long ConfigVersion { get; set; } = 1;
}

/// <summary>
/// 推文统计信息DTO
/// </summary>
[GenerateSerializer]
public class TweetStatisticsDto
{
    [Id(0)] public int TotalTweets { get; set; }
    [Id(1)] public int OriginalTweets { get; set; }
    [Id(2)] public int TweetsWithShareLinks { get; set; }
    [Id(3)] public int UnprocessedTweets { get; set; }
    [Id(4)] public Dictionary<string, int> TweetsByHour { get; set; } = new();
    [Id(5)] public Dictionary<string, int> TopAuthors { get; set; } = new();
    [Id(6)] public DateTime StatisticsGeneratedAt { get; set; }
    [Id(7)] public TimeRangeDto QueryRange { get; set; } = new();
}

/// <summary>
/// 奖励计算状态DTO
/// </summary>
[GenerateSerializer]
public class RewardCalculationStatusDto
{
    [Id(0)] public bool IsRunning { get; set; }
    [Id(1)] public DateTime? LastCalculationTime { get; set; }
    [Id(2)] public long LastCalculationTimeUtc { get; set; }
    [Id(3)] public DateTime NextScheduledCalculation { get; set; }
    [Id(4)] public long NextScheduledCalculationUtc { get; set; }
    [Id(5)] public string LastError { get; set; } = string.Empty;
    [Id(6)] public RewardConfigDto Config { get; set; } = new();
    [Id(7)] public int TotalUsersRewarded { get; set; }
    [Id(8)] public int TotalCreditsDistributed { get; set; }
}

/// <summary>
/// 奖励计算结果DTO
/// </summary>
[GenerateSerializer]
public class RewardCalculationResultDto
{
    [Id(0)] public DateTime CalculationDate { get; set; }
    [Id(1)] public long CalculationDateUtc { get; set; }
    [Id(2)] public TimeRangeDto ProcessedTimeRange { get; set; } = new();
    [Id(3)] public int TotalTweetsProcessed { get; set; }
    [Id(4)] public int EligibleTweets { get; set; }
    [Id(5)] public int UsersRewarded { get; set; }
    [Id(6)] public int TotalCreditsDistributed { get; set; }
    [Id(7)] public List<UserRewardRecordDto> UserRewards { get; set; } = new();
    [Id(8)] public DateTime ProcessingStartTime { get; set; }
    [Id(9)] public DateTime ProcessingEndTime { get; set; }
    [Id(10)] public TimeSpan ProcessingDuration { get; set; }
    [Id(11)] public string ErrorMessage { get; set; } = string.Empty;
    [Id(12)] public bool IsSuccess { get; set; }
}

/// <summary>
/// 奖励计算历史记录DTO
/// </summary>
[GenerateSerializer]
public class RewardCalculationHistoryDto
{
    [Id(0)] public DateTime CalculationDate { get; set; }
    [Id(1)] public long CalculationDateUtc { get; set; }
    [Id(2)] public bool IsSuccess { get; set; }
    [Id(3)] public int UsersRewarded { get; set; }
    [Id(4)] public int TotalCreditsDistributed { get; set; }
    [Id(5)] public TimeSpan ProcessingDuration { get; set; }
    [Id(6)] public string ErrorMessage { get; set; } = string.Empty;
    [Id(7)] public DateTime ProcessedTimeRangeStart { get; set; }
    [Id(8)] public DateTime ProcessedTimeRangeEnd { get; set; }
}

/// <summary>
/// 用户奖励记录DTO
/// </summary>
[GenerateSerializer]
public class UserRewardRecordDto
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string UserHandle { get; set; } = string.Empty;
    [Id(2)] public string TweetId { get; set; } = string.Empty;
    [Id(3)] public DateTime RewardDate { get; set; }
    [Id(4)] public long RewardDateUtc { get; set; }
    [Id(5)] public int BaseCredits { get; set; }
    [Id(6)] public double ShareLinkMultiplier { get; set; } = 1.0;
    [Id(7)] public int FinalCredits { get; set; }
    [Id(8)] public int ViewCount { get; set; }
    [Id(9)] public int FollowerCount { get; set; }
    [Id(10)] public bool HasValidShareLink { get; set; }
    [Id(11)] public bool IsRewardSent { get; set; }
    [Id(12)] public DateTime? RewardSentTime { get; set; }
    [Id(13)] public string RewardTransactionId { get; set; } = string.Empty;
}

/// <summary>
/// 每日奖励统计DTO
/// </summary>
[GenerateSerializer]
public class DailyRewardStatisticsDto
{
    [Id(0)] public DateTime StatisticsDate { get; set; }
    [Id(1)] public long StatisticsDateUtc { get; set; }
    [Id(2)] public int TotalUsersRewarded { get; set; }
    [Id(3)] public int TotalCreditsDistributed { get; set; }
    [Id(4)] public int TotalTweetsEligible { get; set; }
    [Id(5)] public int TweetsWithShareLinks { get; set; }
    [Id(6)] public Dictionary<string, int> RewardsByTier { get; set; } = new();
    [Id(7)] public Dictionary<string, int> UsersRewarded { get; set; } = new();
    [Id(8)] public double AverageCreditsPerUser { get; set; }
    [Id(9)] public double ShareLinkBonusTotal { get; set; }
}

/// <summary>
/// 奖励配置DTO
/// </summary>
[GenerateSerializer]
public class RewardConfigDto
{
    [Id(0)] public int TimeRangeStartHours { get; set; } = 72; // 72 hours ago
    [Id(1)] public int TimeRangeEndHours { get; set; } = 48;   // 48 hours ago
    [Id(2)] public double ShareLinkMultiplier { get; set; } = 1.1;
    [Id(3)] public int MaxDailyCreditsPerUser { get; set; } = 500;
    [Id(4)] public List<RewardTierDto> RewardTiers { get; set; } = new();
    [Id(5)] public bool EnableRewardCalculation { get; set; } = true;
    [Id(6)] public long ConfigVersion { get; set; } = 1;
    [Id(7)] public List<string> ExcludedUserIds { get; set; } = new(); // System accounts to exclude
}

/// <summary>
/// 奖励等级DTO
/// </summary>
[GenerateSerializer]
public class RewardTierDto
{
    [Id(0)] public int MinViews { get; set; }
    [Id(1)] public int MinFollowers { get; set; }
    [Id(2)] public int RewardCredits { get; set; }
    [Id(3)] public string TierName { get; set; } = string.Empty;
}

/// <summary>
/// 时间控制状态DTO
/// </summary>
[GenerateSerializer]
public class TimeControlStatusDto
{
    [Id(0)] public DateTime CurrentUtcTime { get; set; }
    [Id(1)] public long CurrentUtcTimestamp { get; set; }
    [Id(2)] public DateTime LastRewardCalculationTime { get; set; }
    [Id(3)] public long LastRewardCalculationTimestamp { get; set; }
    [Id(4)] public DateTime NextRewardCalculationTime { get; set; }
    [Id(5)] public long NextRewardCalculationTimestamp { get; set; }
    [Id(6)] public TimeSpan TimeUntilNextCalculation { get; set; }
    [Id(7)] public bool IsRewardCalculationDay { get; set; }
    [Id(8)] public string TimezoneInfo { get; set; } = "UTC";
}

/// <summary>
/// 任务执行状态DTO
/// </summary>
[GenerateSerializer]
public class TaskExecutionStatusDto
{
    [Id(0)] public string TaskName { get; set; } = string.Empty;
    [Id(1)] public bool IsEnabled { get; set; }
    [Id(2)] public bool IsRunning { get; set; }
    [Id(3)] public DateTime? LastExecutionTime { get; set; }
    [Id(4)] public long LastExecutionTimestamp { get; set; }
    [Id(5)] public DateTime? LastSuccessTime { get; set; }
    [Id(6)] public long LastSuccessTimestamp { get; set; }
    [Id(7)] public DateTime? NextScheduledTime { get; set; }
    [Id(8)] public long NextScheduledTimestamp { get; set; }
    [Id(9)] public int RetryCount { get; set; }
    [Id(10)] public string LastError { get; set; } = string.Empty;
    [Id(11)] public string ReminderTargetId { get; set; } = string.Empty;
    [Id(12)] public Dictionary<string, object> TaskMetrics { get; set; } = new();
}

/// <summary>
/// Twitter奖励配置DTO
/// </summary>
[GenerateSerializer]
public class TwitterRewardConfigDto
{
    [Id(0)] public string MonitorHandle { get; set; } = "@GodGPT_";
    [Id(1)] public string SelfAccountId { get; set; } = string.Empty;
    [Id(2)] public bool EnablePullTask { get; set; } = true;
    [Id(3)] public bool EnableRewardTask { get; set; } = true;
    [Id(4)] public int TimeOffsetMinutes { get; set; } = 2880; // 48 hours
    [Id(5)] public int TimeWindowMinutes { get; set; } = 1440; // 24 hours
    [Id(6)] public int DataRetentionDays { get; set; } = 5;
    [Id(7)] public int MaxRetryAttempts { get; set; } = 3;
    [Id(8)] public string PullTaskTargetId { get; set; } = string.Empty;
    [Id(9)] public string RewardTaskTargetId { get; set; } = string.Empty;
    [Id(10)] public int PullIntervalMinutes { get; set; } = 30;
    [Id(11)] public int PullBatchSize { get; set; } = 100;
    [Id(12)] public string ShareLinkDomain { get; set; } = "https://app.godgpt.fun";
    [Id(13)] public double ShareLinkMultiplier { get; set; } = 1.1;
    [Id(14)] public int MaxDailyCreditsPerUser { get; set; } = 500;
    [Id(15)] public List<RewardTierDto> RewardTiers { get; set; } = new();
}

/// <summary>
/// 推文拉取结果DTO
/// </summary>
[GenerateSerializer]
public class PullTweetResultDto
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public int TotalFound { get; set; }
    [Id(2)] public int NewTweets { get; set; }
    [Id(3)] public int DuplicateSkipped { get; set; }
    [Id(4)] public int FilteredOut { get; set; }
    [Id(5)] public int TypeFilteredOut { get; set; }
    [Id(6)] public Dictionary<TweetType, int> TypeStatistics { get; set; } = new();
    [Id(7)] public List<string> ProcessedTweetIds { get; set; } = new();
    [Id(8)] public string ErrorMessage { get; set; } = string.Empty;
    [Id(9)] public DateTime ProcessingStartTime { get; set; }
    [Id(10)] public DateTime ProcessingEndTime { get; set; }
    [Id(11)] public long ProcessingTimestamp { get; set; }
    [Id(12)] public TimeSpan ProcessingDuration { get; set; }
}

/// <summary>
/// 系统健康状态DTO
/// </summary>
[GenerateSerializer]
public class SystemHealthDto
{
    [Id(0)] public bool IsHealthy { get; set; }
    [Id(1)] public DateTime LastUpdateTime { get; set; }
    [Id(2)] public long LastUpdateTimestamp { get; set; }
    [Id(3)] public int ActiveTasks { get; set; }
    [Id(4)] public int PendingTweets { get; set; }
    [Id(5)] public int PendingRewards { get; set; }
    [Id(6)] public List<string> Warnings { get; set; } = new();
    [Id(7)] public List<string> Errors { get; set; } = new();
    [Id(8)] public Dictionary<string, object> HealthMetrics { get; set; } = new();
    [Id(9)] public List<TaskHealthStatusDto> TaskStatuses { get; set; } = new();
}

/// <summary>
/// 任务健康状态DTO
/// </summary>
[GenerateSerializer]
public class TaskHealthStatusDto
{
    [Id(0)] public string TaskName { get; set; } = string.Empty;
    [Id(1)] public bool IsHealthy { get; set; }
    [Id(2)] public DateTime? LastSuccessTime { get; set; }
    [Id(3)] public long LastSuccessTimestamp { get; set; }
    [Id(4)] public string Status { get; set; } = string.Empty;
    [Id(5)] public List<string> Issues { get; set; } = new();
}

/// <summary>
/// 系统指标DTO
/// </summary>
[GenerateSerializer]
public class SystemMetricsDto
{
    [Id(0)] public DateTime GeneratedAt { get; set; }
    [Id(1)] public long GeneratedAtTimestamp { get; set; }
    [Id(2)] public int TotalTweetsStored { get; set; }
    [Id(3)] public int TweetsProcessedToday { get; set; }
    [Id(4)] public int TotalUsersRewarded { get; set; }
    [Id(5)] public int CreditsDistributedToday { get; set; }
    [Id(6)] public int TotalCreditsDistributed { get; set; }
    [Id(7)] public double AverageProcessingTime { get; set; }
    [Id(8)] public int ApiCallsToday { get; set; }
    [Id(9)] public double ApiSuccessRate { get; set; }
    [Id(10)] public Dictionary<string, int> TweetsByType { get; set; } = new();
    [Id(11)] public Dictionary<string, double> PerformanceMetrics { get; set; } = new();
}

/// <summary>
/// 缺失时间段DTO
/// </summary>
[GenerateSerializer]
public class MissingPeriodDto
{
    [Id(0)] public DateTime StartTime { get; set; }
    [Id(1)] public DateTime EndTime { get; set; }
    [Id(2)] public long StartTimestamp { get; set; }
    [Id(3)] public long EndTimestamp { get; set; }
    [Id(4)] public string PeriodId { get; set; } = string.Empty;
    [Id(5)] public string MissingType { get; set; } = string.Empty; // "TweetData", "RewardCalculation", "Both"
    [Id(6)] public int ExpectedTweetCount { get; set; }
    [Id(7)] public int ActualTweetCount { get; set; }
    [Id(8)] public bool HasRewardRecord { get; set; }
    [Id(9)] public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 系统故障检测结果DTO
/// </summary>
[GenerateSerializer]
public class SystemOutageDto
{
    [Id(0)] public bool OutageDetected { get; set; }
    [Id(1)] public DateTime OutageStartTime { get; set; }
    [Id(2)] public DateTime OutageEndTime { get; set; }
    [Id(3)] public long OutageStartTimestamp { get; set; }
    [Id(4)] public long OutageEndTimestamp { get; set; }
    [Id(5)] public int OutageDurationMinutes { get; set; }
    [Id(6)] public List<MissingPeriodDto> AffectedPeriods { get; set; } = new();
    [Id(7)] public string RecoveryPlan { get; set; } = string.Empty;
    [Id(8)] public int TotalMissingPeriods { get; set; }
    [Id(9)] public string OutageReason { get; set; } = string.Empty;
}

/// <summary>
/// 数据恢复结果DTO
/// </summary>
[GenerateSerializer]
public class RecoveryResultDto
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public int RecoveredTweets { get; set; }
    [Id(2)] public int RecalculatedRewards { get; set; }
    [Id(3)] public int AffectedUsers { get; set; }
    [Id(4)] public List<string> ProcessedPeriods { get; set; } = new();
    [Id(5)] public List<string> FailedPeriods { get; set; } = new();
    [Id(6)] public string ErrorMessage { get; set; } = string.Empty;
    [Id(7)] public DateTime RecoveryStartTime { get; set; }
    [Id(8)] public DateTime RecoveryEndTime { get; set; }
    [Id(9)] public long RecoveryTimestamp { get; set; }
    [Id(10)] public TimeSpan RecoveryDuration { get; set; }
    [Id(11)] public List<RecoveryStepDto> RecoverySteps { get; set; } = new();
}

/// <summary>
/// 恢复步骤DTO
/// </summary>
[GenerateSerializer]
public class RecoveryStepDto
{
    [Id(0)] public string StepName { get; set; } = string.Empty;
    [Id(1)] public bool IsCompleted { get; set; }
    [Id(2)] public DateTime StartTime { get; set; }
    [Id(3)] public DateTime? EndTime { get; set; }
    [Id(4)] public string Status { get; set; } = string.Empty;
    [Id(5)] public string Details { get; set; } = string.Empty;
    [Id(6)] public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// 数据完整性报告DTO
/// </summary>
[GenerateSerializer]
public class DataIntegrityReportDto
{
    [Id(0)] public DateTime GeneratedAt { get; set; }
    [Id(1)] public long GeneratedAtTimestamp { get; set; }
    [Id(2)] public TimeRangeDto InspectedRange { get; set; } = new();
    [Id(3)] public bool IsDataComplete { get; set; }
    [Id(4)] public int TotalExpectedPeriods { get; set; }
    [Id(5)] public int ValidPeriods { get; set; }
    [Id(6)] public int MissingPeriods { get; set; }
    [Id(7)] public List<MissingPeriodDto> MissingData { get; set; } = new();
    [Id(8)] public List<DataInconsistencyDto> Inconsistencies { get; set; } = new();
    [Id(9)] public string RecommendedActions { get; set; } = string.Empty;
}

/// <summary>
/// 数据不一致DTO
/// </summary>
[GenerateSerializer]
public class DataInconsistencyDto
{
    [Id(0)] public string InconsistencyType { get; set; } = string.Empty;
    [Id(1)] public string Description { get; set; } = string.Empty;
    [Id(2)] public DateTime DetectedAt { get; set; }
    [Id(3)] public string AffectedPeriod { get; set; } = string.Empty;
    [Id(4)] public string Severity { get; set; } = string.Empty; // "Low", "Medium", "High", "Critical"
    [Id(5)] public string RecommendedAction { get; set; } = string.Empty;
}

/// <summary>
/// 恢复操作请求DTO
/// </summary>
[GenerateSerializer]
public class RecoveryRequestDto
{
    [Id(0)] public DateTime StartTime { get; set; }
    [Id(1)] public DateTime EndTime { get; set; }
    [Id(2)] public long StartTimestamp { get; set; }
    [Id(3)] public long EndTimestamp { get; set; }
    [Id(4)] public bool ForceReprocess { get; set; } = false;
    [Id(5)] public List<string> TargetPeriods { get; set; } = new();
    [Id(6)] public bool RecoverTweetData { get; set; } = true;
    [Id(7)] public bool RecalculateRewards { get; set; } = true;
    [Id(8)] public string RequestedBy { get; set; } = string.Empty;
    [Id(9)] public string RecoveryReason { get; set; } = string.Empty;
}

/// <summary>
/// 测试数据摘要DTO
/// </summary>
[GenerateSerializer]
public class TestDataSummaryDto
{
    [Id(0)] public int TotalTestTweets { get; set; }
    [Id(1)] public int TestUsers { get; set; }
    [Id(2)] public Dictionary<string, int> TweetsByType { get; set; } = new();
    [Id(3)] public Dictionary<string, int> TweetsByTimeRange { get; set; } = new();
    [Id(4)] public int CurrentTestTimeOffset { get; set; }
    [Id(5)] public bool IsTestModeActive { get; set; }
    [Id(6)] public DateTime LastDataInjection { get; set; }
    [Id(7)] public long LastDataInjectionTimestamp { get; set; }
}

/// <summary>
/// 测试处理结果DTO
/// </summary>
[GenerateSerializer]
public class TestProcessingResultDto
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public PullTweetResultDto? PullResult { get; set; }
    [Id(2)] public RewardCalculationResultDto? RewardResult { get; set; }
    [Id(3)] public TimeRangeDto ProcessedRange { get; set; } = new();
    [Id(4)] public DateTime ProcessingStartTime { get; set; }
    [Id(5)] public DateTime ProcessingEndTime { get; set; }
    [Id(6)] public TimeSpan ProcessingDuration { get; set; }
    [Id(7)] public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// 测试场景DTO
/// </summary>
[GenerateSerializer]
public class TestScenarioDto
{
    [Id(0)] public string ScenarioName { get; set; } = string.Empty;
    [Id(1)] public string Description { get; set; } = string.Empty;
    [Id(2)] public List<TestStepDto> Steps { get; set; } = new();
    [Id(3)] public TimeRangeDto TestTimeRange { get; set; } = new();
    [Id(4)] public int ExpectedTweets { get; set; }
    [Id(5)] public int ExpectedUsers { get; set; }
    [Id(6)] public List<ValidationRuleDto> ValidationRules { get; set; } = new();
    [Id(7)] public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// 测试步骤DTO
/// </summary>
[GenerateSerializer]
public class TestStepDto
{
    [Id(0)] public string StepName { get; set; } = string.Empty;
    [Id(1)] public string StepType { get; set; } = string.Empty; // "InjectData", "TriggerTask", "Validate", "Wait"
    [Id(2)] public Dictionary<string, object> Parameters { get; set; } = new();
    [Id(3)] public int DelaySeconds { get; set; } = 0;
    [Id(4)] public bool IsOptional { get; set; } = false;
}

/// <summary>
/// 测试场景结果DTO
/// </summary>
[GenerateSerializer]
public class TestScenarioResultDto
{
    [Id(0)] public string ScenarioName { get; set; } = string.Empty;
    [Id(1)] public bool Success { get; set; }
    [Id(2)] public DateTime StartTime { get; set; }
    [Id(3)] public DateTime EndTime { get; set; }
    [Id(4)] public TimeSpan Duration { get; set; }
    [Id(5)] public List<TestStepResultDto> StepResults { get; set; } = new();
    [Id(6)] public List<ValidationResultDto> ValidationResults { get; set; } = new();
    [Id(7)] public string ErrorMessage { get; set; } = string.Empty;
    [Id(8)] public Dictionary<string, object> Metrics { get; set; } = new();
}

/// <summary>
/// 测试步骤结果DTO
/// </summary>
[GenerateSerializer]
public class TestStepResultDto
{
    [Id(0)] public string StepName { get; set; } = string.Empty;
    [Id(1)] public bool Success { get; set; }
    [Id(2)] public DateTime StartTime { get; set; }
    [Id(3)] public DateTime EndTime { get; set; }
    [Id(4)] public TimeSpan Duration { get; set; }
    [Id(5)] public string Result { get; set; } = string.Empty;
    [Id(6)] public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// 压力测试配置DTO
/// </summary>
[GenerateSerializer]
public class StressTestConfigDto
{
    [Id(0)] public string TestName { get; set; } = string.Empty;
    [Id(1)] public int ConcurrentUsers { get; set; } = 10;
    [Id(2)] public int TotalTweets { get; set; } = 1000;
    [Id(3)] public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(10);
    [Id(4)] public int TweetsPerMinute { get; set; } = 100;
    [Id(5)] public bool IncludeRewardCalculation { get; set; } = true;
    [Id(6)] public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// 压力测试结果DTO
/// </summary>
[GenerateSerializer]
public class StressTestResultDto
{
    [Id(0)] public string TestName { get; set; } = string.Empty;
    [Id(1)] public bool Success { get; set; }
    [Id(2)] public DateTime StartTime { get; set; }
    [Id(3)] public DateTime EndTime { get; set; }
    [Id(4)] public TimeSpan ActualDuration { get; set; }
    [Id(5)] public int TotalTweetsProcessed { get; set; }
    [Id(6)] public int TotalRewardsCalculated { get; set; }
    [Id(7)] public double AverageProcessingTime { get; set; }
    [Id(8)] public double MaxProcessingTime { get; set; }
    [Id(9)] public double ThroughputPerSecond { get; set; }
    [Id(10)] public int ErrorCount { get; set; }
    [Id(11)] public List<string> Errors { get; set; } = new();
    [Id(12)] public Dictionary<string, double> PerformanceMetrics { get; set; } = new();
}

/// <summary>
/// 验证规则DTO
/// </summary>
[GenerateSerializer]
public class ValidationRuleDto
{
    [Id(0)] public string RuleName { get; set; } = string.Empty;
    [Id(1)] public string RuleType { get; set; } = string.Empty; // "DataCount", "TimeRange", "DataIntegrity", "Performance"
    [Id(2)] public Dictionary<string, object> Parameters { get; set; } = new();
    [Id(3)] public object ExpectedValue { get; set; } = new();
    [Id(4)] public string Operator { get; set; } = "equals"; // "equals", "greater", "less", "between"
    [Id(5)] public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 验证结果DTO
/// </summary>
[GenerateSerializer]
public class ValidationResultDto
{
    [Id(0)] public string RuleName { get; set; } = string.Empty;
    [Id(1)] public bool Passed { get; set; }
    [Id(2)] public object ActualValue { get; set; } = new();
    [Id(3)] public object ExpectedValue { get; set; } = new();
    [Id(4)] public string Message { get; set; } = string.Empty;
    [Id(5)] public DateTime ValidatedAt { get; set; }
    [Id(6)] public string Details { get; set; } = string.Empty;
}

/// <summary>
/// 测试报告DTO
/// </summary>
[GenerateSerializer]
public class TestReportDto
{
    [Id(0)] public DateTime GeneratedAt { get; set; }
    [Id(1)] public long GeneratedAtTimestamp { get; set; }
    [Id(2)] public TimeRangeDto ReportPeriod { get; set; } = new();
    [Id(3)] public int TotalTestsExecuted { get; set; }
    [Id(4)] public int SuccessfulTests { get; set; }
    [Id(5)] public int FailedTests { get; set; }
    [Id(6)] public double SuccessRate { get; set; }
    [Id(7)] public List<TestScenarioResultDto> ScenarioResults { get; set; } = new();
    [Id(8)] public List<StressTestResultDto> StressTestResults { get; set; } = new();
    [Id(9)] public Dictionary<string, double> PerformanceMetrics { get; set; } = new();
    [Id(10)] public List<string> Recommendations { get; set; } = new();
    [Id(11)] public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// 测试执行记录DTO
/// </summary>
[GenerateSerializer]
public class TestExecutionRecordDto
{
    [Id(0)] public string TestId { get; set; } = string.Empty;
    [Id(1)] public string TestType { get; set; } = string.Empty;
    [Id(2)] public string TestName { get; set; } = string.Empty;
    [Id(3)] public DateTime ExecutionTime { get; set; }
    [Id(4)] public long ExecutionTimestamp { get; set; }
    [Id(5)] public bool Success { get; set; }
    [Id(6)] public TimeSpan Duration { get; set; }
    [Id(7)] public string Result { get; set; } = string.Empty;
    [Id(8)] public string ErrorMessage { get; set; } = string.Empty;
    [Id(9)] public Dictionary<string, object> Metrics { get; set; } = new();
}

/// <summary>
/// 测试数据导出DTO
/// </summary>
[GenerateSerializer]
public class TestDataExportDto
{
    [Id(0)] public string Format { get; set; } = string.Empty;
    [Id(1)] public DateTime ExportedAt { get; set; }
    [Id(2)] public long ExportedAtTimestamp { get; set; }
    [Id(3)] public string Data { get; set; } = string.Empty;
    [Id(4)] public int RecordCount { get; set; }
    [Id(5)] public string Metadata { get; set; } = string.Empty;
    [Id(6)] public List<string> IncludedFields { get; set; } = new();
} 