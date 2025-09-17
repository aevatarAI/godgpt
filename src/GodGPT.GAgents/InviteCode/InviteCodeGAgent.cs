using Aevatar.Application.Grains.Agents.SEvents;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Application.Grains.UserBilling;
using Aevatar.Application.Grains.UserQuota;
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

    public async Task<bool> InitializeFreeTrialCodeAsync(FreeTrialCodeInitDto initDto)
    {
        if (State.CodeType != InvitationCodeType.FriendInvitation || !string.IsNullOrEmpty(State.BatchId))
        {
            _logger.LogWarning("Code already initialized or wrong type. CodeType: {CodeType}, BatchId: {BatchId}", 
                State.CodeType, State.BatchId);
            return false;
        }

        RaiseEvent(new InitializeFreeTrialCodeLogEvent
        {
            // BatchId = initDto.BatchId,
            // TrialDays = initDto.TrialDays,
            // PlanType = initDto.PlanType,
            // IsUltimate = initDto.IsUltimate,
            // CreatedAt = DateTime.UtcNow
        });

        await ConfirmEvents();
        
        _logger.LogInformation("Free trial code initialized. BatchId: {BatchId}, TrialDays: {TrialDays}", 
            initDto.BatchId, initDto.TrialDays);
        
        return true;
    }

    public async Task<ValidateCodeResultDto> ValidateAndRedeemFreeTrialAsync(string userId)
    {
        if (State.CodeType != InvitationCodeType.FreeTrialReward)
        {
            return new ValidateCodeResultDto
            {
                IsValid = false,
                Message = "Invalid code type",
                CodeType = State.CodeType,
                ActivationInfo = null
            };
        }

        if (!State.IsActive)
        {
            return new ValidateCodeResultDto
            {
                IsValid = false,
                Message = "Code is not active",
                CodeType = State.CodeType,
                ActivationInfo = null
            };
        }

        if (!string.IsNullOrEmpty(State.InviteeId))
        {
            return new ValidateCodeResultDto
            {
                IsValid = false,
                Message = "Code already used",
                CodeType = State.CodeType,
                ActivationInfo = null
            };
        }

        try
        {
            return new ValidateCodeResultDto
            {
                IsValid = true,
                Message = "Free trial activated successfully",
                CodeType = State.CodeType,
                //ActivationInfo = activationInfo
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error redeeming free trial code for user {UserId}", userId);
            return new ValidateCodeResultDto
            {
                IsValid = false,
                Message = "Internal error occurred",
                CodeType = State.CodeType,
                ActivationInfo = null
            };
        }
    }

    public Task<FreeTrialCodeInfoDto> GetCodeInfoAsync()
    {
        if (State.CodeType != InvitationCodeType.FreeTrialReward)
        {
            return Task.FromResult<FreeTrialCodeInfoDto>(null);
        }

        var codeInfo = new FreeTrialCodeInfoDto
        {
            BatchId = State.BatchId,
            TrialDays = State.TrialDays,
            PlanType = State.PlanType,
            IsUltimate = State.IsUltimate,
            UsedAt = State.UsedAt
        };

        return Task.FromResult(codeInfo);
    }

    protected sealed override void GAgentTransitionState(InviteCodeState state, StateLogEventBase<InviteCodeLogEvent> @event)
    {
        switch (@event)
        {
            case InitializeInviteCodeLogEvent initEvent:
                state.InviterId = initEvent.InviterId;
                state.CreatedAt = initEvent.CreatedAt;
                state.IsActive = true;
                state.UsageCount = 0;
                state.CodeType = InvitationCodeType.FriendInvitation;
                break;

            case DeactivateInviteCodeLogEvent:
                state.IsActive = false;
                break;
            
            case IncrementUsageCountLogEvent:
                state.UsageCount++;
                break;

            case InitializeFreeTrialCodeLogEvent freeTrialInitEvent:
                // state.CodeType = InvitationCodeType.FreeTrialReward;
                // state.BatchId = freeTrialInitEvent.BatchId;
                // state.TrialDays = freeTrialInitEvent.TrialDays;
                // state.PlanType = freeTrialInitEvent.PlanType;
                // state.IsUltimate = freeTrialInitEvent.IsUltimate;
                // state.CreatedAt = freeTrialInitEvent.CreatedAt;
                // state.IsActive = true;
                // state.UsageCount = 0;
                break;

            case RedeemFreeTrialCodeLogEvent redeemEvent:
                // state.UsedAt = redeemEvent.UsedAt;
                // state.IsActive = false;
                // state.UsageCount++;
                break;
        }
    }
} 