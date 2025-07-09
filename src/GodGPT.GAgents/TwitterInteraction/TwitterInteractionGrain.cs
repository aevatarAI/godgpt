using System.Text.Json;
using System.Text.RegularExpressions;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.TwitterInteraction.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;

namespace Aevatar.Application.Grains.TwitterInteraction;

/// <summary>
/// Twitter API interaction service implementation
/// Responsible for direct interaction with Twitter API, including tweet search, detail retrieval, user information retrieval, etc.
/// </summary>
public class TwitterInteractionGrain : Grain, ITwitterInteractionGrain
{
    private readonly ILogger<TwitterInteractionGrain> _logger;
    private readonly IOptionsMonitor<TwitterRewardOptions> _options;
    private readonly HttpClient _httpClient;
    
    // Twitter API endpoints
    private const string TWITTER_API_BASE = "https://api.twitter.com/2";
    private const string SEARCH_TWEETS_ENDPOINT = "/tweets/search/recent";
    private const string GET_TWEET_ENDPOINT = "/tweets/{0}";
    private const string GET_USER_ENDPOINT = "/users/{0}";
    private const string GET_TWEETS_ENDPOINT = "/tweets";
    
    public TwitterInteractionGrain(
        ILogger<TwitterInteractionGrain> logger,
        IOptionsMonitor<TwitterRewardOptions> options,
        HttpClient httpClient)
    {
        _logger = logger;
        _options = options;
        _httpClient = httpClient;
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("TwitterInteractionGrain activated for key: {GrainKey}", this.GetPrimaryKeyString());
        
        // Configure HttpClient - remove default headers as we'll set them per request
        _httpClient.DefaultRequestHeaders.Clear();
        
        return base.OnActivateAsync(cancellationToken);
    }



    #region Search and Retrieve Tweets

    public async Task<TwitterApiResultDto<SearchTweetsResponseDto>> SearchTweetsAsync(SearchTweetsRequestDto request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return new TwitterApiResultDto<SearchTweetsResponseDto>
                {
                    IsSuccess = false,
                    ErrorMessage = "Search query cannot be empty",
                    Data = new SearchTweetsResponseDto()
                };
            }

            _logger.LogDebug("Searching tweets with query: {Query}", request.Query);
            
            // Comprehensive time validation to prevent future time searches and unreasonable time ranges
            var currentUtc = DateTime.UtcNow;
            var maxPastDays = 7; // Twitter API recent search limit
            var minPastTime = currentUtc.AddDays(-maxPastDays);
            
            // Validate StartTime
            if (request.StartTime.HasValue)
            {
                if (request.StartTime.Value > currentUtc)
                {
                    _logger.LogWarning("StartTime {StartTime} is in the future (current: {CurrentTime}). Adjusting to 1 hour ago.", 
                        request.StartTime.Value, currentUtc);
                    request.StartTime = currentUtc.AddHours(-1);
                }
                else if (request.StartTime.Value < minPastTime)
                {
                    _logger.LogWarning("StartTime {StartTime} is too far in the past (limit: {MinTime}). Adjusting to {Days} days ago.", 
                        request.StartTime.Value, minPastTime, maxPastDays);
                    request.StartTime = minPastTime;
                }
            }
            
            // Validate EndTime: if not null, must be at least 30 seconds before current UTC time (increased buffer for API safety)
            if (request.EndTime.HasValue)
            {
                var minimumEndTime = currentUtc.AddSeconds(-30);  // 增加到30秒缓冲
                
                if (request.EndTime.Value > minimumEndTime)
                {
                    _logger.LogWarning("EndTime {EndTime} is too close to current time or in the future (current: {CurrentTime}). Adjusting to {MinimumEndTime}.", 
                        request.EndTime.Value, currentUtc, minimumEndTime);
                    request.EndTime = minimumEndTime;  
                }
                else if (request.EndTime.Value < minPastTime)
                {
                    _logger.LogWarning("EndTime {EndTime} is too far in the past (limit: {MinTime}). Adjusting to {Days} days ago.", 
                        request.EndTime.Value, minPastTime, maxPastDays);
                    request.EndTime = minPastTime;
                }
            }
            
            // Validate time range consistency
            if (request.StartTime.HasValue && request.EndTime.HasValue && request.StartTime.Value >= request.EndTime.Value)
            {
                _logger.LogWarning("StartTime {StartTime} is not before EndTime {EndTime}. Adjusting to 1-hour window ending at EndTime.", 
                    request.StartTime.Value, request.EndTime.Value);
                request.StartTime = request.EndTime.Value.AddHours(-1);
            }
            
            _logger.LogInformation("Validated time range - StartTime: {StartTime}, EndTime: {EndTime}, Current: {CurrentTime}", 
                request.StartTime, request.EndTime, currentUtc);

            // Build URL with encoded query parameter
            string encodedQuery = Uri.EscapeDataString(request.Query);
            string url = $"{TWITTER_API_BASE}{SEARCH_TWEETS_ENDPOINT}?query={encodedQuery}&max_results={request.MaxResults}" +
                        "&tweet.fields=id,text,author_id,created_at,public_metrics,referenced_tweets,context_annotations,entities" +
                        "&expansions=author_id&user.fields=id,username,name,public_metrics";
            // Add optional parameters
            if (request.StartTime.HasValue)
            {
                url += $"&start_time={request.StartTime.Value:yyyy-MM-ddTHH:mm:ss.fffZ}";
            }
            if (request.EndTime.HasValue)
            {
                url += $"&end_time={request.EndTime.Value:yyyy-MM-ddTHH:mm:ss.fffZ}";
            }
            var bearerToken = _options.CurrentValue.BearerToken;
            
            // Log for debugging
           _logger.LogInformation($"SearchTweetsAsync url--->{url}");
            //_logger.LogDebug($"SearchTweetsAsync bearerToken--->{bearerToken}");
            
            // Set authorization header using the reference code approach
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            
            try
            {
                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("SearchTweetsAsync Error: StatusCode={StatusCode}, Content={Content}, url={url}", 
                        response.StatusCode, content, url);
                    return new TwitterApiResultDto<SearchTweetsResponseDto>
                    {
                        IsSuccess = false,
                        ErrorMessage = $"Twitter API error {response.StatusCode}: {content}",
                        StatusCode = (int)response.StatusCode,
                        Data = new SearchTweetsResponseDto()
                    };
                }
                
