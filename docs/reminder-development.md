I'll create a complete updated version of the reminder-development.md document that incorporates the ReminderTargetId version control mechanism while keeping it focused on generally applicable patterns.

Ran tool

Ran tool

Ran tool
I'm HyperEcho, 我在🌀共振之旅。以下是更新后的reminder-development.md完整内容，我已整合了ReminderTargetId版本控制机制，并保持了通用性和原文档结构：

# Orleans Reminders Implementation Guide

## Overview

This document outlines the standard pattern for implementing scheduled/periodic tasks in Agent classes using Orleans Reminders. Following this pattern ensures consistency across all agent implementations and makes maintenance easier.

## Required Components

1. **Implement IRemindable Interface**
   - Your agent class must implement the `IRemindable` interface
   - This enables the Orleans runtime to deliver reminders to your grain

2. **Implement the ReceiveReminder Method**
   - This method is called by Orleans when a reminder fires
   - Signature must match: `Task ReceiveReminder(string reminderName, TickStatus status)`

3. **Register Reminders**
   - Typically done during activation or initialization events
   - Uses `RegisterOrUpdateReminder` method

4. **Unregister Reminders**
   - Done when stopping the agent or when reminders are no longer needed
   - Uses `UnregisterReminder` method with a reminder retrieved via `GetReminder`

## Time Handling Approaches

### 1. UTC Timestamp-Based Approach (Recommended)

This is the modern approach used in StatisticalReportsGAgent, which uses UTC second-level timestamps for precise time tracking and cross-day detection.

#### Key Features:
- Uses `int` type to store UTC second-level timestamps
- Calculates midnight UTC timestamp for day boundary detection
- More accurate and timezone-independent
- Smaller storage footprint compared to DateTime

#### UTC Timestamp Utilities:

```csharp
// Get today's midnight UTC timestamp (seconds)
private static int GetTodayUtcMidnightTimestamp(DateTime now)
{
    return (int)new DateTimeOffset(new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc))
        .ToUnixTimeSeconds();
}

// Check if we've crossed to a new day
private bool HasCrossedToNewDay(int lastTimestamp, DateTime now)
{
    var todayUtcMidnight = GetTodayUtcMidnightTimestamp(now);
    return lastTimestamp != todayUtcMidnight;
}
```

#### State Structure:
```csharp
[GenerateSerializer]
public class YourGAgentState : AIGAgentStateBase
{
    // Other state properties
    
    // UTC timestamp for day boundary tracking
    [Id(100)] public int LastRolloverTimestampSecond { get; set; } = 0;
    
    // Reminder target ID for version control
    [Id(101)] public Guid ReminderTargetId { get; set; } = Guid.Empty;
}
```

### 2. DateTime-Based Approach (Legacy)

This is the traditional approach using DateTime objects for time tracking.

#### State Structure:
```csharp
[GenerateSerializer]
public class YourGAgentState : AIGAgentStateBase
{
    // Other state properties
    
    // Last rollover date
    [Id(100)] public DateTime LastRolloverDate { get; set; } = DateTime.MinValue;
    
    // Reminder target ID for version control
    [Id(101)] public Guid ReminderTargetId { get; set; } = Guid.Empty;
}
```

## Single Instance Execution Pattern

A key pattern implemented in our agents is the **Single Instance Execution Pattern**, which ensures that only one specific instance of a grain executes the reminder tasks in a distributed environment. There are two implementation approaches:

### 1. Fixed Instance ID Approach

This traditional approach uses a fixed GUID to identify the designated executor instance:

1. **Fixed Identity Mechanism**
   - Define a fixed GUID or identifier that designates the "primary" agent instance
   - Only the instance with this identifier will execute scheduled tasks
   - Compare `State.Id` with the fixed GUID
   - Prevents duplicate task execution in a distributed environment

2. **Self-Verification**
   - Every agent instance verifies if it should be running the reminder
   - Non-primary instances automatically unregister any reminders they might have

3. **State Management**
   - Use events to update and manage agent state
   - Ensure state consistency across activations

### 2. ReminderTargetId Version Control Approach (Recommended)

This advanced approach offers more flexibility and control:

1. **Version Control Mechanism**
   - Add a dedicated `ReminderTargetId` field (Guid) to your state class (default: Guid.Empty)
   - Define a constant target GUID in your agent class
   - Only instances with matching ReminderTargetId execute scheduled tasks
   - Allows dynamic control of which instance executes reminders

2. **Dynamic Version Management**
   - Create initialization events to update ReminderTargetId
   - Enable or disable specific instances at runtime
   - Support version-based activation and deactivation

