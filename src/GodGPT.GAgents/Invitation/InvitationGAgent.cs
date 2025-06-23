using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Core.Abstractions;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Application.Grains.Invitation.SEvents;
using Aevatar.Core;
using GodGPT.GAgents.Invitation.SEvents;
using Microsoft.Extensions.Logging;
using Orleans.Providers;
using Volo.Abp;

namespace GodGPT.GAgents.Invitation;

[GAgent(nameof(InvitationGAgent))]
public class InvitationGAgent : GAgentBase<InvitationState, InvitationLogEvent>, IInvitationGAgent
{
    private readonly ILogger<InvitationGAgent> _logger;

    public InvitationGAgent(ILogger<InvitationGAgent> logger)
    {
        _logger = logger;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Invitation Management GAgent");
    }

    public async Task<string> GenerateInviteCodeAsync()
    {
        var inviteCode = GenerateUniqueInviteCode();
        var inviteCodeGrain = GrainFactory.GetGrain<IInviteCodeGAgent>(CommonHelper.StringToGuid(inviteCode));
        var success = await inviteCodeGrain.InitializeAsync(this.GetPrimaryKey().ToString());

        if (!success)
        {
            return await GenerateInviteCodeAsync(); // Retry with a new code
        }

        RaiseEvent(new SetInviteCodeLogEvent { InviteCode = inviteCode });
        await ConfirmEvents();

        return inviteCode;
    }

    public Task<InvitationStatsDto> GetInvitationStatsAsync()
    {
        return Task.FromResult(new InvitationStatsDto
        {
            TotalInvites = State.TotalInvites,
            ValidInvites = State.ValidInvites,
            PendingInvites = State.Invitees.Count(x => !x.Value.IsValid),
            TotalCreditsEarned = State.TotalCreditsEarned,
            InviteCode = State.CurrentInviteCode
        });
    }

    public Task<List<RewardTierDto>> GetRewardTiersAsync()
    {
        var currentInvites = State.ValidInvites;
        var tiers = new List<RewardTierDto>();

        // Calculate dynamic tiers based on current invites
        var baseCount = (currentInvites / 3) * 3;
        for (var i = 0; i < 6; i++)
        {
            var inviteCount = baseCount + (i * 3);
            if (i < 4) inviteCount = Math.Max(inviteCount, i * 3); // Ensure first 4 tiers are always visible

            tiers.Add(new RewardTierDto
            {
                InviteCount = inviteCount,
                Credits = inviteCount <= 0 ? 30 : 100, // First invite 30, then 100 per 3 invites
                IsCompleted = currentInvites >= inviteCount
            });
        }

        return Task.FromResult(tiers);
    }

    public Task<List<RewardHistoryDto>> GetRewardHistoryAsync()
    {
        return Task.FromResult(State.RewardHistory.Select(r => new RewardHistoryDto
        {
            InviteeId = r.InviteeId,
            Credits = r.Credits,
            RewardType = r.RewardType.ToString(),
            IssuedAt = r.IssuedAt
        }).ToList());
    }

    public async Task<bool> ProcessInviteeRegistrationAsync(string inviteeId)
    {
        if (State.Invitees.ContainsKey(inviteeId))
        {
            return false;
        }

        RaiseEvent(new AddInviteeLogEvent
        {
            InviteeId = inviteeId,
            InvitedAt = DateTime.UtcNow
        });

        await ConfirmEvents();
        return true;
    }

    public async Task ProcessInviteeChatCompletionAsync(string inviteeId)
    {
        if (State.Invitees.TryGetValue(inviteeId, out var invitee) && invitee.HasCompletedChat)
        {
            return;
        }

        RaiseEvent(new UpdateInviteeStatusLogEvent
        {
            InviteeId = inviteeId,
            HasCompletedChat = true,
            HasPaid = invitee.HasPaid,
            PaidPlan = invitee.PaidPlan,
            PaidAt = invitee.PaidAt
        });
        await ConfirmEvents();

        // Issue first invite reward if this is the first valid invite
        if (State.ValidInvites == 0)
        {
            await IssueReward(inviteeId, 30, RewardTypeEnum.FirstInviteReward);
        }
        // Issue group reward if completing a group of 3
        else if ((State.ValidInvites + 1) % 3 == 0)
        {
            await IssueReward(inviteeId, 100, RewardTypeEnum.GroupInviteReward);
        }

        RaiseEvent(new UpdateValidInvitesLogEvent { ValidInvites = State.ValidInvites + 1 });
        
    }

