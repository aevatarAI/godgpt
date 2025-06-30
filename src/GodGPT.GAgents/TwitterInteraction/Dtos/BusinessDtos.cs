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
    [Id(3)] public DateTime CreatedAt { get; set; }
    [Id(4)] public TweetType Type { get; set; }
    [Id(5)] public int ViewCount { get; set; }
    [Id(6)] public int FollowerCount { get; set; }
    [Id(7)] public bool HasValidShareLink { get; set; }
    [Id(8)] public string ShareLinkUrl { get; set; } = string.Empty;
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