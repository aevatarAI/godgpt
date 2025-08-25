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
[GAgent(nameof(TimezoneSchedulerGAgent))]
public class TimezoneSchedulerGAgent : GAgentBase<TimezoneSchedulerGAgentState, DailyPushLogEvent>, 
    ITimezoneSchedulerGAgent, IRemindable
{
    private readonly ILogger<TimezoneSchedulerGAgent> _logger;
    private readonly IGrainFactory _grainFactory;
    private readonly IOptionsMonitor<DailyPushOptions> _options;
    private string _timeZoneId = "";
    
    // Reminder constants
    private const string MORNING_REMINDER = "MorningPush";
    private const string AFTERNOON_REMINDER = "AfternoonRetry";
    
    // Tolerance window for time-based execution (±5 minutes)
    private readonly TimeSpan _toleranceWindow = TimeSpan.FromMinutes(5);
    
    public TimezoneSchedulerGAgent(
        ILogger<TimezoneSchedulerGAgent> logger, 
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

    protected override async Task OnGAgentActivateAsync(CancellationToken cancellationToken)
    {
        _timeZoneId = this.GetPrimaryKeyString();
        
        if (string.IsNullOrEmpty(State.TimeZoneId))
        {
            await InitializeAsync(_timeZoneId);
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

    protected override void GAgentTransitionState(TimezoneSchedulerGAgentState state, StateLogEventBase<DailyPushLogEvent> @event)
    {
        switch (@event)
        {
            case SchedulerStatusLogEvent statusEvent:
                _logger.LogDebug($"Scheduler status changed from {statusEvent.OldStatus} to {statusEvent.NewStatus}");
                break;
            default:
                _logger.LogDebug($"Unhandled event type: {@event.GetType().Name}");
                break;
        }
    }

    public async Task InitializeAsync(string timeZoneId)
    {
        State.TimeZoneId = timeZoneId;
        State.Status = SchedulerStatus.Active;
        State.LastUpdated = DateTime.UtcNow;
        // Initialize with empty ReminderTargetId - requires explicit activation
        State.ReminderTargetId = Guid.Empty;
        
        _timeZoneId = timeZoneId;
        
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
        if (State.ReminderTargetId == _reminderTargetGuid)
        {
            await TryRegisterRemindersAsync();
        }
        else
        {
            // Clean up existing reminders if no longer authorized
            await CleanupRemindersAsync();
        }
    }

    public async Task ProcessMorningPushAsync(DateTime targetDate)
    {
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
            var contentGAgent = _grainFactory.GetGrain<IDailyContentGAgent>("default");
            var dailyContents = await contentGAgent.GetSmartSelectedContentsAsync(
                DailyPushConstants.DAILY_CONTENT_COUNT, targetDate);

            if (!dailyContents.Any())
            {
                _logger.LogWarning("No daily content available for {Date}", targetDate);
                return;
            }

            // Get users in this timezone
            var timezoneIndexGAgent = _grainFactory.GetGrain<ITimezoneUserIndexGAgent>(_timeZoneId);
            
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
            var contentGAgent = _grainFactory.GetGrain<IDailyContentGAgent>("default");
            var dailyContents = await contentGAgent.GetSmartSelectedContentsAsync(
                DailyPushConstants.DAILY_CONTENT_COUNT, targetDate);

            if (!dailyContents.Any())
            {
                _logger.LogWarning("No content available for afternoon retry on {Date}", targetDate);
                return;
            }

            // Get users in this timezone
            var timezoneIndexGAgent = _grainFactory.GetGrain<ITimezoneUserIndexGAgent>(_timeZoneId);
            
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

    public async Task<TimezoneSchedulerGAgentState> GetStatusAsync()
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
            
            // Check if within execution window
            if (IsWithinExecutionWindow(reminderName, now))
            {
                _logger.LogInformation("Executing scheduled task: {ReminderName} for {TimeZone}", 
                    reminderName, _timeZoneId);
                
                var targetDate = now.Date;
                
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
            }
            else
            {
                _logger.LogInformation("Outside execution window for {ReminderName}, skipping execution", 
                    reminderName);
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
    /// Check if current time is within the execution window for the reminder
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
            _logger.LogInformation("Outside execution window for {ReminderName}. " +
                "Current time: {Current}, Target time: {Target}, Difference: {Diff} minutes", 
                reminderName, currentTimeOfDay, targetTime, minutesDifference);
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
}
