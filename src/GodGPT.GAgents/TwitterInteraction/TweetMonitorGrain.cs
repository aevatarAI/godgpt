using Orleans;
using Orleans.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aevatar.Application.Grains.TwitterInteraction.Dtos;
using Aevatar.Application.Grains.Common.Options;

namespace Aevatar.Application.Grains.TwitterInteraction;

/// <summary>
/// 推文监控状态
/// </summary>
[GenerateSerializer]
public class TweetMonitorState
{
    [Id(0)] public bool IsRunning { get; set; }
    [Id(1)] public DateTime? LastFetchTime { get; set; }
    [Id(2)] public long LastFetchTimeUtc { get; set; }
    [Id(3)] public TweetMonitorConfigDto Config { get; set; } = new();
    [Id(4)] public Dictionary<string, TweetRecord> StoredTweets { get; set; } = new();
    [Id(5)] public List<TweetFetchHistoryDto> FetchHistory { get; set; } = new();
    [Id(6)] public string LastError { get; set; } = string.Empty;
    [Id(7)] public long ReminderTargetId { get; set; } = 1;
    [Id(8)] public DateTime NextScheduledFetch { get; set; }
    [Id(9)] public long NextScheduledFetchUtc { get; set; }
}

/// <summary>
/// 推文监控Grain - 负责定时拉取推文数据
/// </summary>
public class TweetMonitorGrain : Grain, ITweetMonitorGrain, IRemindable
{
    private readonly ILogger<TweetMonitorGrain> _logger;
    private readonly IOptionsMonitor<TwitterRewardOptions> _options;
    private readonly IPersistentState<TweetMonitorState> _state;
    private ITwitterInteractionGrain? _twitterGrain;
    
    private const string REMINDER_NAME = "TweetFetchReminder";

    public TweetMonitorGrain(
        ILogger<TweetMonitorGrain> logger,
        IOptionsMonitor<TwitterRewardOptions> options,
        [PersistentState("tweetMonitorState", "DefaultGrainStorage")] IPersistentState<TweetMonitorState> state)
    {
        _logger = logger;
        _options = options;
        _state = state;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TweetMonitorGrain {GrainId} activating", this.GetPrimaryKeyString());
        
        // Initialize TwitterInteractionGrain reference
        _twitterGrain = GrainFactory.GetGrain<ITwitterInteractionGrain>("twitter-api");
        
        // Initialize state if needed
        if (_state.State.Config.SearchQuery == string.Empty)
        {
            _state.State.Config = new TweetMonitorConfigDto
            {
                FetchIntervalMinutes = _options.CurrentValue.MonitoringIntervalMinutes,
                MaxTweetsPerFetch = _options.CurrentValue.BatchFetchSize,
                DataRetentionDays = _options.CurrentValue.DataRetentionDays,
                SearchQuery = "@GodGPT_",
                FilterOriginalOnly = true,
                EnableAutoCleanup = true,
                ConfigVersion = 1
            };
            await _state.WriteStateAsync();
        }

        await base.OnActivateAsync(cancellationToken);
    }

