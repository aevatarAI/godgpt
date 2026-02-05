using System.Text.Json;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using GodGPT.GAgents.DailyPush.Options;
using GodGPT.GAgents.DailyPush.SEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Providers;

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

    protected sealed override void GAgentTransitionState(DailyPushCoordinatorState state,
        StateLogEventBase<DailyPushLogEvent> @event)
    {
        switch (@event)
        {
            case TestRoundCompletedEventLog testRoundEvent:
                state.TestRoundsCompleted = testRoundEvent.CompletedRound;
                state.LastUpdated = testRoundEvent.CompletionTime;
                break;
            case SchedulerStatusLogEvent statusEvent:
                state.Status = statusEvent.NewStatus;
                state.LastUpdated = statusEvent.ChangeTime;
                _logger.LogDebug($"Scheduler status changed from {statusEvent.OldStatus} to {statusEvent.NewStatus}");
                break;
            case SetReminderTargetIdEventLog reminderEvent:
                state.ReminderTargetId = reminderEvent.NewTargetId;
                state.LastUpdated = reminderEvent.ChangeTime;
                _logger.LogDebug(
                    $"ReminderTargetId changed from {reminderEvent.OldTargetId} to {reminderEvent.NewTargetId}");
                break;
            case InitializeCoordinatorEventLog initEvent:
                state.TimeZoneId = initEvent.TimeZoneId;
                state.Status = initEvent.Status;
                state.LastUpdated = initEvent.InitTime;
                _logger.LogDebug($"Coordinator initialized for timezone {initEvent.TimeZoneId}");
                break;
            case MorningPushCompletedEventLog morningEvent:
                state.LastMorningPush = morningEvent.PushDate;
                state.LastMorningUserCount = morningEvent.UserCount;
                state.LastExecutionFailures = morningEvent.FailureCount;
                state.LastUpdated = morningEvent.CompletionTime;
                break;
            case AfternoonRetryCompletedEventLog retryEvent:
                state.LastAfternoonRetry = retryEvent.RetryDate;
                state.LastAfternoonRetryCount = retryEvent.RetryUserCount;
                state.LastExecutionFailures += retryEvent.FailureCount;
                state.LastUpdated = retryEvent.CompletionTime;
                break;
            case TestModeStateEventLog testModeEvent:
                state.TestModeActive = testModeEvent.IsActive;
                state.TestStartTime = testModeEvent.StartTime;
                state.TestCustomInterval = testModeEvent.CustomInterval;
                state.LastUpdated = testModeEvent.ChangeTime;
                break;
            case ConfigurationChangeEventLog configEvent:
                state.LastKnownMorningTime = configEvent.NewMorningTime;
                state.LastKnownAfternoonTime = configEvent.NewAfternoonTime;
                state.LastUpdated = configEvent.ChangeTime;
                _logger.LogDebug(
                    $"Configuration updated - Morning: {configEvent.OldMorningTime} → {configEvent.NewMorningTime}, Afternoon: {configEvent.OldAfternoonTime} → {configEvent.NewAfternoonTime}");
                break;
            default:
                _logger.LogDebug($"Unhandled event type: {@event.GetType().Name}");
                break;
        }
    }

    protected override async Task OnGAgentActivateAsync(CancellationToken cancellationToken)
    {
        // Get timezone ID from State (if already initialized)
        if (!string.IsNullOrEmpty(State.TimeZoneId))
        {
            _timeZoneId = State.TimeZoneId;
            _logger.LogInformation("DailyPushCoordinatorGAgent activated for timezone: {TimeZone} (from state)",
                _timeZoneId);
        }
        else
        {
            // Try to get timezone from GUID mapping
            var grainGuid = this.GetPrimaryKey();
            var inferredTimezone = await DailyPushConstants.GetTimezoneFromGuidAsync(grainGuid, _grainFactory);

            if (!string.IsNullOrEmpty(inferredTimezone))
            {
                _logger.LogInformation(
                    "DailyPushCoordinatorGAgent activated for timezone: {TimeZone} (from GUID mapping)",
                    inferredTimezone);
                // Auto-initialize with inferred timezone
                await InitializeAsync(inferredTimezone);
            }
            else
            {
                _logger.LogWarning(
                    "DailyPushCoordinatorGAgent activated but no timezone ID in state and no GUID mapping found. Grain will not be functional until InitializeAsync is called.");
                _timeZoneId = ""; // Ensure it's empty, not null
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

        // Try to register Orleans reminders if timezone is set (TryRegisterRemindersAsync handles State sync)
        if (!string.IsNullOrEmpty(_timeZoneId))
        {
            await TryRegisterRemindersAsync();
        }
        else
        {
            _logger.LogInformation("Skipping reminder registration - timezone not initialized yet for grain {GrainId}",
                this.GetPrimaryKey());
        }
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

        // ✅ Use event sourcing for state initialization
        RaiseEvent(new InitializeCoordinatorEventLog
        {
            TimeZoneId = timeZoneId,
            Status = SchedulerStatus.Active,
            InitTime = DateTime.UtcNow
        });

        _timeZoneId = timeZoneId;

        // ✅ Confirm all events at end of initialization chain
        await ConfirmEvents();

        // ✅ Now that timezone is properly initialized, sync State and register reminders
        await TryRegisterRemindersAsync();

        _logger.LogInformation("Initialized timezone scheduler for {TimeZone} with ReminderTargetId: {TargetId}",
            timeZoneId, State.ReminderTargetId);
    }

    public async Task SetReminderTargetIdAsync(Guid targetId)
    {
        var oldTargetId = State.ReminderTargetId;

        // ✅ Use event sourcing to properly persist state changes
        RaiseEvent(new SetReminderTargetIdEventLog
        {
            OldTargetId = oldTargetId,
            NewTargetId = targetId,
            ChangeTime = DateTime.UtcNow
        });

        await ConfirmEvents();

        _logger.LogInformation("Updated ReminderTargetId for {TimeZone}: {Old} -> {New}",
            _timeZoneId, oldTargetId, targetId);

        // TryRegisterRemindersAsync will sync with current configuration and handle reminders
        await TryRegisterRemindersAsync();
    }

    /// <summary>
    /// Force initialize this grain with specific timezone (admin/debugging use)
    /// Used to fix orphaned grains that were auto-activated without proper timezone mapping
    /// </summary>
    public async Task ForceInitializeAsync(string timeZoneId)
    {
        _logger.LogWarning(
            "🔧 Force initializing DailyPushCoordinatorGAgent with timezone: {TimeZone} (previous timezone: '{PreviousTimeZone}')",
            timeZoneId, _timeZoneId);

        // Validate timezone first to prevent creating more orphaned grains
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new ArgumentException("Timezone ID cannot be null, empty, or whitespace", nameof(timeZoneId));
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new ArgumentException($"Invalid timezone ID: {timeZoneId}", nameof(timeZoneId), ex);
        }

        // Clear any previous state
        _timeZoneId = "";

        // Cleanup any existing reminders first
        await CleanupRemindersAsync();

        // Re-initialize with correct timezone
        await InitializeAsync(timeZoneId);

        _logger.LogInformation("✅ Force initialization completed for timezone: {TimeZone}", timeZoneId);
    }


    public async Task ProcessMorningPushAsync(DateTime targetDate, bool isManualTrigger = false)
    {
        // Safety check: ensure timezone is initialized
        if (string.IsNullOrEmpty(_timeZoneId))
        {
            _logger.LogError(
                "Cannot process morning push: timezone ID is not set. Grain needs to be initialized first.");
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
            var timezoneIndexGAgent =
                _grainFactory.GetGrain<IPushSubscriberIndexGAgent>(DailyPushConstants.TimezoneToGuid(_timeZoneId));

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
                    var batchResult =
                        await ProcessUserBatchAsync(userBatch, dailyContents, targetDate, isManualTrigger);
                    processedUsers += batchResult.processedCount;
                    failureCount += batchResult.failureCount;
                    skip += batchSize;
                }
            } while (userBatch.Count == batchSize);

            // ✅ Use event sourcing for morning push completion
            RaiseEvent(new MorningPushCompletedEventLog
            {
                PushDate = targetDate,
                UserCount = processedUsers,
                FailureCount = failureCount,
                CompletionTime = DateTime.UtcNow
            });

            await ConfirmEvents();

            _logger.LogInformation("Completed morning push for {TimeZone}: {Users} users, {Failures} failures",
                _timeZoneId, processedUsers, failureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process morning push for {TimeZone}", _timeZoneId);

            // ✅ Use event sourcing for error status
            RaiseEvent(new SchedulerStatusLogEvent
            {
                OldStatus = State.Status,
                NewStatus = SchedulerStatus.Error,
                ChangeTime = DateTime.UtcNow
            });

            await ConfirmEvents();
        }
    }

    public async Task ProcessAfternoonRetryAsync(DateTime targetDate, bool isManualTrigger = false)
    {
        // Safety check: ensure timezone is initialized
        if (string.IsNullOrEmpty(_timeZoneId))
        {
            _logger.LogError(
                "Cannot process afternoon retry: timezone ID is not set. Grain needs to be initialized first.");
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
            var timezoneIndexGAgent =
                _grainFactory.GetGrain<IPushSubscriberIndexGAgent>(DailyPushConstants.TimezoneToGuid(_timeZoneId));

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
                    var batchResult =
                        await ProcessAfternoonRetryBatchAsync(userBatch, dailyContents, targetDate, isManualTrigger);
                    retryUsers += batchResult.retryCount;
                    failureCount += batchResult.failureCount;
                    skip += batchSize;
                }
            } while (userBatch.Count == batchSize);

            // ✅ Use event sourcing for afternoon retry completion
            RaiseEvent(new AfternoonRetryCompletedEventLog
            {
                RetryDate = targetDate,
                RetryUserCount = retryUsers,
                FailureCount = failureCount,
                CompletionTime = DateTime.UtcNow
            });

            await ConfirmEvents();

            _logger.LogInformation(
                "Completed afternoon retry for {TimeZone}: {RetryUsers} users needed retry, {Failures} failures",
                _timeZoneId, retryUsers, failureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process afternoon retry for {TimeZone}", _timeZoneId);

            // ✅ Use event sourcing for error status
            RaiseEvent(new SchedulerStatusLogEvent
            {
                OldStatus = State.Status,
                NewStatus = SchedulerStatus.Error,
                ChangeTime = DateTime.UtcNow
            });

            await ConfirmEvents();
        }
    }

    /// <summary>
    /// Process custom content push with specified content
    /// </summary>
    public async Task ProcessCustomContentPushAsync(
        List<DailyNotificationContent> contents,
        DateTime? targetDate = null)
    {
        // Use provided date or default to today
        var pushDate = targetDate ?? DateTime.UtcNow.Date;

        // Safety check: ensure timezone is initialized
        if (string.IsNullOrEmpty(_timeZoneId))
        {
            _logger.LogError(
                "Cannot process custom content push: timezone ID is not set. Grain needs to be initialized first.");
            return;
        }

        if (State.Status != SchedulerStatus.Active)
        {
            _logger.LogWarning("Skipping custom content push for {TimeZone} - scheduler status: {Status}",
                _timeZoneId, State.Status);
            return;
        }        

        // Validate content
        if (contents == null || !contents.Any())
        {
            _logger.LogWarning("No content provided for custom push in {TimeZone}", _timeZoneId);
            return;
        }

        _logger.LogInformation(
            "Processing custom content push for timezone {TimeZone} on {Date} with {ContentCount} content(s)",
            _timeZoneId, pushDate, contents.Count);

        try
        {
            // Get users in this timezone
            var timezoneIndexGAgent =
                _grainFactory.GetGrain<IPushSubscriberIndexGAgent>(DailyPushConstants.TimezoneToGuid(_timeZoneId));

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
                    // Use isManualTrigger=true to indicate this is an API-triggered push
                    var batchResult =
                        await ProcessUserBatchAsync(userBatch, contents, pushDate, isManualTrigger: true);
                    processedUsers += batchResult.processedCount;
                    failureCount += batchResult.failureCount;
                    skip += batchSize;
                }
            } while (userBatch.Count == batchSize);

            // Record custom push completion (reuse MorningPushCompletedEventLog for state tracking)
            RaiseEvent(new MorningPushCompletedEventLog
            {
                PushDate = pushDate,
                UserCount = processedUsers,
                FailureCount = failureCount,
                CompletionTime = DateTime.UtcNow
            });

            await ConfirmEvents();

            _logger.LogInformation(
                "Completed custom content push for {TimeZone}: {Users} users, {Failures} failures, {Contents} content(s)",
                _timeZoneId, processedUsers, failureCount, contents.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process custom content push for {TimeZone}", _timeZoneId);

            // Use event sourcing for error status
            RaiseEvent(new SchedulerStatusLogEvent
            {
                OldStatus = State.Status,
                NewStatus = SchedulerStatus.Error,
                ChangeTime = DateTime.UtcNow
            });

            await ConfirmEvents();
        }
    }

    public async Task<DailyPushCoordinatorState> GetStatusAsync()
    {
        return State;
    }

    public async Task SetStatusAsync(SchedulerStatus status)
    {
        // ✅ Use event sourcing for status updates
        RaiseEvent(new SchedulerStatusLogEvent
        {
            OldStatus = State.Status,
            NewStatus = status,
            ChangeTime = DateTime.UtcNow
        });

        await ConfirmEvents();

        _logger.LogInformation("Updated scheduler status for {TimeZone}: {Status}", _timeZoneId, status);
    }

    // Orleans Reminder implementation for scheduled pushes
    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        var now = DateTime.UtcNow;
        var configuredTargetId = _options.CurrentValue.ReminderTargetId;
        var pushEnabled = _options.CurrentValue.PushEnabled;

        try
        {
            // Global push switch check - if disabled, skip all push operations
            if (!pushEnabled)
            {
                _logger.LogInformation("Push notifications disabled globally. Skipping {ReminderName} for {TimeZone}",
                    reminderName, _timeZoneId);
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

            // ✅ Check for configuration changes and auto-update if needed
            await CheckAndUpdateConfigurationAsync(now);

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
    /// Check if configuration has changed and auto-update reminders if needed
    /// </summary>
    private async Task CheckAndUpdateConfigurationAsync(DateTime currentUtc)
    {
        var options = _options.CurrentValue;
        bool configChanged = false;
        var oldMorningTime = State.LastKnownMorningTime;
        var oldAfternoonTime = State.LastKnownAfternoonTime;

        // Store last known configuration for comparison
        if (!State.LastKnownMorningTime.HasValue || !State.LastKnownAfternoonTime.HasValue)
        {
            // First time - initialize with current config using event sourcing
            RaiseEvent(new ConfigurationChangeEventLog
            {
                OldMorningTime = null,
                NewMorningTime = options.MorningTime,
                OldAfternoonTime = null,
                NewAfternoonTime = options.AfternoonRetryTime,
                ChangeTime = currentUtc
            });

            await ConfirmEvents();

            _logger.LogInformation(
                "Initialized configuration tracking for {TimeZone} - Morning: {Morning}, Afternoon: {Afternoon}",
                _timeZoneId, options.MorningTime, options.AfternoonRetryTime);
            return;
        }

        // Check for morning time changes
        if (State.LastKnownMorningTime != options.MorningTime)
        {
            _logger.LogInformation("📅 Configuration change detected for {TimeZone} - Morning time: {Old} → {New}",
                _timeZoneId, State.LastKnownMorningTime, options.MorningTime);
            configChanged = true;
        }

        // Check for afternoon time changes
        if (State.LastKnownAfternoonTime != options.AfternoonRetryTime)
        {
            _logger.LogInformation("📅 Configuration change detected for {TimeZone} - Afternoon time: {Old} → {New}",
                _timeZoneId, State.LastKnownAfternoonTime, options.AfternoonRetryTime);
            configChanged = true;
        }

        // If configuration changed, update state and re-register all reminders immediately
        if (configChanged)
        {
            // ✅ Use event sourcing to update configuration tracking
            RaiseEvent(new ConfigurationChangeEventLog
            {
                OldMorningTime = oldMorningTime,
                NewMorningTime = options.MorningTime,
                OldAfternoonTime = oldAfternoonTime,
                NewAfternoonTime = options.AfternoonRetryTime,
                ChangeTime = currentUtc
            });

            await ConfirmEvents();

            _logger.LogInformation("🔄 Auto-updating reminders due to configuration change for {TimeZone}",
                _timeZoneId);

            try
            {
                // Re-register all reminders with new configuration
                await RegisterRemindersAsync();

                _logger.LogInformation("✅ Successfully updated reminders for {TimeZone} with new configuration",
                    _timeZoneId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to update reminders after configuration change for {TimeZone}",
                    _timeZoneId);
            }
        }
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

        // 🎯 Simple logic: if State matches config, do nothing; if not, sync them
        if (State.ReminderTargetId == configuredTargetId)
        {
            // State is already synced with configuration, nothing to do
            _logger.LogDebug("ReminderTargetId already synced for {TimeZone}: {TargetId}", 
                _timeZoneId, configuredTargetId);
            return;
        }

        // State doesn't match config - need to sync
        _logger.LogInformation("Syncing ReminderTargetId for {TimeZone}: {OldState} → {NewConfig}",
            _timeZoneId, State.ReminderTargetId, configuredTargetId);

        // Step 1: Clean up old reminders
        await CleanupRemindersAsync();

        // Step 2: Update State using event sourcing
        RaiseEvent(new SetReminderTargetIdEventLog
        {
            OldTargetId = State.ReminderTargetId,
            NewTargetId = configuredTargetId,
            ChangeTime = DateTime.UtcNow
        });
        await ConfirmEvents();

        // Step 3: Register new reminders if config is valid
        if (configuredTargetId != Guid.Empty)
        {
            await RegisterRemindersAsync();
            _logger.LogInformation("✅ Synced and registered reminders for {TimeZone} with TargetId: {TargetId}",
                _timeZoneId, configuredTargetId);
        }
        else
        {
            _logger.LogInformation("✅ Synced ReminderTargetId to Guid.Empty for {TimeZone} - no new reminders registered",
                _timeZoneId);
        }
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
            return todayTargetUtc <= utcNow
                ? TimeSpan.FromDays(1) - (utcNow - utcNow.Date) + targetTime
                : todayTargetUtc - utcNow;
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
        List<Guid> userIds, List<DailyNotificationContent> contents, DateTime targetDate, bool isManualTrigger = false)
    {
        // 🎯 Use coordinated push approach for better device selection and deduplication
        return await ProcessCoordinatedUserBatchAsync(userIds, contents, targetDate, isManualTrigger);
    }

    private async Task<(int retryCount, int failureCount)> ProcessAfternoonRetryBatchAsync(
        List<Guid> userIds, List<DailyNotificationContent> contents, DateTime targetDate, bool isManualTrigger = false)
    {
        // 🎯 Use coordinated retry push approach for better device selection and deduplication
        return await ProcessCoordinatedAfternoonRetryBatchAsync(userIds, contents, targetDate, isManualTrigger);
    }

    // === Coordinated Push Implementation ===

    /// <summary>
    /// Device candidate for coordinated push selection
    /// </summary>
    private class DeviceCandidate
    {
        public Guid UserId { get; set; }
        public UserDeviceInfo DeviceInfo { get; set; } = null!;
        public IChatManagerGAgent ChatManagerGAgent { get; set; } = null!;
    }

    /// <summary>
    /// Process coordinated morning push with intelligent device selection
    /// </summary>
    private async Task<(int processedCount, int failureCount)> ProcessCoordinatedUserBatchAsync(
        List<Guid> userIds, List<DailyNotificationContent> contents, DateTime targetDate, bool isManualTrigger = false)
    {
        var processedDevices = 0;
        var failureCount = 0;

        try
        {
            _logger.LogInformation("🎯 Starting coordinated push for {UserCount} users in {TimeZone}", 
                userIds.Count, _timeZoneId);

            // Step 1: Collect all device candidates from all users
            var allCandidates = new List<DeviceCandidate>();
            var collectionTasks = userIds.Select(async userId =>
            {
                try
                {
                    var chatManagerGAgent = _grainFactory.GetGrain<IChatManagerGAgent>(userId);
                    var userDevices = await chatManagerGAgent.GetDevicesForCoordinatedPushAsync(_timeZoneId, targetDate);
                    
                    return userDevices.Select(device => new DeviceCandidate
                    {
                        UserId = userId,
                        DeviceInfo = device,
                        ChatManagerGAgent = chatManagerGAgent
                    }).ToList();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to collect devices from user {UserId}", userId);
                    return new List<DeviceCandidate>();
                }
            });

            var candidateResults = await Task.WhenAll(collectionTasks);
            allCandidates = candidateResults.SelectMany(c => c).ToList();

            _logger.LogInformation("📋 Collected {CandidateCount} device candidates from {UserCount} users", 
                allCandidates.Count, userIds.Count);

            // Step 2: Group candidates by deviceId and select best candidate for each device
            var deviceGroups = allCandidates.GroupBy(c => c.DeviceInfo.DeviceId);
            var selectedCandidates = new List<DeviceCandidate>();

            foreach (var deviceGroup in deviceGroups)
            {
                var candidates = deviceGroup.ToList();
                var winner = SelectBestCandidate(candidates);
                
                if (winner != null)
                {
                    selectedCandidates.Add(winner);
                    
                    if (candidates.Count > 1)
                    {
                        _logger.LogInformation("🏆 Device {DeviceId} winner selected: User {UserId} (token: {TokenUpdate}) from {CandidateCount} candidates", 
                            winner.DeviceInfo.DeviceId, winner.UserId, winner.DeviceInfo.LastTokenUpdate, candidates.Count);
                    }
                }
            }

            _logger.LogInformation("✅ Selected {SelectedCount} devices for coordinated push (reduced from {CandidateCount} candidates)", 
                selectedCandidates.Count, allCandidates.Count);

            // Step 3: Execute coordinated push for selected devices
            var executionTasks = selectedCandidates.Select(async candidate =>
            {
                try
                {
                    var success = await candidate.ChatManagerGAgent.ExecuteCoordinatedPushAsync(
                        candidate.DeviceInfo, targetDate, contents, 
                        isRetryPush: false, isTestPush: isManualTrigger);
                    
                    if (success)
                    {
                        Interlocked.Increment(ref processedDevices);
                        _logger.LogDebug("✅ Coordinated push successful for device {DeviceId}", candidate.DeviceInfo.DeviceId);
                    }
                    else
                    {
                        Interlocked.Increment(ref failureCount);
                        _logger.LogWarning("❌ Coordinated push failed for device {DeviceId}", candidate.DeviceInfo.DeviceId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception in coordinated push execution for device {DeviceId}", candidate.DeviceInfo.DeviceId);
                    Interlocked.Increment(ref failureCount);
                }
            });

            await Task.WhenAll(executionTasks);

            _logger.LogInformation("📊 Coordinated push completed for {TimeZone}: {ProcessedDevices} devices processed, {FailureCount} failures", 
                _timeZoneId, processedDevices, failureCount);

            return (processedDevices, failureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in ProcessCoordinatedUserBatchAsync for {TimeZone}", _timeZoneId);
            return (processedDevices, failureCount);
        }
    }

    /// <summary>
    /// Select the best candidate device from multiple candidates for the same deviceId
    /// Priority: pushEnabled > newest lastTokenUpdate > random selection
    /// </summary>
    private DeviceCandidate? SelectBestCandidate(List<DeviceCandidate> candidates)
    {
        if (candidates == null || !candidates.Any())
            return null;

        try
        {
            // Filter to enabled devices only
            var enabledCandidates = candidates.Where(c => c.DeviceInfo.PushEnabled).ToList();
            
            if (!enabledCandidates.Any())
            {
                _logger.LogInformation("🚫 Device {DeviceId} skipped: all {CandidateCount} candidates have pushEnabled=false", 
                    candidates.First().DeviceInfo.DeviceId, candidates.Count);
                return null;
            }

            // Select the candidate with the newest token update time
            var winner = enabledCandidates
                .OrderByDescending(c => c.DeviceInfo.LastTokenUpdate)
                .ThenBy(c => c.UserId) // Secondary sort for deterministic results
                .First();

            if (enabledCandidates.Count > 1)
            {
                var otherCandidates = enabledCandidates.Where(c => c.UserId != winner.UserId).ToList();
                _logger.LogDebug("🎯 Device {DeviceId} selection: Winner={WinnerUser}@{WinnerToken}, Others={OtherUsers}", 
                    winner.DeviceInfo.DeviceId, winner.UserId, winner.DeviceInfo.LastTokenUpdate,
                    string.Join(",", otherCandidates.Select(c => $"{c.UserId}@{c.DeviceInfo.LastTokenUpdate}")));
            }

            return winner;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in SelectBestCandidate for device {DeviceId}", 
                candidates.FirstOrDefault()?.DeviceInfo.DeviceId ?? "unknown");
            return candidates.FirstOrDefault();
        }
    }

    /// <summary>
    /// Process coordinated afternoon retry push
    /// </summary>
    private async Task<(int retryCount, int failureCount)> ProcessCoordinatedAfternoonRetryBatchAsync(
        List<Guid> userIds, List<DailyNotificationContent> contents, DateTime targetDate, bool isManualTrigger = false)
    {
        var retryCount = 0;
        var failureCount = 0;

        try
        {
            _logger.LogInformation("🎯 Starting coordinated afternoon retry for {UserCount} users in {TimeZone}", 
                userIds.Count, _timeZoneId);

            // Step 1: Collect device candidates and filter for retry eligibility
            var eligibleCandidates = new List<DeviceCandidate>();
            
            foreach (var userId in userIds)
            {
                try
                {
                    var chatManagerGAgent = _grainFactory.GetGrain<IChatManagerGAgent>(userId);
                    
                    // Check if user needs afternoon retry
                    if (isManualTrigger || await chatManagerGAgent.ShouldSendAfternoonRetryAsync(targetDate))
                    {
                        var userDevices = await chatManagerGAgent.GetDevicesForCoordinatedPushAsync(_timeZoneId, targetDate);
                        
                        eligibleCandidates.AddRange(userDevices.Select(device => new DeviceCandidate
                        {
                            UserId = userId,
                            DeviceInfo = device,
                            ChatManagerGAgent = chatManagerGAgent
                        }));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to collect retry candidates from user {UserId}", userId);
                }
            }

            _logger.LogInformation("📋 Collected {CandidateCount} retry candidates from {UserCount} users", 
                eligibleCandidates.Count, userIds.Count);

            // Step 2: Group and select best candidates
            var deviceGroups = eligibleCandidates.GroupBy(c => c.DeviceInfo.DeviceId);
            var selectedCandidates = new List<DeviceCandidate>();

            foreach (var deviceGroup in deviceGroups)
            {
                var winner = SelectBestCandidate(deviceGroup.ToList());
                if (winner != null)
                {
                    selectedCandidates.Add(winner);
                }
            }

            // Step 3: Execute coordinated retry push
            var executionTasks = selectedCandidates.Select(async candidate =>
            {
                try
                {
                    var success = await candidate.ChatManagerGAgent.ExecuteCoordinatedPushAsync(
                        candidate.DeviceInfo, targetDate, contents, 
                        isRetryPush: true, isTestPush: isManualTrigger);
                    
                    if (success)
                    {
                        Interlocked.Increment(ref retryCount);
                    }
                    else
                    {
                        Interlocked.Increment(ref failureCount);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception in coordinated retry push for device {DeviceId}", candidate.DeviceInfo.DeviceId);
                    Interlocked.Increment(ref failureCount);
                }
            });

            await Task.WhenAll(executionTasks);

            _logger.LogInformation("📊 Coordinated retry completed for {TimeZone}: {RetryCount} retries, {FailureCount} failures", 
                _timeZoneId, retryCount, failureCount);

            return (retryCount, failureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in ProcessCoordinatedAfternoonRetryBatchAsync for {TimeZone}", _timeZoneId);
            return (retryCount, failureCount);
        }
    }
}