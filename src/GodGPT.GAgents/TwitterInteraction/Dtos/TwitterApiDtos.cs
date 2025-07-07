namespace Aevatar.Application.Grains.TwitterInteraction.Dtos;

/// <summary>
/// Twitter API通用结果包装类
/// </summary>
[GenerateSerializer]
public class TwitterApiResultDto<T>
{
    [Id(0)] public bool IsSuccess { get; set; }
    [Id(1)] public T Data { get; set; } = default!;
    [Id(2)] public string ErrorMessage { get; set; } = string.Empty;
    [Id(3)] public int StatusCode { get; set; }
}

/// <summary>
/// Twitter API搜索推文请求
/// </summary>
[GenerateSerializer]
public class SearchTweetsRequestDto
{
    [Id(0)] public string Query { get; set; } = string.Empty;
    [Id(1)] public int MaxResults { get; set; } = 100;
    [Id(2)] public DateTime? StartTime { get; set; }
    [Id(3)] public DateTime? EndTime { get; set; }
    [Id(4)] public string NextToken { get; set; } = string.Empty;
}

/// <summary>
/// Twitter API搜索推文响应
/// </summary>
[GenerateSerializer]
public class SearchTweetsResponseDto
{
    [Id(0)] public List<TweetDto> Data { get; set; } = new();
    [Id(1)] public TwitterMetaDto Meta { get; set; } = new();
    [Id(2)] public List<TwitterUserDto> Includes { get; set; } = new();
}

/// <summary>
/// 推文信息DTO
/// </summary>
[GenerateSerializer]
public class TweetDto
{
    [Id(0)] public string Id { get; set; } = string.Empty;
    [Id(1)] public string Text { get; set; } = string.Empty;
    [Id(2)] public string AuthorId { get; set; } = string.Empty;
    [Id(3)] public DateTime CreatedAt { get; set; }
    [Id(4)] public TwitterPublicMetricsDto PublicMetrics { get; set; } = new();
    [Id(5)] public List<TwitterReferencedTweetDto> ReferencedTweets { get; set; } = new();
    [Id(6)] public TwitterContextAnnotationDto[]? ContextAnnotations { get; set; }
}

/// <summary>
/// Twitter用户信息DTO
/// </summary>
[GenerateSerializer]
public class TwitterUserDto
{
    [Id(0)] public string Id { get; set; } = string.Empty;
    [Id(1)] public string Username { get; set; } = string.Empty;
    [Id(2)] public string Name { get; set; } = string.Empty;
    [Id(3)] public TwitterPublicMetricsDto PublicMetrics { get; set; } = new();
}

/// <summary>
/// Twitter推文公开指标DTO
/// </summary>
[GenerateSerializer]
public class TwitterPublicMetricsDto
{
    [Id(0)] public int RetweetCount { get; set; }
    [Id(1)] public int LikeCount { get; set; }
    [Id(2)] public int ReplyCount { get; set; }
    [Id(3)] public int QuoteCount { get; set; }
    [Id(4)] public int ViewCount { get; set; }
    [Id(5)] public int BookmarkCount { get; set; }
}

/// <summary>
/// Twitter引用推文DTO
/// </summary>
[GenerateSerializer]
public class TwitterReferencedTweetDto
{
    [Id(0)] public string Type { get; set; } = string.Empty;
    [Id(1)] public string Id { get; set; } = string.Empty;
}

/// <summary>
/// Twitter上下文注释DTO
/// </summary>
[GenerateSerializer]
public class TwitterContextAnnotationDto
{
    [Id(0)] public TwitterDomainDto Domain { get; set; } = new();
    [Id(1)] public TwitterEntityDto Entity { get; set; } = new();
}

/// <summary>
/// Twitter域DTO
/// </summary>
[GenerateSerializer]
public class TwitterDomainDto
{
    [Id(0)] public string Id { get; set; } = string.Empty;
    [Id(1)] public string Name { get; set; } = string.Empty;
    [Id(2)] public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Twitter实体DTO
/// </summary>
[GenerateSerializer]
public class TwitterEntityDto
{
    [Id(0)] public string Id { get; set; } = string.Empty;
    [Id(1)] public string Name { get; set; } = string.Empty;
    [Id(2)] public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Twitter API响应元数据DTO
/// </summary>
[GenerateSerializer]
public class TwitterMetaDto
{
    [Id(0)] public int ResultCount { get; set; }
    [Id(1)] public string NextToken { get; set; } = string.Empty;
    [Id(2)] public string PreviousToken { get; set; } = string.Empty;
} 