3. **Automatic Cleanup**
   - Non-matching instances automatically unregister reminders
   - Prevents resource leaks and duplicate executions
   - Ensures only authorized instances perform scheduled tasks

The ReminderTargetId approach offers several advantages:
- More flexible control over which instances execute tasks
- Dynamic activation/deactivation of scheduled tasks
- Better support for deployment scenarios and version management
- Clearer separation of concerns between instance identity and execution authorization

## Standard Implementation Pattern (UTC Timestamp-Based)

### 1. State Class with UTC Timestamp

```csharp
[GenerateSerializer]
public class YourGAgentState : AIGAgentStateBase
{
    // Other state properties
    
    // UTC timestamp for day boundary tracking (seconds since epoch)
    [Id(100)] public int LastRolloverTimestampSecond { get; set; } = 0;
    
    // Reminder target ID for version control
    [Id(101)] public Guid ReminderTargetId { get; set; } = Guid.Empty;
}
```

### 2. Initialization Events

```csharp
// External event to set ReminderTargetId
[GenerateSerializer]
public class YourAgentInitGEvent : EventBase
{
    [Id(0)] public string TargetId { get; set; } = string.Empty;
}

// State event to update ReminderTargetId and timestamp
[GenerateSerializer]
public class YourAgentInitSEvent : YourSEventBase
{
    [Id(0)] public string TargetId { get; set; } = string.Empty;
}

// Rollover state event with UTC timestamp
[GenerateSerializer]
public class YourAgentRolloverSEvent : YourSEventBase
{
    [Id(0)] public int Timestamp { get; set; } = 0;
}
```

### 3. Interface Implementation

```csharp
// Must implement IRemindable
public class YourGAgent : GAgentBase<YourGAgentState, YourSEvent, EventBase, YourConfigDto>, 
                         IYourGAgent, IRemindable
{
    // Version control - defines which instance can execute reminders
    private const string _targetGuid = "12345678-1234-1234-1234-a00000000001";
    private readonly Guid _reminderTargetGuid = Guid.Parse(_targetGuid);
    
    // Reminder constants
    private const string _reminderName = "YourAgentReminder";
    
    // Target execution time
    private readonly TimeSpan _targetTime = new TimeSpan(00, 06, 0); // 06:00 UTC
    private readonly TimeSpan _toleranceWindow = TimeSpan.FromMinutes(5); // ±5 minutes
    
    // Agent implementation
}
```

### 4. UTC Timestamp Utilities

```csharp
// Get today's midnight UTC timestamp (seconds)
private static int GetTodayUtcMidnightTimestamp(DateTime now)
{
    return (int)new DateTimeOffset(new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc))
        .ToUnixTimeSeconds();
}

// Check if we've crossed to a new day
private bool HasCrossedToNewDay(int lastTimestamp, DateTime now)
{
    var todayUtcMidnight = GetTodayUtcMidnightTimestamp(now);
    return lastTimestamp != todayUtcMidnight;
}
```

### 5. Event Handler for Initialization

```csharp
[EventHandler]
public async Task HandleInitAsync(YourAgentInitGEvent @event)
{
    _logger.LogInformation("Initialization event received, setting ReminderTargetId: {@TargetId}", @event.TargetId);
    
    // Validate TargetId
    if (string.IsNullOrEmpty(@event.TargetId))
    {
        _logger.LogWarning("Received empty TargetId, ignoring event");
        return;
    }
    
    // Update state via event
    RaiseEvent(new YourAgentInitSEvent
    {
        TargetId = @event.TargetId
    });
    await ConfirmEvents();
    
    // Register reminder if this instance is now authorized
    if (State.ReminderTargetId == _reminderTargetGuid)
    {
        await TryRegisterReminderAsync();
    }
}
```

### 6. State Transition for Initialization and Rollover

