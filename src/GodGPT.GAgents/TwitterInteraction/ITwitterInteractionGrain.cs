using Aevatar.Application.Grains.TwitterInteraction.Dtos;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.TwitterInteraction;

/// <summary>
/// Twitter API交互服务接口
/// 负责与Twitter API的直接交互，包括推文搜索、详情获取、用户信息获取等
/// </summary>
public interface ITwitterInteractionGrain : IGrainWithStringKey
{
    /// <summary>
    /// 搜索包含指定内容的推文
    /// </summary>
    /// <param name="request">搜索请求参数</param>
    /// <returns>搜索结果</returns>
    Task<TwitterApiResultDto<SearchTweetsResponseDto>> SearchTweetsAsync(SearchTweetsRequestDto request);

    /// <summary>
    /// 综合分析推文信息（推荐使用）
    /// 一次性获取推文类型、用户信息、分享链接等所有必要信息
    /// </summary>
    /// <param name="tweetId">推文ID</param>
    /// <returns>推文完整分析结果</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<TweetProcessResultDto>> AnalyzeTweetAsync(string tweetId);

    /// <summary>
    /// 批量分析推文信息
    /// 一次性分析多条推文，提高效率
    /// </summary>
    /// <param name="tweetIds">推文ID列表</param>
    /// <returns>批量分析结果</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<List<TweetProcessResultDto>>> BatchAnalyzeTweetsAsync(List<string> tweetIds);

    /// <summary>
    /// 获取推文详细信息（底层方法）
    /// </summary>
    /// <param name="tweetId">推文ID</param>
    /// <returns>推文详情</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<TweetDetailsDto>> GetTweetDetailsAsync(string tweetId);

    /// <summary>
    /// 获取用户信息
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>用户信息</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<UserInfoDto>> GetUserInfoAsync(string userId);

    /// <summary>
    /// 批量获取推文详细信息（底层方法）
    /// </summary>
    /// <param name="tweetIds">推文ID列表</param>
    /// <returns>推文详情列表</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<List<TweetDetailsDto>>> GetBatchTweetDetailsAsync(List<string> tweetIds);

    /// <summary>
    /// 测试Twitter API连接状态
    /// </summary>
    /// <returns>连接测试结果</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<bool>> TestApiConnectionAsync();

    /// <summary>
    /// 获取API配额信息
    /// </summary>
    /// <returns>配额信息</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<TwitterApiQuotaDto>> GetApiQuotaInfoAsync();

    /// <summary>
    /// 获取服务状态信息
    /// </summary>
    /// <returns>服务状态</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<string>> GetServiceStatusAsync();

    // 辅助方法（内部使用，可选暴露）
    
    /// <summary>
    /// 验证分享链接是否有效（辅助方法）
    /// </summary>
    /// <param name="url">待验证的URL</param>
    /// <returns>验证结果</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<ShareLinkValidationDto>> ValidateShareLinkAsync(string url);

    /// <summary>
    /// 从推文文本中提取分享链接（辅助方法）
    /// </summary>
    /// <param name="tweetText">推文文本</param>
    /// <returns>提取的分享链接列表</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<List<string>>> ExtractShareLinksAsync(string tweetText);

    /// <summary>
    /// 从推文文本中提取所有URL链接（辅助方法）
    /// </summary>
    /// <param name="tweetText">推文文本</param>
    /// <returns>提取的URL列表</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<List<string>>> ExtractUrlsFromTweetAsync(string tweetText);

    /// <summary>
    /// 批量处理推文（业务方法）
    /// 包括分析推文类型、验证分享链接等
    /// </summary>
    /// <param name="request">批量处理请求</param>
    /// <returns>批量处理结果</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<BatchTweetProcessResponseDto>> BatchProcessTweetsAsync(BatchTweetProcessRequestDto request);
} 