    public async Task ProcessInviteeSubscriptionAsync(string inviteeId, PlanType planType, bool isUltimate)
    {
        if (!State.Invitees.TryGetValue(inviteeId, out var invitee) || invitee.HasPaid)
        {
            return;
        }

        var credits = GetSubscriptionRewardCredits(planType, isUltimate);
        if (credits <= 0)
        {
            return;
        }

        RaiseEvent(new UpdateInviteeStatusLogEvent
        {
            InviteeId = inviteeId,
            HasCompletedChat = invitee.HasCompletedChat,
            HasPaid = true,
            PaidPlan = planType,
            PaidAt = DateTime.UtcNow,
            MembershipLevel = isUltimate ? MembershipLevel.Membership_Level_Ultimate : MembershipLevel.Membership_Level_Premium
        });
        await ConfirmEvents();
        
        // For annual plans, schedule the reward for 7 days later
        if (planType == PlanType.Year)
        {
            // TODO: Implement delayed reward mechanism
            _logger.LogInformation($"Scheduled reward of {credits} credits for invitee {inviteeId} in 7 days");
        }
        else
        {
            await IssueReward(inviteeId, credits, RewardTypeEnum.SubscriptionReward);
        }
    }

    private async Task IssueReward(string inviteeId, int credits, RewardTypeEnum rewardType)
    {
        var userQuotaGrain = GrainFactory.GetGrain<IUserQuotaGrain>(CommonHelper.GetUserQuotaGAgentId(this.GetPrimaryKey()));
        await userQuotaGrain.AddCreditsAsync(credits);

        RaiseEvent(new AddRewardLogEvent
        {
            InviteeId = inviteeId,
            Credits = credits,
            RewardType = rewardType
        });
        await ConfirmEvents();
    }

    private string GenerateUniqueInviteCode()
    {
        // Generate a 6-character unique code
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 6)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    private int GetSubscriptionRewardCredits(PlanType planType, bool isUltimate)
    {
        if (isUltimate)
        {
            return planType switch
            {
                PlanType.Week => 500,
                PlanType.Month => 2000,
                PlanType.Year => 20000,
                _ => 0
            };
        }
        else
        {
            return planType switch
            {
                PlanType.Week => 100,
                PlanType.Month => 400,
                PlanType.Year => 4000,
                _ => 0
            };
        }
    }

    protected sealed override void GAgentTransitionState(InvitationState state, StateLogEventBase<InvitationLogEvent> @event)
    {
        switch (@event)
        {
            case SetInviteCodeLogEvent setInviteCode:
                State.CurrentInviteCode = setInviteCode.InviteCode;
                break;

            case AddInviteeLogEvent addInvitee:
                State.Invitees[addInvitee.InviteeId] = new InviteeInfo
                {
                    InviteeId = addInvitee.InviteeId,
                    InvitedAt = addInvitee.InvitedAt
                };
                State.TotalInvites++;
                break;

            case UpdateInviteeStatusLogEvent updateStatus:
                if (State.Invitees.TryGetValue(updateStatus.InviteeId, out var invitee))
                {
                    invitee.HasCompletedChat = updateStatus.HasCompletedChat;
                    invitee.HasPaid = updateStatus.HasPaid;
                    invitee.PaidPlan = updateStatus.PaidPlan;
                    invitee.PaidAt = updateStatus.PaidAt;
                    invitee.IsValid = updateStatus.HasCompletedChat;
                }
                else
                {
                    var inviteeInfo = new InviteeInfo
                    {
                        InviteeId = updateStatus.InviteeId,
                        InvitedAt = DateTime.UtcNow,
                        HasCompletedChat = updateStatus.HasCompletedChat,
                        HasPaid = updateStatus.HasPaid,
                        PaidPlan = updateStatus.PaidPlan,
                        PaidAt = updateStatus.PaidAt,
                        RewardIssued = false,
                        IsValid = true
                    };
                    State.Invitees[updateStatus.InviteeId] = inviteeInfo;
                }
                break;

            case AddRewardLogEvent addReward:
                State.RewardHistory.Add(new RewardRecord
                {
                    InviteeId = addReward.InviteeId,
                    Credits = addReward.Credits,
                    RewardType = addReward.RewardType,
                    IssuedAt = DateTime.UtcNow
                });
                State.TotalCreditsEarned += addReward.Credits;
                break;

            case UpdateValidInvitesLogEvent updateValidInvites:
                State.ValidInvites = updateValidInvites.ValidInvites;
                break;
        }
    }
} 