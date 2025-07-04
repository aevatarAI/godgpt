using Orleans;
using Orleans.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aevatar.Application.Grains.TwitterInteraction.Dtos;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Agents.ChatManager;

namespace Aevatar.Application.Grains.TwitterInteraction;

/// <summary>
/// Twitter reward calculation state
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
/// Twitter reward calculation grain - responsible for daily reward calculation execution at 00:00 UTC
/// </summary>
public class TwitterRewardGrain : Grain, ITwitterRewardGrain, IRemindable
{
    private readonly ILogger<TwitterRewardGrain> _logger;
    private readonly IOptionsMonitor<TwitterRewardOptions> _options;
    private readonly IPersistentState<TwitterRewardState> _state;
    private ITwitterMonitorGrain? _tweetMonitorGrain;
    private ITwitterInteractionGrain? _twitterInteractionGrain;
    // Removed IChatManagerGAgent to comply with architecture constraint
    
    private const string REMINDER_NAME = "DailyRewardCalculationReminder";

    public TwitterRewardGrain(
        ILogger<TwitterRewardGrain> logger,
        IOptionsMonitor<TwitterRewardOptions> options,
        [PersistentState("twitterRewardState", "DefaultGrainStorage")] IPersistentState<TwitterRewardState> state)
    {
        _logger = logger;
        _options = options;
        _state = state;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TwitterRewardGrain {GrainId} activating", this.GetPrimaryKeyString());

        if (string.IsNullOrEmpty(_options.CurrentValue.PullTaskTargetId))
        {
            throw new SystemException("Init ITweetMonitorGrain, _options.CurrentValue.PullTaskTargetId is null");
        }
        if (string.IsNullOrEmpty(_options.CurrentValue.RewardTaskTargetId))
        {
            throw new SystemException("Init ITwitterInteractionGrain, _options.CurrentValue.RewardTaskTargetId is null");
        }
        _tweetMonitorGrain = GrainFactory.GetGrain<ITwitterMonitorGrain>(_options.CurrentValue.PullTaskTargetId);
        
        _twitterInteractionGrain = GrainFactory.GetGrain<ITwitterInteractionGrain>(_options.CurrentValue.RewardTaskTargetId);
        // ChatManagerGAgent reference removed to comply with architecture constraint
        
        // Initialize or update configuration from appsettings.json
        var currentConfig = new RewardConfigDto
        {
            TimeRangeStartHours = _options.CurrentValue.TimeOffsetMinutes / 60, // Convert minutes to hours
            TimeRangeEndHours = _options.CurrentValue.TimeWindowMinutes / 60,  // Convert minutes to hours
            ShareLinkMultiplier = _options.CurrentValue.ShareLinkMultiplier,
            MaxDailyCreditsPerUser = _options.CurrentValue.DailyRewardLimit,
            EnableRewardCalculation = true,
            ConfigVersion = 1,
            MinViewsForReward = _options.CurrentValue.MinViewsForReward > 0 ? _options.CurrentValue.MinViewsForReward : 20, // Default minimum views for reward eligibility
            RewardTiers = _options.CurrentValue.RewardTiers.Select(t => new RewardTierDto
            {
                MinViews = t.MinViews,
                MinFollowers = t.MinFollowers,
                RewardCredits = t.RewardCredits,
                TierName = $"Tier-{t.MinViews}v-{t.MinFollowers}f"
            }).ToList()
        };

        // Always update configuration on activation to reflect appsettings.json changes
        bool configChanged = _state.State.Config.RewardTiers.Count == 0 ||
                           _state.State.Config.TimeRangeStartHours != currentConfig.TimeRangeStartHours ||
                           _state.State.Config.TimeRangeEndHours != currentConfig.TimeRangeEndHours ||
                           _state.State.Config.ShareLinkMultiplier != currentConfig.ShareLinkMultiplier ||
                           _state.State.Config.MaxDailyCreditsPerUser != currentConfig.MaxDailyCreditsPerUser ||
                           _state.State.Config.MinViewsForReward != currentConfig.MinViewsForReward ||
                           _state.State.Config.RewardTiers.Count != currentConfig.RewardTiers.Count;

        _state.State.Config = currentConfig;
        await _state.WriteStateAsync();
        if (configChanged)
        {
            _logger.LogInformation("TwitterRewardGrain Configuration changed, updating from appsettings.json. " +
                "TimeRange: {StartHours}-{EndHours}h, MaxDailyCredits: {MaxCredits}, ShareMultiplier: {Multiplier}, MinViews: {MinViews}, RewardTiers: {TierCount}", 
                currentConfig.TimeRangeStartHours, currentConfig.TimeRangeEndHours, 
                currentConfig.MaxDailyCreditsPerUser, currentConfig.ShareLinkMultiplier, currentConfig.MinViewsForReward, currentConfig.RewardTiers.Count);

            // Clean up any existing reminder first (to avoid conflicts with new configuration)
            try
            {
                var existingReminder = await this.GetReminder(REMINDER_NAME);
                if (existingReminder != null)
                {
                    _logger.LogInformation("TwitterRewardGrain Cleaning up existing reminder due to configuration change");
                    await this.UnregisterReminder(existingReminder);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "TwitterRewardGrain Reminder doesn't exist");
                // Reminder doesn't exist, which is fine
            }
            
            // If reward calculation is running, register reminder with new configuration
            if (_state.State.IsRunning)
            {
                _logger.LogInformation("TwitterRewardGrain Restarting reward calculation with new configuration");
                var nextMidnightUtc = GetNextMidnightUtc();
                var timeUntilMidnight = nextMidnightUtc - DateTime.UtcNow;
                await this.RegisterOrUpdateReminder(
                    REMINDER_NAME,
                    timeUntilMidnight,
                    TimeSpan.FromDays(1));
            }
        }

        // Update reminder target ID based on configuration version
        _state.State.ReminderTargetId = _options.CurrentValue.ReminderTargetIdVersion;

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

    public async Task<TwitterApiResultDto<bool>> TriggerRewardCalculationAsync(DateTime targetDate)
    {
        try
        {
            _logger.LogInformation("Manual reward calculation triggered for date {TargetDate}", targetDate.ToString("yyyy-MM-dd"));
            
            // Update state to indicate manual calculation is starting
            _state.State.LastError = "Manual calculation in progress...";
            await _state.WriteStateAsync();
            
            // Use Orleans Timer (Orleans best practice) for background processing
            RegisterTimer(
                callback: async (state) => await ExecuteRewardCalculationAsync(targetDate),
                state: null,
                dueTime: TimeSpan.Zero,                    // Execute immediately
                period: TimeSpan.FromMilliseconds(-1)      // Execute only once
            );
            
            // Return task started status
            return new TwitterApiResultDto<bool>
            {
                IsSuccess = true,
                Data = true, // Task started successfully
                ErrorMessage = $"Manual reward calculation task started successfully for date {targetDate:yyyy-MM-dd}. Check status using GetRewardCalculationStatusAsync()."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting manual reward calculation");
            return new TwitterApiResultDto<bool>
            {
                IsSuccess = false,
                Data = false, // Task failed to start
                ErrorMessage = ex.Message
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

    public async Task<TwitterApiResultDto<bool>> ClearRewardByDayUtcSecondAsync(long utcSeconds)
    {
        try
        {
            // Convert UTC seconds to DateTime
            var targetDate = DateTimeOffset.FromUnixTimeSeconds(utcSeconds).DateTime;
            var dateKey = targetDate.ToString("yyyy-MM-dd");
            
            _logger.LogInformation("Clearing reward records for UTC seconds {UtcSeconds}, converted to date {TargetDate}", utcSeconds, dateKey);
            
            _state.State.UserRewards[dateKey] = new List<UserRewardRecordDto>();
            await _state.WriteStateAsync();
            
            _logger.LogInformation("Successfully cleared reward records for date {TargetDate} (UTC seconds: {UtcSeconds})", dateKey, utcSeconds);
            
            return new TwitterApiResultDto<bool>
            {
                IsSuccess = true,
                Data = true,
                ErrorMessage = $"Reward records cleared for date {dateKey}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing reward records for UTC seconds {UtcSeconds}", utcSeconds);
            return new TwitterApiResultDto<bool>
            {
                IsSuccess = false,
                Data = false,
                ErrorMessage = ex.Message
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
                //Use Task.Run to avoid blocking the reminder and potential timeout
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessReceiveReminderInBackground();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in background reminder processing");
                        _state.State.LastError = ex.Message;
                        await _state.WriteStateAsync();
                    }
                });

                _logger.LogDebug("Background tweet fetch task started");
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in daily reward calculation reminder execution");
                _state.State.LastError = ex.Message;
                await _state.WriteStateAsync();
            }
        }
    }
    

    private async Task ProcessReceiveReminderInBackground()
    {
        // Calculate for the previous day (reward calculation runs at 00:00 UTC for the previous day)
        var targetDate = DateTime.UtcNow.Date.AddDays(0);
                
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

            // Calculate time range: N-M hours before target date
            var startTime = targetDate.AddHours(-_state.State.Config.TimeRangeStartHours);
            var endTime =
                targetDate.AddHours(-_state.State.Config.TimeRangeStartHours + _state.State.Config.TimeRangeEndHours);
            var timeRange = TimeRangeDto.FromDateTime(startTime, endTime);

            result.ProcessedTimeRange = timeRange;

            _logger.LogInformation("Calculating rewards for {Date}, time range: {Start} to {End}", 
                targetDate.ToString("yyyy-MM-dd"), timeRange.StartTime, timeRange.EndTime);

            // Query TweetMonitorGrain for tweets in the time range
            var bonusCreditsTweetsResult = await _tweetMonitorGrain!.QueryTweetsByTimeRangeAsync(timeRange);
            
            var regularCreditsTweetsResult = await _tweetMonitorGrain!.QueryTweetsByTimeRangeAsync(TimeRangeDto.FromDateTime(targetDate, targetDate.AddDays(-1)));
            
            if (!bonusCreditsTweetsResult.IsSuccess)
            {
                result.ErrorMessage = $"Failed to query tweets: {bonusCreditsTweetsResult.ErrorMessage}";
                await RecordCalculationHistory(result, false);
                return new TwitterApiResultDto<RewardCalculationResultDto>
                {
                    IsSuccess = false,
                    ErrorMessage = result.ErrorMessage,
                    Data = result
                };
            }
            
            if (!regularCreditsTweetsResult.IsSuccess)
            {
                result.ErrorMessage = $"Failed to query tweets: {regularCreditsTweetsResult.ErrorMessage}";
                await RecordCalculationHistory(result, false);
                return new TwitterApiResultDto<RewardCalculationResultDto>
                {
                    IsSuccess = false,
                    ErrorMessage = result.ErrorMessage,
                    Data = result
                };
            }

            var bonusCreditsTweets = bonusCreditsTweetsResult.Data;
            var regularCreditsTweets = regularCreditsTweetsResult.Data;
            result.TotalTweetsProcessed = bonusCreditsTweets.Count + regularCreditsTweets.Count;

            // Filter tweets for reward eligibility
            var bonusCreditsEligibleTweets = FilterEligibleTweets(bonusCreditsTweets);
            var regularCreditsEligibleTweets = FilterEligibleTweets(regularCreditsTweets);
            result.EligibleTweets = bonusCreditsEligibleTweets.Count + regularCreditsEligibleTweets.Count;

            _logger.LogInformation(
                "CreditsTweets Found {Total} bonusTweets, {Eligible} bonusEligible for bonusreward evaluation (original tweets, min {MinViews} views, non-excluded users, unprocessed)" +
                " Found {Total} regularTweets, {Eligible} regularEligible for regularreward ", bonusCreditsTweets.Count,
                bonusCreditsEligibleTweets.Count, _state.State.Config.MinViewsForReward, regularCreditsTweets.Count,
                regularCreditsEligibleTweets.Count);
            

            // Group tweets by user and calculate rewards
            var userRewards =
                await CalculateUserRewardsAsync(bonusCreditsEligibleTweets, regularCreditsEligibleTweets, targetDate);
            result.UserRewards = userRewards;
            result.UsersRewarded = userRewards.Count;
            result.TotalCreditsDistributed = userRewards.Sum(r => r.FinalCredits);

            // Send rewards to users
            await SendRewardsToUsersAsync(userRewards);

            // Store user rewards
            _state.State.UserRewards[dateKey] = userRewards;
            
            // Clean up old reward records to control data size - only keep today's records
            var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
            var keysToRemove = _state.State.UserRewards.Keys.Where(key => key != today && key != dateKey).ToList();
            foreach (var keyToRemove in keysToRemove)
            {
                _state.State.UserRewards.Remove(keyToRemove);
            }
            
            if (keysToRemove.Count > 0)
            {
                _logger.LogInformation("Cleaned up {Count} old reward record dates to control data size. Keeping only: {KeptDates}", 
                    keysToRemove.Count, string.Join(", ", _state.State.UserRewards.Keys));
            }
            
            // Update totals
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
            !_state.State.Config.ExcludedUserIds.Contains(tweet.AuthorId) && // Not system accounts
            !tweet.IsProcessed // Not already processed
        ).ToList();
    }

    private async Task<List<UserRewardRecordDto>> CalculateUserRewardsAsync(
        List<TweetRecord> bonusCreditsEligibleTweets, List<TweetRecord> regularCreditsEligibleTweets,
        DateTime rewardDate)
    {
        // Get existing reward records for today to prevent duplicate rewards
        var dateKey = rewardDate.ToString("yyyy-MM-dd");
        var existingRewards = _state.State.UserRewards.ContainsKey(dateKey) 
            ? _state.State.UserRewards[dateKey] 
            : new List<UserRewardRecordDto>();
        
        // Create set of already rewarded users for fast lookup
        var alreadyRewardedUsers = existingRewards.Select(r => r.UserId).ToHashSet();

        _logger.LogInformation(
            "Found {ExistingCount} existing user rewards for date {Date}, processing bonusCreditsType {EligibleCount} regularCreditsType {regularEligibleCount} eligible tweets",
            existingRewards.Count, dateKey, bonusCreditsEligibleTweets.Count, regularCreditsEligibleTweets.Count);

        // Group tweets by user for calculating both regular and bonus credits
        var bonusCreditsTweetsByUser = bonusCreditsEligibleTweets
            .Where(tweet => !alreadyRewardedUsers.Contains(tweet.AuthorId))
            .GroupBy(tweet => tweet.AuthorId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var regularCreditsTweetsByUser = regularCreditsEligibleTweets
            .Where(tweet => !alreadyRewardedUsers.Contains(tweet.AuthorId))
            .GroupBy(tweet => tweet.AuthorId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var processedUsers = 0;
        var skippedAlreadyRewarded = bonusCreditsEligibleTweets.Count(tweet => alreadyRewardedUsers.Contains(tweet.AuthorId)) +
                                     regularCreditsEligibleTweets.Count(tweet => alreadyRewardedUsers.Contains(tweet.AuthorId));

        // Create user reward dictionary to collect all rewards
        var userRewardDict = new Dictionary<string, UserRewardRecordDto>();

        // Get latest user and tweet information only for bonus credits users (regular credits don't need latest data)
        _logger.LogInformation("Fetching latest information for {UserCount} bonus credit users to ensure accurate reward calculation", bonusCreditsTweetsByUser.Count);
        var latestUserAndTweetInfo = await GetLatestUserAndTweetInfoAsync(bonusCreditsTweetsByUser);

        // Step 1: Process regular credits (yesterday's tweets) - No need to fetch latest data, no view count restrictions
        foreach (var userTweets in regularCreditsTweetsByUser)
        {
            try
            {
                var userId = userTweets.Key;
                var tweets = userTweets.Value;
                
                // Calculate regular credits: 2 credits per tweet, max 10 tweets (20 credits)
                var regularTweetCount = tweets.Count;
                var regularCredits = Math.Min(regularTweetCount * 2, 20); // Max 10 tweets * 2 credits = 20 credits
                
                // Use first tweet for record keeping (no need to find best for regular credits)
                var recordTweet = tweets.First();

                var rewardRecord = new UserRewardRecordDto
                {
                    UserId = userId,
                    UserHandle = recordTweet.AuthorHandle, // Use stored username
                    TweetId = recordTweet.TweetId, // Use first tweet for reference
                    RewardDate = rewardDate,
                    RewardDateUtc = ((DateTimeOffset)rewardDate).ToUnixTimeSeconds(),
                    BaseCredits = 0, // No base credits for regular rewards
                    ShareLinkMultiplier = 1.0, // No multiplier for regular rewards
                    FinalCredits = regularCredits,
                    HasValidShareLink = recordTweet.HasValidShareLink,
                    IsRewardSent = false,
                    
                    // New separated credit fields
                    RegularCredits = regularCredits,
                    BonusCredits = 0, // Will be updated in step 2 if user has bonus credits
                    TweetCount = regularTweetCount,
                    BonusCreditsBeforeMultiplier = 0
                };

                userRewardDict[userId] = rewardRecord;
                processedUsers++;

                _logger.LogInformation("Calculated regular rewards for user {UserId} (@{UserHandle}): {RegularCredits} regular credits " +
                    "({TweetCount} tweets, reference tweet: {TweetId}) [USING STORED DATA]", 
                    userId, recordTweet.AuthorHandle, regularCredits, regularTweetCount, recordTweet.TweetId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating regular reward for user {UserId}", userTweets.Key);
            }
        }

        // Step 2: Process bonus credits (some days before)
        foreach (var userTweets in bonusCreditsTweetsByUser)
        {
            try
            {
                var userId = userTweets.Key;
                var tweets = userTweets.Value;
                
                // Skip if we couldn't get latest information for this user
                if (!latestUserAndTweetInfo.ContainsKey(userId))
                {
                    _logger.LogWarning("Skipping user {UserId} - could not fetch latest information", userId);
                    continue;
                }
                
                var (latestUserInfo, latestTweetInfo) = latestUserAndTweetInfo[userId];
                
                // Calculate bonus credits based on 8-tier system using LATEST data
                var totalBonusCredits = 0;
                var totalBonusCreditsBeforeMultiplier = 0;
                var bestTweet = tweets.First(); // Use first tweet for record keeping
                var bestLatestTweet = latestTweetInfo.FirstOrDefault();
                
                // Create a mapping from tweet ID to latest tweet info
                var tweetIdToLatestInfo = latestTweetInfo.ToDictionary(t => t.TweetId, t => t);
                
                foreach (var tweet in tweets)
                {
                    // Get latest tweet information if available
                    var latestTweetData = tweetIdToLatestInfo.ContainsKey(tweet.TweetId) 
                        ? tweetIdToLatestInfo[tweet.TweetId] 
                        : null;
                    
                    // Use latest view count if available, otherwise use stored value
                    var currentViewCount = latestTweetData?.ViewCount ?? 0;
                    var currentFollowerCount = latestUserInfo.FollowersCount;
                    var currentHasValidShareLink = latestTweetData?.HasValidShareLink ?? false;
                    
                    // Check minimum views requirement for bonus credits (using latest data)
                    if (currentViewCount < _state.State.Config.MinViewsForReward)
                    {
                        continue;
                    }
                    
                    // Find matching reward tier for bonus credits (using latest data)
                    var tier = FindRewardTier(currentViewCount, currentFollowerCount);
                    if (tier == null)
                    {
                        continue;
                    }

                    var bonusCreditsForTweet = tier.RewardCredits;
                    totalBonusCreditsBeforeMultiplier += bonusCreditsForTweet;
                    
                    // Apply share link multiplier only to bonus credits (using latest data)
                    if (currentHasValidShareLink)
                    {
                        bonusCreditsForTweet = (int)Math.Floor(bonusCreditsForTweet * _state.State.Config.ShareLinkMultiplier);
                    }
                    
                    totalBonusCredits += bonusCreditsForTweet;
                    
                    // Update best tweet for record keeping (highest view count using latest data)
                    if (currentViewCount > (bestLatestTweet?.ViewCount ?? bestTweet.ViewCount))
                    {
                        bestTweet = tweet;
                        bestLatestTweet = latestTweetData;
                    }
                }

                // Apply bonus credits daily limit (500 credits max)
                totalBonusCredits = Math.Min(totalBonusCredits, _state.State.Config.MaxDailyCreditsPerUser);
                
                                 // Check if user already has a reward record from regular credits
                 if (userRewardDict.ContainsKey(userId))
                 {
                     // Update existing record with bonus credits
                     var existingRecord = userRewardDict[userId];
                     existingRecord.BonusCredits = totalBonusCredits;
                     existingRecord.BonusCreditsBeforeMultiplier = totalBonusCreditsBeforeMultiplier;
                     existingRecord.FinalCredits = existingRecord.RegularCredits + totalBonusCredits;
                     existingRecord.BaseCredits = totalBonusCreditsBeforeMultiplier; // Keep for backward compatibility
                     existingRecord.ShareLinkMultiplier = (bestLatestTweet?.HasValidShareLink ?? bestTweet.HasValidShareLink) ? _state.State.Config.ShareLinkMultiplier : 1.0;
                     existingRecord.HasValidShareLink = bestLatestTweet?.HasValidShareLink ?? bestTweet.HasValidShareLink;
                     existingRecord.TweetCount += tweets.Count; // Add bonus tweet count
                     
                     // Keep the existing best tweet ID for reference (regular credits already set the best tweet)
                     
                     _logger.LogInformation("Updated user {UserId} (@{UserHandle}) with bonus credits: {RegularCredits} regular + {BonusCredits} bonus = {FinalCredits} total credits",
                         userId, latestUserInfo.Username, existingRecord.RegularCredits, totalBonusCredits, existingRecord.FinalCredits);
                 }
                else
                {
                    // Create new record for bonus credits only (user has no regular credits)
                    var rewardRecord = new UserRewardRecordDto
                    {
                        UserId = userId,
                        UserHandle = latestUserInfo.Username,
                        TweetId = bestTweet.TweetId, // Use best tweet for reference
                        RewardDate = rewardDate,
                        RewardDateUtc = ((DateTimeOffset)rewardDate).ToUnixTimeSeconds(),
                        BaseCredits = totalBonusCreditsBeforeMultiplier, // Keep for backward compatibility
                        ShareLinkMultiplier = (bestLatestTweet?.HasValidShareLink ?? bestTweet.HasValidShareLink) ? _state.State.Config.ShareLinkMultiplier : 1.0,
                        FinalCredits = totalBonusCredits,
                        HasValidShareLink = bestLatestTweet?.HasValidShareLink ?? bestTweet.HasValidShareLink,
                        IsRewardSent = false,
                        
                        // New separated credit fields
                        RegularCredits = 0, // No regular credits for this user
                        BonusCredits = totalBonusCredits,
                        TweetCount = tweets.Count,
                        BonusCreditsBeforeMultiplier = totalBonusCreditsBeforeMultiplier
                    };

                    userRewardDict[userId] = rewardRecord;
                    processedUsers++;

                    _logger.LogInformation("Calculated bonus-only rewards for user {UserId} (@{UserHandle}): {BonusCredits} bonus credits " +
                        "({TweetCount} tweets, best tweet: {TweetId} with {Views} views)", 
                        userId, latestUserInfo.Username, totalBonusCredits, tweets.Count, bestTweet.TweetId, 
                        bestLatestTweet?.ViewCount ?? bestTweet.ViewCount);
                }

                // Mark all tweets as processed
                foreach (var tweet in tweets)
                {
                    tweet.IsProcessed = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating bonus reward for user {UserId}", userTweets.Key);
            }
        }

        _logger.LogInformation("Reward calculation summary: {ProcessedUsers} users processed with rewards, {SkippedAlreadyRewarded} tweets skipped (already rewarded)", 
            processedUsers, skippedAlreadyRewarded);

        // Combine new rewards with existing rewards for return
        var allRewards = new List<UserRewardRecordDto>();
        allRewards.AddRange(existingRewards);
        allRewards.AddRange(userRewardDict.Values);
        
        return allRewards;
    }

    /// <summary>
    /// Get latest user and tweet information for reward calculation
    /// </summary>
    /// <param name="userTweets">User tweets grouped by user ID</param>
    /// <returns>Dictionary of user ID to updated tweet information</returns>
    private async Task<Dictionary<string, (UserInfoDto UserInfo, List<TweetProcessResultDto> UpdatedTweets)>> GetLatestUserAndTweetInfoAsync(
        Dictionary<string, List<TweetRecord>> userTweets)
    {
        var result = new Dictionary<string, (UserInfoDto UserInfo, List<TweetProcessResultDto> UpdatedTweets)>();
        
        foreach (var kvp in userTweets)
        {
            var userId = kvp.Key;
            var tweets = kvp.Value;
            
            try
            {
                _logger.LogInformation("Fetching latest information for user {UserId} with {TweetCount} tweets", userId, tweets.Count);
                
                // Get latest user information (follower count, etc.)
                var userInfoResult = await _twitterInteractionGrain!.GetUserInfoAsync(userId);
                if (!userInfoResult.IsSuccess)
                {
                    _logger.LogWarning("Failed to get user info for {UserId}: {Error}", userId, userInfoResult.ErrorMessage);
                    continue;
                }
                
                // Get latest tweet information (view count, etc.) - limit to 10 tweets per user
                var tweetIds = tweets.Take(10).Select(t => t.TweetId).ToList();
                var tweetInfoResult = await _twitterInteractionGrain!.BatchAnalyzeTweetsAsync(tweetIds);
                if (!tweetInfoResult.IsSuccess)
                {
                    _logger.LogWarning("Failed to get tweet info for user {UserId}: {Error}", userId, tweetInfoResult.ErrorMessage);
                    continue;
                }
                
                result[userId] = (userInfoResult.Data, tweetInfoResult.Data);
                
                _logger.LogInformation("Successfully fetched latest info for user {UserId} (@{Handle}): {FollowerCount} followers, {TweetCount} tweets analyzed", 
                    userId, userInfoResult.Data.Username, userInfoResult.Data.FollowersCount, tweetInfoResult.Data.Count);
                
                // Add a small delay to avoid hitting API rate limits
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching latest information for user {UserId}", userId);
            }
        }
        
        return result;
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
                if (reward.IsRewardSent) continue;
                // TODO: Implement actual credit distribution using IGrainWithStringKey structure
                // This follows the architecture constraint: not using IChatManagerGAgent : IGAgent
                // Focus on calculation and recording reward amounts, actual sending will be implemented later
                
                _logger.LogInformation("TODO: Send {Credits} credits to user {UserId} for tweet {TweetId} (calculation complete)", 
                    reward.FinalCredits, reward.UserId, reward.TweetId);

                // Mark as processed for calculation purposes, actual sending pending
                reward.IsRewardSent = true; // Set to false as per development phase requirements
                reward.RewardSentTime = DateTime.UtcNow; // No actual sending yet
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

            // Keep only recent history (last 7 records, sorted by time)
            _state.State.CalculationHistory = _state.State.CalculationHistory
                .OrderByDescending(h => h.CalculationDateUtc)
                .Take(7)
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

/// <summary>
/// Comparer for RewardTierDto to support configuration comparison
/// </summary>
public class RewardTierDtoComparer : IEqualityComparer<RewardTierDto>
{
    public bool Equals(RewardTierDto? x, RewardTierDto? y)
    {
        if (x == null && y == null) return true;
        if (x == null || y == null) return false;
        
        return x.MinViews == y.MinViews &&
               x.MinFollowers == y.MinFollowers &&
               x.RewardCredits == y.RewardCredits &&
               x.TierName == y.TierName;
    }

    public int GetHashCode(RewardTierDto obj)
    {
        if (obj == null) return 0;
        
        return HashCode.Combine(
            obj.MinViews,
            obj.MinFollowers,
            obj.RewardCredits,
            obj.TierName);
    }
} 