    public async Task<TwitterApiResultDto<bool>> StartMonitoringAsync()
    {
        try
        {
            _logger.LogInformation("Starting tweet monitoring");

            if (_state.State.IsRunning)
            {
                return new TwitterApiResultDto<bool>
                {
                    IsSuccess = true,
                    Data = true,
                    ErrorMessage = "Monitoring is already running"
                };
            }

            // Update reminder target ID to ensure single instance
            _state.State.ReminderTargetId++;
            _state.State.IsRunning = true;
            _state.State.NextScheduledFetch = DateTime.UtcNow.AddMinutes(_state.State.Config.FetchIntervalMinutes);
            _state.State.NextScheduledFetchUtc = ((DateTimeOffset)_state.State.NextScheduledFetch).ToUnixTimeSeconds();
            
            await _state.WriteStateAsync();

            // Register reminder
            var reminder = await this.RegisterOrUpdateReminder(
                REMINDER_NAME,
                TimeSpan.FromMinutes(_state.State.Config.FetchIntervalMinutes),
                TimeSpan.FromMinutes(_state.State.Config.FetchIntervalMinutes));

            _logger.LogInformation("Tweet monitoring started with interval {IntervalMinutes} minutes", 
                _state.State.Config.FetchIntervalMinutes);

            return new TwitterApiResultDto<bool>
            {
                IsSuccess = true,
                Data = true,
                ErrorMessage = "Monitoring started successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting tweet monitoring");
            return new TwitterApiResultDto<bool>
            {
                IsSuccess = false,
                Data = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<TwitterApiResultDto<bool>> StopMonitoringAsync()
    {
        try
        {
            _logger.LogInformation("Stopping tweet monitoring");

            if (!_state.State.IsRunning)
            {
                return new TwitterApiResultDto<bool>
                {
                    IsSuccess = true,
                    Data = true,
                    ErrorMessage = "Monitoring is not running"
                };
            }

            // Unregister reminder
            try
            {
                var reminder = await this.GetReminder(REMINDER_NAME);
                if (reminder != null)
                {
                    await this.UnregisterReminder(reminder);
                }
            }
            catch (Exception)
            {
                // Reminder doesn't exist, which is fine
            }

            _state.State.IsRunning = false;
            await _state.WriteStateAsync();

            _logger.LogInformation("Tweet monitoring stopped");

            return new TwitterApiResultDto<bool>
            {
                IsSuccess = true,
                Data = true,
                ErrorMessage = "Monitoring stopped successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping tweet monitoring");
            return new TwitterApiResultDto<bool>
            {
                IsSuccess = false,
                Data = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<TwitterApiResultDto<TweetMonitorStatusDto>> GetMonitoringStatusAsync()
    {
        try
        {
            var status = new TweetMonitorStatusDto
            {
                IsRunning = _state.State.IsRunning,
                LastFetchTime = _state.State.LastFetchTime,
                LastFetchTimeUtc = _state.State.LastFetchTimeUtc,
                TotalTweetsStored = _state.State.StoredTweets.Count,
                TweetsFetchedToday = GetTweetsFetchedToday(),
                LastError = _state.State.LastError,
                Config = _state.State.Config,
                NextScheduledFetch = _state.State.NextScheduledFetch,
                NextScheduledFetchUtc = _state.State.NextScheduledFetchUtc
            };

            return new TwitterApiResultDto<TweetMonitorStatusDto>
            {
                IsSuccess = true,
                Data = status
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting monitoring status");
            return new TwitterApiResultDto<TweetMonitorStatusDto>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new TweetMonitorStatusDto()
            };
        }
    }

    public async Task<TwitterApiResultDto<TweetFetchResultDto>> FetchTweetsManuallyAsync()
    {
        try
        {
            _logger.LogInformation("Manual tweet fetch requested");
            return await FetchTweetsInternalAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in manual tweet fetch");
            return new TwitterApiResultDto<TweetFetchResultDto>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new TweetFetchResultDto()
            };
        }
    }

    public async Task<TwitterApiResultDto<List<TweetRecord>>> QueryTweetsByTimeRangeAsync(TimeRangeDto timeRange)
    {
        try
        {
            var startUtc = timeRange.StartTimeUtc;
            var endUtc = timeRange.EndTimeUtc;

            var filteredTweets = _state.State.StoredTweets.Values
                .Where(tweet => tweet.CreatedAtUtc >= startUtc && tweet.CreatedAtUtc <= endUtc)
                .OrderBy(tweet => tweet.CreatedAtUtc)
                .ToList();

            return new TwitterApiResultDto<List<TweetRecord>>
            {
                IsSuccess = true,
                Data = filteredTweets
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying tweets by time range");
            return new TwitterApiResultDto<List<TweetRecord>>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new List<TweetRecord>()
            };
        }
    }

    public async Task<TwitterApiResultDto<List<TweetFetchHistoryDto>>> GetFetchHistoryAsync(int days = 7)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddDays(-days);
            var cutoffUtc = ((DateTimeOffset)cutoffTime).ToUnixTimeSeconds();

            var recentHistory = _state.State.FetchHistory
                .Where(h => h.FetchTimeUtc >= cutoffUtc)
                .OrderByDescending(h => h.FetchTimeUtc)
                .ToList();

            return new TwitterApiResultDto<List<TweetFetchHistoryDto>>
            {
                IsSuccess = true,
                Data = recentHistory
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting fetch history");
            return new TwitterApiResultDto<List<TweetFetchHistoryDto>>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new List<TweetFetchHistoryDto>()
            };
        }
    }

    public async Task<TwitterApiResultDto<bool>> CleanupExpiredTweetsAsync()
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddDays(-_state.State.Config.DataRetentionDays);
            var cutoffUtc = ((DateTimeOffset)cutoffTime).ToUnixTimeSeconds();

            var expiredTweetIds = _state.State.StoredTweets
                .Where(kvp => kvp.Value.CreatedAtUtc < cutoffUtc)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var tweetId in expiredTweetIds)
            {
                _state.State.StoredTweets.Remove(tweetId);
            }

            // Also cleanup fetch history
            _state.State.FetchHistory = _state.State.FetchHistory
                .Where(h => h.FetchTimeUtc >= cutoffUtc)
                .ToList();

            await _state.WriteStateAsync();

            _logger.LogInformation("Cleaned up {Count} expired tweets", expiredTweetIds.Count);

            return new TwitterApiResultDto<bool>
            {
                IsSuccess = true,
                Data = true,
                ErrorMessage = $"Cleaned up {expiredTweetIds.Count} expired tweets"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up expired tweets");
            return new TwitterApiResultDto<bool>
            {
                IsSuccess = false,
                Data = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<TwitterApiResultDto<bool>> UpdateMonitoringConfigAsync(TweetMonitorConfigDto config)
    {
        try
        {
            var oldConfig = _state.State.Config;
            _state.State.Config = config;
            _state.State.Config.ConfigVersion++;

            await _state.WriteStateAsync();

            // If interval changed and monitoring is running, restart the reminder
            if (_state.State.IsRunning && oldConfig.FetchIntervalMinutes != config.FetchIntervalMinutes)
            {
                await StopMonitoringAsync();
                await StartMonitoringAsync();
            }

            _logger.LogInformation("Monitoring configuration updated");

            return new TwitterApiResultDto<bool>
            {
                IsSuccess = true,
                Data = true,
                ErrorMessage = "Configuration updated successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating monitoring config");
            return new TwitterApiResultDto<bool>
            {
                IsSuccess = false,
                Data = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<TwitterApiResultDto<TweetMonitorConfigDto>> GetMonitoringConfigAsync()
    {
        try
        {
            return new TwitterApiResultDto<TweetMonitorConfigDto>
            {
                IsSuccess = true,
                Data = _state.State.Config
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting monitoring config");
            return new TwitterApiResultDto<TweetMonitorConfigDto>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new TweetMonitorConfigDto()
            };
        }
    }

    public async Task<TwitterApiResultDto<TweetFetchResultDto>> RefetchTweetsByTimeRangeAsync(TimeRangeDto timeRange)
    {
        try
        {
            _logger.LogInformation("Refetching tweets for time range {Start} to {End}", 
                timeRange.StartTime, timeRange.EndTime);

            var searchRequest = new SearchTweetsRequestDto
            {
                Query = _state.State.Config.SearchQuery,
                MaxResults = _state.State.Config.MaxTweetsPerFetch,
                StartTime = timeRange.StartTime,
                EndTime = timeRange.EndTime
            };

            return await FetchTweetsWithRequestAsync(searchRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refetching tweets by time range");
            return new TwitterApiResultDto<TweetFetchResultDto>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new TweetFetchResultDto()
            };
        }
    }

    public async Task<TwitterApiResultDto<TweetStatisticsDto>> GetTweetStatisticsAsync(TimeRangeDto timeRange)
    {
        try
        {
            var tweets = _state.State.StoredTweets.Values
                .Where(tweet => tweet.CreatedAtUtc >= timeRange.StartTimeUtc && 
                               tweet.CreatedAtUtc <= timeRange.EndTimeUtc)
                .ToList();

            var statistics = new TweetStatisticsDto
            {
                TotalTweets = tweets.Count,
                OriginalTweets = tweets.Count(t => t.Type == TweetType.Original),
                TweetsWithShareLinks = tweets.Count(t => t.HasValidShareLink),
                UnprocessedTweets = tweets.Count(t => !t.IsProcessed),
                StatisticsGeneratedAt = DateTime.UtcNow,
                QueryRange = timeRange
            };

            // Generate hourly statistics
            statistics.TweetsByHour = tweets
                .GroupBy(t => t.CreatedAt.ToString("yyyy-MM-dd HH:00"))
                .ToDictionary(g => g.Key, g => g.Count());

            // Generate top authors
            statistics.TopAuthors = tweets
                .GroupBy(t => t.AuthorHandle)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .ToDictionary(g => g.Key, g => g.Count());

            return new TwitterApiResultDto<TweetStatisticsDto>
            {
                IsSuccess = true,
                Data = statistics
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating tweet statistics");
            return new TwitterApiResultDto<TweetStatisticsDto>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new TweetStatisticsDto()
            };
        }
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName == REMINDER_NAME && _state.State.IsRunning)
        {
            try
            {
                _logger.LogDebug("Reminder triggered for tweet fetch");
                
                // Update next scheduled fetch time
                _state.State.NextScheduledFetch = DateTime.UtcNow.AddMinutes(_state.State.Config.FetchIntervalMinutes);
                _state.State.NextScheduledFetchUtc = ((DateTimeOffset)_state.State.NextScheduledFetch).ToUnixTimeSeconds();
                
                var result = await FetchTweetsInternalAsync();
                
                if (!result.IsSuccess)
                {
                    _logger.LogWarning("Scheduled tweet fetch failed: {Error}", result.ErrorMessage);
                    _state.State.LastError = result.ErrorMessage;
                }
                else
                {
                    _state.State.LastError = string.Empty;
                }

                // Auto cleanup if enabled
                if (_state.State.Config.EnableAutoCleanup)
                {
                    await CleanupExpiredTweetsAsync();
                }

                await _state.WriteStateAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in reminder execution");
                _state.State.LastError = ex.Message;
                await _state.WriteStateAsync();
            }
        }
    }

    private async Task<TwitterApiResultDto<TweetFetchResultDto>> FetchTweetsInternalAsync()
    {
        var fetchStartTime = DateTime.UtcNow;
        var fetchStartTimeUtc = ((DateTimeOffset)fetchStartTime).ToUnixTimeSeconds();

        try
        {
            var searchRequest = new SearchTweetsRequestDto
            {
                Query = _state.State.Config.SearchQuery,
                MaxResults = _state.State.Config.MaxTweetsPerFetch,
                StartTime = _state.State.LastFetchTime ?? DateTime.UtcNow.AddHours(-1),
                EndTime = DateTime.UtcNow
            };

            var result = await FetchTweetsWithRequestAsync(searchRequest);
            
            _state.State.LastFetchTime = fetchStartTime;
            _state.State.LastFetchTimeUtc = fetchStartTimeUtc;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in internal tweet fetch");
            throw;
        }
    }

    private async Task<TwitterApiResultDto<TweetFetchResultDto>> FetchTweetsWithRequestAsync(SearchTweetsRequestDto searchRequest)
    {
        var fetchStartTime = DateTime.UtcNow;
        var fetchStartTimeUtc = ((DateTimeOffset)fetchStartTime).ToUnixTimeSeconds();
        
        var fetchResult = new TweetFetchResultDto
        {
            FetchStartTime = fetchStartTime,
            FetchStartTimeUtc = fetchStartTimeUtc
        };

        try
        {
            // Search for tweets using TwitterInteractionGrain
            var searchResult = await _twitterGrain!.SearchTweetsAsync(searchRequest);
            
            if (!searchResult.IsSuccess)
            {
                fetchResult.ErrorMessage = searchResult.ErrorMessage;
                await RecordFetchHistory(fetchResult, false, searchResult.ErrorMessage);
                
                return new TwitterApiResultDto<TweetFetchResultDto>
                {
                    IsSuccess = false,
                    ErrorMessage = searchResult.ErrorMessage,
                    Data = fetchResult
                };
            }

            fetchResult.TotalFetched = searchResult.Data.Data.Count;

            // Process each tweet
            foreach (var tweet in searchResult.Data.Data)
            {
                try
                {
                    if (_state.State.StoredTweets.ContainsKey(tweet.Id))
                    {
                        fetchResult.DuplicateSkipped++;
                        continue;
                    }

                    // Get detailed tweet analysis
                    var analysisResult = await _twitterGrain.AnalyzeTweetAsync(tweet.Id);
                    
                    if (!analysisResult.IsSuccess)
                    {
                        _logger.LogWarning("Failed to analyze tweet {TweetId}: {Error}", tweet.Id, analysisResult.ErrorMessage);
                        fetchResult.FilteredOut++;
                        continue;
                    }

                    var tweetDetails = analysisResult.Data;

                    // Filter if only original tweets required
                    if (_state.State.Config.FilterOriginalOnly && tweetDetails.Type != TweetType.Original)
                    {
                        fetchResult.FilteredOut++;
                        continue;
                    }

                    // Create tweet record
                    var tweetRecord = new TweetRecord
                    {
                        TweetId = tweet.Id,
                        AuthorId = tweetDetails.AuthorId,
                        AuthorHandle = tweetDetails.AuthorHandle,
                        AuthorName = tweetDetails.AuthorName,
                        CreatedAt = tweet.CreatedAt,
                        CreatedAtUtc = ((DateTimeOffset)tweet.CreatedAt).ToUnixTimeSeconds(),
                        Text = tweet.Text,
                        Type = tweetDetails.Type,
                        ViewCount = tweetDetails.ViewCount,
                        FollowerCount = tweetDetails.FollowerCount,
                        HasValidShareLink = tweetDetails.HasValidShareLink,
                        ShareLinkUrl = tweetDetails.ShareLinkUrl,
                        IsProcessed = false,
                        FetchedAt = fetchStartTime
                    };

                    _state.State.StoredTweets[tweet.Id] = tweetRecord;
                    fetchResult.NewTweetIds.Add(tweet.Id);
                    fetchResult.NewTweets++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing tweet {TweetId}", tweet.Id);
                    fetchResult.FilteredOut++;
                }
            }

            fetchResult.FetchEndTime = DateTime.UtcNow;
            fetchResult.FetchEndTimeUtc = ((DateTimeOffset)fetchResult.FetchEndTime).ToUnixTimeSeconds();

            await _state.WriteStateAsync();
            await RecordFetchHistory(fetchResult, true, string.Empty);

            _logger.LogInformation("Fetch completed: {Total} total, {New} new, {Duplicates} duplicates, {Filtered} filtered", 
                fetchResult.TotalFetched, fetchResult.NewTweets, fetchResult.DuplicateSkipped, fetchResult.FilteredOut);

            return new TwitterApiResultDto<TweetFetchResultDto>
            {
                IsSuccess = true,
                Data = fetchResult
            };
        }
        catch (Exception ex)
        {
            fetchResult.FetchEndTime = DateTime.UtcNow;
            fetchResult.FetchEndTimeUtc = ((DateTimeOffset)fetchResult.FetchEndTime).ToUnixTimeSeconds();
            fetchResult.ErrorMessage = ex.Message;
            
            await RecordFetchHistory(fetchResult, false, ex.Message);
            
            _logger.LogError(ex, "Error in fetch tweets with request");
            return new TwitterApiResultDto<TweetFetchResultDto>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = fetchResult
            };
        }
    }

    private async Task RecordFetchHistory(TweetFetchResultDto fetchResult, bool isSuccess, string errorMessage)
    {
        try
        {
            var historyRecord = new TweetFetchHistoryDto
            {
                FetchTime = fetchResult.FetchStartTime,
                FetchTimeUtc = fetchResult.FetchStartTimeUtc,
                TweetsFetched = fetchResult.TotalFetched,
                NewTweets = fetchResult.NewTweets,
                IsSuccess = isSuccess,
                ErrorMessage = errorMessage,
                Duration = fetchResult.FetchEndTime - fetchResult.FetchStartTime
            };

            _state.State.FetchHistory.Add(historyRecord);

            // Keep only recent history (last 30 days)
            var cutoffTime = DateTime.UtcNow.AddDays(-30);
            var cutoffUtc = ((DateTimeOffset)cutoffTime).ToUnixTimeSeconds();
            
            _state.State.FetchHistory = _state.State.FetchHistory
                .Where(h => h.FetchTimeUtc >= cutoffUtc)
                .ToList();

            await _state.WriteStateAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording fetch history");
        }
    }

    private int GetTweetsFetchedToday()
    {
        var todayStart = DateTime.UtcNow.Date;
        var todayStartUtc = ((DateTimeOffset)todayStart).ToUnixTimeSeconds();
        
        return _state.State.FetchHistory
            .Where(h => h.FetchTimeUtc >= todayStartUtc && h.IsSuccess)
            .Sum(h => h.NewTweets);
    }
} 