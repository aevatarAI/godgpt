using System;
using System.Collections.Generic;
using Orleans;

namespace Aevatar.Application.Grains.TwitterInteraction.Dtos;

/// <summary>
/// Task information
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
/// Twitter system configuration DTO
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
/// Processing history DTO
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
/// Tweet processing result DTO
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
/// Tweet details DTO
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
/// User information DTO
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
/// Share link validation result DTO
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
/// Batch tweet processing request DTO
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
/// Batch tweet processing response DTO
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
/// Twitter API quota information DTO
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
/// Tweet record - Tweet data structure stored in TweetMonitorGrain
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
/// Time range DTO - simplified to use only UTC timestamps
/// </summary>
[GenerateSerializer]
public class TimeRangeDto
{
    /// <summary>
    /// Start time as UTC timestamp in seconds
    /// </summary>
    [Id(0)] public long StartTimeUtcSecond { get; set; }
    
    /// <summary>
    /// End time as UTC timestamp in seconds
    /// </summary>
    [Id(1)] public long EndTimeUtcSecond { get; set; }
    
    /// <summary>
    /// Convenience property to get StartTime as DateTime
    /// </summary>
    public DateTime StartTime => DateTimeOffset.FromUnixTimeSeconds(StartTimeUtcSecond).DateTime;
    
    /// <summary>
    /// Convenience property to get EndTime as DateTime
    /// </summary>
    public DateTime EndTime => DateTimeOffset.FromUnixTimeSeconds(EndTimeUtcSecond).DateTime;
    
    /// <summary>
    /// Create TimeRangeDto from UTC timestamps
    /// </summary>
    /// <param name="startTimeUtcSecond">Start time UTC timestamp in seconds</param>
    /// <param name="endTimeUtcSecond">End time UTC timestamp in seconds</param>
    /// <returns>TimeRangeDto instance</returns>
    public static TimeRangeDto FromUtcSeconds(long startTimeUtcSecond, long endTimeUtcSecond)
    {
        return new TimeRangeDto
        {
            StartTimeUtcSecond = startTimeUtcSecond,
            EndTimeUtcSecond = endTimeUtcSecond
        };
    }
    
    /// <summary>
    /// Create TimeRangeDto from DateTime objects
    /// </summary>
    /// <param name="startTime">Start DateTime</param>
    /// <param name="endTime">End DateTime</param>
    /// <returns>TimeRangeDto instance</returns>
    public static TimeRangeDto FromDateTime(DateTime startTime, DateTime endTime)
    {
        return new TimeRangeDto
        {
            StartTimeUtcSecond = ((DateTimeOffset)startTime).ToUnixTimeSeconds(),
            EndTimeUtcSecond = ((DateTimeOffset)endTime).ToUnixTimeSeconds()
        };
    }
    
    /// <summary>
    /// Create TimeRangeDto for last N hours
    /// </summary>
    /// <param name="hours">Number of hours back from now</param>
    /// <returns>TimeRangeDto instance</returns>
    public static TimeRangeDto LastHours(int hours)
    {
        var endTime = DateTime.UtcNow.AddMinutes(-1); // 30 minutes ago to avoid very recent tweets
        var startTime = endTime.AddHours(-hours);
        return FromDateTime(startTime, endTime);
    }
}

/// <summary>
/// Tweet monitoring status DTO
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
/// Tweet fetch result DTO
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
    [Id(10)] public DateTime QueryStartTime { get; set; }
    [Id(11)] public DateTime QueryEndTime { get; set; }
    [Id(12)] public long QueryStartTimeUtc { get; set; }
    [Id(13)] public long QueryEndTimeUtc { get; set; }
}

/// <summary>
/// Tweet fetch history record DTO
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
/// Tweet monitoring configuration DTO
/// </summary>
[GenerateSerializer]
public class TweetMonitorConfigDto
{
    [Id(0)] public int FetchIntervalMinutes { get; set; } = 30;
    [Id(1)] public int MaxTweetsPerFetch { get; set; } = 100;
    [Id(2)] public int DataRetentionDays { get; set; } = 5;
    [Id(3)] public string SearchQuery { get; set; } = "@godgpt_";
    [Id(4)] public bool FilterOriginalOnly { get; set; } = true;
    [Id(5)] public bool EnableAutoCleanup { get; set; } = true;
    [Id(6)] public long ConfigVersion { get; set; } = 1;
}

