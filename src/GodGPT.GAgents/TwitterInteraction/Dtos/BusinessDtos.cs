namespace Aevatar.Application.Grains.TwitterInteraction.Dtos;

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