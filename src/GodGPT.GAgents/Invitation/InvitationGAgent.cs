using System.Text;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Agents.Invitation;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Invitation.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aevatar.Application.Grains.Invitation;

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
        if (!string.IsNullOrEmpty(State.CurrentInviteCode))
        {
            return State.CurrentInviteCode;
        }

        var inviteCode = await GenerateUniqueCodeAsync();
        var inviteCodeGrain = GrainFactory.GetGrain<IInviteCodeGAgent>(CommonHelper.StringToGuid(inviteCode));
        await inviteCodeGrain.InitializeAsync(this.GetPrimaryKey().ToString());

        RaiseEvent(new SetInviteCodeLogEvent
        {
            InviteCode = inviteCode,
            InviterId = this.GetPrimaryKey().ToString()
        });
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

        int currentLevel;
        if (currentInvites == 0)
        {
            currentLevel = 0;
        }
        else if (currentInvites == 1)
        {
            currentLevel = 1;
        }
        else
        {
            currentLevel = 1 + (currentInvites - 1) / 3;
        }

        const int totalLevels = 6;
        int startLevel;
        
        if (currentLevel <= 4)
        {
            startLevel = 1;
        }
        else
        {
            startLevel = currentLevel - 3;
        }

        for (int i = 0; i < totalLevels; i++)
        {
            int level = startLevel + i;
            int inviteCount = level == 1 ? 1 : 1 + (level - 1) * 3;
            tiers.Add(new RewardTierDto
            {
                InviteCount = inviteCount,
                Credits = level == 1 ? 30 : 100,
                IsCompleted = level <= currentLevel
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
            IssuedAt = r.IssuedAt,
            IsScheduled = r.IsScheduled,
            ScheduledDate = r.ScheduledDate,
            InvoiceId = r.InvoiceId
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
        if (!State.Invitees.TryGetValue(inviteeId, out var invitee) || invitee.HasCompletedChat)
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
        else if ((State.ValidInvites) % 3 == 0)
        {
            await IssueReward(inviteeId, 100, RewardTypeEnum.GroupInviteReward);
        }

        RaiseEvent(new UpdateValidInvitesLogEvent { ValidInvites = State.ValidInvites + 1 });
        
    }

    public async Task ProcessInviteeSubscriptionAsync(string inviteeId, PlanType planType, bool isUltimate,
        string invoiceId)
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
        
        // For annual plans, schedule the reward for 30 days later
        if (planType == PlanType.Year)
        {
            RaiseEvent(new AddRewardLogEvent
            {
                InviteeId = inviteeId,
                Credits = credits,
                RewardType = RewardTypeEnum.SubscriptionReward,
                IsScheduled = true,
                ScheduledDate = DateTime.UtcNow.AddDays(30),
                InvoiceId = invoiceId
            });
            await ConfirmEvents();
            _logger.LogInformation($"Scheduled reward of {credits} credits for invitee {inviteeId} in 30 days");
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

    private async Task<string> GenerateUniqueCodeAsync()
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int attemptCount = 0;
        while (true)
        {
            attemptCount++;
            string code = ToBase62(timestamp);
            var codeGrainId = CommonHelper.StringToGuid(code);
            var codeGrain = GrainFactory.GetGrain<IInviteCodeGAgent>(codeGrainId);
            var isUsed = await codeGrain.IsInitialized();

            _logger.LogDebug($"[InvitationGAgent][GenerateUniqueCodeAsync] Attempt {attemptCount}: Generated invite code {code}, isUsed: {isUsed}");

            if (!isUsed)
            {
                return code;
            }
            timestamp++;
        }
    }

    private string ToBase62(long number)
    {
        const string chars = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var sb = new StringBuilder();
        if (number == 0)
            return "0";
        while (number > 0)
        {
            sb.Insert(0, chars[(int)(number % 62)]);
            number /= 62;
        }
        return sb.ToString();
    }

    protected sealed override void GAgentTransitionState(InvitationState state, StateLogEventBase<InvitationLogEvent> @event)
    {
        switch (@event)
        {
            case SetInviteCodeLogEvent setInviteCode:
                State.InviterId = setInviteCode.InviterId;
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
                break;

            case AddRewardLogEvent addReward:
                State.RewardHistory.Add(new RewardRecord
                {
                    InviteeId = addReward.InviteeId,
                    Credits = addReward.Credits,
                    RewardType = addReward.RewardType,
                    IssuedAt = DateTime.UtcNow,
                    IsScheduled = addReward.IsScheduled,
                    ScheduledDate = addReward.ScheduledDate,
                    InvoiceId = addReward.InvoiceId
                });
                if (!addReward.IsScheduled)
                {
                    State.TotalCreditsEarned += addReward.Credits;
                }
                break;

            case UpdateValidInvitesLogEvent updateValidInvites:
                State.ValidInvites = updateValidInvites.ValidInvites;
                break;

            case MarkRewardIssuedLogEvent markIssued:
                var reward = State.RewardHistory.FirstOrDefault(r => 
                    r.InviteeId == markIssued.InviteeId && 
                    r.InvoiceId == markIssued.InvoiceId &&
                    r.IsScheduled);
                
                if (reward != null)
                {
                    reward.IsScheduled = false;
                    State.TotalCreditsEarned += reward.Credits;
                }
                break;
        }
    }

    public async Task MarkRewardAsIssuedAsync(string inviteeId, string invoiceId)
    {
        var reward = State.RewardHistory.FirstOrDefault(r => 
            r.InviteeId == inviteeId && 
            r.InvoiceId == invoiceId && 
            r.IsScheduled);
        
        if (reward != null)
        {
            RaiseEvent(new MarkRewardIssuedLogEvent
            {
                InviteeId = inviteeId,
                InvoiceId = invoiceId
            });
            await ConfirmEvents();
        }
    }
} 