/// <summary>
/// Tweet statistics DTO
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
/// Reward calculation status DTO
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
/// Reward calculation result DTO
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
    [Id(7)] public int TotalCreditsAwarded { get; set; }
    [Id(8)] public List<UserRewardRecordDto> UserRewards { get; set; } = new();
    [Id(9)] public DateTime ProcessingStartTime { get; set; }
    [Id(10)] public DateTime ProcessingEndTime { get; set; }
    [Id(11)] public TimeSpan ProcessingDuration { get; set; }
    [Id(12)] public string ErrorMessage { get; set; } = string.Empty;
    [Id(13)] public bool IsSuccess { get; set; }
}

/// <summary>
/// Reward calculation history record DTO
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
/// User reward record DTO
/// </summary>
[GenerateSerializer]
public class UserRewardRecordDto
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string UserHandle { get; set; } = string.Empty;
    [Id(2)] public string TweetId { get; set; } = string.Empty;
    [Id(3)] public DateTime? RewardDate { get; set; }
    [Id(4)] public long RewardDateUtc { get; set; } = 0;
    [Id(5)] public int BaseCredits { get; set; } = 0;
    [Id(6)] public double ShareLinkMultiplier { get; set; } = 1.0;
    [Id(7)] public int FinalCredits { get; set; } = 0;
    [Id(8)] public bool HasValidShareLink { get; set; }
    [Id(9)] public bool IsRewardSent { get; set; }
    [Id(10)] public DateTime? RewardSentTime { get; set; }
    [Id(11)] public string RewardTransactionId { get; set; } = string.Empty;
    
    // New fields for separated credit calculation
    [Id(12)] public int RegularCredits { get; set; } // Regular credits: 2 per tweet, max 10 tweets
    [Id(13)] public int BonusCredits { get; set; } // Bonus credits: based on 8-tier system
    [Id(14)] public int TweetCount { get; set; } // Number of tweets for this user
    [Id(15)] public int BonusCreditsBeforeMultiplier { get; set; } // Bonus credits before share link multiplier
}

/// <summary>
/// Daily reward statistics DTO
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
/// Reward configuration DTO
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
    [Id(8)] public int MinViewsForReward { get; set; } = 20; // Minimum views required for reward eligibility
}

