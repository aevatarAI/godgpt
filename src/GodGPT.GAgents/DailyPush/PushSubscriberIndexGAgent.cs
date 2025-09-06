using Aevatar.Core;
using Aevatar.Core.Abstractions;
using GodGPT.GAgents.DailyPush.SEvents;
using Microsoft.Extensions.Logging;
using Orleans.Providers;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Timezone user index GAgent implementation
/// </summary>
[StorageProvider(ProviderName = "PubSubStore")]
[LogConsistencyProvider(ProviderName = "LogStorage")]
[GAgent(nameof(PushSubscriberIndexGAgent))]
public class PushSubscriberIndexGAgent : GAgentBase<PushSubscriberIndexState, DailyPushLogEvent>,
    IPushSubscriberIndexGAgent
{
    private readonly ILogger<PushSubscriberIndexGAgent> _logger;

    public PushSubscriberIndexGAgent(ILogger<PushSubscriberIndexGAgent> logger)
    {
        _logger = logger;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Timezone user index management");
    }

    protected override async Task OnGAgentActivateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("PushSubscriberIndexGAgent activated");
    }

    protected override void GAgentTransitionState(PushSubscriberIndexState state,
        StateLogEventBase<DailyPushLogEvent> @event)
    {
        switch (@event)
        {
            case InitializeTimezoneIndexEventLog initEvent:
                state.TimeZoneId = initEvent.TimeZoneId;
                state.LastUpdated = initEvent.InitTime;
                break;

            case AddUserToTimezoneEventLog addEvent:
                state.ActiveUsers.Add(addEvent.UserId);
                state.ActiveUserCount = state.ActiveUsers.Count;
                state.LastUpdated = DateTime.UtcNow;
                break;

            case RemoveUserFromTimezoneEventLog removeEvent:
                state.ActiveUsers.Remove(removeEvent.UserId);
                state.ActiveUserCount = state.ActiveUsers.Count;
                state.LastUpdated = DateTime.UtcNow;
                break;

            case BatchUpdateUsersEventLog batchEvent:
                foreach (var update in batchEvent.Updates)
                {
                    if (update.IsAdd)
                    {
                        state.ActiveUsers.Add(update.UserId);
                    }
                    else
                    {
                        state.ActiveUsers.Remove(update.UserId);
                    }
                }

                state.ActiveUserCount = state.ActiveUsers.Count;
                state.LastUpdated = DateTime.UtcNow;
                break;

            default:
                _logger.LogDebug($"Unhandled event type: {@event.GetType().Name}");
                break;
        }
    }

    public async Task InitializeAsync(string timeZoneId)
    {
        RaiseEvent(new InitializeTimezoneIndexEventLog
        {
            TimeZoneId = timeZoneId,
            InitTime = DateTime.UtcNow
        });

        await ConfirmEvents();
        _logger.LogInformation($"Initialized timezone index for: {timeZoneId}");
    }

    public async Task AddUserToTimezoneAsync(Guid userId)
    {
        RaiseEvent(new AddUserToTimezoneEventLog
        {
            UserId = userId,
            TimeZoneId = State.TimeZoneId
        });

        await ConfirmEvents();
        _logger.LogInformation($"Added user {userId} to timezone {State.TimeZoneId}");
    }

    public async Task RemoveUserFromTimezoneAsync(Guid userId)
    {
        RaiseEvent(new RemoveUserFromTimezoneEventLog
        {
            UserId = userId,
            TimeZoneId = State.TimeZoneId
        });

        await ConfirmEvents();
        _logger.LogInformation($"Removed user {userId} from timezone {State.TimeZoneId}");
    }

    public async Task<List<Guid>> GetActiveUsersAsync()
    {
        return State.ActiveUsers.ToList();
    }

    public async Task<List<Guid>> GetActiveUsersInTimezoneAsync(int skip, int take)
    {
        return State.ActiveUsers.Skip(skip).Take(take).ToList();
    }

    public async Task<int> GetActiveUserCountAsync()
    {
        return State.ActiveUsers.Count;
    }

    public async Task<int> GetUserCountAsync()
    {
        return State.ActiveUsers.Count;
    }

    public async Task<string> GetTimezoneIdAsync()
    {
        return State.TimeZoneId;
    }

    public async Task RefreshUserIndexAsync()
    {
        // 🧹 Lightweight cleanup: Only log current status for monitoring
        var currentCount = State.ActiveUsers.Count;
        
        // TODO: Future enhancement could involve:
        // 1. Querying all ChatManagerGAgents to find users with devices in this timezone
        // 2. Updating the ActiveUsers set based on current device states
        // 3. Removing users who no longer have enabled devices in this timezone
        // 
        // Current approach: Passive cleanup via natural user activity
        // - Users are added when they register/update devices
        // - Cleanup happens naturally when users update their timezone or device status
        // - HashSet prevents duplicates automatically

        RaiseEvent(new InitializeTimezoneIndexEventLog
        {
            TimeZoneId = State.TimeZoneId,
            InitTime = DateTime.UtcNow
        });

        await ConfirmEvents();
        _logger.LogInformation("📊 Timezone index status: {TimeZone} has {UserCount} active users", 
            State.TimeZoneId, currentCount);
    }

    public async Task<bool> HasActiveDeviceInTimezoneAsync(Guid userId)
    {
        return State.ActiveUsers.Contains(userId);
    }

    public async Task BatchUpdateUsersAsync(List<TimezoneUpdateRequest> updates)
    {
        var validUpdates = updates.Where(u => !string.IsNullOrEmpty(u.TargetTimezone)).ToList();
        if (validUpdates.Count > 0)
        {
            RaiseEvent(new BatchUpdateUsersEventLog
            {
                Updates = validUpdates,
                UpdatedCount = validUpdates.Count
            });

            await ConfirmEvents();
        }

        _logger.LogInformation($"Batch updated {validUpdates.Count} users in timezone {State.TimeZoneId}");
    }
}