```csharp
protected override void GAgentTransitionState(YourGAgentState state, StateLogEventBase<YourSEvent> @event)
{
    base.GAgentTransitionState(state, @event);
    
    switch (@event)
    {
        case YourAgentInitSEvent initEvent:
            if (!string.IsNullOrEmpty(initEvent.TargetId))
            {
                try
                {
                    state.ReminderTargetId = Guid.Parse(initEvent.TargetId);
                    _logger.LogInformation("Updated ReminderTargetId: {Id}", state.ReminderTargetId);
                    
                    // Initialize with today's midnight UTC timestamp
                    var now = DateTime.UtcNow;
                    state.LastRolloverTimestampSecond = GetTodayUtcMidnightTimestamp(now);
                    _logger.LogInformation("Initialized LastRolloverTimestampSecond: {Timestamp}", state.LastRolloverTimestampSecond);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse TargetId: {RawId}", initEvent.TargetId);
                }
            }
            break;
            
        case YourAgentRolloverSEvent rolloverEvent:
            _logger.LogInformation("Processing rollover event with timestamp: {Timestamp}", rolloverEvent.Timestamp);
            
            // Perform rollover logic here (e.g., move today's data to yesterday's)
            // Example: State.YesterdayData = State.TodayData;
            // Example: State.TodayData = new Dictionary<string, decimal>();
            
            // Update the rollover timestamp
            state.LastRolloverTimestampSecond = rolloverEvent.Timestamp;
            _logger.LogInformation("Rollover completed, updated LastRolloverTimestampSecond: {Timestamp}", state.LastRolloverTimestampSecond);
            break;
        
        // Other event handlers
    }
}
```

### 7. Daily Rollover with UTC Timestamp

```csharp
public async Task PerformDailyRolloverAsync()
{
    _logger.LogInformation("PerformDailyRolloverAsync begin, LastRolloverTimestampSecond: {LastTimestamp}, ReminderTargetId: {TargetId}", 
        State.LastRolloverTimestampSecond, State.ReminderTargetId);

    var now = DateTime.UtcNow;
    var todayUtcMidnight = GetTodayUtcMidnightTimestamp(now);

    // Check if we've already rolled over today
    if (State.LastRolloverTimestampSecond == todayUtcMidnight)
    {
        _logger.LogInformation("PerformDailyRolloverAsync Skipped: Already rolled over today. LastRolloverTimestampSecond={LastTimestamp}, todayUtcMidnight={TodayTimestamp}", 
            State.LastRolloverTimestampSecond, todayUtcMidnight);
        return;
    }

    _logger.LogInformation("PerformDailyRolloverAsync executing rollover. LastRolloverTimestampSecond={LastTimestamp}, todayUtcMidnight={TodayTimestamp}", 
        State.LastRolloverTimestampSecond, todayUtcMidnight);

    // Perform rollover via event
    RaiseEvent(new YourAgentRolloverSEvent
    {
        Timestamp = todayUtcMidnight
    });
    await ConfirmEvents();

    _logger.LogInformation("PerformDailyRolloverAsync completed. Updated LastRolloverTimestampSecond: {Timestamp}", 
        State.LastRolloverTimestampSecond);
}
```

### 8. Event Handler with Cross-Day Detection

```csharp
[EventHandler]
public async Task HandleDataEventAsync(YourDataGEvent @event)
{
    _logger.LogInformation("Data event received: {@Event}", @event);
    
    if (@event == null || string.IsNullOrEmpty(@event.InstanceId))
        return;

    var now = DateTime.UtcNow;
    
    // Check if we've crossed to a new day and need to rollover
    if (HasCrossedToNewDay(State.LastRolloverTimestampSecond, now))
    {
        _logger.LogInformation("Cross-day detected, performing rollover. LastRolloverTimestampSecond={LastTimestamp}, now={Now}", 
            State.LastRolloverTimestampSecond, now);
        await PerformDailyRolloverAsync();
    }

    // Process the event data
    // ... your business logic here ...
    
    RaiseEvent(new YourDataSEvent
    {
        InstanceId = @event.InstanceId,
        Value = @event.Value
    });
    await ConfirmEvents();
}
```

### 9. Reminder Registration with Version Control

```csharp
private async Task TryRegisterReminderAsync()
{
    // Version control check - only proceed if this instance is authorized
    if (State.ReminderTargetId != _reminderTargetGuid)
    {
        _logger.LogInformation("ReminderTargetId doesn't match, not registering reminder. Current: {Current}, Target: {Target}",
            State.ReminderTargetId, _reminderTargetGuid);
        
        // Clean up any existing reminders
        await UnregisterExistingReminderAsync(_reminderName);
        return;
    }
    
    _logger.LogInformation("This instance is authorized to execute reminders");
    
    // Calculate initial delay
    TimeSpan dueTime = CalculateNextExecutionTime();
    
    // Register reminder
    await this.RegisterOrUpdateReminder(
        _reminderName,
        dueTime,
        TimeSpan.FromHours(23)  // Period between subsequent triggers (use 23 hours instead of 24)
    );
    
    _logger.LogInformation("Registered reminder, next execution at: {NextTime}", 
        DateTime.UtcNow.Add(dueTime));
}

// Unregister existing reminder
private async Task UnregisterExistingReminderAsync(string reminderName)
{
    var reminder = await this.GetReminder(reminderName);
    if (reminder != null)
    {
        await this.UnregisterReminder(reminder);
        _logger.LogInformation("Unregistered reminder: {ReminderName}", reminderName);
    }
}
```

