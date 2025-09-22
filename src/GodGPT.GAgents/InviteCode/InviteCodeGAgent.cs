using Aevatar.Application.Grains.Agents.SEvents;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
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

    public async Task<bool> InitializeAsync(string inviterId, string inviteCode)
    {
        if (!string.IsNullOrEmpty(State.InviterId))
        {
            return false;
        }

        RaiseEvent(new InitializeInviteCodeLogEvent
        {
            InviterId = inviterId,
            CreatedAt = DateTime.UtcNow,
            InviteCode = inviteCode
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
        if (!State.InviteCode.IsNullOrWhiteSpace())
        {
            _logger.LogWarning("InviteCodeGAgent already initialized. {Code}", initDto.FreeTrialCode);
            return false;
        }
        RaiseEvent(new InitializeFreeTrialCodeLogEvent
        {
            Code = initDto.FreeTrialCode,
            BatchId = initDto.BatchId,
            TrialDays = initDto.TrialDays,
            ProductId = initDto.ProductId,
            PlanType = initDto.PlanType,
            IsUltimate = initDto.IsUltimate,
            StartDate = initDto.StartDate,
            EndDate = initDto.EndDate,
            InviteeId = initDto.InviteeId,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            Platform = initDto.Platform,
            SessionUrl = initDto.SessionUrl,
            SessionExpiresAt = initDto.SessionExpiresAt
        });

        await ConfirmEvents();
        
        _logger.LogInformation("Free trial code initialized. BatchId: {BatchId}, TrialDays: {TrialDays}", 
            initDto.BatchId, initDto.TrialDays);
        
        return true;
    }

    public async Task<ValidateCodeResultDto> ValidateAndGetFreeTrialCodeInfoAsync(string inviteeId)
    {
        if (State.InviteCode.IsNullOrWhiteSpace())
        {
            return new ValidateCodeResultDto
            {
                IsValid = true,
                Message = string.Empty,
                CodeType = InvitationCodeType.FreeTrialReward,
                ActivationInfo = null
            };
        } 
        
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

        if (State.InviteeId != inviteeId)
        {
            return new ValidateCodeResultDto
            {
                IsValid = false,
                Message = "Code already used by another user",
                CodeType = State.CodeType,
                ActivationInfo = null
            };
        }

        try
        {
            return new ValidateCodeResultDto
            {
                IsValid = true,
                Message = string.Empty,
                CodeType = State.CodeType,
                ActivationInfo = new FreeTrialActivationDto
                {
                    CreatedAt = State.CreatedAt,
                    IsActive = State.IsActive,
                    UsageCount = State.UsageCount,
                    InviteCode = State.InviteCode,
                    CodeType = State.CodeType,
                    BatchId = State.BatchId,
                    TrialDays = State.TrialDays,
                    ProductId = State.ProductId,
                    PlanType = State.PlanType,
                    IsUltimate = State.IsUltimate,
                    Platform = State.Platform,
                    InviteeId = State.InviteeId,
                    UsedAt = State.UsedAt,
                    SessionUrl = State.SessionUrl,
                    SessionExpiresAt = State.SessionExpiresAt
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error redeeming free trial code for user {UserId}", inviteeId);
            return new ValidateCodeResultDto
            {
                IsValid = false,
                Message = "Internal error occurred",
                CodeType = State.CodeType,
                ActivationInfo = null
            };
        }
    }

    public async Task<bool> MarkCodeAsUsedAsync()
    {
        RaiseEvent(new MarkCodeAsUsedLogEvent
        {
            IsActive = false,
            UsedAt = DateTime.UtcNow
        });

        await ConfirmEvents();
        
        _logger.LogInformation("Free trial code Used. BatchId: {BatchId}, TrialDays: {InviteCode}", 
            State.BatchId, State.InviteCode);
        return true;
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
                state.InviteCode = initEvent.InviteCode;
                break;

            case DeactivateInviteCodeLogEvent:
                state.IsActive = false;
                break;
            
            case IncrementUsageCountLogEvent:
                state.UsageCount++;
                break;

            case InitializeFreeTrialCodeLogEvent freeTrialInitEvent:
                state.InviteCode = freeTrialInitEvent.Code;
                state.CreatedAt = freeTrialInitEvent.CreatedAt;
                state.IsActive = freeTrialInitEvent.IsActive;
                state.UsageCount = 1;
                state.CodeType = InvitationCodeType.FreeTrialReward;
                state.BatchId = freeTrialInitEvent.BatchId;
                state.TrialDays = freeTrialInitEvent.TrialDays;
                state.ProductId = freeTrialInitEvent.ProductId;
                state.PlanType = freeTrialInitEvent.PlanType;
                state.IsUltimate = freeTrialInitEvent.IsUltimate;
                state.Platform = freeTrialInitEvent.Platform;
                state.InviteeId = freeTrialInitEvent.InviteeId;
                state.SessionUrl = freeTrialInitEvent.SessionUrl;
                state.SessionExpiresAt = freeTrialInitEvent.SessionExpiresAt;
                break;

            case MarkCodeAsUsedLogEvent redeemEvent:
                state.UsedAt = redeemEvent.UsedAt;
                state.IsActive = redeemEvent.IsActive;
                break;
        }
    }
} 