/// <summary>
/// Reward tier DTO
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
/// Time control status DTO
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
/// Task execution status DTO
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
/// Twitter reward configuration DTO
/// </summary>
[GenerateSerializer]
public class TwitterRewardConfigDto
{
    [Id(0)] public string MonitorHandle { get; set; } = "@godgpt_";
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
/// Tweet pull result DTO
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
/// System health status DTO
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
/// Task health status DTO
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
/// System metrics DTO
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
/// Missing period DTO
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
/// System outage detection result DTO
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
/// Data recovery result DTO
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
/// Recovery step DTO
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
/// Data integrity report DTO
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
/// Data inconsistency DTO
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
/// Recovery operation request DTO
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
/// Test data summary DTO
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
/// Test processing result DTO
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
/// Test scenario DTO
/// </summary>
[GenerateSerializer]
public class TestScenarioDto
{
    [Id(0)] public string ScenarioName { get; set; } = string.Empty;
    [Id(1)] public string ScenarioId { get; set; } = string.Empty;
    [Id(2)] public string Description { get; set; } = string.Empty;
    [Id(3)] public List<TestStepDto> Steps { get; set; } = new();
    [Id(4)] public TimeRangeDto TestTimeRange { get; set; } = new();
    [Id(5)] public int ExpectedTweets { get; set; }
    [Id(6)] public int ExpectedUsers { get; set; }
    [Id(7)] public List<ValidationRuleDto> ValidationRules { get; set; } = new();
    [Id(8)] public Dictionary<string, object> Parameters { get; set; } = new();
    [Id(9)] public bool StopOnFirstFailure { get; set; } = false;
}

/// <summary>
/// Test step DTO
/// </summary>
[GenerateSerializer]
public class TestStepDto
{
    [Id(0)] public string StepName { get; set; } = string.Empty;
    [Id(1)] public string StepType { get; set; } = string.Empty; // "InjectData", "TriggerTask", "Validate", "Wait"
    [Id(2)] public string Action { get; set; } = string.Empty;
    [Id(3)] public Dictionary<string, object> Parameters { get; set; } = new();
    [Id(4)] public int DelaySeconds { get; set; } = 0;
    [Id(5)] public bool IsOptional { get; set; } = false;
}

/// <summary>
/// Test scenario result DTO
/// </summary>
[GenerateSerializer]
public class TestScenarioResultDto
{
    [Id(0)] public string ScenarioName { get; set; } = string.Empty;
    [Id(1)] public string ScenarioId { get; set; } = string.Empty;
    [Id(2)] public bool Success { get; set; }
    [Id(3)] public DateTime StartTime { get; set; }
    [Id(4)] public DateTime EndTime { get; set; }
    [Id(5)] public DateTime ExecutionStartTime { get; set; }
    [Id(6)] public DateTime ExecutionEndTime { get; set; }
    [Id(7)] public TimeSpan Duration { get; set; }
    [Id(8)] public TimeSpan TotalDuration { get; set; }
    [Id(9)] public List<TestStepResultDto> StepResults { get; set; } = new();
    [Id(10)] public List<ValidationResultDto> ValidationResults { get; set; } = new();
    [Id(11)] public string ErrorMessage { get; set; } = string.Empty;
    [Id(12)] public Dictionary<string, object> Metrics { get; set; } = new();
}

/// <summary>
/// Test step result DTO
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
    [Id(6)] public string Output { get; set; } = string.Empty;
    [Id(7)] public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// Stress test configuration DTO
/// </summary>
[GenerateSerializer]
public class StressTestConfigDto
{
    [Id(0)] public string TestName { get; set; } = string.Empty;
    [Id(1)] public int ConcurrentUsers { get; set; } = 10;
    [Id(2)] public int TotalTweets { get; set; } = 1000;
    [Id(3)] public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(10);
    [Id(4)] public int TestDurationMinutes { get; set; } = 10;
    [Id(5)] public int TweetsPerMinute { get; set; } = 100;
    [Id(6)] public bool IncludeRewardCalculation { get; set; } = true;
    [Id(7)] public List<string> TestOperations { get; set; } = new();
    [Id(8)] public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// Stress test result DTO
/// </summary>
[GenerateSerializer]
public class StressTestResultDto
{
    [Id(0)] public string TestName { get; set; } = string.Empty;
    [Id(1)] public bool Success { get; set; }
    [Id(2)] public int ConcurrentUsers { get; set; }
    [Id(3)] public int TestDurationMinutes { get; set; }
    [Id(4)] public DateTime StartTime { get; set; }
    [Id(5)] public DateTime EndTime { get; set; }
    [Id(6)] public TimeSpan ActualDuration { get; set; }
    [Id(7)] public int TotalTweetsProcessed { get; set; }
    [Id(8)] public int TotalRewardsCalculated { get; set; }
    [Id(9)] public int TotalRequests { get; set; }
    [Id(10)] public int SuccessfulRequests { get; set; }
    [Id(11)] public int FailedRequests { get; set; }
    [Id(12)] public double AverageProcessingTime { get; set; }
    [Id(13)] public double AverageResponseTime { get; set; }
    [Id(14)] public double MaxProcessingTime { get; set; }
    [Id(15)] public double MaxResponseTime { get; set; }
    [Id(16)] public double MinResponseTime { get; set; }
    [Id(17)] public double ThroughputPerSecond { get; set; }
    [Id(18)] public int ErrorCount { get; set; }
    [Id(19)] public string ErrorMessage { get; set; } = string.Empty;
    [Id(20)] public List<string> Errors { get; set; } = new();
    [Id(21)] public Dictionary<string, double> PerformanceMetrics { get; set; } = new();
    [Id(22)] public Dictionary<string, double> Metrics { get; set; } = new();
}

/// <summary>
/// Validation rule DTO
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
    [Id(6)] public bool IsValid { get; set; } = true;
    [Id(7)] public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// Validation result DTO
/// </summary>
[GenerateSerializer]
public class ValidationResultDto
{
    [Id(0)] public string RuleName { get; set; } = string.Empty;
    [Id(1)] public bool Passed { get; set; }
    [Id(2)] public bool Success { get; set; }
    [Id(3)] public object ActualValue { get; set; } = new();
    [Id(4)] public object ExpectedValue { get; set; } = new();
    [Id(5)] public string Message { get; set; } = string.Empty;
    [Id(6)] public string ErrorMessage { get; set; } = string.Empty;
    [Id(7)] public DateTime ValidatedAt { get; set; }
    [Id(8)] public DateTime ValidationStartTime { get; set; }
    [Id(9)] public DateTime ValidationEndTime { get; set; }
    [Id(10)] public TimeSpan ValidationDuration { get; set; }
    [Id(11)] public string Details { get; set; } = string.Empty;
    [Id(12)] public List<ValidationRuleDto> RuleResults { get; set; } = new();
    [Id(13)] public int PassedRules { get; set; }
    [Id(14)] public int FailedRules { get; set; }
}

/// <summary>
/// Test report DTO
/// </summary>
[GenerateSerializer]
public class TestReportDto
{
    [Id(0)] public string ReportId { get; set; } = string.Empty;
    [Id(1)] public string ReportType { get; set; } = string.Empty;
    [Id(2)] public DateTime GeneratedAt { get; set; }
    [Id(3)] public long GeneratedAtTimestamp { get; set; }
    [Id(4)] public TimeRangeDto ReportPeriod { get; set; } = new();
    [Id(5)] public int TotalTestsExecuted { get; set; }
    [Id(6)] public int TotalExecutions { get; set; }
    [Id(7)] public int SuccessfulTests { get; set; }
    [Id(8)] public int SuccessfulExecutions { get; set; }
    [Id(9)] public int FailedTests { get; set; }
    [Id(10)] public int FailedExecutions { get; set; }
    [Id(11)] public double SuccessRate { get; set; }
    [Id(12)] public List<TestScenarioResultDto> ScenarioResults { get; set; } = new();
    [Id(13)] public List<StressTestResultDto> StressTestResults { get; set; } = new();
    [Id(14)] public Dictionary<string, double> PerformanceMetrics { get; set; } = new();
    [Id(15)] public Dictionary<string, double> TestMetrics { get; set; } = new();
    [Id(16)] public Dictionary<string, int> ExecutionsByType { get; set; } = new();
    [Id(17)] public string TestSummary { get; set; } = string.Empty;
    [Id(18)] public List<string> Recommendations { get; set; } = new();
    [Id(19)] public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// Test execution record DTO
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
    // Additional properties for testing
    [Id(10)] public string ExecutionId { get; set; } = string.Empty;
    [Id(11)] public string OperationType { get; set; } = string.Empty;
    [Id(12)] public string Details { get; set; } = string.Empty;
    [Id(13)] public double TestTimeOffset { get; set; }
}

/// <summary>
/// Test data export DTO
/// </summary>
[GenerateSerializer]
public class TestDataExportDto
{
    [Id(0)] public string ExportId { get; set; } = string.Empty;
    [Id(1)] public string Format { get; set; } = string.Empty;
    [Id(2)] public DateTime ExportTime { get; set; }
    [Id(3)] public long ExportTimestamp { get; set; }
    [Id(4)] public string ExportData { get; set; } = string.Empty;
    [Id(5)] public TestDataSummaryDto TestDataSummary { get; set; } = new();
    [Id(6)] public int DataSize { get; set; }
}

/// <summary>
/// Rule validation result
/// </summary>
[GenerateSerializer]
public class RuleValidationResult
{
    [Id(0)] public string RuleName { get; set; } = string.Empty;
    [Id(1)] public bool IsValid { get; set; }
    [Id(2)] public DateTime ValidationTime { get; set; }
    [Id(3)] public string ValidationMessage { get; set; } = string.Empty;
    [Id(4)] public string ErrorMessage { get; set; } = string.Empty;
    [Id(5)] public Dictionary<string, object> ValidationData { get; set; } = new();
} 