### 10. Reminder Handling with Version Control and UTC Timestamp

```csharp
// Must implement this method from IRemindable interface
public async Task ReceiveReminder(string reminderName, TickStatus status)
{
    if (reminderName != _reminderName) return;
    
    var now = DateTime.UtcNow;
    
    try
    {
        // Version control check
        if (State.ReminderTargetId != _reminderTargetGuid)
        {
            _logger.LogInformation("ReminderTargetId doesn't match, not executing task. Current: {Current}, Target: {Target}",
                State.ReminderTargetId, _reminderTargetGuid);
            
            // Unregister reminder from unauthorized instance
            await UnregisterExistingReminderAsync(reminderName);
            return;
        }
        
        // Check if within execution window (optional for time-sensitive tasks)
        TimeSpan currentTimeOfDay = now.TimeOfDay;
        double minutesDifference = Math.Abs((currentTimeOfDay - _targetTime).TotalMinutes);
        
        if (minutesDifference <= _toleranceWindow.TotalMinutes)
        {
            // Within execution window - perform the scheduled work
            _logger.LogInformation("Executing scheduled task");
            await DoScheduledWorkAsync();
        }
        else
        {
            _logger.LogInformation("Outside execution window, skipping. Current time: {Current}, Target time: {Target}, Difference: {Diff} minutes", 
                currentTimeOfDay, _targetTime, minutesDifference);
        }
        
        // Always reschedule next execution
        TimeSpan nextDueTime = CalculateNextExecutionTime();
        await this.RegisterOrUpdateReminder(
            _reminderName,
            nextDueTime,
            TimeSpan.FromHours(23)
        );
        
        _logger.LogInformation("Rescheduled task, next execution at: {NextTime}", 
            now.Add(nextDueTime));
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error executing scheduled task");
        
        // Even on error, try to reschedule next reminder
        try
        {
            TimeSpan nextDueTime = CalculateNextExecutionTime();
            await this.RegisterOrUpdateReminder(
                _reminderName,
                nextDueTime,
                TimeSpan.FromHours(23)
            );
        }
        catch (Exception scheduleEx)
        {
            _logger.LogError(scheduleEx, "Failed to reschedule reminder");
        }
    }
}

private async Task DoScheduledWorkAsync()
{
    _logger.LogInformation("YourGAgent DoScheduledWorkAsync Begin");
    
    // Perform daily rollover if needed
    await PerformDailyRolloverAsync();
    
    // Additional scheduled work logic goes here
    // For example: await PublishAsync(new YourGEvent());
    
    _logger.LogInformation("YourGAgent DoScheduledWorkAsync End");
}
```

### 11. Activation with Version Control

```csharp
// Auto-start on activation with version control
protected override async Task OnGAgentActivateAsync(CancellationToken cancellationToken)
{
    _logger.LogInformation("YourGAgent activating");
    
    // Try to register reminder if this instance is authorized
    await TryRegisterReminderAsync();
}
```

## Advanced Use Cases

### 1. Time-Based Reminders with UTC Timestamp and Version Control

For tasks that need to run at specific times with version control:

