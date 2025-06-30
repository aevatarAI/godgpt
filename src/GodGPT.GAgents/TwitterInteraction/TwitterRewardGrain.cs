using Orleans;
using Orleans.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aevatar.Application.Grains.TwitterInteraction.Dtos;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Agents.ChatManager;

namespace Aevatar.Application.Grains.TwitterInteraction;

/// <summary>
/// 推特奖励计算状态
/// </summary>
[GenerateSerializer]
public class TwitterRewardState
{
    [Id(0)] public bool IsRunning { get; set; }
    [Id(1)] public DateTime? LastCalculationTime { get; set; }
    [Id(2)] public long LastCalculationTimeUtc { get; set; }
    [Id(3)] public RewardConfigDto Config { get; set; } = new();
    [Id(4)] public List<RewardCalculationHistoryDto> CalculationHistory { get; set; } = new();
    [Id(5)] public Dictionary<string, List<UserRewardRecordDto>> UserRewards { get; set; } = new(); // Key: Date string (yyyy-MM-dd)
    [Id(6)] public string LastError { get; set; } = string.Empty;
    [Id(7)] public long ReminderTargetId { get; set; } = 1;
    [Id(8)] public DateTime NextScheduledCalculation { get; set; }
    [Id(9)] public long NextScheduledCalculationUtc { get; set; }
    [Id(10)] public int TotalUsersRewarded { get; set; }
    [Id(11)] public int TotalCreditsDistributed { get; set; }
}

/// <summary>
/// 推特奖励计算Grain - 负责每日00:00 UTC执行奖励计算
/// </summary>
public class TwitterRewardGrain : Grain, ITwitterRewardGrain, IRemindable
{
    private readonly ILogger<TwitterRewardGrain> _logger;
    private readonly TwitterRewardOptions _options;
    private readonly IPersistentState<TwitterRewardState> _state;
    private ITweetMonitorGrain? _tweetMonitorGrain;
    // Removed IChatManagerGAgent to comply with architecture constraint
    
    private const string REMINDER_NAME = "DailyRewardCalculationReminder";

    public TwitterRewardGrain(
        ILogger<TwitterRewardGrain> logger,
        IOptions<TwitterRewardOptions> options,
        [PersistentState("twitterRewardState", "DefaultGrainStorage")] IPersistentState<TwitterRewardState> state)
    {
        _logger = logger;
        _options = options.Value;
        _state = state;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TwitterRewardGrain {GrainId} activating", this.GetPrimaryKeyString());
        
        // Initialize grain references
        _tweetMonitorGrain = GrainFactory.GetGrain<ITweetMonitorGrain>("tweet-monitor");
        // ChatManagerGAgent reference removed to comply with architecture constraint
        
        // Initialize state if needed
        if (_state.State.Config.RewardTiers.Count == 0)
        {
            _state.State.Config = new RewardConfigDto
            {
                TimeRangeStartHours = 72,
                TimeRangeEndHours = 48,
                ShareLinkMultiplier = 1.1,
                MaxDailyCreditsPerUser = 500,
                EnableRewardCalculation = true,
                ConfigVersion = 1,
                RewardTiers = _options.RewardTiers.Select(t => new RewardTierDto
                {
                    MinViews = t.MinViews,
                    MinFollowers = t.MinFollowers,
                    RewardCredits = t.RewardCredits,
                    TierName = $"Tier-{t.MinViews}v-{t.MinFollowers}f"
                }).ToList()
            };
            await _state.WriteStateAsync();
        }

        await base.OnActivateAsync(cancellationToken);
    }