                //_logger.LogDebug("SearchTweetsAsync Response: {resp}", content);

                // Parse response
                var searchResponse = await ParseSearchResponseFromApiResponse(content);
                
                return new TwitterApiResultDto<SearchTweetsResponseDto>
                {
                    IsSuccess = true,
                    Data = searchResponse,
                    StatusCode = (int)response.StatusCode
                };
            }
            catch (HttpRequestException e)
            { 
                _logger.LogError("SearchTweetsAsync Error: {err}, code: {code} url: {url}", e.Message, e.Data, url);
                return new TwitterApiResultDto<SearchTweetsResponseDto>
                {
                    IsSuccess = false,
                    ErrorMessage = $"HTTP request failed: {e.Message}",
                    Data = new SearchTweetsResponseDto()
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching tweets with query: {Query}", request.Query);
            return new TwitterApiResultDto<SearchTweetsResponseDto>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new SearchTweetsResponseDto()
            };
        }
    }

    public async Task<TwitterApiResultDto<TweetDetailsDto>> GetTweetDetailsAsync(string tweetId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(tweetId))
            {
                return new TwitterApiResultDto<TweetDetailsDto>
                {
                    IsSuccess = false,
                    ErrorMessage = "Tweet ID cannot be empty",
                    Data = new TweetDetailsDto()
                };
            }

            _logger.LogDebug("Getting tweet details for ID: {TweetId}", tweetId);

            var url = $"{TWITTER_API_BASE}{string.Format(GET_TWEET_ENDPOINT, tweetId)}" +
                     "?tweet.fields=id,text,author_id,created_at,public_metrics,referenced_tweets,context_annotations,entities" +
                     "&expansions=author_id" +
                     "&user.fields=id,username,name,public_metrics";

            var bearerToken = _options.CurrentValue.BearerToken;
            _logger.LogInformation($"GetTweetDetailsAsync url--->{url}");
            //_logger.LogError($"GetTweetDetailsAsync bearerToken--->{bearerToken}");
            
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
            
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get tweet details for ID {TweetId}: {StatusCode} - {Content}", 
                    tweetId, response.StatusCode, content);
                return new TwitterApiResultDto<TweetDetailsDto>
                {
                    IsSuccess = false,
                    ErrorMessage = $"Twitter API error: {response.StatusCode}",
                    StatusCode = (int)response.StatusCode,
                    Data = new TweetDetailsDto()
                };
            }

            var tweetDetails = await ParseTweetDetailsFromApiResponse(content);
            
            return new TwitterApiResultDto<TweetDetailsDto>
            {
                IsSuccess = true,
                Data = tweetDetails,
                StatusCode = (int)response.StatusCode
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tweet details for ID: {TweetId}", tweetId);
            return new TwitterApiResultDto<TweetDetailsDto>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new TweetDetailsDto()
            };
        }
    }

    #endregion

    #region Tweet Type Identification

    public async Task<TwitterApiResultDto<TweetType>> DetermineTweetTypeAsync(string tweetId)
    {
        try
        {
            var tweetDetailsResult = await GetTweetDetailsAsync(tweetId);
            if (!tweetDetailsResult.IsSuccess)
            {
                return new TwitterApiResultDto<TweetType>
                {
                    IsSuccess = false,
                    ErrorMessage = tweetDetailsResult.ErrorMessage,
                    Data = TweetType.Original
                };
            }

            return new TwitterApiResultDto<TweetType>
            {
                IsSuccess = true,
                Data = tweetDetailsResult.Data.Type
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error determining tweet type for ID: {TweetId}", tweetId);
            return new TwitterApiResultDto<TweetType>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = TweetType.Original
            };
        }
    }

    public async Task<TwitterApiResultDto<bool>> IsOriginalTweetAsync(string tweetId)
    {
        try
        {
            var typeResult = await DetermineTweetTypeAsync(tweetId);
            return new TwitterApiResultDto<bool>
            {
                IsSuccess = typeResult.IsSuccess,
                ErrorMessage = typeResult.ErrorMessage,
                Data = typeResult.IsSuccess && typeResult.Data == TweetType.Original
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if tweet is original: {TweetId}", tweetId);
            return new TwitterApiResultDto<bool>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = false
            };
        }
    }

    #endregion

    #region Share Link Processing

    public async Task<TwitterApiResultDto<ShareLinkValidationDto>> ValidateShareLinkAsync(string url)
    {
        try
        {
            _logger.LogDebug("Validating share link: {Url}", url);

            var validation = new ShareLinkValidationDto
            {
                Url = url,
                IsValid = false,
                ValidationMessage = "Invalid URL format",
                IsAccessible = false
            };

            // Check if URL format matches share link domain
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                validation.ValidationMessage = "Invalid URL format";
                return new TwitterApiResultDto<ShareLinkValidationDto>
                {
                    IsSuccess = true,
                    Data = validation
                };
            }

            // Check if it's a valid share link domain
            if (!url.StartsWith(_options.CurrentValue.ShareLinkDomain, StringComparison.OrdinalIgnoreCase))
            {
                validation.ValidationMessage = "URL is not a valid share link domain";
                return new TwitterApiResultDto<ShareLinkValidationDto>
                {
                    IsSuccess = true,
                    Data = validation
                };
            }

            // For test environment, any link matching the domain is considered valid
            // In production environment, stricter path validation can be added
            validation.IsValid = true;
            validation.ValidationMessage = "Valid share link";
            
            // Try to extract share ID (if available)
            var shareIdMatch = Regex.Match(url, @"/share/([a-zA-Z0-9\-]+)", RegexOptions.IgnoreCase);
            if (shareIdMatch.Success)
            {
                validation.ExtractedShareId = shareIdMatch.Groups[1].Value;
            }
            else
            {
                // Extract ID from other paths (e.g., /chat/123, /profile/456)
                var pathIdMatch = Regex.Match(url, @"/[^/]+/([a-zA-Z0-9\-]+)", RegexOptions.IgnoreCase);
                if (pathIdMatch.Success)
                {
                    validation.ExtractedShareId = pathIdMatch.Groups[1].Value;
                }
            }

            // Skip network connection check in test environment to avoid external network dependency
            validation.IsAccessible = true;

            return new TwitterApiResultDto<ShareLinkValidationDto>
            {
                IsSuccess = true,
                Data = validation
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating share link: {Url}", url);
            return new TwitterApiResultDto<ShareLinkValidationDto>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new ShareLinkValidationDto { Url = url, IsValid = false }
            };
        }
    }

    public async Task<TwitterApiResultDto<List<string>>> ExtractUrlsFromTweetAsync(string tweetText)
    {
        try
        {
            var urls = new List<string>();
            
            // URL regex patterns
            var urlPattern = @"https?://[^\s]+";
            var matches = Regex.Matches(tweetText, urlPattern, RegexOptions.IgnoreCase);
            
            foreach (Match match in matches)
            {
                urls.Add(match.Value);
            }

            return new TwitterApiResultDto<List<string>>
            {
                IsSuccess = true,
                ErrorMessage = $"Extracted {urls.Count} URLs",
                Data = urls
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting URLs from tweet text");
            return new TwitterApiResultDto<List<string>>
            {
                IsSuccess = false,
                ErrorMessage = $"Error extracting URLs: {ex.Message}",
                Data = new List<string>()
            };
        }
    }

    public async Task<TwitterApiResultDto<bool>> HasValidShareLinkAsync(string tweetText)
    {
        try
        {
            var urlsResult = await ExtractUrlsFromTweetAsync(tweetText);
            if (!urlsResult.IsSuccess)
            {
                return new TwitterApiResultDto<bool>
                {
                    IsSuccess = false,
                    ErrorMessage = urlsResult.ErrorMessage,
                    Data = false
                };
            }

            foreach (var url in urlsResult.Data)
            {
                var validationResult = await ValidateShareLinkAsync(url);
                if (validationResult.IsSuccess && validationResult.Data.IsValid)
                {
                    return new TwitterApiResultDto<bool>
                    {
                        IsSuccess = true,
                        ErrorMessage = "Valid share link found",
                        Data = true
                    };
                }
            }

            return new TwitterApiResultDto<bool>
            {
                IsSuccess = true,
                ErrorMessage = "No valid share link found",
                Data = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for valid share link in tweet text");
            return new TwitterApiResultDto<bool>
            {
                IsSuccess = false,
                ErrorMessage = $"Error checking share link: {ex.Message}",
                Data = false
            };
        }
    }

    #endregion

    #region Helper Methods

    private async Task<SearchTweetsResponseDto> ParseSearchResponseFromApiResponse(string apiResponse)
    {
        try
        {
            using var document = JsonDocument.Parse(apiResponse);
            var root = document.RootElement;

            var response = new SearchTweetsResponseDto
            {
                Data = new List<TweetDto>(),
                Includes = new List<TwitterUserDto>(),
                Meta = new TwitterMetaDto()
            };

            // Parse tweets data
            if (root.TryGetProperty("data", out var dataElement) && 
                dataElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var tweetElement in dataElement.EnumerateArray())
                {
                    var tweet = new TweetDto();
                    
                    if (tweetElement.TryGetProperty("id", out var idElement))
                        tweet.Id = idElement.GetString() ?? "";
                    
                    if (tweetElement.TryGetProperty("text", out var textElement))
                        tweet.Text = textElement.GetString() ?? "";
                    
                    if (tweetElement.TryGetProperty("author_id", out var authorIdElement))
                        tweet.AuthorId = authorIdElement.GetString() ?? "";
                    
                    if (tweetElement.TryGetProperty("created_at", out var createdAtElement))
                    {
                        if (DateTimeOffset.TryParse(createdAtElement.GetString(), out var createdAt))
                            tweet.CreatedAt = createdAt.UtcDateTime;
                    }

                    // Parse public metrics
                    if (tweetElement.TryGetProperty("public_metrics", out var metricsElement))
                    {
                        var metrics = new TwitterPublicMetricsDto();
                        if (metricsElement.TryGetProperty("impression_count", out var impressionElement))
                            metrics.ViewCount = impressionElement.GetInt32();
                        if (metricsElement.TryGetProperty("retweet_count", out var retweetElement))
                            metrics.RetweetCount = retweetElement.GetInt32();
                        if (metricsElement.TryGetProperty("like_count", out var likeElement))
                            metrics.LikeCount = likeElement.GetInt32();
                        if (metricsElement.TryGetProperty("reply_count", out var replyElement))
                            metrics.ReplyCount = replyElement.GetInt32();
                        if (metricsElement.TryGetProperty("quote_count", out var quoteElement))
                            metrics.QuoteCount = quoteElement.GetInt32();
                        if (metricsElement.TryGetProperty("bookmark_count", out var bookmarkElement))
                            metrics.BookmarkCount = bookmarkElement.GetInt32();
                        
                        tweet.PublicMetrics = metrics;
                    }

                    // Parse referenced tweets
                    if (tweetElement.TryGetProperty("referenced_tweets", out var referencedElement) && 
                        referencedElement.ValueKind == JsonValueKind.Array)
                    {
                        var referencedTweets = new List<TwitterReferencedTweetDto>();
                        foreach (var refElement in referencedElement.EnumerateArray())
                        {
                            var refTweet = new TwitterReferencedTweetDto();
                            if (refElement.TryGetProperty("type", out var typeElement))
                                refTweet.Type = typeElement.GetString() ?? "";
                            if (refElement.TryGetProperty("id", out var refIdElement))
                                refTweet.Id = refIdElement.GetString() ?? "";
                            referencedTweets.Add(refTweet);
                        }
                        tweet.ReferencedTweets = referencedTweets;
                    }

                    response.Data.Add(tweet);
                }
            }

            // Parse users data
            if (root.TryGetProperty("includes", out var includesElement) &&
                includesElement.TryGetProperty("users", out var usersElement) &&
                usersElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var userElement in usersElement.EnumerateArray())
                {
                    var user = new TwitterUserDto();
                    
                    if (userElement.TryGetProperty("id", out var idElement))
                        user.Id = idElement.GetString() ?? "";
                    
                    if (userElement.TryGetProperty("username", out var usernameElement))
                        user.Username = usernameElement.GetString() ?? "";
                    
                    if (userElement.TryGetProperty("name", out var nameElement))
                        user.Name = nameElement.GetString() ?? "";

                    // Parse user public metrics
                    if (userElement.TryGetProperty("public_metrics", out var metricsElement))
                    {
                        var metrics = new TwitterPublicMetricsDto();
                        if (metricsElement.TryGetProperty("followers_count", out var followersElement))
                            metrics.ViewCount = followersElement.GetInt32(); // Using ViewCount as a placeholder since TwitterPublicMetricsDto doesn't have specific user metrics
                        if (metricsElement.TryGetProperty("following_count", out var followingElement))
                            metrics.RetweetCount = followingElement.GetInt32(); // Using RetweetCount as placeholder
                        if (metricsElement.TryGetProperty("tweet_count", out var tweetCountElement))
                            metrics.LikeCount = tweetCountElement.GetInt32(); // Using LikeCount as placeholder
                        if (metricsElement.TryGetProperty("listed_count", out var listedElement))
                            metrics.ReplyCount = listedElement.GetInt32(); // Using ReplyCount as placeholder
                        
                        user.PublicMetrics = metrics;
                    }

                    response.Includes.Add(user);
                }
            }

            // Parse meta data
            if (root.TryGetProperty("meta", out var metaElement))
            {
                if (metaElement.TryGetProperty("result_count", out var resultCountElement))
                    response.Meta.ResultCount = resultCountElement.GetInt32();
                
                if (metaElement.TryGetProperty("next_token", out var nextTokenElement))
                    response.Meta.NextToken = nextTokenElement.GetString() ?? "";
                
                if (metaElement.TryGetProperty("previous_token", out var previousTokenElement))
                    response.Meta.PreviousToken = previousTokenElement.GetString() ?? "";
            }

            _logger.LogDebug("Successfully parsed search response with {TweetCount} tweets and {UserCount} users", 
                response.Data.Count, response.Includes.Count);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing search response from API response: {ApiResponse}", apiResponse);
            return new SearchTweetsResponseDto
            {
                Data = new List<TweetDto>(),
                Includes = new List<TwitterUserDto>(),
                Meta = new TwitterMetaDto()
            };
        }
    }

    private async Task<TweetDetailsDto> ParseTweetDetailsFromApiResponse(string apiResponse)
    {
        try
        {
            using var document = JsonDocument.Parse(apiResponse);
            var root = document.RootElement;

            var tweetDetails = new TweetDetailsDto();

            if (root.TryGetProperty("data", out var dataElement))
            {
                // Basic tweet information
                if (dataElement.TryGetProperty("id", out var idElement))
                    tweetDetails.TweetId = idElement.GetString() ?? "";

                // Privacy protection: Do not store tweet text content, but temporarily get it for share link validation
                string tweetText = "";
                if (dataElement.TryGetProperty("text", out var textElement))
                {
                    tweetText = textElement.GetString() ?? "";
                    // Keep Text field as empty string, do not store content
                    tweetDetails.Text = string.Empty;
                }

                if (dataElement.TryGetProperty("author_id", out var authorIdElement))
                    tweetDetails.AuthorId = authorIdElement.GetString() ?? "";

                if (dataElement.TryGetProperty("created_at", out var createdAtElement))
                {
                    if (DateTimeOffset.TryParse(createdAtElement.GetString(), out var createdAt))
                        tweetDetails.CreatedAt = createdAt.UtcDateTime;
                }

                // Tweet metrics
                if (dataElement.TryGetProperty("public_metrics", out var metricsElement))
                {
                    if (metricsElement.TryGetProperty("impression_count", out var viewElement))
                        tweetDetails.ViewCount = viewElement.GetInt32();
                    if (metricsElement.TryGetProperty("retweet_count", out var retweetElement))
                        tweetDetails.RetweetCount = retweetElement.GetInt32();
                    if (metricsElement.TryGetProperty("like_count", out var likeElement))
                        tweetDetails.LikeCount = likeElement.GetInt32();
                    if (metricsElement.TryGetProperty("reply_count", out var replyElement))
                        tweetDetails.ReplyCount = replyElement.GetInt32();
                    if (metricsElement.TryGetProperty("quote_count", out var quoteElement))
                        tweetDetails.QuoteCount = quoteElement.GetInt32();
                }

                // Tweet type identification
                tweetDetails.Type = TweetType.Original; // Default to original

                if (dataElement.TryGetProperty("referenced_tweets", out var referencedElement) && 
                    referencedElement.ValueKind == JsonValueKind.Array && 
                    referencedElement.GetArrayLength() > 0)
                {
                    var firstReference = referencedElement[0];
                    if (firstReference.TryGetProperty("type", out var typeElement))
                    {
                        var referenceType = typeElement.GetString();
                        tweetDetails.Type = referenceType?.ToLowerInvariant() switch
                        {
                            "replied_to" => TweetType.Reply,
                            "retweeted" => TweetType.Retweet,
                            "quoted" => TweetType.Quote,
                            _ => TweetType.Original
                        };
                    }
                }

                // Share link validation: Extract URLs from entities first, then fallback to text regex
                var extractedUrls = new List<string>();
                
                // Primary method: Extract from entities.urls (expanded URLs)
                if (dataElement.TryGetProperty("entities", out var entitiesElement) &&
                    entitiesElement.TryGetProperty("urls", out var urlsElement) &&
                    urlsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var urlElement in urlsElement.EnumerateArray())
                    {
                        if (urlElement.TryGetProperty("expanded_url", out var expandedUrlElement))
                        {
                            var expandedUrl = expandedUrlElement.GetString();
                            if (!string.IsNullOrEmpty(expandedUrl))
                            {
                                extractedUrls.Add(expandedUrl);
                            }
                        }
                    }
                }
                
                // Fallback method: Extract from text using regex (for backward compatibility)
                if (!extractedUrls.Any() && !string.IsNullOrEmpty(tweetText))
                {
                    var urlPattern = @"https?://[^\s]+";
                    var matches = Regex.Matches(tweetText, urlPattern, RegexOptions.IgnoreCase);
                    foreach (Match match in matches)
                    {
                        extractedUrls.Add(match.Value);
                    }
                }

                // Store extracted URLs for reference
                tweetDetails.ExtractedUrls = extractedUrls;

                // Validate share links
                var validShareLinks = new List<string>();
                foreach (var url in extractedUrls)
                {
                    if (url.StartsWith(_options.CurrentValue.ShareLinkDomain, StringComparison.OrdinalIgnoreCase))
                    {
                        validShareLinks.Add(url);
                    }
                }

                if (validShareLinks.Any())
                {
                    // Validate first share link
                    var firstShareLink = validShareLinks.First();
                    var validationResult = await ValidateShareLinkAsync(firstShareLink);
                    
                    if (validationResult.IsSuccess && validationResult.Data.IsValid)
                    {
                        tweetDetails.HasValidShareLink = true;
                        // Keep ShareLinkUrl field as empty string, do not store content
                        tweetDetails.ShareLinkUrl = string.Empty;
                    }
                    else
                    {
                        tweetDetails.HasValidShareLink = false;
                        tweetDetails.ShareLinkUrl = string.Empty;
                    }
                }
                else
                {
                    tweetDetails.HasValidShareLink = false;
                    tweetDetails.ShareLinkUrl = string.Empty;
                }
            }

            // Process user information
            if (root.TryGetProperty("includes", out var includesElement) &&
                includesElement.TryGetProperty("users", out var usersElement) &&
                usersElement.ValueKind == JsonValueKind.Array &&
                usersElement.GetArrayLength() > 0)
            {
                var user = usersElement[0];
                if (user.TryGetProperty("username", out var usernameElement))
                    tweetDetails.AuthorHandle = usernameElement.GetString() ?? "";
            }

            return tweetDetails;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing tweet details from API response");
            return new TweetDetailsDto();
        }
    }

    #endregion

    #region Other Interface Implementations

    public async Task<TwitterApiResultDto<UserInfoDto>> GetUserInfoAsync(string userId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return new TwitterApiResultDto<UserInfoDto>
                {
                    IsSuccess = false,
                    ErrorMessage = "User ID cannot be empty",
                    Data = new UserInfoDto()
                };
            }

            _logger.LogDebug("Getting user info for ID: {UserId}", userId);

            var url = $"{TWITTER_API_BASE}{string.Format(GET_USER_ENDPOINT, userId)}" +
                     "?user.fields=id,username,name,public_metrics,verified,created_at";

            var bearerToken = _options.CurrentValue.BearerToken;
            _logger.LogInformation($"GetUserInfoAsync url--->{url}");
            //_logger.LogInformation($"GetUserInfoAsync bearerToken--->{bearerToken}");
            
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
            
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get user info for ID {UserId}: {StatusCode} - {Content}", 
                    userId, response.StatusCode, content);
                return new TwitterApiResultDto<UserInfoDto>
                {
                    IsSuccess = false,
                    ErrorMessage = $"Twitter API error: {response.StatusCode}",
                    StatusCode = (int)response.StatusCode,
                    Data = new UserInfoDto()
                };
            }

            var userInfo = await ParseUserInfoFromApiResponse(content);
            
            return new TwitterApiResultDto<UserInfoDto>
            {
                IsSuccess = true,
                Data = userInfo,
                StatusCode = (int)response.StatusCode
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user info for ID: {UserId}", userId);
            return new TwitterApiResultDto<UserInfoDto>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new UserInfoDto()
            };
        }
    }

    public async Task<TwitterApiResultDto<List<TweetDetailsDto>>> GetBatchTweetDetailsAsync(List<string> tweetIds)
    {
        try
        {
            if (tweetIds?.Any() != true)
            {
                return new TwitterApiResultDto<List<TweetDetailsDto>>
                {
                    IsSuccess = true,
                    ErrorMessage = "Empty tweet IDs list provided",
                    Data = new List<TweetDetailsDto>()
                };
            }

            _logger.LogDebug("Getting batch tweet details for {Count} tweets", tweetIds.Count);

            // Twitter API limits maximum 100 IDs per request
            var batchSize = Math.Min(100, tweetIds.Count);
            var tweetIdsString = string.Join(",", tweetIds.Take(batchSize));

            var url = $"{TWITTER_API_BASE}{GET_TWEETS_ENDPOINT}" +
                      $"?ids={tweetIdsString}" +
                      "&tweet.fields=id,text,author_id,created_at,public_metrics,referenced_tweets,context_annotations,entities" +
                      "&expansions=author_id" +
                      "&user.fields=id,username,name,public_metrics";

            var bearerToken = _options.CurrentValue.BearerToken;
            _logger.LogError($"GetBatchTweetDetailsAsync url--->{url}");
            _logger.LogError($"GetBatchTweetDetailsAsync bearerToken--->{bearerToken}");

            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);

            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get batch tweet details: {StatusCode} - {Content}",
                    response.StatusCode, content);
                return new TwitterApiResultDto<List<TweetDetailsDto>>
                {
                    IsSuccess = false,
                    ErrorMessage = $"Twitter API error: {response.StatusCode}",
                    StatusCode = (int)response.StatusCode,
                    Data = new List<TweetDetailsDto>()
                };
            }

            var tweetDetailsList = await ParseBatchTweetDetailsFromApiResponse(content);

            return new TwitterApiResultDto<List<TweetDetailsDto>>
            {
                IsSuccess = true,
                Data = tweetDetailsList,
                StatusCode = (int)response.StatusCode
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting batch tweet details");
            return new TwitterApiResultDto<List<TweetDetailsDto>>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new List<TweetDetailsDto>()
            };
        }
    }

    public async Task<TwitterApiResultDto<BatchTweetProcessResponseDto>> ProcessBatchTweetsAsync(BatchTweetProcessRequestDto request)
    {
        try
        {
            _logger.LogInformation("Processing batch of {Count} tweets", request.TweetIds.Count);

            var response = new BatchTweetProcessResponseDto
            {
                TotalProcessed = request.TweetIds.Count
            };

            var batchDetailsResult = await GetBatchTweetDetailsAsync(request.TweetIds);
            if (!batchDetailsResult.IsSuccess)
            {
                response.FailedCount = request.TweetIds.Count;
                response.FailedTweetIds = request.TweetIds.ToList();
                response.Message = batchDetailsResult.ErrorMessage;
                
                return new TwitterApiResultDto<BatchTweetProcessResponseDto>
                {
                    IsSuccess = false,
                    ErrorMessage = batchDetailsResult.ErrorMessage,
                    Data = response
                };
            }

            foreach (var tweetDetails in batchDetailsResult.Data)
            {
                try
                {
                    // Filter original tweets
                    if (request.FilterOriginalOnly && tweetDetails.Type != TweetType.Original)
                    {
                        response.FailedTweetIds.Add(tweetDetails.TweetId);
                        continue;
                    }

                    var processResult = new TweetProcessResultDto
                    {
                        TweetId = tweetDetails.TweetId,
                        AuthorId = tweetDetails.AuthorId,
                        AuthorHandle = tweetDetails.AuthorHandle,
                        CreatedAt = tweetDetails.CreatedAt,
                        Type = tweetDetails.Type,
                        ViewCount = tweetDetails.ViewCount,
                        HasValidShareLink = tweetDetails.HasValidShareLink,
                        ShareLinkUrl = tweetDetails.ShareLinkUrl
                    };

                    // Get user information to populate follower count
                    if (request.IncludeUserInfo)
                    {
                        var userInfoResult = await GetUserInfoAsync(tweetDetails.AuthorId);
                        if (userInfoResult.IsSuccess)
                        {
                            processResult.FollowerCount = userInfoResult.Data.FollowersCount;
                        }
                    }

                    response.ProcessedTweets.Add(processResult);
                    response.SuccessCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing tweet {TweetId}", tweetDetails.TweetId);
                    response.FailedTweetIds.Add(tweetDetails.TweetId);
                    response.FailedCount++;
                }
            }

            response.Message = $"Processed {response.SuccessCount} tweets successfully, {response.FailedCount} failed";

            return new TwitterApiResultDto<BatchTweetProcessResponseDto>
            {
                IsSuccess = true,
                ErrorMessage = "Batch processing completed",
                Data = response
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in batch tweet processing");
            return new TwitterApiResultDto<BatchTweetProcessResponseDto>
            {
                IsSuccess = false,
                ErrorMessage = $"Error in batch processing: {ex.Message}",
                Data = new BatchTweetProcessResponseDto
                {
                    TotalProcessed = request.TweetIds.Count,
                    FailedCount = request.TweetIds.Count,
                    FailedTweetIds = request.TweetIds.ToList()
                }
            };
        }
    }

    public async Task<TwitterApiResultDto<TwitterApiQuotaDto>> GetApiQuotaInfoAsync()
    {
        // Note: Twitter API v2 quota information is usually in response headers, providing a basic implementation here
        return await Task.FromResult(new TwitterApiResultDto<TwitterApiQuotaDto>
        {
            IsSuccess = true,
            Data = new TwitterApiQuotaDto
            {
                Limit = 100,
                Remaining = 100,
                ResetTime = DateTime.UtcNow.AddMinutes(15),
                UsedCount = 0,
                UsagePercentage = 0.0
            }
        });
    }

    public async Task<TwitterApiResultDto<bool>> TestApiConnectionAsync()
    {
        try
        {
            // Use a simple search to test connection
            var testRequest = new SearchTweetsRequestDto
            {
                Query = "hello",
                MaxResults = 1
            };

            var result = await SearchTweetsAsync(testRequest);
            return new TwitterApiResultDto<bool>
            {
                IsSuccess = result.IsSuccess,
                ErrorMessage = result.ErrorMessage,
                Data = result.IsSuccess,
                StatusCode = result.StatusCode
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing API connection");
            return new TwitterApiResultDto<bool>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = false
            };
        }
    }

    private async Task<UserInfoDto> ParseUserInfoFromApiResponse(string apiResponse)
    {
        try
        {
            using var document = JsonDocument.Parse(apiResponse);
            var root = document.RootElement;

            var userInfo = new UserInfoDto();

            if (root.TryGetProperty("data", out var dataElement))
            {
                if (dataElement.TryGetProperty("id", out var idElement))
                    userInfo.UserId = idElement.GetString() ?? "";

                if (dataElement.TryGetProperty("username", out var usernameElement))
                    userInfo.Username = usernameElement.GetString() ?? "";

                if (dataElement.TryGetProperty("name", out var nameElement))
                    userInfo.Name = nameElement.GetString() ?? "";

                if (dataElement.TryGetProperty("verified", out var verifiedElement))
                    userInfo.IsVerified = verifiedElement.GetBoolean();

                if (dataElement.TryGetProperty("created_at", out var createdAtElement))
                {
                    if (DateTimeOffset.TryParse(createdAtElement.GetString(), out var createdAt))
                        userInfo.CreatedAt = createdAt.UtcDateTime;
                }

                // Public metrics
                if (dataElement.TryGetProperty("public_metrics", out var metricsElement))
                {
                    if (metricsElement.TryGetProperty("followers_count", out var followersElement))
                        userInfo.FollowersCount = followersElement.GetInt32();
                    if (metricsElement.TryGetProperty("following_count", out var followingElement))
                        userInfo.FollowingCount = followingElement.GetInt32();
                    if (metricsElement.TryGetProperty("tweet_count", out var tweetElement))
                        userInfo.TweetCount = tweetElement.GetInt32();
                }
            }

            return userInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing user info from API response");
            return new UserInfoDto();
        }
    }

    private async Task<List<TweetDetailsDto>> ParseBatchTweetDetailsFromApiResponse(string apiResponse)
    {
        try
        {
            using var document = JsonDocument.Parse(apiResponse);
            var root = document.RootElement;

            var tweetDetailsList = new List<TweetDetailsDto>();

            if (root.TryGetProperty("data", out var dataElement) && 
                dataElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var tweetElement in dataElement.EnumerateArray())
                {
                    // Create a temporary API response for each tweet to parse
                    var tempResponse = JsonSerializer.Serialize(new 
                    { 
                        data = tweetElement,
                        includes = root.TryGetProperty("includes", out var includesElement) ? includesElement : (JsonElement?)null
                    });
                    
                    var tweetDetails = await ParseTweetDetailsFromApiResponse(tempResponse);
                    tweetDetailsList.Add(tweetDetails);
                }
            }

            return tweetDetailsList;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing batch tweet details from API response");
            return new List<TweetDetailsDto>();
        }
    }

    /// <summary>
    /// Identify tweet type (original/reply/retweet/quote)
    /// </summary>
    public async Task<TwitterApiResultDto<TweetType>> IdentifyTweetTypeAsync(TweetDto tweet)
    {
        try
        {
            _logger.LogDebug("Identifying tweet type for tweet ID: {TweetId}", tweet.Id);

            // Check if there are referenced tweets
            if (tweet.ReferencedTweets?.Any() == true)
            {
                var firstReference = tweet.ReferencedTweets.First();
                return firstReference.Type.ToLowerInvariant() switch
                {
                    "replied_to" => new TwitterApiResultDto<TweetType>
                    {
                        IsSuccess = true,
                        Data = TweetType.Reply
                    },
                    "retweeted" => new TwitterApiResultDto<TweetType>
                    {
                        IsSuccess = true,
                        Data = TweetType.Retweet
                    },
                    "quoted" => new TwitterApiResultDto<TweetType>
                    {
                        IsSuccess = true,
                        Data = TweetType.Quote
                    },
                    _ => new TwitterApiResultDto<TweetType>
                    {
                        IsSuccess = true,
                        Data = TweetType.Original
                    }
                };
            }

            // No referenced tweets, considered as original tweet
            return new TwitterApiResultDto<TweetType>
            {
                IsSuccess = true,
                Data = TweetType.Original
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error identifying tweet type for tweet ID: {TweetId}", tweet.Id);
            return new TwitterApiResultDto<TweetType>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Extract valid share links from tweet text
    /// </summary>
    public async Task<TwitterApiResultDto<List<string>>> ExtractShareLinksAsync(string tweetText)
    {
        try
        {
            var shareLinks = new List<string>();
            
            // URL regex patterns
            var urlPattern = @"https?://[^\s]+";
            var matches = Regex.Matches(tweetText, urlPattern, RegexOptions.IgnoreCase);
            
            foreach (Match match in matches)
            {
                var url = match.Value;
                // Only extract URLs from share link domain
                if (url.StartsWith(_options.CurrentValue.ShareLinkDomain, StringComparison.OrdinalIgnoreCase))
                {
                    shareLinks.Add(url);
                }
            }

            return new TwitterApiResultDto<List<string>>
            {
                IsSuccess = true,
                Data = shareLinks
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting share links from tweet text");
            return new TwitterApiResultDto<List<string>>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new List<string>()
            };
        }
    }

    /// <summary>
    /// Batch validate share links in tweets
    /// Warning: This method relies on TweetDto.Text field, only for processing raw data from Twitter API
    /// For stored tweet data, use HasValidShareLink field instead
    /// </summary>
    public async Task<TwitterApiResultDto<Dictionary<string, bool>>> BatchValidateShareLinksAsync(List<TweetDto> tweets)
    {
        try
        {
            _logger.LogDebug("Batch validating share links for {TweetCount} tweets", tweets.Count);
            
            var results = new Dictionary<string, bool>();
            
            foreach (var tweet in tweets)
            {
                // Using tweet.Text here because TweetDto comes from Twitter API raw response
                // For stored data, Text field is empty, use HasValidShareLink field instead
                var linksResult = await ExtractShareLinksAsync(tweet.Text);
                if (linksResult.IsSuccess)
                {
                    results[tweet.Id] = linksResult.Data.Any();
                }
                else
                {
                    results[tweet.Id] = false;
                }
            }

            return new TwitterApiResultDto<Dictionary<string, bool>>
            {
                IsSuccess = true,
                Data = results
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in batch validating share links");
            return new TwitterApiResultDto<Dictionary<string, bool>>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new Dictionary<string, bool>()
            };
        }
    }

    /// <summary>
    /// Check service status
    /// </summary>
    public async Task<TwitterApiResultDto<string>> GetServiceStatusAsync()
    {
        return await Task.FromResult(new TwitterApiResultDto<string>
        {
            IsSuccess = true,
            Data = "TwitterInteractionGrain is operational"
        });
    }

    /// <summary>
    /// Comprehensive tweet analysis (recommended)
    /// Get tweet type, user information, share links and all necessary information in one call
    /// </summary>
    public async Task<TwitterApiResultDto<TweetProcessResultDto>> AnalyzeTweetAsync(string tweetId)
    {
        try
        {
            _logger.LogDebug("🔍 Starting tweet analysis: {TweetId}", tweetId);

            // 1. Get detailed tweet information (including user info)
            _logger.LogDebug("📄 Getting tweet details: {TweetId}", tweetId);
            var tweetDetailsResult = await GetTweetDetailsAsync(tweetId);
            if (!tweetDetailsResult.IsSuccess)
            {
                _logger.LogWarning("❌ Failed to get tweet details {TweetId}: {Error}", tweetId, tweetDetailsResult.ErrorMessage);
                return new TwitterApiResultDto<TweetProcessResultDto>
                {
                    IsSuccess = false,
                    ErrorMessage = tweetDetailsResult.ErrorMessage,
                    Data = new TweetProcessResultDto { TweetId = tweetId }
                };
            }

            var tweetDetails = tweetDetailsResult.Data;
            _logger.LogDebug("✅ Tweet details retrieved successfully {TweetId} - Author: @{AuthorHandle} ({AuthorId}), Type: {Type}", 
                tweetId, tweetDetails.AuthorHandle, tweetDetails.AuthorId, tweetDetails.Type);
            
            // Add delay between API calls if configured
            if (_options.CurrentValue.ApiCallDelayMs > 0)
            {
                await Task.Delay(_options.CurrentValue.ApiCallDelayMs);
            }
            
            // 2. Get user information (to get follower count etc.)
            _logger.LogDebug("👤 Getting user info: {AuthorId}", tweetDetails.AuthorId);
            var userInfoResult = await GetUserInfoAsync(tweetDetails.AuthorId);
            var followerCount = userInfoResult.IsSuccess ? userInfoResult.Data.FollowersCount : 0;
            
            if (userInfoResult.IsSuccess)
            {
                _logger.LogDebug("✅ User info retrieved successfully @{Username} - Followers: {FollowerCount}", 
                    userInfoResult.Data.Username, followerCount);
            }
            else
            {
                _logger.LogWarning("⚠️ Failed to get user info {AuthorId}: {Error}", tweetDetails.AuthorId, userInfoResult.ErrorMessage);
            }

            // 3. Create comprehensive result
            var result = new TweetProcessResultDto
            {
                TweetId = tweetId,
                AuthorId = tweetDetails.AuthorId,
                AuthorHandle = tweetDetails.AuthorHandle,
                AuthorName = userInfoResult.IsSuccess ? userInfoResult.Data.Name : string.Empty,
                CreatedAt = tweetDetails.CreatedAt,
                Type = tweetDetails.Type,
                ViewCount = tweetDetails.ViewCount,
                FollowerCount = followerCount,
                HasValidShareLink = tweetDetails.HasValidShareLink,
                ShareLinkUrl = tweetDetails.ShareLinkUrl
            };

            _logger.LogDebug("✅ Tweet analysis completed {TweetId} - Author: @{AuthorHandle} ({AuthorId}), Type: {Type}, Followers: {FollowerCount}", 
                tweetId, result.AuthorHandle, result.AuthorId, result.Type, result.FollowerCount);

            return new TwitterApiResultDto<TweetProcessResultDto>
            {
                IsSuccess = true,
                Data = result
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in comprehensive tweet analysis for {TweetId}", tweetId);
            return new TwitterApiResultDto<TweetProcessResultDto>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new TweetProcessResultDto { TweetId = tweetId }
            };
        }
    }

    /// <summary>
    /// Lightweight tweet analysis for fetching phase (without user info)
    /// Only gets tweet details to reduce API calls during bulk fetching operations
    /// </summary>
    public async Task<TwitterApiResultDto<TweetProcessResultDto>> AnalyzeTweetLightweightAsync(string tweetId)
    {
        try
        {
            _logger.LogDebug("🔍 Starting lightweight tweet analysis: {TweetId}", tweetId);

            // Only get detailed tweet information (no user info to reduce API calls)
            _logger.LogDebug("📄 Getting tweet details: {TweetId}", tweetId);
            var tweetDetailsResult = await GetTweetDetailsAsync(tweetId);
            if (!tweetDetailsResult.IsSuccess)
            {
                _logger.LogWarning("❌ Failed to get tweet details {TweetId}: {Error}", tweetId, tweetDetailsResult.ErrorMessage);
                return new TwitterApiResultDto<TweetProcessResultDto>
                {
                    IsSuccess = false,
                    ErrorMessage = tweetDetailsResult.ErrorMessage,
                    Data = new TweetProcessResultDto { TweetId = tweetId }
                };
            }

            var tweetDetails = tweetDetailsResult.Data;
            _logger.LogDebug("✅ Tweet details retrieved successfully {TweetId} - Author: @{AuthorHandle} ({AuthorId}), Type: {Type}", 
                tweetId, tweetDetails.AuthorHandle, tweetDetails.AuthorId, tweetDetails.Type);

            // Create lightweight result (no follower count - will be populated later if needed)
            var result = new TweetProcessResultDto
            {
                TweetId = tweetId,
                AuthorId = tweetDetails.AuthorId,
                AuthorHandle = tweetDetails.AuthorHandle,
                AuthorName = string.Empty, // Not fetched in lightweight mode to reduce API calls
                CreatedAt = tweetDetails.CreatedAt,
                Type = tweetDetails.Type,
                ViewCount = tweetDetails.ViewCount,
                FollowerCount = 0, // Not fetched in lightweight mode to reduce API calls
                HasValidShareLink = tweetDetails.HasValidShareLink,
                ShareLinkUrl = tweetDetails.ShareLinkUrl
            };

            _logger.LogDebug("✅ Lightweight tweet analysis completed {TweetId} - Author: @{AuthorHandle} ({AuthorId}), Type: {Type}", 
                tweetId, result.AuthorHandle, result.AuthorId, result.Type);

            return new TwitterApiResultDto<TweetProcessResultDto>
            {
                IsSuccess = true,
                Data = result
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in lightweight tweet analysis for {TweetId}", tweetId);
            return new TwitterApiResultDto<TweetProcessResultDto>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new TweetProcessResultDto { TweetId = tweetId }
            };
        }
    }

    /// <summary>
    /// Batch analyze tweets with comprehensive information
    /// </summary>
    public async Task<TwitterApiResultDto<List<TweetProcessResultDto>>> BatchAnalyzeTweetsAsync(List<string> tweetIds)
    {
        try
        {
            _logger.LogInformation("📊 Starting batch tweet analysis for {Count} tweets", tweetIds.Count);
            
            var results = new List<TweetProcessResultDto>();
            
            foreach (var tweetId in tweetIds)
            {
                var analysisResult = await AnalyzeTweetAsync(tweetId);
                if (analysisResult.IsSuccess)
                {
                    results.Add(analysisResult.Data);
                }
                else
                {
                    _logger.LogWarning("❌ Failed to analyze tweet {TweetId}: {Error}", tweetId, analysisResult.ErrorMessage);
                    // Add empty result to maintain index alignment
                    results.Add(new TweetProcessResultDto { TweetId = tweetId });
                }
            }
            
            _logger.LogInformation("✅ Batch analysis completed: {SuccessCount}/{TotalCount} tweets", 
                results.Count(r => !string.IsNullOrEmpty(r.AuthorId)), tweetIds.Count);
            
            return new TwitterApiResultDto<List<TweetProcessResultDto>>
            {
                IsSuccess = true,
                Data = results
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in batch tweet analysis");
            return new TwitterApiResultDto<List<TweetProcessResultDto>>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new List<TweetProcessResultDto>()
            };
        }
    }

    /// <summary>
    /// Batch analyze tweets with lightweight approach (without user info)
    /// Reduces API calls by not fetching user information for each tweet
    /// </summary>
    public async Task<TwitterApiResultDto<List<TweetProcessResultDto>>> BatchAnalyzeTweetsLightweightAsync(List<string> tweetIds)
    {
        try
        {
            _logger.LogInformation("📊 Starting lightweight batch tweet analysis for {Count} tweets", tweetIds.Count);
            
            var results = new List<TweetProcessResultDto>();
            var options = _options.CurrentValue;
            
            foreach (var tweetId in tweetIds)
            {
                var analysisResult = await AnalyzeTweetLightweightAsync(tweetId);
                if (analysisResult.IsSuccess)
                {
                    results.Add(analysisResult.Data);
                }
                else
                {
                    _logger.LogWarning("❌ Failed to analyze tweet {TweetId}: {Error}", tweetId, analysisResult.ErrorMessage);
                    // Add empty result to maintain index alignment
                    results.Add(new TweetProcessResultDto { TweetId = tweetId });
                }
                
                // Add delay between API calls to avoid rate limiting
                if (options.ApiCallDelayMs > 0)
                {
                    await Task.Delay(options.ApiCallDelayMs);
                }
            }
            
            _logger.LogInformation("✅ Lightweight batch analysis completed: {SuccessCount}/{TotalCount} tweets", 
                results.Count(r => !string.IsNullOrEmpty(r.AuthorId)), tweetIds.Count);
            
            return new TwitterApiResultDto<List<TweetProcessResultDto>>
            {
                IsSuccess = true,
                Data = results
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in lightweight batch tweet analysis");
            return new TwitterApiResultDto<List<TweetProcessResultDto>>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new List<TweetProcessResultDto>()
            };
        }
    }

    #endregion
} 