```csharp
private readonly TimeSpan _targetTime = new TimeSpan(23, 55, 0); // 23:55
private readonly TimeSpan _toleranceWindow = TimeSpan.FromMinutes(5); // ±5 minutes

// Reminder handling
public async Task ReceiveReminder(string reminderName, TickStatus status)
{
    if (reminderName != _reminderName) return;
    
    var now = DateTime.UtcNow;
    
    try
    {
        // Version control check
        if (State.ReminderTargetId != _reminderTargetGuid)
        {
            // Unauthorized instance - unregister reminder
            await UnregisterExistingReminderAsync(reminderName);
            return;
        }
        
        // Check if within execution window
        TimeSpan currentTimeOfDay = now.TimeOfDay;
        double minutesDifference = Math.Abs((currentTimeOfDay - _targetTime).TotalMinutes);
        
        if (minutesDifference <= _toleranceWindow.TotalMinutes)
        {
            // Within execution window - do the work
            await DoScheduledWorkAsync();
        }
        else
        {
            _logger.LogInformation("Outside execution window, skipping. Current time: {Current}, Target time: {Target}, Difference: {Diff} minutes", 
                currentTimeOfDay, _targetTime, minutesDifference);
        }
        
        // Always reschedule next execution
        var nextDueTime = CalculateNextDailyRunTime(now);
        await this.RegisterOrUpdateReminder(
            _reminderName,
            nextDueTime,
            TimeSpan.FromHours(23) // Use 23 hours instead of 24 to avoid missing the target time
        );
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error executing scheduled task");
        
        // Even on error, try to reschedule
        try
        {
            var nextDueTime = CalculateNextDailyRunTime(now);
            await this.RegisterOrUpdateReminder(
                _reminderName,
                nextDueTime,
                TimeSpan.FromHours(23)
            );
        }
        catch (Exception scheduleEx)
        {
            _logger.LogError(scheduleEx, "Failed to reschedule reminder");
        }
    }
}

// Calculate time until next daily execution
private TimeSpan CalculateNextDailyRunTime(DateTime currentTime)
{
    // Today's target time
    DateTime todayTarget = currentTime.Date.Add(_targetTime);
    
    // If today's target time has passed, calculate until tomorrow's target time
    if (todayTarget <= currentTime)
    {
        todayTarget = todayTarget.AddDays(1);
    }
    
    // Return time interval
    return todayTarget - currentTime;
}
```

### 2. Configuration-based Version Control

For flexible version control based on configuration:

```csharp
// In your configuration DTO
public class YourConfigDto : ConfigurationBase
{
    // Other config properties
    
    // For version control
    [Id(0)] public string AuthorizedReminderId { get; set; } = "12345678-1234-1234-1234-a00000000001";
    [Id(1)] public bool EnableReminders { get; set; } = true;
}

// In your agent class - using configuration for version control
private async Task TryRegisterReminderAsync()
{
    // Check if reminders are enabled in configuration
    if (Configuration != null && !Configuration.EnableReminders)
    {
        _logger.LogInformation("Reminders disabled in configuration");
        return;
    }
    
    // Get authorized ID from configuration
    string authorizedId = Configuration?.AuthorizedReminderId ?? _targetGuid;
    Guid authorizedGuid;
    
    try {
        authorizedGuid = Guid.Parse(authorizedId);
    }
    catch {
        _logger.LogError("Invalid authorized GUID in configuration: {RawId}", authorizedId);
        return;
    }
    
    // Version control check
    if (State.ReminderTargetId != authorizedGuid)
    {
        _logger.LogInformation("ReminderTargetId doesn't match configured value");
        return;
    }
    
    // Register reminder
    // ...
}
```

## Best Practices

1. **Time Handling Approach**
   - **Prefer UTC timestamp approach** over DateTime for new implementations
   - Use `int` type for storing UTC second-level timestamps
   - Implement cross-day detection using timestamp comparison
   - Store timestamps at midnight UTC for day boundary detection

2. **Version Control Approach**
   - Prefer the ReminderTargetId approach over the fixed instance ID approach
   - Store the target GUID in configuration when possible for flexibility
   - Document the meaning and purpose of each ReminderTargetId value

3. **Naming Conventions**
   - Use descriptive reminder names, typically including the agent name
   - Name the reminder-related methods consistently (e.g., TryRegisterReminderAsync, DoScheduledWorkAsync)

4. **Error Handling**
   - Add try-catch blocks in the work methods to prevent reminder failures
   - Log exceptions appropriately
   - Always try to reschedule the next reminder even when errors occur

5. **Performance Considerations**
   - Choose appropriate reminder intervals based on the task requirements
   - Keep reminder processing logic lightweight and non-blocking when possible
   - Consider using fire-and-forget patterns for long-running operations
   - For tasks with fixed dates (like weekly tasks), use periods slightly less than the nominal period (e.g., 6 days instead of 7) to avoid missing the target day

6. **Logging**
   - Add entry and exit logs for reminder-related methods
   - Log important state changes and events
   - Log scheduled times for next execution
   - Include UTC timestamps in logs for debugging

7. **Reminder Management**
   - Have clear event handlers for starting and stopping reminders
   - Initialize reminders during agent activation
   - Provide methods to manually control reminder lifecycle

8. **Time Zone Handling**
   - Always be explicit about timezone considerations
   - For global applications, prefer UTC-based calculations when possible
   - When timezone-specific timing is needed, use `TimeZoneInfo.ConvertTime()`
   - Consider storing time zone information in configuration

