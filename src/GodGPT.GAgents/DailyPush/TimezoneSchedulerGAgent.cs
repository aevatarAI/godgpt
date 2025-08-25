using Aevatar.Core.Abstractions;
using Aevatar.Core;
using Aevatar.Application.Grains.Agents.ChatManager;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Providers;
using GodGPT.GAgents.DailyPush.SEvents;

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
    private string _timeZoneId = "";
    
    // Version control - TODO: move to configuration
    private static readonly Guid _reminderTargetGuid = new Guid("12345678-1234-1234-1234-a00000000001");
    
    public TimezoneSchedulerGAgent(ILogger<TimezoneSchedulerGAgent> logger, IGrainFactory grainFactory)
    {
        _logger = logger;
        _grainFactory = grainFactory;
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
        // Version control check - only authorized instances should execute
        if (State.ReminderTargetId != _reminderTargetGuid)
        {
            _logger.LogWarning("Unauthorized instance received reminder {ReminderName} for {TimeZone}. " +
                "Current: {Current}, Expected: {Expected}. Cleaning up reminder.", 
                reminderName, _timeZoneId, State.ReminderTargetId, _reminderTargetGuid);
            
            // Clean up unauthorized reminder
            try
            {
                var existingReminder = await this.GetReminder(reminderName);
                if (existingReminder != null)
                {
                    await this.UnregisterReminder(existingReminder);
                    _logger.LogInformation("Cleaned up unauthorized reminder {ReminderName} for {TimeZone}", 
                        reminderName, _timeZoneId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up unauthorized reminder {ReminderName} for {TimeZone}", 
                    reminderName, _timeZoneId);
            }
            return;
        }
        
        var targetDate = DateTime.UtcNow.Date;
        
        try
        {
            switch (reminderName)
            {
                case "MorningPush":
                    await ProcessMorningPushAsync(targetDate);
                    break;
                case "AfternoonRetry":
                    await ProcessAfternoonRetryAsync(targetDate);
                    break;
                default:
                    _logger.LogWarning("Unknown reminder: {ReminderName}", reminderName);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing reminder {ReminderName} for {TimeZone}", 
                reminderName, _timeZoneId);
        }
    }

    private async Task TryRegisterRemindersAsync()
    {
        // Version control check - only authorized instances can register reminders
        if (State.ReminderTargetId != _reminderTargetGuid)
        {
            _logger.LogInformation("ReminderTargetId doesn't match for {TimeZone}, not registering reminders. " +
                "Current: {Current}, Expected: {Expected}", 
                _timeZoneId, State.ReminderTargetId, _reminderTargetGuid);
            
            // Clean up any existing reminders
            await CleanupRemindersAsync();
            return;
        }
        
        await RegisterRemindersAsync();
    }
    
    private async Task RegisterRemindersAsync()
    {
        var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(_timeZoneId);
        var now = DateTime.UtcNow;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, timeZoneInfo);
        
        // Calculate next morning push time (8:00 AM local)
        var nextMorning = localNow.Date.Add(DailyPushConstants.MORNING_TIME);
        if (nextMorning <= localNow)
            nextMorning = nextMorning.AddDays(1);
            
        var nextMorningUtc = TimeZoneInfo.ConvertTimeToUtc(nextMorning, timeZoneInfo);
        
        // Calculate next afternoon retry time (3:00 PM local)
        var nextAfternoon = localNow.Date.Add(DailyPushConstants.AFTERNOON_TIME);
        if (nextAfternoon <= localNow)
            nextAfternoon = nextAfternoon.AddDays(1);
            
        var nextAfternoonUtc = TimeZoneInfo.ConvertTimeToUtc(nextAfternoon, timeZoneInfo);
        
        // Register reminders (23-hour period for DST compatibility)
        await this.RegisterOrUpdateReminder("MorningPush", 
            nextMorningUtc - now, DailyPushConstants.DAILY_CYCLE);
        await this.RegisterOrUpdateReminder("AfternoonRetry", 
            nextAfternoonUtc - now, DailyPushConstants.DAILY_CYCLE);
        
        _logger.LogInformation("Registered reminders for {TimeZone} - Morning: {Morning}, Afternoon: {Afternoon}", 
            _timeZoneId, nextMorningUtc, nextAfternoonUtc);
    }
    
    private async Task CleanupRemindersAsync()
    {
        string[] reminderNames = { "MorningPush", "AfternoonRetry" };
        
        foreach (var reminderName in reminderNames)
        {
            try
            {
                var existingReminder = await this.GetReminder(reminderName);
                if (existingReminder != null)
                {
                    await this.UnregisterReminder(existingReminder);
                    _logger.LogInformation("Cleaned up reminder {ReminderName} for {TimeZone}", 
                        reminderName, _timeZoneId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up reminder {ReminderName} for {TimeZone}", 
                    reminderName, _timeZoneId);
            }
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
