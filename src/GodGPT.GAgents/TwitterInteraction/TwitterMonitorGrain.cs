using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aevatar.Application.Grains.TwitterInteraction.Dtos;
using Aevatar.Application.Grains.Common.Options;

namespace Aevatar.Application.Grains.TwitterInteraction;

/// <summary>
/// Tweet monitoring state
/// </summary>
[GenerateSerializer]
public class TwitterMonitorState
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
/// Tweet monitoring Grain - responsible for scheduled tweet data fetching
/// </summary>
public class TwitterMonitorGrain : Grain, ITwitterMonitorGrain, IRemindable
{
    private readonly ILogger<TwitterMonitorGrain> _logger;
    private readonly IOptionsMonitor<TwitterRewardOptions> _options;
    private readonly IPersistentState<TwitterMonitorState> _state;
    private ITwitterInteractionGrain? _twitterGrain;
    
    private const string REMINDER_NAME = "TweetFetchReminder";

    public TwitterMonitorGrain(
        ILogger<TwitterMonitorGrain> logger,
        IOptionsMonitor<TwitterRewardOptions> options,
        [PersistentState("tweetMonitorState", "DefaultGrainStorage")] IPersistentState<TwitterMonitorState> state)
    {
        _logger = logger;
        _options = options;
        _state = state;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TweetMonitorGrain {GrainId} activating", this.GetPrimaryKeyString());
        
        // Initialize TwitterInteractionGrain reference
        if (string.IsNullOrEmpty(_options.CurrentValue.PullTaskTargetId))
        {
            throw new SystemException("Init ITwitterInteractionGrain, _options.CurrentValue.PullTaskTargetId is null");
        }
        _twitterGrain = GrainFactory.GetGrain<ITwitterInteractionGrain>(_options.CurrentValue.PullTaskTargetId);
        
        // Initialize or update configuration from appsettings.json
        var currentConfig = new TweetMonitorConfigDto
        {
            FetchIntervalMinutes = _options.CurrentValue.PullIntervalMinutes,
            MaxTweetsPerFetch = _options.CurrentValue.BatchFetchSize,
            DataRetentionDays = _options.CurrentValue.DataRetentionDays,
            SearchQuery = _options.CurrentValue.MonitorHandle,
            FilterOriginalOnly = true,
            EnableAutoCleanup = true,
            ConfigVersion = 1
        };

        // Always update configuration on activation to reflect appsettings.json changes
        bool configChanged = _state.State.Config.SearchQuery == string.Empty ||
                           _state.State.Config.FetchIntervalMinutes != currentConfig.FetchIntervalMinutes ||
                           _state.State.Config.MaxTweetsPerFetch != currentConfig.MaxTweetsPerFetch ||
                           _state.State.Config.DataRetentionDays != currentConfig.DataRetentionDays ||
                           _state.State.Config.SearchQuery != currentConfig.SearchQuery;

        _state.State.Config = currentConfig;
        await _state.WriteStateAsync();
        if (configChanged)
        {
            _logger.LogInformation("TweetMonitorGrain Configuration changed, updating from appsettings.json. New interval: {IntervalMinutes} minutes", 
                currentConfig.FetchIntervalMinutes);
            
            // Clean up any existing reminder first (to avoid conflicts with new configuration)
            try
            {
                var existingReminder = await this.GetReminder(REMINDER_NAME);
                if (existingReminder != null)
                {
                    _logger.LogInformation("TweetMonitorGrain Cleaning up existing reminder due to configuration change");
                    await this.UnregisterReminder(existingReminder);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e,"TweetMonitorGrain Reminder doesn't exist");
                // Reminder doesn't exist, which is fine
            }
            
            // If monitoring is running, register reminder with new configuration
            if (_state.State.IsRunning)
            {
                _logger.LogInformation("TweetMonitorGrain Restarting monitoring with new configuration");
                await this.RegisterOrUpdateReminder(
                    REMINDER_NAME,
                    TimeSpan.FromMinutes(_state.State.Config.FetchIntervalMinutes),
                    TimeSpan.FromMinutes(_state.State.Config.FetchIntervalMinutes));
            }
        }

        // Ensure state consistency: check if reminder registration matches IsRunning state
        await EnsureStateConsistencyAsync();

        await base.OnActivateAsync(cancellationToken);
    }