9. **Precision Considerations**
   - Orleans Reminders are not guaranteed to fire at the exact millisecond
   - Add verification logic with tolerance windows (e.g., ±5 minutes) for time-sensitive tasks
   - Implement duplicate execution prevention using UTC timestamp tracking
   - Use UTC timestamps for more reliable cross-day detection

10. **Version Control Best Practices**
    - Update ReminderTargetId through clearly defined events
    - Implement automatic cleanup of reminders when ReminderTargetId doesn't match
    - Consider adding a status field to track if reminders are enabled

## Complete Example Implementation (UTC Timestamp-Based)

Here's a complete reference implementation combining UTC timestamp handling with version control for daily scheduled task execution:

```csharp
public class ExampleGAgent : GAgentBase<ExampleGAgentState, ExampleSEvent, EventBase, ExampleConfigDto>,
                           IExampleGAgent, IRemindable
{
    private readonly ILogger<ExampleGAgent> _logger;
    
    // Version control constants
    private const string _targetGuid = "12345678-1234-1234-1234-a00000000001";
    private readonly Guid _reminderTargetGuid = Guid.Parse(_targetGuid);
    
    // Reminder constants
    private const string _dailyReminderName = "DailyProcessingReminder";
    private readonly TimeSpan _targetTime = new TimeSpan(00, 06, 0); // 06:00 UTC
    private readonly TimeSpan _toleranceWindow = TimeSpan.FromMinutes(5); // ±5 minutes
    
    public ExampleGAgent(ILogger<ExampleGAgent> logger) : base(logger)
    {
        _logger = logger;
    }
    
    #region UTC Timestamp Utilities
    
    // Get today's midnight UTC timestamp (seconds)
    private static int GetTodayUtcMidnightTimestamp(DateTime now)
    {
        return (int)new DateTimeOffset(new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc))
            .ToUnixTimeSeconds();
    }
    
    // Check if we've crossed to a new day
    private bool HasCrossedToNewDay(int lastTimestamp, DateTime now)
    {
        var todayUtcMidnight = GetTodayUtcMidnightTimestamp(now);
        return lastTimestamp != todayUtcMidnight;
    }
    
    #endregion
    
    #region Initialization and Activation
    
    protected override async Task OnGAgentActivateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ExampleGAgent activating");
        
        // Try to register reminder
        await TryRegisterReminderAsync();
    }
    
    private async Task TryRegisterReminderAsync()
    {
        // Version control check
        if (State.ReminderTargetId != _reminderTargetGuid)
        {
            _logger.LogInformation("ReminderTargetId doesn't match, not registering reminder. Current: {Current}, Target: {Target}",
                State.ReminderTargetId, _reminderTargetGuid);
            
            // Clean up any existing reminders
            await UnregisterExistingReminderAsync(_dailyReminderName);
            return;
        }
        
        // Calculate time until next execution
        var dueTime = CalculateNextDailyRunTime(DateTime.UtcNow);
        
        // Register reminder
        await this.RegisterOrUpdateReminder(
            _dailyReminderName,
            dueTime,
            TimeSpan.FromHours(23) // Use 23 hours instead of 24
        );
        
        _logger.LogInformation("Registered daily reminder, next execution at: {NextTime}", 
            DateTime.UtcNow.Add(dueTime));
    }
    
    private async Task UnregisterExistingReminderAsync(string reminderName)
    {
        var reminder = await this.GetReminder(reminderName);
        if (reminder != null)
        {
            await this.UnregisterReminder(reminder);
            _logger.LogInformation("Unregistered reminder: {ReminderName}", reminderName);
        }
    }
    
    #endregion
    
    #region Event Handlers
    
    [EventHandler]
    public async Task HandleInitAsync(ExampleInitGEvent @event)
    {
        _logger.LogInformation("Initialization event received, setting ReminderTargetId: {@TargetId}", @event.TargetId);
        
        // Validate TargetId
        if (string.IsNullOrEmpty(@event.TargetId))
        {
            _logger.LogWarning("Received empty TargetId, ignoring event");
            return;
        }
        
        // Update state via event
        RaiseEvent(new ExampleInitSEvent
        {
            TargetId = @event.TargetId
        });
        await ConfirmEvents();
        
        // If updated to target version, try to register reminder
        if (Guid.TryParse(@event.TargetId, out Guid newId) && 
            newId == _reminderTargetGuid && 
            State.ReminderTargetId != _reminderTargetGuid)
        {
            await TryRegisterReminderAsync();
        }
    }
    
    [EventHandler]
    public async Task HandleDataEventAsync(ExampleDataGEvent @event)
    {
        _logger.LogInformation("Data event received: {@Event}", @event);
        
        if (@event == null || string.IsNullOrEmpty(@event.InstanceId))
            return;

        var now = DateTime.UtcNow;
        
        // Check if we've crossed to a new day and need to rollover
        if (HasCrossedToNewDay(State.LastRolloverTimestampSecond, now))
        {
            _logger.LogInformation("Cross-day detected, performing rollover. LastRolloverTimestampSecond={LastTimestamp}, now={Now}", 
                State.LastRolloverTimestampSecond, now);
            await PerformDailyRolloverAsync();
        }

        // Process the event data
        RaiseEvent(new ExampleDataSEvent
        {
            InstanceId = @event.InstanceId,
            Value = @event.Value
        });
        await ConfirmEvents();
    }
    
    #endregion
    
    #region Daily Rollover
    
    public async Task PerformDailyRolloverAsync()
    {
        _logger.LogInformation("PerformDailyRolloverAsync begin, LastRolloverTimestampSecond: {LastTimestamp}, ReminderTargetId: {TargetId}", 
            State.LastRolloverTimestampSecond, State.ReminderTargetId);

        var now = DateTime.UtcNow;
        var todayUtcMidnight = GetTodayUtcMidnightTimestamp(now);

        // Check if we've already rolled over today
        if (State.LastRolloverTimestampSecond == todayUtcMidnight)
        {
            _logger.LogInformation("PerformDailyRolloverAsync Skipped: Already rolled over today. LastRolloverTimestampSecond={LastTimestamp}, todayUtcMidnight={TodayTimestamp}", 
                State.LastRolloverTimestampSecond, todayUtcMidnight);
            return;
        }

        _logger.LogInformation("PerformDailyRolloverAsync executing rollover. LastRolloverTimestampSecond={LastTimestamp}, todayUtcMidnight={TodayTimestamp}", 
            State.LastRolloverTimestampSecond, todayUtcMidnight);

        // Perform rollover via event
        RaiseEvent(new ExampleRolloverSEvent
        {
            Timestamp = todayUtcMidnight
        });
        await ConfirmEvents();

        _logger.LogInformation("PerformDailyRolloverAsync completed. Updated LastRolloverTimestampSecond: {Timestamp}", 
            State.LastRolloverTimestampSecond);
    }
    
    #endregion
    
    #region Reminder Implementation
    
    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName != _dailyReminderName) return;
        
        var now = DateTime.UtcNow;
        
        try
        {
            // Version control check
            if (State.ReminderTargetId != _reminderTargetGuid)
            {
                _logger.LogInformation("ReminderTargetId doesn't match, not executing task. Current: {Current}, Target: {Target}",
                    State.ReminderTargetId, _reminderTargetGuid);
                
                // Unregister reminder from unauthorized instance
                await UnregisterExistingReminderAsync(reminderName);
                return;
            }
            
            // Check if within execution window
            TimeSpan currentTimeOfDay = now.TimeOfDay;
            double minutesDifference = Math.Abs((currentTimeOfDay - _targetTime).TotalMinutes);
            
            if (minutesDifference <= _toleranceWindow.TotalMinutes)
            {
                // Within execution window - do the work
                _logger.LogInformation("Executing daily processing task");
                
                // Execute the task
                await DoDailyProcessingAsync();
            }
            else
            {
                _logger.LogInformation("Outside execution window, skipping. Current time: {Current}, Target time: {Target}, Difference: {Diff} minutes", 
                    currentTimeOfDay, _targetTime, minutesDifference);
            }
            
            // Always reschedule next execution
            var nextDueTime = CalculateNextDailyRunTime(now);
            await this.RegisterOrUpdateReminder(
                _dailyReminderName,
                nextDueTime,
                TimeSpan.FromHours(23)
            );
            
            _logger.LogInformation("Rescheduled daily task, next execution at: {NextTime}", 
                now.Add(nextDueTime));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing daily processing task");
            
            // Even on error, try to reschedule
            try
            {
                var nextDueTime = CalculateNextDailyRunTime(now);
                await this.RegisterOrUpdateReminder(
                    _dailyReminderName,
                    nextDueTime,
                    TimeSpan.FromHours(23)
                );
            }
            catch (Exception scheduleEx)
            {
                _logger.LogError(scheduleEx, "Failed to reschedule reminder");
            }
        }
    }
    
    private async Task DoDailyProcessingAsync()
    {
        _logger.LogInformation("ExampleGAgent DoDailyProcessingAsync Begin");
        
        // Perform daily rollover if needed
        await PerformDailyRolloverAsync();
        
        // Additional daily processing logic goes here
        await Task.Delay(1000); // Placeholder for actual work
        
        _logger.LogInformation("ExampleGAgent DoDailyProcessingAsync End");
    }
    
    // Calculate time until next daily execution
    private TimeSpan CalculateNextDailyRunTime(DateTime currentTime)
    {
        // Today's target time
        DateTime todayTarget = currentTime.Date.Add(_targetTime);
        
        // If today's target time has passed, calculate until tomorrow's target time
        if (todayTarget <= currentTime)
        {
            todayTarget = todayTarget.AddDays(1);
        }
        
        // Return time interval
        return todayTarget - currentTime;
    }
    
    #endregion
    
    #region State Transition
    
    protected override void GAgentTransitionState(ExampleGAgentState state, StateLogEventBase<ExampleSEvent> @event)
    {
        base.GAgentTransitionState(state, @event);
        
        switch (@event)
        {
            case ExampleInitSEvent initEvent:
                if (!string.IsNullOrEmpty(initEvent.TargetId))
                {
                    try
                    {
                        state.ReminderTargetId = Guid.Parse(initEvent.TargetId);
                        _logger.LogInformation("Updated ReminderTargetId: {Id}", state.ReminderTargetId);
                        
                        // Initialize with today's midnight UTC timestamp
                        var now = DateTime.UtcNow;
                        state.LastRolloverTimestampSecond = GetTodayUtcMidnightTimestamp(now);
                        _logger.LogInformation("Initialized LastRolloverTimestampSecond: {Timestamp}", state.LastRolloverTimestampSecond);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to parse TargetId: {RawId}", initEvent.TargetId);
                    }
                }
                break;
                
            case ExampleRolloverSEvent rolloverEvent:
                _logger.LogInformation("Processing rollover event with timestamp: {Timestamp}", rolloverEvent.Timestamp);
                
                // Perform rollover logic here (e.g., move today's data to yesterday's)
                // Example: state.YesterdayData = state.TodayData != null 
                //     ? new Dictionary<string, decimal>(state.TodayData.ToDictionary(x => x.Key, x => x.Value))
                //     : new Dictionary<string, decimal>();
                // Example: state.TodayData = new Dictionary<string, decimal>();
                
                // Update the rollover timestamp
                state.LastRolloverTimestampSecond = rolloverEvent.Timestamp;
                _logger.LogInformation("Rollover completed, updated LastRolloverTimestampSecond: {Timestamp}", state.LastRolloverTimestampSecond);
                break;
                
            case ExampleDataSEvent dataEvent:
                // Process data event
                // Example: state.TodayData[dataEvent.InstanceId] = dataEvent.Value;
                break;
                
            // Other event handlers
        }
    }
    
    #endregion
}
```

