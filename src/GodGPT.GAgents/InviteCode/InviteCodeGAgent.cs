using Aevatar.Application.Grains.Agents.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Orleans.Providers;

namespace Aevatar.Application.Grains.Agents.Invitation;

[GAgent(nameof(InviteCodeGAgent))]
public class InviteCodeGAgent : GAgentBase<InviteCodeState, InviteCodeLogEvent>, IInviteCodeGAgent
{
    private readonly ILogger<InviteCodeGAgent> _logger;

    public InviteCodeGAgent(ILogger<InviteCodeGAgent> logger)
    {
        _logger = logger;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Invite Code Management GAgent");
    }

    public async Task<bool> InitializeAsync(string inviterId)
    {
        if (!string.IsNullOrEmpty(State.InviterId))
        {
            return false;
        }

        RaiseEvent(new InitializeInviteCodeLogEvent
        {
            InviterId = inviterId,
            CreatedAt = DateTime.UtcNow
        });

        await ConfirmEvents();
        return true;
    }

    public async Task<(bool isValid, string inviterId)> ValidateAndGetInviterAsync()
    {
        if (!State.IsActive || string.IsNullOrEmpty(State.InviterId))
        {
            return (false, string.Empty);
        }

        RaiseEvent(new IncrementUsageCountLogEvent());
        await ConfirmEvents();

        return (true, State.InviterId);
    }

    public Task<bool> IsInitialized()
    {
        return Task.FromResult(!string.IsNullOrEmpty(State.InviterId));
    }

    public async Task DeactivateCodeAsync()
    {
        if (!State.IsActive)
        {
            return;
        }

        RaiseEvent(new DeactivateInviteCodeLogEvent());
        await ConfirmEvents();
    }

    protected sealed override void GAgentTransitionState(InviteCodeState state, StateLogEventBase<InviteCodeLogEvent> @event)
    {
        switch (@event)
        {
            case InitializeInviteCodeLogEvent initEvent:
                State.InviterId = initEvent.InviterId;
                State.CreatedAt = initEvent.CreatedAt;
                State.IsActive = true;
                State.UsageCount = 0;
                break;

            case DeactivateInviteCodeLogEvent:
                State.IsActive = false;
                break;
            
            case IncrementUsageCountLogEvent:
                State.UsageCount++;
                break;
        }
    }
} 