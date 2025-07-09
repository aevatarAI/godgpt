using Orleans;
using Orleans.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aevatar.Application.Grains.TwitterInteraction.Dtos;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Application.Grains.Twitter;
using RewardTierDto = Aevatar.Application.Grains.TwitterInteraction.Dtos.RewardTierDto;

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
    //private IInvitationGAgent _invitationAgent;
    
    // Removed IChatManagerGAgent to comply with architecture constraint
    
    private const string REMINDER_NAME = "RewardCalculationReminder";

    public TwitterRewardGrain(
        ILogger<TwitterRewardGrain> logger,
        IOptionsMonitor<TwitterRewardOptions> options,
        [PersistentState("twitterRewardState", "PubSubStore")] IPersistentState<TwitterRewardState> state)
    {
        _logger = logger;
        _options = options;
        _state = state;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation($"TwitterRewardGrain {this.GetPrimaryKeyString()} activating");

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
            TimeRangeStartHours =
                _options.CurrentValue.TimeOffsetMinutes / 60 + (_options.CurrentValue.TimeOffsetMinutes % 60 == 0
                    ? 0
                    : 1), // Convert minutes to hours
            TimeRangeEndHours =
                _options.CurrentValue.TimeWindowMinutes / 60 + (_options.CurrentValue.TimeOffsetMinutes % 60 == 0
                    ? 0
                    : 1), // Convert minutes to hours
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
            _logger.LogInformation($"TwitterRewardGrain Configuration changed, updating from appsettings.json. " +
                $"TimeRange: {currentConfig.TimeRangeStartHours}-{currentConfig.TimeRangeEndHours}h, MaxDailyCredits: {currentConfig.MaxDailyCreditsPerUser}, ShareMultiplier: {currentConfig.ShareLinkMultiplier}, MinViews: {currentConfig.MinViewsForReward}, RewardTiers: {currentConfig.RewardTiers.Count}");

            // Clean up any existing reminder first (to avoid conflicts with new configuration)
            try
            {
                var existingReminder = await this.GetReminder(REMINDER_NAME);
                if (existingReminder != null)
                {
                    _logger.LogInformation($"TwitterRewardGrain Cleaning up existing reminder due to configuration change");
                    await this.UnregisterReminder(existingReminder);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"TwitterRewardGrain Reminder doesn't exist");
                // Reminder doesn't exist, which is fine
            }
            
            // If reward calculation is running, register reminder with new configuration
            if (_state.State.IsRunning)
            {
                _logger.LogInformation($"TwitterRewardGrain Restarting reward calculation with new configuration");
                var nextTriggerTime = GetNextRewardTriggerTimeUtc();
                var timeUntilNextTrigger = nextTriggerTime - DateTime.UtcNow;
                await this.RegisterOrUpdateReminder(
                    REMINDER_NAME,
                    timeUntilNextTrigger,
                    TimeSpan.FromHours(4));
            }
        }

        // Update reminder target ID based on configuration version
        _state.State.ReminderTargetId = _options.CurrentValue.ReminderTargetIdVersion;

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

            _logger.LogInformation($"TwitterRewardGrain State consistency check - IsRunning: {_state.State.IsRunning}, HasReminder: {hasReminder}");

            // Case 1: Should be running but no reminder - register it
            if (_state.State.IsRunning && !hasReminder)
            {
                _logger.LogWarning($"TwitterRewardGrain ⚠️ Inconsistent state detected: IsRunning=true but no reminder found. Registering reminder...");
                
                var nextTriggerTime = GetNextRewardTriggerTimeUtc();
                var timeUntilNextTrigger = nextTriggerTime - DateTime.UtcNow;
                await this.RegisterOrUpdateReminder(
                    REMINDER_NAME,
                    timeUntilNextTrigger,
                    TimeSpan.FromHours(4));
                    
                _logger.LogInformation($"TwitterRewardGrain ✅ Reminder registered to match IsRunning=true state, next execution at {nextTriggerTime} UTC");
            }
            // Case 2: Should not be running but has reminder - unregister it
            else if (!_state.State.IsRunning && hasReminder)
            {
                _logger.LogWarning($"TwitterRewardGrain ⚠️ Inconsistent state detected: IsRunning=false but reminder exists. Cleaning up reminder...");
                var reminder = await this.GetReminder(REMINDER_NAME);
                if (reminder != null)
                {
                    await this.UnregisterReminder(reminder);
                }
                _logger.LogInformation($"TwitterRewardGrain ✅ Reminder cleaned up to match IsRunning=false state");
            }
            // Case 3: States are consistent
            else
            {
                _logger.LogInformation($"TwitterRewardGrain ✅ State consistency verified - no action needed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"TwitterRewardGrain ❌ Error during state consistency check");
        }
    }

    public async Task<TwitterApiResultDto<bool>> StartRewardCalculationAsync()
    {
        try
        {
            _logger.LogInformation($"Starting reward calculation with 4-hour intervals");

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
            var nextTriggerTime = GetNextRewardTriggerTimeUtc();
            _state.State.NextScheduledCalculation = nextTriggerTime;
            _state.State.NextScheduledCalculationUtc = ((DateTimeOffset)nextTriggerTime).ToUnixTimeSeconds();
            
            await _state.WriteStateAsync();

            // Register reminder for execution every 4 hours
            var timeUntilNextTrigger = nextTriggerTime - DateTime.UtcNow;
            var reminder = await this.RegisterOrUpdateReminder(
                REMINDER_NAME,
                timeUntilNextTrigger,
                TimeSpan.FromHours(4)); // Repeat every 4 hours

            _logger.LogInformation($"Reward calculation started with 4-hour intervals, next execution at {nextTriggerTime} UTC");

            return new TwitterApiResultDto<bool>
            {
                IsSuccess = true,
                Data = true,
                ErrorMessage = $"Reward calculation scheduled for {nextTriggerTime:yyyy-MM-dd HH:mm:ss} UTC"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error starting reward calculation");
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
            _logger.LogInformation($"Stopping reward calculation");

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

            _logger.LogInformation($"Reward calculation stopped");

            return new TwitterApiResultDto<bool>
            {
                IsSuccess = true,
                Data = true,
                ErrorMessage = "Reward calculation stopped successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error stopping reward calculation");
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
            _logger.LogError(ex, $"Error getting reward calculation status");
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
            _logger.LogInformation($"Manual reward calculation triggered for date {targetDate.ToString("yyyy-MM-dd")}");
            
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
            _logger.LogError(ex, $"Error starting manual reward calculation");
            return new TwitterApiResultDto<bool>
            {
                IsSuccess = false,
                Data = false, // Task failed to start
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<TwitterApiResultDto<List<RewardCalculationHistoryDto>>> GetRewardCalculationHistoryAsync(int days = 7)
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
            _logger.LogError(ex, $"Error getting calculation history");
            return new TwitterApiResultDto<List<RewardCalculationHistoryDto>>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new List<RewardCalculationHistoryDto>()
            };
        }
    }

    public async Task<TwitterApiResultDto<List<UserRewardRecordDto>>> GetUserRewardRecordsAsync(string userId, int days = 7)
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
            _logger.LogError(ex, $"Error getting user reward records for user {userId}");
            return new TwitterApiResultDto<List<UserRewardRecordDto>>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new List<UserRewardRecordDto>()
            };
        }
    }

    public async Task<TwitterApiResultDto<Dictionary<string, List<UserRewardRecordDto>>>> GetUserRewardsByUserIdAsync(string userId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return new TwitterApiResultDto<Dictionary<string, List<UserRewardRecordDto>>>
                {
                    IsSuccess = false,
                    ErrorMessage = "User ID cannot be empty",
                    Data = new Dictionary<string, List<UserRewardRecordDto>>()
                };
            }

            _logger.LogInformation($"Getting user rewards for user ID {userId}");

            var result = new Dictionary<string, List<UserRewardRecordDto>>();

            // Iterate through all date keys in UserRewards
            foreach (var kvp in _state.State.UserRewards)
            {
                var dateKey = kvp.Key;
                var dayRewards = kvp.Value;

                // Filter rewards for the specified user
                var userDayRewards = dayRewards
                    .Where(r => r.UserId == userId)
                    .ToList();

                // Only add to result if user has rewards for this date
                if (userDayRewards.Count > 0)
                {
                    result[dateKey] = userDayRewards;
                }
            }

            _logger.LogInformation($"Found user rewards for {result.Count} dates for user ID {userId}");

            return new TwitterApiResultDto<Dictionary<string, List<UserRewardRecordDto>>>
            {
                IsSuccess = true,
                Data = result
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting user rewards for user ID {userId}");
            return new TwitterApiResultDto<Dictionary<string, List<UserRewardRecordDto>>>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = new Dictionary<string, List<UserRewardRecordDto>>()
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
                ShareLinkBonusTotal = rewards.Where(r => r.HasValidShareLink).Sum(r => r.BonusCredits - r.BonusCreditsBeforeMultiplier)
            };

            // Generate reward tier statistics
            foreach (var tier in _state.State.Config.RewardTiers)
            {
                var tierRewards = rewards.Where(r => r.BonusCreditsBeforeMultiplier == tier.RewardCredits).ToList();
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
            _logger.LogError(ex, $"Error generating daily statistics for {targetDate.ToString("yyyy-MM-dd")}");
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
            var nextTriggerTime = GetNextRewardTriggerTimeUtc();
            
            var status = new TimeControlStatusDto
            {
                CurrentUtcTime = currentUtc,
                CurrentUtcTimestamp = ((DateTimeOffset)currentUtc).ToUnixTimeSeconds(),
                LastRewardCalculationTime = _state.State.LastCalculationTime ?? DateTime.MinValue,
                LastRewardCalculationTimestamp = _state.State.LastCalculationTimeUtc,
                NextRewardCalculationTime = nextTriggerTime,
                NextRewardCalculationTimestamp = ((DateTimeOffset)nextTriggerTime).ToUnixTimeSeconds(),
                TimeUntilNextCalculation = nextTriggerTime - currentUtc,
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
        if (reminderName == REMINDER_NAME)
        {
            // Check for state inconsistency and auto-correct if needed
            if (!_state.State.IsRunning)
            {
                _logger.LogWarning("TwitterRewardGrain ⚠️ Reminder triggered but IsRunning=false. This indicates state inconsistency - auto-correcting...");
                _state.State.IsRunning = true;
                await _state.WriteStateAsync();
                _logger.LogInformation("TwitterRewardGrain ✅ State auto-corrected: IsRunning set to true to match active reminder");
            }
            
            try
            {
                _logger.LogInformation("Daily reward calculation reminder triggered at {CurrentTime} UTC", DateTime.UtcNow);
                
                // Process directly in the grain context instead of using Task.Run
                // This ensures proper Orleans activation context access
                await ProcessReceiveReminderInBackground();
                
                _logger.LogDebug("Daily reward calculation task completed");
                
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
        var nextTriggerTime = GetNextRewardTriggerTimeUtc();
        _state.State.NextScheduledCalculation = nextTriggerTime;
        _state.State.NextScheduledCalculationUtc = ((DateTimeOffset)nextTriggerTime).ToUnixTimeSeconds();
                
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

            var regularCreditsTweetsResult =
                await _tweetMonitorGrain!.QueryTweetsByTimeRangeAsync(
                    TimeRangeDto.FromDateTime(targetDate.AddDays(-1), targetDate));
            
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
                $"CreditsTweets Found {bonusCreditsTweets.Count} bonusTweets, {bonusCreditsEligibleTweets.Count} bonusEligible for bonusreward evaluation (original tweets, min {_state.State.Config.MinViewsForReward} views, non-excluded users, unprocessed)" +
                " Found {regularCreditsTweets.Count} regularTweets, {regularCreditsEligibleTweets.Count} regularEligible for regularreward ");
            

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
            
            // Clean up old reward records based on data retention policy (relative to target date)
            var cutoffDate = targetDate.AddDays(-_options.CurrentValue.DataRetentionDays);
            var cutoffDateKey = cutoffDate.ToString("yyyy-MM-dd");
            var keysToRemove = _state.State.UserRewards.Keys
                .Where(key => string.Compare(key, cutoffDateKey, StringComparison.Ordinal) < 0)
                .ToList();
            
            foreach (var keyToRemove in keysToRemove)
            {
                _state.State.UserRewards.Remove(keyToRemove);
            }
            
            if (keysToRemove.Count > 0)
            {
                _logger.LogInformation("Cleaned up {Count} old reward record dates (older than {CutoffDate}). Keeping {RetainedDates} recent dates: {KeptDates}", 
                    keysToRemove.Count, cutoffDateKey, _state.State.UserRewards.Keys.Count, string.Join(", ", _state.State.UserRewards.Keys.OrderBy(k => k)));
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

        // _logger.LogInformation(
        //     "Found {ExistingCount} existing user rewards for date {Date}, processing bonusCreditsType {EligibleCount} regularCreditsType {regularEligibleCount} eligible tweets",
        //     existingRewards.Count, dateKey, bonusCreditsEligibleTweets.Count, regularCreditsEligibleTweets.Count);

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
        // Apply batch processing to prevent API rate limiting
        var options = _options.CurrentValue;
        var totalBonusUsers = bonusCreditsTweetsByUser.Count;
        var batchSize = options.RewardCalculationBatchSize;
        
        _logger.LogInformation($"Fetching latest information for {totalBonusUsers} bonus credit users using batch processing (batch size: {batchSize})");
        
        var latestUserAndTweetInfo = new Dictionary<string, (UserInfoDto UserInfo, List<TweetProcessResultDto> UpdatedTweets)>();
        
        // Process users in batches to respect API limits
        var userBatches = bonusCreditsTweetsByUser
            .Select((kvp, index) => new { kvp.Key, kvp.Value, Index = index })
            .GroupBy(x => x.Index / batchSize)
            .Select(g => g.ToDictionary(x => x.Key, x => x.Value))
            .ToList();
        
        for (int batchIndex = 0; batchIndex < userBatches.Count; batchIndex++)
        {
            var batch = userBatches[batchIndex];
            var isLastBatch = batchIndex == userBatches.Count - 1;
            
            _logger.LogInformation($"Processing batch {batchIndex + 1}/{userBatches.Count} with {batch.Count} users");
            
            var batchResult = await GetLatestUserAndTweetInfoAsync(batch);
            
            // Merge batch results
            foreach (var kvp in batchResult)
            {
                latestUserAndTweetInfo[kvp.Key] = kvp.Value;
            }
            
            // Apply inter-batch delay to ensure API safety (except for last batch)
            if (!isLastBatch)
            {
                var interBatchDelayMinutes = options.MinTimeWindowMinutes;
                _logger.LogInformation($"Completed batch {batchIndex + 1}/{userBatches.Count}. Applying {interBatchDelayMinutes}-minute inter-batch delay for API safety");
                await Task.Delay(TimeSpan.FromMinutes(interBatchDelayMinutes));
            }
            else
            {
                _logger.LogInformation($" Completed final batch {batchIndex + 1}/{userBatches.Count}. No inter-batch delay needed.");
            }
        }

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

                // _logger.LogInformation("Calculated regular rewards for user {UserId} (@{UserHandle}): {RegularCredits} regular credits " +
                //     "({TweetCount} tweets, reference tweet: {TweetId}) [USING STORED DATA]", 
                //     userId, recordTweet.AuthorHandle, regularCredits, regularTweetCount, recordTweet.TweetId);
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
                
                // Use latest information if available, otherwise use stored data
                UserInfoDto userInfo;
                List<TweetProcessResultDto> tweetInfo;
                bool usingStoredData = false;
                
                if (latestUserAndTweetInfo.ContainsKey(userId))
                {
                    (userInfo, tweetInfo) = latestUserAndTweetInfo[userId];
                }
                else
                {
                    // Fallback to stored data to ensure user doesn't lose rewards
                    usingStoredData = true;
                    userInfo = new UserInfoDto
                    {
                        UserId = userId,
                        Username = tweets.First().AuthorHandle,
                        FollowersCount = tweets.First().FollowerCount // Use stored follower count
                    };
                    
                    tweetInfo = tweets.Select(t => new TweetProcessResultDto
                    {
                        TweetId = t.TweetId,
                        ViewCount = t.ViewCount, // Use stored view count
                        HasValidShareLink = t.HasValidShareLink,
                        FollowerCount = t.FollowerCount
                    }).ToList();
                    
                    _logger.LogWarning("⚠️ Using stored data for user {UserId} - API calls failed but ensuring user gets rewards", userId);
                }
                
                // Calculate bonus credits based on 8-tier system using available data (latest or stored)
                var totalBonusCredits = 0;
                var totalBonusCreditsBeforeMultiplier = 0;
                var bestTweet = tweets.First(); // Use first tweet for record keeping
                var bestLatestTweet = tweetInfo.FirstOrDefault();
                
                // Create a mapping from tweet ID to tweet info
                var tweetIdToLatestInfo = tweetInfo.ToDictionary(t => t.TweetId, t => t);
                
                foreach (var tweet in tweets)
                {
                    // Get latest tweet information if available
                    var latestTweetData = tweetIdToLatestInfo.ContainsKey(tweet.TweetId) 
                        ? tweetIdToLatestInfo[tweet.TweetId] 
                        : null;
                    
                    // Use available view count (latest or stored)
                    var currentViewCount = latestTweetData?.ViewCount ?? 0;
                    var currentFollowerCount = userInfo.FollowersCount;
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
                     existingRecord.ShareLinkMultiplier = (bestLatestTweet?.HasValidShareLink ?? bestTweet.HasValidShareLink) ? _state.State.Config.ShareLinkMultiplier : 1.0;
                     existingRecord.HasValidShareLink = bestLatestTweet?.HasValidShareLink ?? bestTweet.HasValidShareLink;
                     existingRecord.TweetCount += tweets.Count; // Add bonus tweet count
                     
                     // Keep the existing best tweet ID for reference (regular credits already set the best tweet)
                     
                     var dataSource = usingStoredData ? "[STORED DATA]" : "[LATEST DATA]";
                     _logger.LogInformation($"Updated user {userId} (@{userInfo.Username}) with bonus credits: {existingRecord.RegularCredits} regular + {totalBonusCredits} bonus = {existingRecord.FinalCredits} total credits {dataSource}");
                 }
                else
                {
                    // Create new record for bonus credits only (user has no regular credits)
                    var rewardRecord = new UserRewardRecordDto
                    {
                        UserId = userId,
                        UserHandle = userInfo.Username,
                        TweetId = bestTweet.TweetId, // Use best tweet for reference
                        RewardDate = rewardDate,
                        RewardDateUtc = ((DateTimeOffset)rewardDate).ToUnixTimeSeconds(),
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

                    // var dataSource = usingStoredData ? "[STORED DATA]" : "[LATEST DATA]";
                    // _logger.LogInformation("Calculated bonus-only rewards for user {UserId} (@{UserHandle}): {BonusCredits} bonus credits " +
                    //     "({TweetCount} tweets, best tweet: {TweetId} with {Views} views) {DataSource}", 
                    //     userId, userInfo.Username, totalBonusCredits, tweets.Count, bestTweet.TweetId, 
                    //     bestLatestTweet?.ViewCount ?? bestTweet.ViewCount, dataSource);
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

        _logger.LogInformation($"Reward calculation summary: {processedUsers} users processed with rewards, {skippedAlreadyRewarded} tweets skipped (already rewarded)");

        // Combine new rewards with existing rewards for return
        var allRewards = new List<UserRewardRecordDto>();
        allRewards.AddRange(existingRewards);
        allRewards.AddRange(userRewardDict.Values);
        
        return allRewards;
    }

    /// <summary>
    /// Get latest user and tweet information for reward calculation using RefetchTweetsByTimeRangeAsync pattern
    /// Applies intelligent delay strategy with retry mechanism to ensure no user rewards are lost
    /// </summary>
    /// <param name="userTweets">User tweets grouped by user ID</param>
    /// <returns>Dictionary of user ID to updated tweet information</returns>
    private async Task<Dictionary<string, (UserInfoDto UserInfo, List<TweetProcessResultDto> UpdatedTweets)>> GetLatestUserAndTweetInfoAsync(
        Dictionary<string, List<TweetRecord>> userTweets)
    {
        var result = new Dictionary<string, (UserInfoDto UserInfo, List<TweetProcessResultDto> UpdatedTweets)>();
        var options = _options.CurrentValue;
        var userCount = 0;
        var totalUsers = userTweets.Count;
        var failedUsers = new List<string>(); // Track failed users for retry
        
        _logger.LogInformation("🚀 Starting reward calculation user processing using RefetchTweetsByTimeRangeAsync pattern. Total users: {TotalUsers}", totalUsers);
        
        foreach (var kvp in userTweets)
        {
            var userId = kvp.Key;
            var tweets = kvp.Value;
            userCount++;
            
            try
            {
                _logger.LogInformation("📅 Processing user {UserCount}/{TotalUsers}: {UserId} with {TweetCount} tweets", 
                    userCount, totalUsers, userId, tweets.Count);
                
                // 1. Get latest user information (follower count, etc.) - ONE API call per user
                var userInfoResult = await _twitterInteractionGrain!.GetUserInfoAsync(userId);
                if (!userInfoResult.IsSuccess)
                {
                    _logger.LogWarning("⚠️ Failed to get user info for {UserId}: {Error}. Adding to retry queue.", userId, userInfoResult.ErrorMessage);
                    failedUsers.Add(userId);
                    
                    // Apply error delay but don't skip user - will retry later
                    _logger.LogInformation("🛡️ API error detected. Applying mandatory {DelayMinutes}-minute delay for API safety", 
                        options.MinTimeWindowMinutes);
                    await Task.Delay(TimeSpan.FromMinutes(options.MinTimeWindowMinutes));
                    continue;
                }
                
                // 2. Get latest tweet information (view count, etc.) using LIGHTWEIGHT method
                // This avoids duplicate GetUserInfoAsync calls - limit to BatchFetchSize tweets per user
                var tweetIds = tweets.Take(options.BatchFetchSize).Select(t => t.TweetId).ToList();
                var tweetInfoResult = await _twitterInteractionGrain!.BatchAnalyzeTweetsLightweightAsync(tweetIds);
                if (!tweetInfoResult.IsSuccess)
                {
                    _logger.LogWarning("⚠️ Failed to get tweet info for user {UserId}: {Error}. Adding to retry queue.", userId, tweetInfoResult.ErrorMessage);
                    failedUsers.Add(userId);
                    
                    // Apply error delay but don't skip user - will retry later
                    _logger.LogInformation("🛡️ API error detected. Applying mandatory {DelayMinutes}-minute delay for API safety", 
                        options.MinTimeWindowMinutes);
                    await Task.Delay(TimeSpan.FromMinutes(options.MinTimeWindowMinutes));
                    continue;
                }
                
                // 3. Update tweet results with user follower count (from step 1)
                foreach (var tweet in tweetInfoResult.Data)
                {
                    tweet.FollowerCount = userInfoResult.Data.FollowersCount;
                }
                
                result[userId] = (userInfoResult.Data, tweetInfoResult.Data);
                
                _logger.LogInformation("✅ Successfully fetched latest info for user {UserId} (@{Handle}): {FollowerCount} followers, {TweetCount} tweets analyzed", 
                    userId, userInfoResult.Data.Username, userInfoResult.Data.FollowersCount, tweetInfoResult.Data.Count);
                
                // Optimized delay strategy for reward calculation: Use shorter delays to balance speed and API safety
                var hasData = tweetInfoResult.Data.Count > 0;
                var isLastUser = userCount >= totalUsers;
                
                if (isLastUser)
                {
                    _logger.LogInformation("🎉 Completed processing last user {UserCount}/{TotalUsers}. No delay needed.", userCount, totalUsers);
                }
                else if (!hasData)
                {
                    // Priority 2 - No data found, skip delay for efficiency
                    _logger.LogInformation("⚡ No tweet data found for user {UserId}. Skipping delay and proceeding immediately", userId);
                }
                else
                {
                    // Priority 3 - Data found, use shorter delay for reward calculation efficiency
                    // Use TweetProcessingDelayMs (3 seconds) instead of MinTimeWindowMinutes (15 minutes) for better balance
                    var delayMs = options.TweetProcessingDelayMs;
                    _logger.LogInformation("⏱️ Data found for user {UserId}. Applying {DelaySeconds}-second delay for API rate limiting", 
                        userId, delayMs / 1000);
                    await Task.Delay(delayMs);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error fetching latest information for user {UserId}. Adding to retry queue.", userId);
                failedUsers.Add(userId);
                
                // Apply error delay for API safety
                _logger.LogInformation("🛡️ Exception occurred. Applying mandatory {DelayMinutes}-minute delay for API safety", 
                    options.MinTimeWindowMinutes);
                await Task.Delay(TimeSpan.FromMinutes(options.MinTimeWindowMinutes));
            }
        }
        
        // Retry failed users to ensure no rewards are lost
        if (failedUsers.Count > 0)
        {
            _logger.LogWarning("🔄 Retrying {FailedCount} failed users to ensure no rewards are lost: {FailedUsers}", 
                failedUsers.Count, string.Join(", ", failedUsers));
                
            foreach (var userId in failedUsers)
            {
                if (result.ContainsKey(userId)) continue; // Skip if already processed successfully
                
                try
                {
                    var tweets = userTweets[userId];
                    _logger.LogInformation("🔄 Retry attempt for user {UserId} with {TweetCount} tweets", userId, tweets.Count);
                    
                    // Retry with longer delay between attempts
                    await Task.Delay(TimeSpan.FromMinutes(options.MinTimeWindowMinutes));
                    
                    // 1. Retry user information
                    var userInfoResult = await _twitterInteractionGrain!.GetUserInfoAsync(userId);
                    if (!userInfoResult.IsSuccess)
                    {
                        _logger.LogError("🚫 Retry failed for user {UserId} user info: {Error}. User will be processed with stored data only.", 
                            userId, userInfoResult.ErrorMessage);
                        continue;
                    }
                    
                    // 2. Retry tweet information
                    var tweetIds = tweets.Take(options.BatchFetchSize).Select(t => t.TweetId).ToList();
                    var tweetInfoResult = await _twitterInteractionGrain!.BatchAnalyzeTweetsLightweightAsync(tweetIds);
                    if (!tweetInfoResult.IsSuccess)
                    {
                        _logger.LogError("🚫 Retry failed for user {UserId} tweet info: {Error}. User will be processed with stored data only.", 
                            userId, tweetInfoResult.ErrorMessage);
                        continue;
                    }
                    
                    // 3. Success on retry
                    foreach (var tweet in tweetInfoResult.Data)
                    {
                        tweet.FollowerCount = userInfoResult.Data.FollowersCount;
                    }
                    
                    result[userId] = (userInfoResult.Data, tweetInfoResult.Data);
                    _logger.LogInformation("✅ Retry successful for user {UserId} (@{Handle}): {FollowerCount} followers, {TweetCount} tweets", 
                        userId, userInfoResult.Data.Username, userInfoResult.Data.FollowersCount, tweetInfoResult.Data.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "🚫 Retry exception for user {UserId}. User will be processed with stored data only.", userId);
                }
            }
        }
        
        _logger.LogInformation("🎯 Completed reward calculation user processing. Processed: {ProcessedUsers}/{TotalUsers} users, Failed: {FailedUsers}", 
            result.Count, totalUsers, failedUsers.Count - result.Count);
        
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
                var bindingGrain = GrainFactory.GetGrain<ITwitterIdentityBindingGAgent>(CommonHelper.StringToGuid(reward.UserId));
                

                if (bindingGrain == null)
                {
                    _logger.LogInformation($"SendRewardsToUsersAsync no binding info 1, continue FinalCredits={reward.FinalCredits} credits to twitter_userId={reward.UserId}");

                    continue;
                }
                var userId = await bindingGrain.GetUserIdAsync();
                
                if (userId == null || userId == Guid.Empty)
                {
                    _logger.LogInformation($"SendRewardsToUsersAsync no binding info 2, continue FinalCredits={reward.FinalCredits} credits to twitter_userId={reward.UserId}");

                    continue;
                }
                
                _logger.LogInformation($"SendRewardsToUsersAsync binding info, continue FinalCredits={reward.FinalCredits} credits to twitter_userId={reward.UserId}  userId={userId}");
                
                var invitationAgent = GrainFactory.GetGrain<IInvitationGAgent>(userId.Value);
                
                await invitationAgent.ProcessTwitterRewardAsync(reward.TweetId, reward.FinalCredits);
                
                _logger.LogInformation($"SendRewardsToUsersAsync ProcessTwitterRewardAsync FinalCredits={reward.FinalCredits} credits to twitter_userId={reward.UserId} for tweetId={reward.TweetId} userId={userId} result={invitationAgent}");

                
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

    private DateTime GetNextRewardTriggerTimeUtc()
    {
        var now = DateTime.UtcNow;
        
        // Define trigger times: 0:10, 4:10, 8:10, 12:10, 16:10, 20:10
        var triggerHours = new[] { 0, 4, 8, 12, 16, 20 };
        
        var today = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        
        // Find the next trigger time today
        foreach (var hour in triggerHours)
        {
            var triggerTime = today.AddHours(hour).AddMinutes(10);
            if (now < triggerTime)
                return triggerTime;
        }
        
        // If all today's triggers have passed, return tomorrow's first trigger (0:10)
        return today.AddDays(1).AddMinutes(10);
    }

    private bool ShouldExecuteRewardCalculation(DateTime currentTime)
    {
        // Execute at 00:00 UTC daily
        return currentTime.Hour == 0 && currentTime.Minute < 5; // 5-minute window
    }

    public Task<List<RewardCalculationHistoryDto>> GetCalculationHistoryListAsync()
    {
        return Task.FromResult(_state.State.CalculationHistory);
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