    /// <summary>
    /// Ensure reminder registration state matches the IsRunning state
    /// This handles cases where service interruption caused state inconsistency
    /// </summary>
    private async Task EnsureStateConsistencyAsync()
    {
        try
        {
            var hasReminder = false;
            try
            {
                var reminder = await this.GetReminder(REMINDER_NAME);
                hasReminder = reminder != null;
            }
            catch (Exception)
            {
                hasReminder = false;
            }

            _logger.LogInformation("TweetMonitorGrain State consistency check - IsRunning: {IsRunning}, HasReminder: {HasReminder}", 
                _state.State.IsRunning, hasReminder);

            // Case 1: Should be running but no reminder - register it
            if (_state.State.IsRunning && !hasReminder)
            {
                _logger.LogWarning("TweetMonitorGrain ⚠️ Inconsistent state detected: IsRunning=true but no reminder found. Registering reminder...");
                await this.RegisterOrUpdateReminder(
                    REMINDER_NAME,
                    TimeSpan.FromMinutes(_state.State.Config.FetchIntervalMinutes),
                    TimeSpan.FromMinutes(_state.State.Config.FetchIntervalMinutes));
                _logger.LogInformation("TweetMonitorGrain ✅ Reminder registered to match IsRunning=true state");
            }
            // Case 2: Should not be running but has reminder - unregister it
            else if (!_state.State.IsRunning && hasReminder)
            {
                _logger.LogWarning("TweetMonitorGrain ⚠️ Inconsistent state detected: IsRunning=false but reminder exists. Cleaning up reminder...");
                var reminder = await this.GetReminder(REMINDER_NAME);
                if (reminder != null)
                {
                    await this.UnregisterReminder(reminder);
                }
                _logger.LogInformation("TweetMonitorGrain ✅ Reminder cleaned up to match IsRunning=false state");
            }
            // Case 3: States are consistent
            else
            {
                _logger.LogInformation("TweetMonitorGrain ✅ State consistency verified - no action needed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TweetMonitorGrain ❌ Error during state consistency check");
        }
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

            // Update reminder target ID based on configuration version
            _state.State.ReminderTargetId = _options.CurrentValue.ReminderTargetIdVersion;
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
            var startUtc = timeRange.StartTimeUtcSecond;
            var endUtc = timeRange.EndTimeUtcSecond;

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

    // UpdateMonitoringConfigAsync method removed - all configuration now managed through appsettings.json

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

    public async Task<TwitterApiResultDto<bool>> RefetchTweetsByTimeRangeAsync(TimeRangeDto timeRange)
    {
        try
        {
            _logger.LogInformation("Starting background refetch for time range {Start} to {End}", 
                timeRange.StartTime, timeRange.EndTime);

            var maxEndTime = DateTime.UtcNow;
            var actualEndTime = timeRange.EndTime > maxEndTime ? maxEndTime : timeRange.EndTime;
            
            _logger.LogInformation("Adjusted end time from {OriginalEnd} to {ActualEnd}", 
                timeRange.EndTime, actualEndTime);

            // Handle edge case: StartTime equals or after EndTime
            if (timeRange.StartTime >= actualEndTime)
            {
                _logger.LogWarning("Invalid time range: StartTime {Start} >= EndTime {End}", 
                    timeRange.StartTime, actualEndTime);
                return new TwitterApiResultDto<bool>
                {
                    IsSuccess = false,
                    Data = false,
                    ErrorMessage = "Invalid time range: StartTime must be before EndTime"
                };
            }

            // Use Orleans Timer (Orleans best practice) for background processing
            RegisterTimer(
                callback: async (state) => await ProcessTimeRangeInBackground(timeRange, actualEndTime),
                state: null,
                dueTime: TimeSpan.Zero,                    // Execute immediately
                period: TimeSpan.FromMilliseconds(-1)      // Execute only once
            );

            _logger.LogInformation("Background refetch task started successfully using Orleans Timer");

            // Return task started status
            return new TwitterApiResultDto<bool>
            {
                IsSuccess = true,
                Data = true, // Task started successfully
                ErrorMessage = $"Background refetch task started successfully for time range {timeRange.StartTime:yyyy-MM-dd HH:mm:ss} to {actualEndTime:yyyy-MM-dd HH:mm:ss} UTC. Check monitoring status for progress."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting background refetch");
            return new TwitterApiResultDto<bool>
            {
                IsSuccess = false,
                Data = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task ProcessTimeRangeInBackground(TimeRangeDto timeRange, DateTime actualEndTime)
    {
        try
        {
            _logger.LogInformation("Background processing started for time range {Start} to {End}", 
                timeRange.StartTime, actualEndTime);

            var overallResult = new TweetFetchResultDto
            {
                FetchStartTime = DateTime.UtcNow,
                FetchStartTimeUtc = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds(),
                NewTweetIds = new List<string>(),
                TotalFetched = 0,
                NewTweets = 0,
                DuplicateSkipped = 0,
                FilteredOut = 0
            };

            var errorMessages = new List<string>();
            var currentStart = timeRange.StartTime;
            var hourlyIntervals = 0;

            _logger.LogInformation("Starting time window fetch process from {Start} to {End} (Window size: {WindowHours}h, Max tweets per window: {MaxTweets})", 
                currentStart, actualEndTime, _options.CurrentValue.TimeWindowHours, _options.CurrentValue.MaxTweetsPerWindow);

            while (currentStart < actualEndTime)
            {
                var currentEnd = currentStart.AddHours(_options.CurrentValue.TimeWindowHours);
                if (currentEnd > actualEndTime)
                    currentEnd = actualEndTime;

                hourlyIntervals++;
                
                _logger.LogInformation("Processing time interval {Interval}: {Start} to {End} (Window: {WindowHours}h)", 
                    hourlyIntervals, currentStart, currentEnd, _options.CurrentValue.TimeWindowHours);

                var searchRequest = new SearchTweetsRequestDto
                {
                    Query = _options.CurrentValue.MonitorHandle,
                    MaxResults = _options.CurrentValue.MaxTweetsPerWindow,
                    StartTime = currentStart,
                    EndTime = currentEnd
                };

                var result = await FetchTweetsWithRequestAsync(searchRequest);
                
                if (result.IsSuccess)
                {
                    // Check if we need to adjust window size for high tweet density
                    if (result.Data.TotalFetched >= _options.CurrentValue.MaxTweetsPerWindow)
                    {
                        _logger.LogWarning("High tweet density detected ({TweetCount} tweets in {Hours}h window). " +
                                           "Consider reducing time window for better API rate control.", 
                                           result.Data.TotalFetched, _options.CurrentValue.TimeWindowHours);
                    }
                    
                    // Accumulate results
                    overallResult.TotalFetched += result.Data.TotalFetched;
                    overallResult.NewTweets += result.Data.NewTweets;
                    overallResult.DuplicateSkipped += result.Data.DuplicateSkipped;
                    overallResult.FilteredOut += result.Data.FilteredOut;
                    overallResult.NewTweetIds.AddRange(result.Data.NewTweetIds);
                    
                    _logger.LogInformation("Time interval {Interval} completed: Fetched={Fetched}, New={New}, Duplicates={Duplicates}, Filtered={Filtered}", 
                        hourlyIntervals, result.Data.TotalFetched, result.Data.NewTweets, 
                        result.Data.DuplicateSkipped, result.Data.FilteredOut);
                }
                else
                {
                    var errorMsg = $"Time window {currentStart:HH:mm}-{currentEnd:HH:mm}: {result.ErrorMessage}";
                    errorMessages.Add(errorMsg);
                    _logger.LogError("Time interval {Interval} failed: {Error}", hourlyIntervals, result.ErrorMessage);
                    
                    // Check if it's a rate limit error
                    if (result.ErrorMessage.Contains("TooManyRequests") || result.ErrorMessage.Contains("429"))
                    {
                        _logger.LogWarning("Rate limit detected. Consider reducing TimeWindowHours or MaxTweetsPerWindow in configuration.");
                    }
                }

                currentStart = currentEnd;

                // Intelligent delay based on result: Prioritize API safety for errors
                if (currentStart < actualEndTime) // Don't delay after the last iteration
                {
                    // Priority 1: Always delay when there's an error (API safety)
                    if (!result.IsSuccess)
                    {
                        _logger.LogWarning("⚠️ Error occurred, applying mandatory {Delay}min delay for API safety: {Error}", 
                            _options.CurrentValue.MinTimeWindowMinutes, result.ErrorMessage);
                        await Task.Delay(TimeSpan.FromMinutes(_options.CurrentValue.MinTimeWindowMinutes));
                    }
                    // Priority 2: Skip delay only when successful with no tweets found
                    else if (result.Data.TotalFetched == 0)
                    {
                        _logger.LogInformation("⚡ No tweets found in time window, proceeding immediately to next window (skipping {Delay}min delay)...", 
                            _options.CurrentValue.MinTimeWindowMinutes);
                    }
                    // Priority 3: Normal delay when tweets were found
                    else
                    {
                        _logger.LogInformation("⏳ Waiting {Delay} minutes to avoid API rate limiting (found {TweetCount} tweets)...", 
                            _options.CurrentValue.MinTimeWindowMinutes, result.Data.TotalFetched);
                        await Task.Delay(TimeSpan.FromMinutes(_options.CurrentValue.MinTimeWindowMinutes));
                    }
                }
            }

            overallResult.FetchEndTime = DateTime.UtcNow;
            overallResult.FetchEndTimeUtc = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
            
            if (errorMessages.Count > 0)
            {
                overallResult.ErrorMessage = string.Join("; ", errorMessages);
            }

            var isOverallSuccess = errorMessages.Count == 0;
            
            _logger.LogInformation("Background processing completed: {Intervals} time windows processed ({WindowSize}h each), " +
                                  "Overall: Fetched={TotalFetched}, New={NewTweets}, Duplicates={Duplicates}, Filtered={Filtered}, Success={Success}", 
                                  hourlyIntervals, _options.CurrentValue.TimeWindowHours, overallResult.TotalFetched, overallResult.NewTweets, 
                                  overallResult.DuplicateSkipped, overallResult.FilteredOut, isOverallSuccess);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in background processing of time range");
        }
    }

    public async Task<TwitterApiResultDto<TweetStatisticsDto>> GetTweetStatisticsAsync(TimeRangeDto timeRange)
    {
        try
        {
            var tweets = _state.State.StoredTweets.Values
                .Where(tweet => tweet.CreatedAtUtc >= timeRange.StartTimeUtcSecond && 
                               tweet.CreatedAtUtc <= timeRange.EndTimeUtcSecond)
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
                
                // Process directly in the grain context instead of using Task.Run
                // This ensures proper Orleans activation context access
                await ProcessReceiveReminderInBackground();
                
                _logger.LogDebug("Tweet fetch task completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in reminder processing");
                _state.State.LastError = ex.Message;
                await _state.WriteStateAsync();
            }
        }
    }

    private async Task ProcessReceiveReminderInBackground()
    {
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

    private async Task<TwitterApiResultDto<TweetFetchResultDto>> FetchTweetsInternalAsync()
    {
        var fetchStartTime = DateTime.UtcNow;
        var fetchStartTimeUtc = ((DateTimeOffset)fetchStartTime).ToUnixTimeSeconds();

        try
        {
            // Build query with filter conditions to exclude retweets and replies
            var queryWithFilters = $"{_state.State.Config.SearchQuery}";
            
            var startTime = _state.State.LastFetchTime ?? DateTime.UtcNow.AddHours(-1);
            var endTime = DateTime.UtcNow;
            var timeRangeMinutes = (endTime - startTime).TotalMinutes;
            
            _logger.LogInformation("🚀 Starting scheduled tweet fetch - Query: '{Query}', Max results per window: {MaxResults}, Time range: {TimeRange} minutes", 
                queryWithFilters, _options.CurrentValue.BatchFetchSize, timeRangeMinutes);
            
            // Always use RefetchTweetsByTimeRangeAsync pattern: fixed window processing for all scheduled tasks
            _logger.LogInformation("🔄 Applying RefetchTweetsByTimeRangeAsync pattern: fixed window processing for scheduled tasks");
            
            var result = await FetchTweetsWithRefetchPatternAsync(startTime, endTime);
            _state.State.LastFetchTime = fetchStartTime;
            _state.State.LastFetchTimeUtc = fetchStartTimeUtc;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Scheduled tweet fetch failed");
            throw;
        }
    }

    private async Task<TwitterApiResultDto<TweetFetchResultDto>> FetchTweetsWithRefetchPatternAsync(DateTime startTime, DateTime endTime)
    {
        var fetchStartTime = DateTime.UtcNow;
        var fetchStartTimeUtc = ((DateTimeOffset)fetchStartTime).ToUnixTimeSeconds();
        
        var overallResult = new TweetFetchResultDto
        {
            FetchStartTime = fetchStartTime,
            FetchStartTimeUtc = fetchStartTimeUtc,
            NewTweetIds = new List<string>(),
            TotalFetched = 0,
            NewTweets = 0,
            DuplicateSkipped = 0,
            FilteredOut = 0
        };

        var errorMessages = new List<string>();
        var currentStart = startTime;
        var windowCount = 0;

        try
        {
            var maxWindowMinutes = _options.CurrentValue.TimeWindowHours * 60; // Convert hours to minutes
            _logger.LogInformation("🔄 Starting scheduled fetch using RefetchTweetsByTimeRangeAsync pattern from {Start} to {End} (Window size: {WindowMinutes} min, Max tweets per window: {MaxTweets})", 
                currentStart, endTime, maxWindowMinutes, _options.CurrentValue.BatchFetchSize);

            while (currentStart < endTime)
            {
                var currentEnd = currentStart.AddMinutes(maxWindowMinutes);
                if (currentEnd > endTime)
                    currentEnd = endTime;

                windowCount++;
                
                _logger.LogInformation("📅 Processing scheduled fetch window {Window}: {Start} to {End} (Window: {WindowMinutes} min)", 
                    windowCount, currentStart, currentEnd, maxWindowMinutes);

                var searchRequest = new SearchTweetsRequestDto
                {
                    Query = _state.State.Config.SearchQuery,
                    MaxResults = _options.CurrentValue.BatchFetchSize, // Use BatchFetchSize for scheduled tasks
                    StartTime = currentStart,
                    EndTime = currentEnd
                };

                var result = await FetchTweetsWithRequestAsync(searchRequest);
                
                if (result.IsSuccess)
                {
                    // Check if we need to adjust window size for high tweet density
                    if (result.Data.TotalFetched >= _options.CurrentValue.BatchFetchSize)
                    {
                        _logger.LogWarning("High tweet density detected in scheduled fetch ({TweetCount} tweets in {Minutes} min window). " +
                                           "Consider reducing TimeWindowHours for better API rate control.", 
                                           result.Data.TotalFetched, maxWindowMinutes);
                    }
                    
                    // Accumulate results
                    overallResult.TotalFetched += result.Data.TotalFetched;
                    overallResult.NewTweets += result.Data.NewTweets;
                    overallResult.DuplicateSkipped += result.Data.DuplicateSkipped;
                    overallResult.FilteredOut += result.Data.FilteredOut;
                    overallResult.NewTweetIds.AddRange(result.Data.NewTweetIds);
                    
                    _logger.LogInformation("✅ Scheduled fetch window {Window} completed: Fetched={WindowFetched}, New={WindowNew}, Duplicates={WindowDuplicates}, Filtered={WindowFiltered}", 
                        windowCount, result.Data.TotalFetched, result.Data.NewTweets, result.Data.DuplicateSkipped, result.Data.FilteredOut);
                }
                else
                {
                    var errorMsg = $"Scheduled fetch window {windowCount} ({currentStart:HH:mm}-{currentEnd:HH:mm}): {result.ErrorMessage}";
                    errorMessages.Add(errorMsg);
                    _logger.LogError("❌ Scheduled fetch window {Window} failed: {Error}", windowCount, result.ErrorMessage);
                    
                    // Check if it's a rate limit error
                    if (result.ErrorMessage.Contains("TooManyRequests") || result.ErrorMessage.Contains("429"))
                    {
                        _logger.LogWarning("⚠️ Rate limit detected in scheduled fetch. Consider reducing TimeWindowHours or BatchFetchSize in configuration.");
                    }
                }

                currentStart = currentEnd;

                // Apply RefetchTweetsByTimeRangeAsync intelligent delay pattern
                if (currentStart < endTime) // Don't delay after the last iteration
                {
                    // Priority 1: Always delay when there's an error (API safety first)
                    if (!result.IsSuccess)
                    {
                        _logger.LogWarning("⚠️ Error in scheduled fetch, applying mandatory {Delay}min delay for API safety: {Error}", 
                            _options.CurrentValue.MinTimeWindowMinutes, result.ErrorMessage);
                        await Task.Delay(TimeSpan.FromMinutes(_options.CurrentValue.MinTimeWindowMinutes));
                    }
                    // Priority 2: Skip delay only when successful with no tweets found (efficiency)
                    else if (result.Data.TotalFetched == 0)
                    {
                        _logger.LogInformation("⚡ No tweets found in scheduled fetch window, proceeding immediately to next window (skipping {Delay}min delay)...", 
                            _options.CurrentValue.MinTimeWindowMinutes);
                    }
                    // Priority 3: Normal delay when tweets were found (API rate limiting)
                    else
                    {
                        _logger.LogInformation("⏳ Scheduled fetch waiting {Delay} minutes to avoid API rate limiting (found {TweetCount} tweets)...", 
                            _options.CurrentValue.MinTimeWindowMinutes, result.Data.TotalFetched);
                        await Task.Delay(TimeSpan.FromMinutes(_options.CurrentValue.MinTimeWindowMinutes));
                    }
                }
            }

            overallResult.FetchEndTime = DateTime.UtcNow;
            overallResult.FetchEndTimeUtc = ((DateTimeOffset)overallResult.FetchEndTime).ToUnixTimeSeconds();
            
            if (errorMessages.Count > 0)
            {
                overallResult.ErrorMessage = string.Join("; ", errorMessages);
            }

            var isOverallSuccess = errorMessages.Count == 0;
            
            _logger.LogInformation("🎉 Scheduled fetch using RefetchTweetsByTimeRangeAsync pattern completed: {Windows} windows processed ({WindowSize} min each), " +
                                  "Overall: Fetched={TotalFetched}, New={NewTweets}, Duplicates={Duplicates}, Filtered={Filtered}, Success={Success}", 
                                  windowCount, maxWindowMinutes, overallResult.TotalFetched, 
                                  overallResult.NewTweets, overallResult.DuplicateSkipped, overallResult.FilteredOut, isOverallSuccess);

            return new TwitterApiResultDto<TweetFetchResultDto>
            {
                IsSuccess = isOverallSuccess,
                Data = overallResult,
                ErrorMessage = overallResult.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in scheduled fetch using RefetchTweetsByTimeRangeAsync pattern");
            return new TwitterApiResultDto<TweetFetchResultDto>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = overallResult
            };
        }
    }

    private async Task<TwitterApiResultDto<TweetFetchResultDto>> FetchTweetsWithTimeWindowSplittingAsync(DateTime startTime, DateTime endTime)
    {
        var fetchStartTime = DateTime.UtcNow;
        var fetchStartTimeUtc = ((DateTimeOffset)fetchStartTime).ToUnixTimeSeconds();
        
        var overallResult = new TweetFetchResultDto
        {
            FetchStartTime = fetchStartTime,
            FetchStartTimeUtc = fetchStartTimeUtc,
            NewTweetIds = new List<string>(),
            TotalFetched = 0,
            NewTweets = 0,
            DuplicateSkipped = 0,
            FilteredOut = 0
        };

        var errorMessages = new List<string>();
        var currentStart = startTime;
        var windowCount = 0;

        try
        {
            var maxWindowMinutes = _options.CurrentValue.TimeWindowHours * 60; // Convert hours to minutes
            _logger.LogInformation("🔄 Starting time window splitting fetch from {Start} to {End} (Window size: {WindowMinutes} min, Max tweets per window: {MaxTweets})", 
                currentStart, endTime, maxWindowMinutes, _state.State.Config.MaxTweetsPerFetch);

            while (currentStart < endTime)
            {
                var currentEnd = currentStart.AddMinutes(maxWindowMinutes);
                if (currentEnd > endTime)
                    currentEnd = endTime;

                windowCount++;
                
                _logger.LogInformation("📅 Processing scheduled fetch window {Window}: {Start} to {End} (Window: {WindowMinutes} min)", 
                    windowCount, currentStart, currentEnd, maxWindowMinutes);

                var searchRequest = new SearchTweetsRequestDto
                {
                    Query = _state.State.Config.SearchQuery,
                    MaxResults = _state.State.Config.MaxTweetsPerFetch,
                    StartTime = currentStart,
                    EndTime = currentEnd
                };

                var result = await FetchTweetsWithRequestAsync(searchRequest);
                
                if (result.IsSuccess)
                {
                    overallResult.TotalFetched += result.Data.TotalFetched;
                    overallResult.NewTweets += result.Data.NewTweets;
                    overallResult.DuplicateSkipped += result.Data.DuplicateSkipped;
                    overallResult.FilteredOut += result.Data.FilteredOut;
                    overallResult.NewTweetIds.AddRange(result.Data.NewTweetIds);
                    
                    _logger.LogInformation("✅ Window {Window} completed: Fetched={WindowFetched}, New={WindowNew}, Duplicates={WindowDuplicates}, Filtered={WindowFiltered}", 
                        windowCount, result.Data.TotalFetched, result.Data.NewTweets, result.Data.DuplicateSkipped, result.Data.FilteredOut);
                }
                else
                {
                    var errorMsg = $"Window {windowCount} ({currentStart:HH:mm}-{currentEnd:HH:mm}): {result.ErrorMessage}";
                    errorMessages.Add(errorMsg);
                    _logger.LogError("❌ Window {Window} failed: {Error}", windowCount, result.ErrorMessage);
                    
                    // Check if it's a rate limit error
                    if (result.ErrorMessage.Contains("TooManyRequests") || result.ErrorMessage.Contains("429"))
                    {
                        _logger.LogWarning("⚠️ Rate limit detected. Consider reducing TimeWindowHours or BatchFetchSize in configuration.");
                    }
                }

                currentStart = currentEnd;

                // Intelligent delay based on result: Prioritize API safety
                if (currentStart < endTime) // Don't delay after the last iteration
                {
                    // Priority 1: Always delay when there's an error (API safety)
                    if (!result.IsSuccess)
                    {
                        _logger.LogWarning("⚠️ Error in scheduled fetch, applying mandatory {Delay}min delay for API safety: {Error}", 
                            _options.CurrentValue.MinTimeWindowMinutes, result.ErrorMessage);
                        await Task.Delay(TimeSpan.FromMinutes(_options.CurrentValue.MinTimeWindowMinutes));
                    }
                    // Priority 2: Skip delay only when successful with no tweets found
                    else if (result.Data.TotalFetched == 0)
                    {
                        _logger.LogInformation("⚡ No tweets found in scheduled fetch window, proceeding immediately to next window (skipping {Delay}min delay)...", 
                            _options.CurrentValue.MinTimeWindowMinutes);
                    }
                    // Priority 3: Normal delay when tweets were found
                    else
                    {
                        _logger.LogInformation("⏳ Scheduled fetch waiting {Delay} minutes to avoid API rate limiting (found {TweetCount} tweets)...", 
                            _options.CurrentValue.MinTimeWindowMinutes, result.Data.TotalFetched);
                        await Task.Delay(TimeSpan.FromMinutes(_options.CurrentValue.MinTimeWindowMinutes));
                    }
                }
            }

            overallResult.FetchEndTime = DateTime.UtcNow;
            overallResult.FetchEndTimeUtc = ((DateTimeOffset)overallResult.FetchEndTime).ToUnixTimeSeconds();
            
            if (errorMessages.Count > 0)
            {
                overallResult.ErrorMessage = string.Join("; ", errorMessages);
            }

            var isOverallSuccess = errorMessages.Count == 0;
            
            _logger.LogInformation("🎉 Scheduled fetch time window splitting completed: {Windows} windows processed ({WindowSize} min each), " +
                                  "Overall: Fetched={TotalFetched}, New={NewTweets}, Duplicates={Duplicates}, Filtered={Filtered}, Success={Success}", 
                                  windowCount, maxWindowMinutes, overallResult.TotalFetched, 
                                  overallResult.NewTweets, overallResult.DuplicateSkipped, overallResult.FilteredOut, isOverallSuccess);

            return new TwitterApiResultDto<TweetFetchResultDto>
            {
                IsSuccess = isOverallSuccess,
                Data = overallResult,
                ErrorMessage = overallResult.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in scheduled fetch time window splitting");
            return new TwitterApiResultDto<TweetFetchResultDto>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = overallResult
            };
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
            _logger.LogInformation("🔍 Calling Twitter API to search tweets...");
            var searchResult = await _twitterGrain!.SearchTweetsAsync(searchRequest);
            if (!searchResult.IsSuccess)
            {
                _logger.LogError("❌ Twitter API search failed: {ErrorMessage}", searchResult.ErrorMessage);
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
            _logger.LogInformation("📊 Twitter API returned {Count} tweets", fetchResult.TotalFetched);

            // Process each tweet
            _logger.LogInformation("🔄 Starting tweet processing and filtering...");
            foreach (var tweet in searchResult.Data.Data)
            {
                try
                {
                    _logger.LogDebug("📝 Processing tweet {TweetId} (author: {AuthorId})", tweet.Id, tweet.AuthorId);

                    if (_state.State.StoredTweets.ContainsKey(tweet.Id))
                    {
                        _logger.LogDebug("⚠️ Skipping duplicate tweet {TweetId}", tweet.Id);
                        fetchResult.DuplicateSkipped++;
                        continue;
                    }

                    if (_options.CurrentValue.ExcludedAccountIds.Contains(tweet.AuthorId))
                    {
                        _logger.LogDebug("⚠️ Skipping Excluded AuthorId tweet {TweetId} AuthorId {AuthorId}", tweet.Id,
                            tweet.AuthorId);
                        fetchResult.DuplicateSkipped++;
                        continue;
                    }
                    

                    // Get detailed tweet analysis
                    _logger.LogDebug("🔍 Analyzing tweet details {TweetId}...", tweet.Id);
                    var analysisResult = await _twitterGrain.AnalyzeTweetLightweightAsync(tweet.Id);
                    
                    if (!analysisResult.IsSuccess)
                    {
                        _logger.LogWarning("❌ Tweet analysis failed {TweetId}: {Error}", tweet.Id, analysisResult.ErrorMessage);
                        fetchResult.FilteredOut++;
                        continue;
                    }

                    var tweetDetails = analysisResult.Data;
                    _logger.LogDebug("✅ Tweet analysis completed {TweetId} - Author: @{AuthorHandle} ({AuthorId}), Type: {Type}", 
                        tweet.Id, tweetDetails.AuthorHandle, tweetDetails.AuthorId, tweetDetails.Type);

                    // Filter if only original tweets required
                    if (_state.State.Config.FilterOriginalOnly && tweetDetails.Type != TweetType.Original)
                    {
                        _logger.LogDebug("🚫 Filtering non-original tweet {TweetId} - Type: {Type}", tweet.Id, tweetDetails.Type);
                        fetchResult.FilteredOut++;
                        continue;
                    }
                    
                    // Filter excluded accounts (blacklist)
                    var excludedAccountIds = _options.CurrentValue.GetExcludedAccountIds();
                    if (excludedAccountIds.Contains(tweetDetails.AuthorId))
                    {
                        _logger.LogInformation("🚫 Filtering excluded account tweet {TweetId} - Author ID: {AuthorId} (@{AuthorHandle})", 
                            tweet.Id, tweetDetails.AuthorId, tweetDetails.AuthorHandle);
                        fetchResult.FilteredOut++;
                        continue;
                    }

                    // Check user daily tweet limit for the day when the tweet was created (using UTC date)
                    var tweetDateUtc = tweetDetails.CreatedAt.Date;  // Use UTC date from tweetDetails which is already correctly parsed
                    var tweetDateStartUtc = new DateTimeOffset(DateTime.SpecifyKind(tweetDateUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
                    var tweetDateEndUtc = new DateTimeOffset(DateTime.SpecifyKind(tweetDateUtc.AddDays(1), DateTimeKind.Utc)).ToUnixTimeSeconds();
                    

                    
                    var userTweetCountOnThatDay = _state.State.StoredTweets.Values
                        .Where(t => t.AuthorId == tweetDetails.AuthorId && 
                                   t.CreatedAtUtc >= tweetDateStartUtc && 
                                   t.CreatedAtUtc < tweetDateEndUtc)
                        .Count();

                    if (userTweetCountOnThatDay >= _options.CurrentValue.MaxTweetsPerUser)
                    {
                        _logger.LogInformation("🚫 User daily limit reached - Tweet {TweetId}, Author: @{AuthorHandle} ({AuthorId}), " +
                                              "Tweets on {TweetDateUtc} UTC: {Count}/{Limit}", 
                                              tweet.Id, tweetDetails.AuthorHandle, tweetDetails.AuthorId, 
                                              tweetDateUtc.ToString("yyyy-MM-dd"), userTweetCountOnThatDay, _options.CurrentValue.MaxTweetsPerUser);
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
                        CreatedAt = tweetDetails.CreatedAt, // Use UTC time from tweetDetails which is already correctly parsed
                        CreatedAtUtc = new DateTimeOffset(tweetDetails.CreatedAt).ToUnixTimeSeconds(),
                        Text = string.Empty, // Privacy protection: Do not store tweet text content
                        Type = tweetDetails.Type,
                        ViewCount = tweetDetails.ViewCount,
                        FollowerCount = 0, // Not fetched in lightweight mode - will be populated during reward processing
                        HasValidShareLink = tweetDetails.HasValidShareLink,
                        ShareLinkUrl = string.Empty, // Privacy protection: Do not store share link URL
                        IsProcessed = false,
                        FetchedAt = fetchStartTime
                    };

                    _state.State.StoredTweets[tweet.Id] = tweetRecord;
                    fetchResult.NewTweetIds.Add(tweet.Id);
                    fetchResult.NewTweets++;
                    
                    _logger.LogInformation("✅ Saved valid tweet {TweetId} - Author: @{AuthorHandle}, Share link: {HasShareLink}", 
                        tweet.Id, tweetDetails.AuthorHandle, tweetDetails.HasValidShareLink);
                    
                    // Add delay between processing tweets to avoid API rate limiting
                    if (_options.CurrentValue.TweetProcessingDelayMs > 0)
                    {
                        _logger.LogDebug("⏳ Waiting {DelayMs}ms before processing next tweet...", _options.CurrentValue.TweetProcessingDelayMs);
                        await Task.Delay(_options.CurrentValue.TweetProcessingDelayMs);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Tweet processing exception {TweetId}", tweet.Id);
                    fetchResult.FilteredOut++;
                }
            }

            fetchResult.FetchEndTime = DateTime.UtcNow;
            fetchResult.FetchEndTimeUtc = ((DateTimeOffset)fetchResult.FetchEndTime).ToUnixTimeSeconds();

            await _state.WriteStateAsync();
            await RecordFetchHistory(fetchResult, true, string.Empty);

            _logger.LogInformation("🎉 Fetch completed - Total: {Total}, New: {New}, Duplicates: {Duplicates}, Filtered: {Filtered}", 
                fetchResult.TotalFetched, fetchResult.NewTweets, fetchResult.DuplicateSkipped, fetchResult.FilteredOut);
            
            if (fetchResult.NewTweets > 0)
            {
                _logger.LogInformation("📋 New tweet IDs: [{TweetIds}]", string.Join(", ", fetchResult.NewTweetIds));
            }

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

            // Keep only the latest 7 records
            _state.State.FetchHistory = _state.State.FetchHistory
                .OrderByDescending(h => h.FetchTimeUtc)
                .Take(7)
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