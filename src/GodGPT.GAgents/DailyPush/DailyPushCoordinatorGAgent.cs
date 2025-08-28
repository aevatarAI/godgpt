using Aevatar.Core.Abstractions;
using Aevatar.Core;
using Aevatar.Application.Grains.Agents.ChatManager;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Providers;
using GodGPT.GAgents.DailyPush.SEvents;
using GodGPT.GAgents.DailyPush.Options;


namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Timezone-specific push scheduler implementation
/// </summary>
[StorageProvider(ProviderName = "PubSubStore")]
[LogConsistencyProvider(ProviderName = "LogStorage")]
[GAgent(nameof(DailyPushCoordinatorGAgent))]
public class DailyPushCoordinatorGAgent : GAgentBase<DailyPushCoordinatorState, DailyPushLogEvent>, 
    IDailyPushCoordinatorGAgent, IRemindable
{
    private readonly ILogger<DailyPushCoordinatorGAgent> _logger;
    private readonly IGrainFactory _grainFactory;
    private readonly IOptionsMonitor<DailyPushOptions> _options;
    private string _timeZoneId = "";
    
    // Reminder constants
    private const string MORNING_REMINDER = "MorningPush";
    private const string AFTERNOON_REMINDER = "AfternoonRetry";
    
    // Test mode constants - TODO: Remove before production
    private static class TestModeConstants
    {
        public const string TEST_PUSH_REMINDER = "QA_TEST_PUSH_V2";
        public const string TEST_RETRY_REMINDER = "QA_TEST_RETRY_V2";
        
        public static readonly TimeSpan PUSH_INTERVAL = TimeSpan.FromMinutes(10);    // Push every 10 minutes
        public static readonly TimeSpan RETRY_DELAY = TimeSpan.FromMinutes(5);       // 5 minutes retry delay
        public const int MAX_TEST_ROUNDS = 6;                                        // Maximum 6 test rounds
    }
    
    // Tolerance window for time-based execution (±5 minutes)
    private readonly TimeSpan _toleranceWindow = TimeSpan.FromMinutes(5);
    
    public DailyPushCoordinatorGAgent(
        ILogger<DailyPushCoordinatorGAgent> logger, 
        IGrainFactory grainFactory,
        IOptionsMonitor<DailyPushOptions> options)
    {
        _logger = logger;
        _grainFactory = grainFactory;
        _options = options;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Timezone scheduler for {State.TimeZoneId}");
    }
    
    protected sealed override void GAgentTransitionState(DailyPushCoordinatorState state, StateLogEventBase<DailyPushLogEvent> @event)
    {
        switch (@event)
        {
            case TestRoundCompletedEventLog testRoundEvent:
                state.TestRoundsCompleted = testRoundEvent.CompletedRound;
                state.LastUpdated = testRoundEvent.CompletionTime;
                break;
            case SchedulerStatusLogEvent statusEvent:
                _logger.LogDebug($"Scheduler status changed from {statusEvent.OldStatus} to {statusEvent.NewStatus}");
                break;
            default:
                _logger.LogDebug($"Unhandled event type: {@event.GetType().Name}");
                break;
        }
    }

    protected override async Task OnGAgentActivateAsync(CancellationToken cancellationToken)
    {
        // Note: Legacy string-key reminders will fail naturally and be cleaned up by Orleans over time
        // No manual cleanup needed - this avoids grain context issues
        
        // Get timezone ID from State (if already initialized)
        if (!string.IsNullOrEmpty(State.TimeZoneId))
        {
            _timeZoneId = State.TimeZoneId;
            _logger.LogInformation("DailyPushCoordinatorGAgent activated for timezone: {TimeZone} (from state)", _timeZoneId);
        }
        else
        {
            try
            {
                // Try to get timezone from GUID mapping
                var grainGuid = this.GetPrimaryKey();
                var inferredTimezone = await DailyPushConstants.GetTimezoneFromGuidAsync(grainGuid, _grainFactory);
                
                if (!string.IsNullOrEmpty(inferredTimezone))
                {
                    _logger.LogInformation("DailyPushCoordinatorGAgent activated for timezone: {TimeZone} (from GUID mapping)", inferredTimezone);
                    // Auto-initialize with inferred timezone
                    await InitializeAsync(inferredTimezone);
                }
                else
                {
                    // Compatibility: Try to infer timezone from common GUID patterns
                    var commonTimezone = TryInferTimezoneFromGuid(grainGuid);
                    if (!string.IsNullOrEmpty(commonTimezone))
                    {
                        _logger.LogInformation("DailyPushCoordinatorGAgent activated for timezone: {TimeZone} (inferred from common pattern)", commonTimezone);
                        await InitializeAsync(commonTimezone);
                    }
                    else
                    {
                        _logger.LogWarning("DailyPushCoordinatorGAgent activated but no timezone ID in state and no GUID mapping found. Grain will not be functional until InitializeAsync is called.");
                        _timeZoneId = ""; // Ensure it's empty, not null
                    }
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Unable to extract GUID key"))
            {
                // Handle legacy string-key grain instances
                _logger.LogWarning("Legacy string-key DailyPushCoordinatorGAgent detected. This grain instance will be inactive and cleaned up by Orleans over time. Error: {Error}", ex.Message);
                _timeZoneId = ""; // Set to empty to make grain inactive
                // Don't rethrow - let the grain activate but remain inactive
            }
        }
        
        // Validate timezone if we have one
        if (!string.IsNullOrEmpty(_timeZoneId))
        {
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(_timeZoneId);
            }
            catch (TimeZoneNotFoundException ex)
            {
                _logger.LogError(ex, "Invalid timezone ID: {TimeZone}", _timeZoneId);
                _timeZoneId = ""; // Reset to empty to prevent further errors
                throw new ArgumentException($"Invalid timezone ID: {_timeZoneId}", ex);
            }
            catch (InvalidTimeZoneException ex)
            {
                _logger.LogError(ex, "Invalid timezone format: {TimeZone}", _timeZoneId);
                _timeZoneId = ""; // Reset to empty to prevent further errors
                throw new ArgumentException($"Invalid timezone format: {_timeZoneId}", ex);
            }
        }
        
        // 🚀 Auto-activation: Use configured ReminderTargetId
        var configuredTargetId = _options.CurrentValue.ReminderTargetId;
        if (configuredTargetId != Guid.Empty && State.ReminderTargetId != configuredTargetId)
        {
            State.ReminderTargetId = configuredTargetId;
            State.LastUpdated = DateTime.UtcNow;
            
            _logger.LogInformation("Auto-activated timezone scheduler for {TimeZone} with ReminderTargetId: {TargetId}", 
                _timeZoneId, configuredTargetId);
        }
        
        // Try to register Orleans reminders if authorized
        await TryRegisterRemindersAsync();
    }

    public async Task InitializeAsync(string timeZoneId)
    {
        // Validate timezone first
        if (string.IsNullOrEmpty(timeZoneId))
        {
            throw new ArgumentException("Timezone ID cannot be null or empty", nameof(timeZoneId));
        }
        
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new ArgumentException($"Invalid timezone ID: {timeZoneId}", ex);
        }
        catch (InvalidTimeZoneException ex)
        {
            throw new ArgumentException($"Invalid timezone format: {timeZoneId}", ex);
        }
        
        // Register timezone mapping for reverse lookup
        await DailyPushConstants.RegisterTimezoneMapping(timeZoneId, _grainFactory);
        
        State.TimeZoneId = timeZoneId;
        State.Status = SchedulerStatus.Active;
        State.LastUpdated = DateTime.UtcNow;
        // Initialize with empty ReminderTargetId - requires explicit activation
        State.ReminderTargetId = Guid.Empty;
        
        _timeZoneId = timeZoneId;
        
        await ConfirmEvents();
        
        _logger.LogInformation("Initialized timezone scheduler for {TimeZone}", timeZoneId);
    }
    
    public async Task SetReminderTargetIdAsync(Guid targetId)
    {
        var oldTargetId = State.ReminderTargetId;
        State.ReminderTargetId = targetId;
        State.LastUpdated = DateTime.UtcNow;
        
        _logger.LogInformation("Updated ReminderTargetId for {TimeZone}: {Old} -> {New}", 
            _timeZoneId, oldTargetId, targetId);
        
        // If this instance is now authorized, register reminders
        if (State.ReminderTargetId == _options.CurrentValue.ReminderTargetId)
        {
            await TryRegisterRemindersAsync();
        }
        else
        {
            // Clean up existing reminders if no longer authorized
            await CleanupRemindersAsync();
        }
    }
    
    /// <summary>
    /// Start test mode with rapid push testing - TODO: Remove before production
    /// </summary>
    public async Task StartTestModeAsync(int intervalSeconds = 600)
    {
        
        if (State.TestModeActive)
        {
            _logger.LogWarning("Test mode already active for timezone {TimeZone}. Current rounds: {Rounds}/{MaxRounds}", 
                _timeZoneId, State.TestRoundsCompleted, TestModeConstants.MAX_TEST_ROUNDS);
            return;
        }
        
        // Validate interval (minimum 10 seconds, maximum 1 hour)
        intervalSeconds = Math.Max(10, Math.Min(intervalSeconds, 3600));
        var testInterval = TimeSpan.FromSeconds(intervalSeconds);
        
        _logger.LogInformation("Starting test mode for timezone {TimeZone} with {IntervalSeconds}s interval", 
            _timeZoneId, intervalSeconds);
        
        // Additional cleanup for safety
        await CleanupTestRemindersAsync();
        
        // Initialize test state with custom interval
        State.TestModeActive = true;
        State.TestStartTime = DateTime.UtcNow;
        State.TestRoundsCompleted = 0;
        State.TestCustomInterval = intervalSeconds; // Store custom interval
        State.LastUpdated = DateTime.UtcNow;
        
        await ConfirmEvents();
        
        // Register first test reminder with custom interval
        await this.RegisterOrUpdateReminder(
            TestModeConstants.TEST_PUSH_REMINDER,
            TimeSpan.FromSeconds(10), // Start in 10 seconds
            testInterval); // Use custom interval
            
        _logger.LogInformation("Test mode started for {TimeZone}. Max rounds: {MaxRounds}, Interval: {Interval}s", 
            _timeZoneId, TestModeConstants.MAX_TEST_ROUNDS, intervalSeconds);
    }
    
    /// <summary>
    /// Stop test mode and cleanup test reminders - TODO: Remove before production
    /// </summary>
    public async Task StopTestModeAsync()
    {
        if (!State.TestModeActive)
        {
            _logger.LogInformation("Test mode not active for timezone {TimeZone}", _timeZoneId);
            return;
        }
        
        _logger.LogInformation("Stopping test mode for timezone {TimeZone}. Completed {Rounds} rounds", 
            _timeZoneId, State.TestRoundsCompleted);
        
        // Cleanup test reminders
        await CleanupTestRemindersAsync();
        
        // Reset test state
        State.TestModeActive = false;
        State.TestStartTime = DateTime.MinValue;
        State.TestRoundsCompleted = 0;
        State.LastUpdated = DateTime.UtcNow;
        
        await ConfirmEvents();
        
        _logger.LogInformation("Test mode stopped and cleaned up for timezone {TimeZone}", _timeZoneId);
    }
    
    /// <summary>
    /// Get test mode status - TODO: Remove before production
    /// </summary>
    public async Task<(bool IsActive, DateTime StartTime, int RoundsCompleted, int MaxRounds)> GetTestStatusAsync()
    {
        return (State.TestModeActive, State.TestStartTime, State.TestRoundsCompleted, TestModeConstants.MAX_TEST_ROUNDS);
    }
    
    /// <summary>
    /// Compatibility method: Try to infer timezone from GUID for common timezones
    /// This helps with migration from string-key to GUID-key GAgents
    /// </summary>
    private string? TryInferTimezoneFromGuid(Guid grainGuid)
    {
        // Common timezones that might exist in production
        var commonTimezones = new[]
        {
            "Asia/Shanghai",
            "America/New_York", 
            "Europe/London",
            "Asia/Tokyo",
            "UTC",
            "America/Los_Angeles",
            "Europe/Paris",
            "Asia/Seoul"
        };
        
        foreach (var timezone in commonTimezones)
        {
            var expectedGuid = DailyPushConstants.TimezoneToGuid(timezone);
            if (expectedGuid == grainGuid)
            {
                _logger.LogInformation("Inferred timezone {TimeZone} from GUID {Guid} during migration", timezone, grainGuid);
                return timezone;
            }
        }
        
        return null;
    }

    public async Task ProcessMorningPushAsync(DateTime targetDate)
    {
        // Safety check: ensure timezone is initialized
        if (string.IsNullOrEmpty(_timeZoneId))
        {
            _logger.LogError("Cannot process morning push: timezone ID is not set. Grain needs to be initialized first.");
            return;
        }
        
        if (State.Status != SchedulerStatus.Active)
        {
            _logger.LogWarning("Skipping morning push for {TimeZone} - scheduler status: {Status}", 
                _timeZoneId, State.Status);
            return;
        }

        _logger.LogInformation("Processing morning push for timezone {TimeZone} on {Date}", 
            _timeZoneId, targetDate);

        try
        {
            // Get daily content selection
            var contentGAgent = _grainFactory.GetGrain<IDailyContentGAgent>(DailyPushConstants.CONTENT_GAGENT_ID);
            var dailyContents = await contentGAgent.GetSmartSelectedContentsAsync(
                DailyPushConstants.DAILY_CONTENT_COUNT, targetDate);

            if (!dailyContents.Any())
            {
                _logger.LogWarning("No daily content available for {Date}", targetDate);
                return;
            }

            // Get users in this timezone
            var timezoneIndexGAgent = _grainFactory.GetGrain<IPushSubscriberIndexGAgent>(DailyPushConstants.TimezoneToGuid(_timeZoneId));
            
            // Process users in batches
            const int batchSize = 1000;
            int skip = 0;
            int processedUsers = 0;
            int failureCount = 0;
            List<Guid> userBatch;

            do
            {
                userBatch = await timezoneIndexGAgent.GetActiveUsersInTimezoneAsync(skip, batchSize);
                
                if (userBatch.Any())
                {
                    var batchResult = await ProcessUserBatchAsync(userBatch, dailyContents, targetDate);
                    processedUsers += batchResult.processedCount;
                    failureCount += batchResult.failureCount;
                    skip += batchSize;
                }
                
            } while (userBatch.Count == batchSize);

            // Update state
            State.LastMorningPush = targetDate;
            State.LastMorningUserCount = processedUsers;
            State.LastExecutionFailures = failureCount;
            State.LastUpdated = DateTime.UtcNow;

            _logger.LogInformation("Completed morning push for {TimeZone}: {Users} users, {Failures} failures", 
                _timeZoneId, processedUsers, failureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process morning push for {TimeZone}", _timeZoneId);
            State.Status = SchedulerStatus.Error;
            State.LastUpdated = DateTime.UtcNow;
        }
    }

    public async Task ProcessAfternoonRetryAsync(DateTime targetDate)
    {
        // Safety check: ensure timezone is initialized
        if (string.IsNullOrEmpty(_timeZoneId))
        {
            _logger.LogError("Cannot process afternoon retry: timezone ID is not set. Grain needs to be initialized first.");
            return;
        }
        
        if (State.Status != SchedulerStatus.Active)
        {
            _logger.LogWarning("Skipping afternoon retry for {TimeZone} - scheduler status: {Status}", 
                _timeZoneId, State.Status);
            return;
        }

        _logger.LogInformation("Processing afternoon retry for timezone {TimeZone} on {Date}", 
            _timeZoneId, targetDate);

        try
        {
            // Get same content as morning push
            var contentGAgent = _grainFactory.GetGrain<IDailyContentGAgent>(DailyPushConstants.CONTENT_GAGENT_ID);
            var dailyContents = await contentGAgent.GetSmartSelectedContentsAsync(
                DailyPushConstants.DAILY_CONTENT_COUNT, targetDate);

            if (!dailyContents.Any())
            {
                _logger.LogWarning("No content available for afternoon retry on {Date}", targetDate);
                return;
            }

            // Get users in this timezone
            var timezoneIndexGAgent = _grainFactory.GetGrain<IPushSubscriberIndexGAgent>(DailyPushConstants.TimezoneToGuid(_timeZoneId));
            
            // Process users in batches (only those who haven't read morning push)
            const int batchSize = 1000;
            int skip = 0;
            int retryUsers = 0;
            int failureCount = 0;
            List<Guid> userBatch;

            do
            {
                userBatch = await timezoneIndexGAgent.GetActiveUsersInTimezoneAsync(skip, batchSize);
                
                if (userBatch.Any())
                {
                    var batchResult = await ProcessAfternoonRetryBatchAsync(userBatch, dailyContents, targetDate);
                    retryUsers += batchResult.retryCount;
                    failureCount += batchResult.failureCount;
                    skip += batchSize;
                }
                
            } while (userBatch.Count == batchSize);

            // Update state
            State.LastAfternoonRetry = targetDate;
            State.LastAfternoonRetryCount = retryUsers;
            State.LastExecutionFailures += failureCount;
            State.LastUpdated = DateTime.UtcNow;

            _logger.LogInformation("Completed afternoon retry for {TimeZone}: {RetryUsers} users needed retry, {Failures} failures", 
                _timeZoneId, retryUsers, failureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process afternoon retry for {TimeZone}", _timeZoneId);
            State.Status = SchedulerStatus.Error;
            State.LastUpdated = DateTime.UtcNow;
        }
    }

    public async Task<DailyPushCoordinatorState> GetStatusAsync()
    {
        return State;
    }

    public async Task SetStatusAsync(SchedulerStatus status)
    {
        State.Status = status;
        State.LastUpdated = DateTime.UtcNow;
        
        _logger.LogInformation("Updated scheduler status for {TimeZone}: {Status}", _timeZoneId, status);
    }

    // Orleans Reminder implementation for scheduled pushes
    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        var now = DateTime.UtcNow;
        var configuredTargetId = _options.CurrentValue.ReminderTargetId;
        
        try
        {
            // Handle test mode reminders first - TODO: Remove before production
            if (reminderName == TestModeConstants.TEST_PUSH_REMINDER)
            {
                await HandleTestPushReminderAsync(now);
                return;
            }
            
            if (reminderName == TestModeConstants.TEST_RETRY_REMINDER)
            {
                await HandleTestRetryReminderAsync(now);
                return;
            }
            // Version control check - only authorized instances should execute
            if (State.ReminderTargetId != configuredTargetId || configuredTargetId == Guid.Empty)
            {
                _logger.LogWarning("Unauthorized instance received reminder {ReminderName} for {TimeZone}. " +
                    "Current: {Current}, Expected: {Expected}. Cleaning up reminder.", 
                    reminderName, _timeZoneId, State.ReminderTargetId, configuredTargetId);
                
                // Clean up unauthorized reminder
                await UnregisterSpecificReminderAsync(reminderName);
                return;
            }
            
            // Execute the scheduled task (reliability over timing precision)
            var targetDate = now.Date;
            var isWithinWindow = IsWithinExecutionWindow(reminderName, now);
            
            if (!isWithinWindow)
            {
                _logger.LogWarning("Executing {ReminderName} outside ideal window for {TimeZone} - " +
                    "ensuring delivery reliability over timing precision", 
                    reminderName, _timeZoneId);
            }
            else
            {
                _logger.LogInformation("Executing scheduled task: {ReminderName} for {TimeZone}", 
                    reminderName, _timeZoneId);
            }
            
            switch (reminderName)
            {
                case MORNING_REMINDER:
                    await ProcessMorningPushAsync(targetDate);
                    break;
                case AFTERNOON_REMINDER:
                    await ProcessAfternoonRetryAsync(targetDate);
                    break;
                default:
                    _logger.LogWarning("Unknown reminder: {ReminderName}", reminderName);
                    break;
            }
            
            // Always reschedule the next reminder (best practice from documentation)
            await RescheduleReminderAsync(reminderName, now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing reminder {ReminderName} for {TimeZone}", 
                reminderName, _timeZoneId);
            
            // Even on error, try to reschedule next reminder
            try
            {
                await RescheduleReminderAsync(reminderName, now);
            }
            catch (Exception scheduleEx)
            {
                _logger.LogError(scheduleEx, "Failed to reschedule reminder {ReminderName}", reminderName);
            }
        }
    }
    
    /// <summary>
    /// Check if current time is within the ideal execution window for the reminder
    /// This is used for monitoring and logging purposes only - execution always proceeds
    /// </summary>
    private bool IsWithinExecutionWindow(string reminderName, DateTime currentUtc)
    {
        var options = _options.CurrentValue;
        var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(_timeZoneId);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(currentUtc, timeZoneInfo);
        var currentTimeOfDay = localNow.TimeOfDay;
        
        TimeSpan targetTime = reminderName switch
        {
            MORNING_REMINDER => options.MorningTime,
            AFTERNOON_REMINDER => options.AfternoonRetryTime,
            _ => TimeSpan.Zero
        };
        
        if (targetTime == TimeSpan.Zero) return false;
        
        double minutesDifference = Math.Abs((currentTimeOfDay - targetTime).TotalMinutes);
        bool withinWindow = minutesDifference <= _toleranceWindow.TotalMinutes;
        
        if (!withinWindow)
        {
            _logger.LogInformation("Timing analysis for {ReminderName}: " +
                "Current time: {Current}, Target time: {Target}, Difference: {Diff} minutes (outside ideal ±{Tolerance}min window)", 
                reminderName, currentTimeOfDay, targetTime, minutesDifference, _toleranceWindow.TotalMinutes);
        }
        
        return withinWindow;
    }
    
    /// <summary>
    /// Reschedule a specific reminder for the next execution
    /// </summary>
    private async Task RescheduleReminderAsync(string reminderName, DateTime currentUtc)
    {
        var options = _options.CurrentValue;
        
        TimeSpan targetTime = reminderName switch
        {
            MORNING_REMINDER => options.MorningTime,
            AFTERNOON_REMINDER => options.AfternoonRetryTime,
            _ => TimeSpan.Zero
        };
        
        if (targetTime == TimeSpan.Zero) return;
        
        TimeSpan nextDueTime = CalculateNextExecutionTime(targetTime, currentUtc);
        
        await this.RegisterOrUpdateReminder(
            reminderName,
            nextDueTime,
            TimeSpan.FromHours(23) // Use 23 hours as per best practices
        );
        
        _logger.LogInformation("Rescheduled {ReminderName}, next execution at: {NextTime}", 
            reminderName, currentUtc.Add(nextDueTime));
    }
    
    /// <summary>
    /// Unregister a specific reminder
    /// </summary>
    private async Task UnregisterSpecificReminderAsync(string reminderName)
    {
        try
        {
            var existingReminder = await this.GetReminder(reminderName);
            if (existingReminder != null)
            {
                await this.UnregisterReminder(existingReminder);
                _logger.LogInformation("Unregistered reminder {ReminderName} for {TimeZone}", 
                    reminderName, _timeZoneId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unregistering reminder {ReminderName} for {TimeZone}", 
                reminderName, _timeZoneId);
        }
    }

    private async Task TryRegisterRemindersAsync()
    {
        var configuredTargetId = _options.CurrentValue.ReminderTargetId;
        
        // Version control check - only authorized instances can register reminders
        if (State.ReminderTargetId != configuredTargetId || configuredTargetId == Guid.Empty)
        {
            _logger.LogInformation("ReminderTargetId doesn't match for {TimeZone}, not registering reminders. " +
                "Current: {Current}, Expected: {Expected}", 
                _timeZoneId, State.ReminderTargetId, configuredTargetId);
            
            // Clean up any existing reminders
            await CleanupRemindersAsync();
            return;
        }
        
        await RegisterRemindersAsync();
    }
    
    private async Task RegisterRemindersAsync()
    {
        var options = _options.CurrentValue;
        var now = DateTime.UtcNow;
        
        // Calculate next morning push time
        var nextMorningDueTime = CalculateNextExecutionTime(options.MorningTime, now);
        
        // Calculate next afternoon retry time  
        var nextAfternoonDueTime = CalculateNextExecutionTime(options.AfternoonRetryTime, now);
        
        // Register reminders with 23-hour period (best practice from documentation)
        await this.RegisterOrUpdateReminder(
            MORNING_REMINDER, 
            nextMorningDueTime, 
            TimeSpan.FromHours(23)
        );
        
        await this.RegisterOrUpdateReminder(
            AFTERNOON_REMINDER, 
            nextAfternoonDueTime, 
            TimeSpan.FromHours(23)
        );
        
        _logger.LogInformation("Registered reminders for {TimeZone} - Morning: {Morning}, Afternoon: {Afternoon}", 
            _timeZoneId, now.Add(nextMorningDueTime), now.Add(nextAfternoonDueTime));
    }
    
    /// <summary>
    /// Calculate time until next execution for a given target time in the timezone
    /// </summary>
    private TimeSpan CalculateNextExecutionTime(TimeSpan targetTime, DateTime currentUtc)
    {
        // Safety check: ensure timezone ID is available
        if (string.IsNullOrEmpty(_timeZoneId))
        {
            _logger.LogError("Cannot calculate execution time: timezone ID is not set. Using UTC as fallback.");
            // Fallback to UTC to prevent crashes
            var utcNow = currentUtc;
            var todayTargetUtc = utcNow.Date.Add(targetTime);
            return todayTargetUtc <= utcNow ? 
                TimeSpan.FromDays(1) - (utcNow - utcNow.Date) + targetTime : 
                todayTargetUtc - utcNow;
        }
        
        var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(_timeZoneId);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(currentUtc, timeZoneInfo);
        
        // Today's target time in local timezone
        var todayTarget = localNow.Date.Add(targetTime);
        
        // If today's target time has passed, calculate until tomorrow's target time
        if (todayTarget <= localNow)
        {
            todayTarget = todayTarget.AddDays(1);
        }
        
        // Convert back to UTC and calculate the time difference
        var nextTargetUtc = TimeZoneInfo.ConvertTimeToUtc(todayTarget, timeZoneInfo);
        return nextTargetUtc - currentUtc;
    }
    
    private async Task CleanupRemindersAsync()
    {
        string[] reminderNames = { MORNING_REMINDER, AFTERNOON_REMINDER };
        
        foreach (var reminderName in reminderNames)
        {
            await UnregisterSpecificReminderAsync(reminderName);
        }
    }

    private async Task<(int processedCount, int failureCount)> ProcessUserBatchAsync(
        List<Guid> userIds, List<DailyNotificationContent> contents, DateTime targetDate)
    {
        var processedCount = 0;
        var failureCount = 0;
        
        // Process with limited concurrency to avoid overloading
        var semaphore = new SemaphoreSlim(50);
        var tasks = userIds.Select(async userId =>
        {
            await semaphore.WaitAsync();
            try
            {
                var chatManagerGAgent = _grainFactory.GetGrain<IChatManagerGAgent>(userId);
                
                // Check if user has enabled devices in this timezone
                if (await chatManagerGAgent.HasEnabledDeviceInTimezoneAsync(_timeZoneId))
                {
                    await chatManagerGAgent.ProcessDailyPushAsync(targetDate, contents, _timeZoneId);
                    Interlocked.Increment(ref processedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process daily push for user {UserId}", userId);
                Interlocked.Increment(ref failureCount);
            }
            finally
            {
                semaphore.Release();
            }
        });
        
        await Task.WhenAll(tasks);
        return (processedCount, failureCount);
    }

    /// <summary>
    /// Process morning push for test mode (bypasses read status check)
    /// </summary>
    private async Task ProcessTestMorningPushAsync(DateTime targetDate)
    {
        // Safety check: ensure timezone is initialized
        if (string.IsNullOrEmpty(_timeZoneId))
        {
            _logger.LogError("Cannot process test morning push: timezone ID is not set. Grain needs to be initialized first.");
            return;
        }
        
        if (State.Status != SchedulerStatus.Active)
        {
            _logger.LogWarning("Skipping test morning push for {TimeZone} - scheduler status: {Status}", 
                _timeZoneId, State.Status);
            return;
        }

        _logger.LogInformation("🧪 Processing test morning push for timezone {TimeZone} on {Date}", 
            _timeZoneId, targetDate);

        try
        {
            // Get daily content selection
            var contentGAgent = _grainFactory.GetGrain<IDailyContentGAgent>(DailyPushConstants.CONTENT_GAGENT_ID);
            var dailyContents = await contentGAgent.GetSmartSelectedContentsAsync(
                DailyPushConstants.DAILY_CONTENT_COUNT, targetDate);

            if (!dailyContents.Any())
            {
                _logger.LogWarning("No daily content available for test push on {Date}", targetDate);
                return;
            }

            // Get users in this timezone
            var timezoneIndexGAgent = _grainFactory.GetGrain<IPushSubscriberIndexGAgent>(DailyPushConstants.TimezoneToGuid(_timeZoneId));
            
            // Process users in batches (test mode bypasses read status check)
            const int batchSize = 1000;
            int skip = 0;
            int processedUsers = 0;
            int failureCount = 0;
            List<Guid> userBatch;

            do
            {
                userBatch = await timezoneIndexGAgent.GetActiveUsersInTimezoneAsync(skip, batchSize);
                
                if (userBatch.Any())
                {
                    var batchResult = await ProcessTestUserBatchAsync(userBatch, dailyContents, targetDate);
                    processedUsers += batchResult.processedCount;
                    failureCount += batchResult.failureCount;
                    skip += batchSize;
                }
                
            } while (userBatch.Count == batchSize);

            // Update state
            State.LastMorningPush = targetDate;
            State.LastMorningUserCount = processedUsers;
            State.LastExecutionFailures = failureCount;
            State.LastUpdated = DateTime.UtcNow;

            _logger.LogInformation("🧪 Completed test morning push for {TimeZone}: {Users} users, {Failures} failures", 
                _timeZoneId, processedUsers, failureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process test morning push for {TimeZone}", _timeZoneId);
            State.Status = SchedulerStatus.Error;
            State.LastUpdated = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Process test user batch with bypass read status check
    /// </summary>
    private async Task<(int processedCount, int failureCount)> ProcessTestUserBatchAsync(
        List<Guid> userIds, List<DailyNotificationContent> contents, DateTime targetDate)
    {
        var processedCount = 0;
        var failureCount = 0;
        
        // Process with limited concurrency to avoid overloading
        var semaphore = new SemaphoreSlim(50);
        var tasks = userIds.Select(async userId =>
        {
            await semaphore.WaitAsync();
            try
            {
                var chatManagerGAgent = _grainFactory.GetGrain<IChatManagerGAgent>(userId);
                
                // Check if user has enabled devices in this timezone
                if (await chatManagerGAgent.HasEnabledDeviceInTimezoneAsync(_timeZoneId))
                {
                    // For test mode main push, bypass read status check
                    await chatManagerGAgent.ProcessDailyPushAsync(targetDate, contents, _timeZoneId, bypassReadStatusCheck: true);
                    Interlocked.Increment(ref processedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process test daily push for user {UserId}", userId);
                Interlocked.Increment(ref failureCount);
            }
            finally
            {
                semaphore.Release();
            }
        });
        
        await Task.WhenAll(tasks);
        return (processedCount, failureCount);
    }

    private async Task<(int retryCount, int failureCount)> ProcessAfternoonRetryBatchAsync(
        List<Guid> userIds, List<DailyNotificationContent> contents, DateTime targetDate)
    {
        var retryCount = 0;
        var failureCount = 0;
        
        var semaphore = new SemaphoreSlim(50);
        var tasks = userIds.Select(async userId =>
        {
            await semaphore.WaitAsync();
            try
            {
                var chatManagerGAgent = _grainFactory.GetGrain<IChatManagerGAgent>(userId);
                
                // Check if user needs afternoon retry and has enabled devices
                if (await chatManagerGAgent.HasEnabledDeviceInTimezoneAsync(_timeZoneId) &&
                    await chatManagerGAgent.ShouldSendAfternoonRetryAsync(targetDate))
                {
                    await chatManagerGAgent.ProcessDailyPushAsync(targetDate, contents, _timeZoneId);
                    Interlocked.Increment(ref retryCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process afternoon retry for user {UserId}", userId);
                Interlocked.Increment(ref failureCount);
            }
            finally
            {
                semaphore.Release();
            }
        });
        
        await Task.WhenAll(tasks);
        return (retryCount, failureCount);
    }
    
    // Test mode reminder handlers - TODO: Remove before production
    
    /// <summary>
    /// Handle test push reminder - executes test push and schedules retry
    /// </summary>
    private async Task HandleTestPushReminderAsync(DateTime now)
    {
        if (!State.TestModeActive)
        {
            _logger.LogWarning("Received test push reminder but test mode not active for {TimeZone}", _timeZoneId);
            await CleanupTestRemindersAsync();
            return;
        }
        
        // Check if reached maximum rounds
        if (State.TestRoundsCompleted >= TestModeConstants.MAX_TEST_ROUNDS)
        {
            _logger.LogInformation("Test mode completed maximum rounds ({MaxRounds}) for {TimeZone}. Stopping test mode.", 
                TestModeConstants.MAX_TEST_ROUNDS, _timeZoneId);
            await StopTestModeAsync();
            return;
        }
        
        try
        {
            _logger.LogInformation("Executing test push round {Round}/{MaxRounds} for {TimeZone}", 
                State.TestRoundsCompleted + 1, TestModeConstants.MAX_TEST_ROUNDS, _timeZoneId);
            
            // Execute test push (use current date) - main push should bypass read status check
            var targetDate = now.Date;
            await ProcessTestMorningPushAsync(targetDate);
            
            // Increment round counter using proper event-driven approach
            RaiseEvent(new TestRoundCompletedEventLog
            {
                CompletedRound = State.TestRoundsCompleted + 1,
                CompletionTime = DateTime.UtcNow
            });
            await ConfirmEvents();
            
            // Schedule retry reminder
            await this.RegisterOrUpdateReminder(
                TestModeConstants.TEST_RETRY_REMINDER,
                TestModeConstants.RETRY_DELAY,
                TestModeConstants.RETRY_DELAY);
                
            _logger.LogInformation("Test push executed and retry scheduled for {TimeZone}. Round {Round} completed.", 
                _timeZoneId, State.TestRoundsCompleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute test push for {TimeZone}", _timeZoneId);
            
            // Continue with next round even if current failed
            await RescheduleTestPushReminderAsync();
        }
    }
    
    /// <summary>
    /// Handle test retry reminder - executes retry logic for unread messages
    /// </summary>
    private async Task HandleTestRetryReminderAsync(DateTime now)
    {
        if (!State.TestModeActive)
        {
            _logger.LogWarning("Received test retry reminder but test mode not active for {TimeZone}", _timeZoneId);
            await CleanupTestRemindersAsync();
            return;
        }
        
        try
        {
            _logger.LogInformation("Executing test retry for {TimeZone}", _timeZoneId);
            
            // Execute retry logic (use current date)
            var targetDate = now.Date;
            await ProcessAfternoonRetryAsync(targetDate);
            
            // Schedule next push round if not completed
            await RescheduleTestPushReminderAsync();
            
            _logger.LogInformation("Test retry executed for {TimeZone}", _timeZoneId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute test retry for {TimeZone}", _timeZoneId);
            
            // Continue with next round even if retry failed
            await RescheduleTestPushReminderAsync();
        }
    }
    
    /// <summary>
    /// Schedule next test push reminder if test mode still active and under limit
    /// </summary>
    private async Task RescheduleTestPushReminderAsync()
    {
        if (!State.TestModeActive)
            return;
            
        if (State.TestRoundsCompleted >= TestModeConstants.MAX_TEST_ROUNDS)
        {
            _logger.LogInformation("Test mode reached maximum rounds for {TimeZone}. Stopping.", _timeZoneId);
            await StopTestModeAsync();
            return;
        }
        
        // Schedule next push round using custom interval
        var customInterval = TimeSpan.FromSeconds(State.TestCustomInterval);
        await this.RegisterOrUpdateReminder(
            TestModeConstants.TEST_PUSH_REMINDER,
            customInterval,
            customInterval);
    }
    
    /// <summary>
    /// Cleanup test mode reminders
    /// </summary>
    private async Task CleanupTestRemindersAsync()
    {
        try
        {
            await UnregisterSpecificReminderAsync(TestModeConstants.TEST_PUSH_REMINDER);
            await UnregisterSpecificReminderAsync(TestModeConstants.TEST_RETRY_REMINDER);
            
            _logger.LogInformation("Test reminders cleaned up for {TimeZone}", _timeZoneId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup test reminders for {TimeZone}", _timeZoneId);
        }
    }
    
    /// <summary>
    /// Get all devices registered in this timezone with detailed information - TODO: Remove before production
    /// </summary>
    public async Task<List<TimezoneDeviceInfo>> GetDevicesInTimezoneAsync()
    {
        // Safety check: ensure timezone is initialized
        if (string.IsNullOrEmpty(_timeZoneId))
        {
            _logger.LogError("Cannot get devices: timezone ID is not set. Grain needs to be initialized first.");
            return new List<TimezoneDeviceInfo>();
        }
        
        _logger.LogInformation("Getting all devices in timezone {TimeZone}", _timeZoneId);
        
        try
        {
            var devices = new List<TimezoneDeviceInfo>();
            
            // Get users in this timezone
            var timezoneIndexGAgent = _grainFactory.GetGrain<IPushSubscriberIndexGAgent>(DailyPushConstants.TimezoneToGuid(_timeZoneId));
            
            // Get all users in batches
            const int batchSize = 1000;
            int skip = 0;
            List<Guid> userBatch;
            
            do
            {
                userBatch = await timezoneIndexGAgent.GetActiveUsersInTimezoneAsync(skip, batchSize);
                
                if (userBatch.Any())
                {
                    // Process each user to get their device information
                    var userDeviceTasks = userBatch.Select(async userId =>
                    {
                        try
                        {
                            var chatManagerGAgent = _grainFactory.GetGrain<IChatManagerGAgent>(userId);
                            
                            // Get user device information
                            var hasEnabledDeviceInTimezone = await chatManagerGAgent.HasEnabledDeviceInTimezoneAsync(_timeZoneId);
                            var allDevices = await chatManagerGAgent.GetAllUserDevicesAsync();
                            
                            // Filter devices for this timezone
                            var timezoneDevices = allDevices.Where(d => d.TimeZoneId == _timeZoneId).ToList();
                            
                            // Create device info for each device
                            var userDeviceInfos = timezoneDevices.Select(device => new TimezoneDeviceInfo
                            {
                                UserId = userId,
                                DeviceId = device.DeviceId,
                                PushToken = string.IsNullOrEmpty(device.PushToken) ? "" : $"{device.PushToken.Substring(0, Math.Min(10, device.PushToken.Length))}...", // Truncate for privacy
                                TimeZoneId = device.TimeZoneId,
                                PushLanguage = device.PushLanguage,
                                PushEnabled = device.PushEnabled,
                                RegisteredAt = device.RegisteredAt,
                                LastTokenUpdate = device.LastTokenUpdate,
                                HasEnabledDeviceInTimezone = hasEnabledDeviceInTimezone,
                                TotalDeviceCount = allDevices.Count,
                                EnabledDeviceCount = allDevices.Count(d => d.PushEnabled && d.TimeZoneId == _timeZoneId)
                            }).ToList();
                            
                            // If user has no devices in this timezone but is in index, create a placeholder entry
                            if (!userDeviceInfos.Any())
                            {
                                userDeviceInfos.Add(new TimezoneDeviceInfo
                                {
                                    UserId = userId,
                                    DeviceId = "(No devices in this timezone)",
                                    PushToken = "",
                                    TimeZoneId = _timeZoneId,
                                    PushLanguage = "",
                                    PushEnabled = false,
                                    RegisteredAt = DateTime.MinValue,
                                    LastTokenUpdate = DateTime.MinValue,
                                    HasEnabledDeviceInTimezone = hasEnabledDeviceInTimezone,
                                    TotalDeviceCount = allDevices.Count,
                                    EnabledDeviceCount = 0
                                });
                            }
                            
                            return userDeviceInfos;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to get device info for user {UserId}", userId);
                            return new List<TimezoneDeviceInfo>
                            {
                                new TimezoneDeviceInfo
                                {
                                    UserId = userId,
                                    DeviceId = "(Error retrieving device info)",
                                    PushToken = ex.Message,
                                    TimeZoneId = _timeZoneId,
                                    PushLanguage = "",
                                    PushEnabled = false,
                                    RegisteredAt = DateTime.MinValue,
                                    LastTokenUpdate = DateTime.MinValue,
                                    HasEnabledDeviceInTimezone = false,
                                    TotalDeviceCount = 0,
                                    EnabledDeviceCount = 0
                                }
                            };
                        }
                    });
                    
                    var batchDevices = await Task.WhenAll(userDeviceTasks);
                    devices.AddRange(batchDevices.SelectMany(d => d));
                    
                    skip += batchSize;
                }
                
            } while (userBatch.Count == batchSize);
            
            _logger.LogInformation("Retrieved {DeviceCount} device entries for {UserCount} users in timezone {TimeZone}", 
                devices.Count, devices.Select(d => d.UserId).Distinct().Count(), _timeZoneId);
            
                    return devices.OrderBy(d => d.UserId).ThenBy(d => d.DeviceId).ToList();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to get devices in timezone {TimeZone}", _timeZoneId);
        return new List<TimezoneDeviceInfo>();
    }
}

public async Task<InstantPushResult> SendInstantPushAsync()
{
    try
    {
        _logger.LogInformation("🚀 Starting instant push for timezone {TimeZone}", _timeZoneId);
        
        if (string.IsNullOrEmpty(_timeZoneId))
        {
            _logger.LogWarning("Timezone not initialized for instant push");
            return new InstantPushResult
            { 
                Error = "Timezone not initialized", 
                Timezone = _timeZoneId,
                Timestamp = DateTime.Now 
            };
        }

        // Get timezone user index to find users
        var timezoneIndexGAgent = _grainFactory.GetGrain<IPushSubscriberIndexGAgent>(DailyPushConstants.TimezoneToGuid(_timeZoneId));
        var activeUsers = await timezoneIndexGAgent.GetActiveUsersInTimezoneAsync(0, 1000);
        
        _logger.LogInformation("📱 Found {UserCount} active users in timezone {TimeZone}", activeUsers.Count, _timeZoneId);
        
        int totalDevices = 0;
        int successfulPushes = 0;
        int failedPushes = 0;
        
        // Try to get content from CSV file first, fall back to test content if needed
        List<DailyNotificationContent> testContent;
        
        try
        {
            // Get content service to load from CSV
            var contentService = ServiceProvider.GetService(typeof(Services.DailyPushContentService)) as Services.DailyPushContentService;
            if (contentService != null)
            {
                _logger.LogInformation("📋 Loading push content from CSV file...");
                var csvContents = await contentService.GetAllContentsAsync();
                
                if (csvContents?.Count >= 2)
                {
                    // Select 2 random contents from CSV
                    var random = new Random();
                    var selectedContents = csvContents.OrderBy(x => random.Next()).Take(2).ToList();
                    
                    testContent = new List<DailyNotificationContent>();
                    
                    foreach (var csvContent in selectedContents)
                    {
                        var notificationContent = new DailyNotificationContent
                        {
                            Id = $"csv_{csvContent.ContentKey}_{DateTime.Now.Ticks}",
                            IsActive = true,
                            LocalizedContents = new Dictionary<string, LocalizedContentData>()
                        };
                        
                        // Add English content if available
                        if (!string.IsNullOrEmpty(csvContent.TitleEn) || !string.IsNullOrEmpty(csvContent.ContentEn))
                        {
                            notificationContent.LocalizedContents["en"] = new LocalizedContentData
                            {
                                Title = csvContent.TitleEn ?? "📱 Daily Inspiration",
                                Content = csvContent.ContentEn ?? "Have a wonderful day!"
                            };
                        }
                        
                        // Add Traditional Chinese content if available
                        Logger.LogDebug("🔍 CSV Traditional Chinese content check: Key={ContentKey}, TitleZh='{TitleZh}', ContentZh='{ContentZh}'", 
                            csvContent.ContentKey, csvContent.TitleZh, csvContent.ContentZh);
                        
                        if (!string.IsNullOrEmpty(csvContent.TitleZh) || !string.IsNullOrEmpty(csvContent.ContentZh))
                        {
                            notificationContent.LocalizedContents["zh-tw"] = new LocalizedContentData
                            {
                                Title = csvContent.TitleZh ?? "📱 每日靈感",
                                Content = csvContent.ContentZh ?? "祝你有美好的一天！"
                            };
                            Logger.LogInformation("✅ Added Traditional Chinese content for key={ContentKey}: Title='{Title}'", 
                                csvContent.ContentKey, csvContent.TitleZh);
                        }
                        else
                        {
                            Logger.LogWarning("⚠️ No Traditional Chinese content available for key={ContentKey}, skipping zh-tw", 
                                csvContent.ContentKey);
                        }
                        
                        // Add Simplified Chinese content if available
                        if (!string.IsNullOrEmpty(csvContent.TitleZhSc) || !string.IsNullOrEmpty(csvContent.ContentZhSc))
                        {
                            notificationContent.LocalizedContents["zh-cn"] = new LocalizedContentData
                            {
                                Title = csvContent.TitleZhSc ?? "📱 每日灵感",
                                Content = csvContent.ContentZhSc ?? "祝你有美好的一天！"
                            };
                        }
                        
                        // Ensure at least English content exists
                        if (notificationContent.LocalizedContents.Count == 0)
                        {
                            notificationContent.LocalizedContents["en"] = new LocalizedContentData
                            {
                                Title = "📱 Daily Inspiration",
                                Content = "Have a wonderful day!"
                            };
                        }
                        
                        testContent.Add(notificationContent);
                    }
                    
                    _logger.LogInformation("✅ Successfully loaded {Count} contents from CSV for instant push", testContent.Count);
                }
                else
                {
                    _logger.LogWarning("⚠️ Not enough content in CSV file ({Count} available, need 2), using fallback test content", csvContents?.Count ?? 0);
                    testContent = CreateFallbackTestContent();
                }
            }
            else
            {
                _logger.LogWarning("⚠️ DailyPushContentService not available, using fallback test content");
                testContent = CreateFallbackTestContent();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to load content from CSV, using fallback test content");
            testContent = CreateFallbackTestContent();
        }
        
        // Process each user
        foreach (var userId in activeUsers)
        {
            try
            {
                var chatManager = _grainFactory.GetGrain<IChatManagerGAgent>(userId);
                var hasEnabledDevice = await chatManager.HasEnabledDeviceInTimezoneAsync(_timeZoneId);
                
                if (!hasEnabledDevice)
                {
                    _logger.LogDebug("User {UserId} has no enabled devices in timezone {TimeZone}", userId, _timeZoneId);
                    continue;
                }
                
                // Get user devices for counting
                var userDevices = await chatManager.GetAllUserDevicesAsync();
                var enabledDevicesInTimezone = userDevices.Where(d => d.PushEnabled && d.TimeZoneId == _timeZoneId).ToList();
                totalDevices += enabledDevicesInTimezone.Count;
                
                // Send instant push to this user (two messages) - bypasses read status check
                await chatManager.ProcessInstantPushAsync(testContent, _timeZoneId);
                
                // Count as success for each device (2 notifications per device)
                successfulPushes += enabledDevicesInTimezone.Count * 2;
                
                _logger.LogDebug("✅ Sent instant push to user {UserId} with {DeviceCount} devices", userId, enabledDevicesInTimezone.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to send instant push to user {UserId}", userId);
                failedPushes += 2; // Failed to send 2 notifications
            }
        }
        
        var result = new InstantPushResult
        {
            Timezone = _timeZoneId,
            TotalUsers = activeUsers.Count,
            TotalDevices = totalDevices,
            SuccessfulPushes = successfulPushes,
            FailedPushes = failedPushes,
            NotificationsPerDevice = 2,
            Timestamp = DateTime.Now,
            Message = $"Instant push completed: {successfulPushes} successful, {failedPushes} failed"
        };
        
        _logger.LogInformation("🎉 Instant push completed for timezone {TimeZone}: {TotalUsers} users, {TotalDevices} devices, {SuccessfulPushes} successful, {FailedPushes} failed", 
            _timeZoneId, activeUsers.Count, totalDevices, successfulPushes, failedPushes);
        
        return result;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "💥 Error during instant push for timezone {TimeZone}", _timeZoneId);
        return new InstantPushResult
        { 
            Timezone = _timeZoneId, 
            Error = ex.Message, 
            Timestamp = DateTime.Now 
        };
    }
}
    
    /// <summary>
    /// Create fallback test content when CSV is unavailable
    /// </summary>
    private List<DailyNotificationContent> CreateFallbackTestContent()
    {
        return new List<DailyNotificationContent>
        {
            new DailyNotificationContent
            {
                Id = "instant_test_1",
                LocalizedContents = new Dictionary<string, LocalizedContentData>
                {
                    ["zh-cn"] = new LocalizedContentData
                    {
                        Title = "🧪 即时推送测试 #1",
                        Content = $"这是即时推送测试消息，发送时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                    },
                    ["zh-tw"] = new LocalizedContentData
                    {
                        Title = "🧪 即時推送測試 #1",
                        Content = $"這是即時推送測試消息，發送時間：{DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                    },
                    ["es"] = new LocalizedContentData
                    {
                        Title = "🧪 Prueba de Push Instantáneo #1",
                        Content = $"Este es un mensaje de prueba de push instantáneo, enviado en: {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                    },
                    ["en"] = new LocalizedContentData
                    {
                        Title = "🧪 Instant Push Test #1",
                        Content = $"This is an instant push test message, sent at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                    }
                },
                IsActive = true
            },
            new DailyNotificationContent
            {
                Id = "instant_test_2",
                LocalizedContents = new Dictionary<string, LocalizedContentData>
                {
                    ["zh-cn"] = new LocalizedContentData
                    {
                        Title = "🧪 即时推送测试 #2",
                        Content = $"这是第二条即时推送测试消息，发送时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                    },
                    ["zh-tw"] = new LocalizedContentData
                    {
                        Title = "🧪 即時推送測試 #2", 
                        Content = $"這是第二條即時推送測試消息，發送時間：{DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                    },
                    ["es"] = new LocalizedContentData
                    {
                        Title = "🧪 Prueba de Push Instantáneo #2",
                        Content = $"Este es el segundo mensaje de prueba de push instantáneo, enviado en: {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                    },
                    ["en"] = new LocalizedContentData
                    {
                        Title = "🧪 Instant Push Test #2",
                        Content = $"This is the second instant push test message, sent at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                    }
                },
                IsActive = true
            }
        };
    }
}
