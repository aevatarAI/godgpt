using Orleans;
using Aevatar.Application.Grains.TwitterInteraction.Dtos;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.TwitterInteraction;

/// <summary>
/// Twitter API interaction service interface
/// Responsible for direct interaction with Twitter API, including tweet search, detail retrieval, user information retrieval, etc.
/// </summary>
public interface ITwitterInteractionGrain : IGrainWithStringKey
{
    /// <summary>
    /// Search tweets containing specified content
    /// </summary>
    /// <param name="request">Search request parameters</param>
    /// <returns>Search results</returns>
    Task<TwitterApiResultDto<SearchTweetsResponseDto>> SearchTweetsAsync(SearchTweetsRequestDto request);

    /// <summary>
    /// Comprehensive tweet analysis (recommended)
    /// Get tweet type, user information, share links and all necessary information in one call
    /// </summary>
    /// <param name="tweetId">Tweet ID</param>
    /// <returns>Complete tweet analysis result</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<TweetProcessResultDto>> AnalyzeTweetAsync(string tweetId);

    /// <summary>
    /// Lightweight tweet analysis for fetching phase (without user info)
    /// Only gets tweet details to reduce API calls during bulk fetching operations
    /// </summary>
    /// <param name="tweetId">Tweet ID</param>
    /// <returns>Tweet analysis result without user info</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<TweetProcessResultDto>> AnalyzeTweetLightweightAsync(string tweetId);

    /// <summary>
    /// Batch analyze tweets with comprehensive information
    /// </summary>
    /// <param name="tweetIds">List of tweet IDs to analyze</param>
    /// <returns>List of tweet analysis results</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<List<TweetProcessResultDto>>> BatchAnalyzeTweetsAsync(List<string> tweetIds);

    /// <summary>
    /// Batch analyze tweets with lightweight approach (without user info)
    /// Reduces API calls by not fetching user information for each tweet
    /// </summary>
    /// <param name="tweetIds">List of tweet IDs to analyze</param>
    /// <returns>List of lightweight tweet analysis results</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<List<TweetProcessResultDto>>> BatchAnalyzeTweetsLightweightAsync(List<string> tweetIds);

    /// <summary>
    /// Get tweet details (low-level method)
    /// </summary>
    /// <param name="tweetId">Tweet ID</param>
    /// <returns>Tweet details</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<TweetDetailsDto>> GetTweetDetailsAsync(string tweetId);

    /// <summary>
    /// Get user information
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <returns>User information</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<UserInfoDto>> GetUserInfoAsync(string userId);

    /// <summary>
    /// Batch get tweet details (low-level method)
    /// </summary>
    /// <param name="tweetIds">List of tweet IDs</param>
    /// <returns>List of tweet details</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<List<TweetDetailsDto>>> GetBatchTweetDetailsAsync(List<string> tweetIds);

    /// <summary>
    /// Test Twitter API connection status
    /// </summary>
    /// <returns>Connection test result</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<bool>> TestApiConnectionAsync();

    /// <summary>
    /// Get API quota information
    /// </summary>
    /// <returns>Quota information</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<TwitterApiQuotaDto>> GetApiQuotaInfoAsync();

    /// <summary>
    /// Get service status information
    /// </summary>
    /// <returns>Service status</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<string>> GetServiceStatusAsync();

    // Helper methods (internal use, optional exposure)
    
    /// <summary>
    /// Validate share link (helper method)
    /// </summary>
    /// <param name="url">URL to be validated</param>
    /// <returns>Validation result</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<ShareLinkValidationDto>> ValidateShareLinkAsync(string url);

    /// <summary>
    /// Extract share links from tweet text (helper method)
    /// </summary>
    /// <param name="tweetText">Tweet text</param>
    /// <returns>List of extracted share links</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<List<string>>> ExtractShareLinksAsync(string tweetText);

    /// <summary>
    /// Extract all URL links from tweet text (helper method)
    /// </summary>
    /// <param name="tweetText">Tweet text</param>
    /// <returns>List of extracted URLs</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<List<string>>> ExtractUrlsFromTweetAsync(string tweetText);

    /// <summary>
    /// Process batch tweets with options
    /// </summary>
    /// <param name="request">Batch processing request</param>
    /// <returns>Batch processing response</returns>
    [ReadOnly]
    Task<TwitterApiResultDto<BatchTweetProcessResponseDto>> ProcessBatchTweetsAsync(BatchTweetProcessRequestDto request);
} 