    public async Task<TwitterApiResultDto<bool>> StartRewardCalculationAsync()
    {
        try
        {
            _logger.LogInformation("Starting daily reward calculation");

            if (_state.State.IsRunning)
            {
                return new TwitterApiResultDto<bool>
                {
                    IsSuccess = true,
                    Data = true,
                    ErrorMessage = "Reward calculation is already running"
                };
            }

            // Update reminder target ID to ensure single instance
            _state.State.ReminderTargetId++;
            _state.State.IsRunning = true;
            
            // Calculate next 00:00 UTC time
            var nextMidnightUtc = GetNextMidnightUtc();
            _state.State.NextScheduledCalculation = nextMidnightUtc;
            _state.State.NextScheduledCalculationUtc = ((DateTimeOffset)nextMidnightUtc).ToUnixTimeSeconds();
            
            await _state.WriteStateAsync();

            // Register reminder for daily execution at 00:00 UTC
            var timeUntilMidnight = nextMidnightUtc - DateTime.UtcNow;
            var reminder = await this.RegisterOrUpdateReminder(
                REMINDER_NAME,
                timeUntilMidnight,
                TimeSpan.FromDays(1)); // Repeat every 24 hours

            _logger.LogInformation("Daily reward calculation started, next execution at {NextTime} UTC", nextMidnightUtc);

            return new TwitterApiResultDto<bool>
            {
                IsSuccess = true,
                Data = true,
                ErrorMessage = $"Reward calculation scheduled for {nextMidnightUtc:yyyy-MM-dd HH:mm:ss} UTC"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting reward calculation");
            return new TwitterApiResultDto<bool>
            {
                IsSuccess = false,
                Data = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<TwitterApiResultDto<bool>> StopRewardCalculationAsync()
    {
        try
        {
            _logger.LogInformation("Stopping daily reward calculation");

            if (!_state.State.IsRunning)
            {
                return new TwitterApiResultDto<bool>
                {
                    IsSuccess = true,
                    Data = true,
                    ErrorMessage = "Reward calculation is not running"
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

            _logger.LogInformation("Daily reward calculation stopped");

            return new TwitterApiResultDto<bool>
            {
                IsSuccess = true,
                Data = true,
                ErrorMessage = "Reward calculation stopped successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping reward calculation");
            return new TwitterApiResultDto<bool>
            {
                IsSuccess = false,
                Data = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<TwitterApiResultDto<RewardCalculationStatusDto>> GetRewardCalculationStatusAsync()
    {
        try
        {
            var status = new RewardCalculationStatusDto
            {
                IsRunning = _state.State.IsRunning,
                LastCalculationTime = _state.State.LastCalculationTime,
                LastCalculationTimeUtc = _state.State.LastCalculationTimeUtc,
                NextScheduledCalculation = _state.State.NextScheduledCalculation,
                NextScheduledCalculationUtc = _state.State.NextScheduledCalculationUtc,
                LastError = _state.State.LastError,
                Config = _state.State.Config,
                TotalUsersRewarded = _state.State.TotalUsersRewarded,
                TotalCreditsDistributed = _state.State.TotalCreditsDistributed
            };

            return new TwitterApiResultDto<RewardCalculationStatusDto>
            {
                IsSuccess = true,
                Data = status
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting reward calculation status");
            return new TwitterApiResultDto<RewardCalculationStatusDto>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new RewardCalculationStatusDto()
            };
        }
    }

    public async Task<TwitterApiResultDto<RewardCalculationResultDto>> TriggerRewardCalculationAsync(DateTime targetDate)
    {
        try
        {
            _logger.LogInformation("Manual reward calculation triggered for date {TargetDate}", targetDate.ToString("yyyy-MM-dd"));
            return await ExecuteRewardCalculationAsync(targetDate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in manual reward calculation");
            return new TwitterApiResultDto<RewardCalculationResultDto>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new RewardCalculationResultDto()
            };
        }
    }

    public async Task<TwitterApiResultDto<List<RewardCalculationHistoryDto>>> GetRewardCalculationHistoryAsync(int days = 30)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddDays(-days);
            var cutoffUtc = ((DateTimeOffset)cutoffTime).ToUnixTimeSeconds();

            var recentHistory = _state.State.CalculationHistory
                .Where(h => h.CalculationDateUtc >= cutoffUtc)
                .OrderByDescending(h => h.CalculationDateUtc)
                .ToList();

            return new TwitterApiResultDto<List<RewardCalculationHistoryDto>>
            {
                IsSuccess = true,
                Data = recentHistory
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting calculation history");
            return new TwitterApiResultDto<List<RewardCalculationHistoryDto>>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new List<RewardCalculationHistoryDto>()
            };
        }
    }

    public async Task<TwitterApiResultDto<List<UserRewardRecordDto>>> GetUserRewardRecordsAsync(string userId, int days = 30)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddDays(-days);
            var cutoffUtc = ((DateTimeOffset)cutoffTime).ToUnixTimeSeconds();

            var userRewards = new List<UserRewardRecordDto>();
            
            foreach (var dayRewards in _state.State.UserRewards.Values)
            {
                var userDayRewards = dayRewards
                    .Where(r => r.UserId == userId && r.RewardDateUtc >= cutoffUtc)
                    .ToList();
                userRewards.AddRange(userDayRewards);
            }

            userRewards = userRewards.OrderByDescending(r => r.RewardDateUtc).ToList();

            return new TwitterApiResultDto<List<UserRewardRecordDto>>
            {
                IsSuccess = true,
                Data = userRewards
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user reward records for user {UserId}", userId);
            return new TwitterApiResultDto<List<UserRewardRecordDto>>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new List<UserRewardRecordDto>()
            };
        }
    }

    public async Task<TwitterApiResultDto<DailyRewardStatisticsDto>> GetDailyRewardStatisticsAsync(DateTime targetDate)
    {
        try
        {
            var dateKey = targetDate.ToString("yyyy-MM-dd");
            var rewards = _state.State.UserRewards.ContainsKey(dateKey) 
                ? _state.State.UserRewards[dateKey] 
                : new List<UserRewardRecordDto>();

            var statistics = new DailyRewardStatisticsDto
            {
                StatisticsDate = targetDate,
                StatisticsDateUtc = ((DateTimeOffset)targetDate).ToUnixTimeSeconds(),
                TotalUsersRewarded = rewards.Count,
                TotalCreditsDistributed = rewards.Sum(r => r.FinalCredits),
                TotalTweetsEligible = rewards.Count,
                TweetsWithShareLinks = rewards.Count(r => r.HasValidShareLink),
                AverageCreditsPerUser = rewards.Count > 0 ? rewards.Average(r => r.FinalCredits) : 0,
                ShareLinkBonusTotal = rewards.Where(r => r.HasValidShareLink).Sum(r => r.FinalCredits - r.BaseCredits)
            };

            // Generate reward tier statistics
            foreach (var tier in _state.State.Config.RewardTiers)
            {
                var tierRewards = rewards.Where(r => r.BaseCredits == tier.RewardCredits).ToList();
                statistics.RewardsByTier[tier.TierName] = tierRewards.Count;
            }

            // Generate user statistics
            var userGroups = rewards.GroupBy(r => r.UserHandle);
            foreach (var group in userGroups)
            {
                statistics.UsersRewarded[group.Key] = group.Sum(r => r.FinalCredits);
            }

            return new TwitterApiResultDto<DailyRewardStatisticsDto>
            {
                IsSuccess = true,
                Data = statistics
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating daily statistics for {Date}", targetDate.ToString("yyyy-MM-dd"));
            return new TwitterApiResultDto<DailyRewardStatisticsDto>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new DailyRewardStatisticsDto()
            };
        }
    }

    public async Task<TwitterApiResultDto<bool>> UpdateRewardConfigAsync(RewardConfigDto config)
    {
        try
        {
            _state.State.Config = config;
            _state.State.Config.ConfigVersion++;
            await _state.WriteStateAsync();

            _logger.LogInformation("Reward configuration updated");

            return new TwitterApiResultDto<bool>
            {
                IsSuccess = true,
                Data = true,
                ErrorMessage = "Configuration updated successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating reward config");
            return new TwitterApiResultDto<bool>
            {
                IsSuccess = false,
                Data = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<TwitterApiResultDto<RewardConfigDto>> GetRewardConfigAsync()
    {
        try
        {
            return new TwitterApiResultDto<RewardConfigDto>
            {
                IsSuccess = true,
                Data = _state.State.Config
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting reward config");
            return new TwitterApiResultDto<RewardConfigDto>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new RewardConfigDto()
            };
        }
    }

    public async Task<TwitterApiResultDto<RewardCalculationResultDto>> RecalculateRewardsForDateAsync(DateTime targetDate, bool forceRecalculate = false)
    {
        try
        {
            var dateKey = targetDate.ToString("yyyy-MM-dd");
            
            // Check if already calculated and not forcing recalculation
            if (!forceRecalculate && _state.State.UserRewards.ContainsKey(dateKey))
            {
                return new TwitterApiResultDto<RewardCalculationResultDto>
                {
                    IsSuccess = false,
                    ErrorMessage = "Rewards already calculated for this date. Use forceRecalculate=true to override.",
                    Data = new RewardCalculationResultDto()
                };
            }

            _logger.LogInformation("Recalculating rewards for date {TargetDate}, force={Force}", targetDate.ToString("yyyy-MM-dd"), forceRecalculate);
            return await ExecuteRewardCalculationAsync(targetDate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recalculating rewards for date {Date}", targetDate.ToString("yyyy-MM-dd"));
            return new TwitterApiResultDto<RewardCalculationResultDto>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new RewardCalculationResultDto()
            };
        }
    }

    public async Task<TwitterApiResultDto<bool>> HasUserReceivedDailyRewardAsync(string userId, DateTime targetDate)
    {
        try
        {
            var dateKey = targetDate.ToString("yyyy-MM-dd");
            var hasReceived = _state.State.UserRewards.ContainsKey(dateKey) &&
                              _state.State.UserRewards[dateKey].Any(r => r.UserId == userId);

            return new TwitterApiResultDto<bool>
            {
                IsSuccess = true,
                Data = hasReceived
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking user daily reward for {UserId} on {Date}", userId, targetDate.ToString("yyyy-MM-dd"));
            return new TwitterApiResultDto<bool>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = false
            };
        }
    }

    public async Task<TwitterApiResultDto<TimeControlStatusDto>> GetTimeControlStatusAsync()
    {
        try
        {
            var currentUtc = DateTime.UtcNow;
            var nextMidnightUtc = GetNextMidnightUtc();
            
            var status = new TimeControlStatusDto
            {
                CurrentUtcTime = currentUtc,
                CurrentUtcTimestamp = ((DateTimeOffset)currentUtc).ToUnixTimeSeconds(),
                LastRewardCalculationTime = _state.State.LastCalculationTime ?? DateTime.MinValue,
                LastRewardCalculationTimestamp = _state.State.LastCalculationTimeUtc,
                NextRewardCalculationTime = nextMidnightUtc,
                NextRewardCalculationTimestamp = ((DateTimeOffset)nextMidnightUtc).ToUnixTimeSeconds(),
                TimeUntilNextCalculation = nextMidnightUtc - currentUtc,
                IsRewardCalculationDay = ShouldExecuteRewardCalculation(currentUtc),
                TimezoneInfo = "UTC"
            };

            return new TwitterApiResultDto<TimeControlStatusDto>
            {
                IsSuccess = true,
                Data = status
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting time control status");
            return new TwitterApiResultDto<TimeControlStatusDto>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new TimeControlStatusDto()
            };
        }
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName == REMINDER_NAME && _state.State.IsRunning)
        {
            try
            {
                _logger.LogInformation("Daily reward calculation reminder triggered at {CurrentTime} UTC", DateTime.UtcNow);
                
                // Calculate for the previous day (reward calculation runs at 00:00 UTC for the previous day)
                var targetDate = DateTime.UtcNow.Date.AddDays(-1);
                
                // Update next scheduled calculation
                var nextMidnightUtc = GetNextMidnightUtc();
                _state.State.NextScheduledCalculation = nextMidnightUtc;
                _state.State.NextScheduledCalculationUtc = ((DateTimeOffset)nextMidnightUtc).ToUnixTimeSeconds();
                
                var result = await ExecuteRewardCalculationAsync(targetDate);
                
                if (!result.IsSuccess)
                {
                    _logger.LogWarning("Scheduled reward calculation failed: {Error}", result.ErrorMessage);
                    _state.State.LastError = result.ErrorMessage;
                }
                else
                {
                    _state.State.LastError = string.Empty;
                    _state.State.LastCalculationTime = DateTime.UtcNow;
                    _state.State.LastCalculationTimeUtc = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
                }

                await _state.WriteStateAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in daily reward calculation reminder execution");
                _state.State.LastError = ex.Message;
                await _state.WriteStateAsync();
            }
        }
    }

    private async Task<TwitterApiResultDto<RewardCalculationResultDto>> ExecuteRewardCalculationAsync(DateTime targetDate)
    {
        var processingStartTime = DateTime.UtcNow;
        var dateKey = targetDate.ToString("yyyy-MM-dd");

        var result = new RewardCalculationResultDto
        {
            CalculationDate = targetDate,
            CalculationDateUtc = ((DateTimeOffset)targetDate).ToUnixTimeSeconds(),
            ProcessingStartTime = processingStartTime,
            IsSuccess = false
        };

        try
        {
            if (!_state.State.Config.EnableRewardCalculation)
            {
                result.ErrorMessage = "Reward calculation is disabled in configuration";
                return new TwitterApiResultDto<RewardCalculationResultDto>
                {
                    IsSuccess = false,
                    ErrorMessage = result.ErrorMessage,
                    Data = result
                };
            }

            // Calculate time range: 72-48 hours before target date
            var timeRange = new TimeRangeDto
            {
                StartTime = targetDate.AddHours(-_state.State.Config.TimeRangeStartHours),
                EndTime = targetDate.AddHours(-_state.State.Config.TimeRangeEndHours)
            };
            timeRange.StartTimeUtc = ((DateTimeOffset)timeRange.StartTime).ToUnixTimeSeconds();
            timeRange.EndTimeUtc = ((DateTimeOffset)timeRange.EndTime).ToUnixTimeSeconds();

            result.ProcessedTimeRange = timeRange;

            _logger.LogInformation("Calculating rewards for {Date}, time range: {Start} to {End}", 
                targetDate.ToString("yyyy-MM-dd"), timeRange.StartTime, timeRange.EndTime);

            // Query TweetMonitorGrain for tweets in the time range
            var tweetsResult = await _tweetMonitorGrain!.QueryTweetsByTimeRangeAsync(timeRange);
            
            if (!tweetsResult.IsSuccess)
            {
                result.ErrorMessage = $"Failed to query tweets: {tweetsResult.ErrorMessage}";
                await RecordCalculationHistory(result, false);
                return new TwitterApiResultDto<RewardCalculationResultDto>
                {
                    IsSuccess = false,
                    ErrorMessage = result.ErrorMessage,
                    Data = result
                };
            }

            var tweets = tweetsResult.Data;
            result.TotalTweetsProcessed = tweets.Count;

            // Filter tweets for reward eligibility
            var eligibleTweets = FilterEligibleTweets(tweets);
            result.EligibleTweets = eligibleTweets.Count;

            _logger.LogInformation("Found {Total} tweets, {Eligible} eligible for rewards", tweets.Count, eligibleTweets.Count);

            // Group tweets by user and calculate rewards
            var userRewards = await CalculateUserRewardsAsync(eligibleTweets, targetDate);
            result.UserRewards = userRewards;
            result.UsersRewarded = userRewards.Count;
            result.TotalCreditsDistributed = userRewards.Sum(r => r.FinalCredits);

            // Send rewards to users
            await SendRewardsToUsersAsync(userRewards);

            // Store user rewards
            _state.State.UserRewards[dateKey] = userRewards;
            
            // Update totals
            _state.State.TotalUsersRewarded += result.UsersRewarded;
            _state.State.TotalCreditsDistributed += result.TotalCreditsDistributed;

            result.ProcessingEndTime = DateTime.UtcNow;
            result.ProcessingDuration = result.ProcessingEndTime - result.ProcessingStartTime;
            result.IsSuccess = true;

            await _state.WriteStateAsync();
            await RecordCalculationHistory(result, true);

            _logger.LogInformation("Reward calculation completed: {Users} users rewarded, {Credits} credits distributed", 
                result.UsersRewarded, result.TotalCreditsDistributed);

            return new TwitterApiResultDto<RewardCalculationResultDto>
            {
                IsSuccess = true,
                Data = result
            };
        }
        catch (Exception ex)
        {
            result.ProcessingEndTime = DateTime.UtcNow;
            result.ProcessingDuration = result.ProcessingEndTime - result.ProcessingStartTime;
            result.ErrorMessage = ex.Message;
            
            await RecordCalculationHistory(result, false);
            
            _logger.LogError(ex, "Error in reward calculation execution");
            return new TwitterApiResultDto<RewardCalculationResultDto>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = result
            };
        }
    }

    private List<TweetRecord> FilterEligibleTweets(List<TweetRecord> tweets)
    {
        return tweets.Where(tweet => 
            tweet.Type == TweetType.Original && // Only original tweets
            tweet.ViewCount >= 20 && // Minimum 20 views
            !_state.State.Config.ExcludedUserIds.Contains(tweet.AuthorId) && // Not system accounts
            !tweet.IsProcessed // Not already processed
        ).ToList();
    }

    private async Task<List<UserRewardRecordDto>> CalculateUserRewardsAsync(List<TweetRecord> eligibleTweets, DateTime rewardDate)
    {
        var userRewards = new List<UserRewardRecordDto>();
        var userDailyCredits = new Dictionary<string, int>(); // Track daily limits

        foreach (var tweet in eligibleTweets)
        {
            try
            {
                // Check daily limit
                if (userDailyCredits.ContainsKey(tweet.AuthorId) && 
                    userDailyCredits[tweet.AuthorId] >= _state.State.Config.MaxDailyCreditsPerUser)
                {
                    continue; // User has reached daily limit
                }

                // Find matching reward tier
                var tier = FindRewardTier(tweet.ViewCount, tweet.FollowerCount);
                if (tier == null)
                {
                    continue; // No matching tier
                }

                var baseCredits = tier.RewardCredits;
                var multiplier = tweet.HasValidShareLink ? _state.State.Config.ShareLinkMultiplier : 1.0;
                var finalCredits = (int)Math.Floor(baseCredits * multiplier);

                // Check if adding this reward would exceed daily limit
                var currentUserCredits = userDailyCredits.GetValueOrDefault(tweet.AuthorId, 0);
                if (currentUserCredits + finalCredits > _state.State.Config.MaxDailyCreditsPerUser)
                {
                    finalCredits = _state.State.Config.MaxDailyCreditsPerUser - currentUserCredits;
                }

                if (finalCredits <= 0)
                {
                    continue; // No credits to award
                }

                var rewardRecord = new UserRewardRecordDto
                {
                    UserId = tweet.AuthorId,
                    UserHandle = tweet.AuthorHandle,
                    TweetId = tweet.TweetId,
                    RewardDate = rewardDate,
                    RewardDateUtc = ((DateTimeOffset)rewardDate).ToUnixTimeSeconds(),
                    BaseCredits = baseCredits,
                    ShareLinkMultiplier = multiplier,
                    FinalCredits = finalCredits,
                    ViewCount = tweet.ViewCount,
                    FollowerCount = tweet.FollowerCount,
                    HasValidShareLink = tweet.HasValidShareLink,
                    IsRewardSent = false
                };

                userRewards.Add(rewardRecord);
                userDailyCredits[tweet.AuthorId] = currentUserCredits + finalCredits;

                // Mark tweet as processed
                tweet.IsProcessed = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating reward for tweet {TweetId}", tweet.TweetId);
            }
        }

        return userRewards;
    }

    private RewardTierDto? FindRewardTier(int viewCount, int followerCount)
    {
        // Find the best matching tier (highest reward where both conditions are met)
        return _state.State.Config.RewardTiers
            .Where(tier => viewCount >= tier.MinViews && followerCount >= tier.MinFollowers)
            .OrderByDescending(tier => tier.RewardCredits)
            .FirstOrDefault();
    }

    private async Task SendRewardsToUsersAsync(List<UserRewardRecordDto> userRewards)
    {
        foreach (var reward in userRewards)
        {
            try
            {
                // TODO: Implement actual credit distribution using IGrainWithStringKey structure
                // This follows the architecture constraint: not using IChatManagerGAgent : IGAgent
                // Focus on calculation and recording reward amounts, actual sending will be implemented later
                
                _logger.LogInformation("TODO: Send {Credits} credits to user {UserId} for tweet {TweetId} (calculation complete)", 
                    reward.FinalCredits, reward.UserId, reward.TweetId);

                // Mark as processed for calculation purposes, actual sending pending
                reward.IsRewardSent = false; // Set to false as per development phase requirements
                reward.RewardSentTime = null; // No actual sending yet
                reward.RewardTransactionId = $"PENDING_{Guid.NewGuid()}"; // Mark as pending
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing reward calculation for user {UserId}", reward.UserId);
                reward.IsRewardSent = false;
            }
        }
    }

    private async Task RecordCalculationHistory(RewardCalculationResultDto result, bool isSuccess)
    {
        try
        {
            var historyRecord = new RewardCalculationHistoryDto
            {
                CalculationDate = result.CalculationDate,
                CalculationDateUtc = result.CalculationDateUtc,
                IsSuccess = isSuccess,
                UsersRewarded = result.UsersRewarded,
                TotalCreditsDistributed = result.TotalCreditsDistributed,
                ProcessingDuration = result.ProcessingDuration,
                ErrorMessage = result.ErrorMessage,
                ProcessedTimeRangeStart = result.ProcessedTimeRange?.StartTime ?? DateTime.MinValue,
                ProcessedTimeRangeEnd = result.ProcessedTimeRange?.EndTime ?? DateTime.MinValue
            };

            _state.State.CalculationHistory.Add(historyRecord);

            // Keep only recent history (last 90 days)
            var cutoffTime = DateTime.UtcNow.AddDays(-90);
            var cutoffUtc = ((DateTimeOffset)cutoffTime).ToUnixTimeSeconds();
            
            _state.State.CalculationHistory = _state.State.CalculationHistory
                .Where(h => h.CalculationDateUtc >= cutoffUtc)
                .ToList();

            await _state.WriteStateAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording calculation history");
        }
    }

    private DateTime GetNextMidnightUtc()
    {
        var now = DateTime.UtcNow;
        return now.Date.AddDays(1); // Next day at 00:00 UTC
    }

    private bool ShouldExecuteRewardCalculation(DateTime currentTime)
    {
        // Execute at 00:00 UTC daily
        return currentTime.Hour == 0 && currentTime.Minute < 5; // 5-minute window
    }
} 