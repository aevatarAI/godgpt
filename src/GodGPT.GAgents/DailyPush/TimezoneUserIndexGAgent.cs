using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Core.Abstractions;
using Aevatar.Core;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Providers;
using GodGPT.GAgents.DailyPush.SEvents;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Timezone user index GAgent implementation
/// </summary>
[StorageProvider(ProviderName = "PubSubStore")]
[LogConsistencyProvider(ProviderName = "LogStorage")]
[GAgent(nameof(TimezoneUserIndexGAgent))]
public class TimezoneUserIndexGAgent : GAgentBase<TimezoneUserIndexGAgentState, DailyPushLogEvent>, ITimezoneUserIndexGAgent
{
    private readonly ILogger<TimezoneUserIndexGAgent> _logger;
    
    public TimezoneUserIndexGAgent(ILogger<TimezoneUserIndexGAgent> logger)
    {
        _logger = logger;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Timezone user index management");
    }

    protected override async Task OnGAgentActivateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TimezoneUserIndexGAgent activated");
    }

    protected override void GAgentTransitionState(TimezoneUserIndexGAgentState state, StateLogEventBase<DailyPushLogEvent> @event)
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
        // TODO: This would typically involve:
        // 1. Querying all ChatManagerGAgents to find users with devices in this timezone
        // 2. Updating the ActiveUsers set based on current device states
        // 3. Removing users who no longer have enabled devices in this timezone
        
        // For now, just update timestamp
        RaiseEvent(new InitializeTimezoneIndexEventLog
        {
            TimeZoneId = State.TimeZoneId,
            InitTime = DateTime.UtcNow
        });
        
        await ConfirmEvents();
        _logger.LogInformation($"Refreshed user index for timezone {State.TimeZoneId} (placeholder implementation)");
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