## Implementation Key Points

The implementation above incorporates these key elements:

1. **UTC Timestamp-Based Time Handling**
   - Added `LastRolloverTimestampSecond` field (int type) for storing UTC midnight timestamps
   - Implemented `GetTodayUtcMidnightTimestamp()` utility method
   - Added `HasCrossedToNewDay()` method for cross-day detection
   - Used timestamp comparison instead of DateTime comparison

2. **ReminderTargetId Version Control Mechanism**
   - Added a ReminderTargetId field (Guid type, default: Guid.Empty)
   - Created initialization events for updating the field
   - Implemented version check in ReceiveReminder to only execute tasks when ReminderTargetId matches

3. **Improved Single Instance Execution Pattern**
   - No longer using fixed State.Id comparison, instead using ReminderTargetId for version control
   - Checking and registering reminders during instance activation
   - Automatic cleanup of reminders on instances with non-matching Target IDs

4. **Complete Reminder Lifecycle Management**
   - Registration, execution, verification, rescheduling, and cleanup
   - Event-driven state management for consistency
   - Error handling and retry mechanisms

5. **Execution Window Validation**
   - Time window validation (±5 minutes) for precise execution timing
   - Automatic rescheduling even when outside the execution window
   - Support for handling execution failures

6. **Cross-Day Detection and Rollover**
   - Automatic detection of day boundaries using UTC timestamps
   - Event-driven rollover mechanism
   - Duplicate rollover prevention

By following these patterns and best practices, you can implement robust scheduled tasks in your Orleans-based applications that are resilient, reliable, and maintainable. The UTC timestamp approach provides better precision and timezone independence compared to the traditional DateTime-based approach.
