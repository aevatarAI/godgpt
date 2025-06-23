using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Core.Abstractions;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Invitation.SEvents;
using Aevatar.Core;
using Microsoft.Extensions.Logging;
using Orleans.Providers;
using System.Text;

namespace Aevatar.Application.Grains.Agents.Invitation;

[StorageProvider(ProviderName = "PubSubStore")]
[LogConsistencyProvider(ProviderName = "LogStorage")]
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
        await inviteCodeGrain.InitializeAsync(this.GetPrimaryKeyString());

        RaiseEvent(new SetInviteCodeLogEvent { InviteCode = inviteCode });
        await ConfirmEvents();

        return inviteCode;
    }

    public async Task RecordNewInviteeAsync(string inviteeId)
    {
        if (State.Invitees.ContainsKey(inviteeId))
        {
            _logger.LogWarning("Attempted to record an existing invitee: {InviteeId}", inviteeId);
            return;
        }
        
        RaiseEvent(new AddInviteeLogEvent
        {
            InviteeId = inviteeId,
            InvitedAt = DateTime.UtcNow
        });

        await ConfirmEvents();
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
            RewardType = r.RewardType,
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

        // Issue first invite reward if this is the first valid invite
        if (State.ValidInvites == 0)
        {
            await IssueReward(inviteeId, 30, "FirstInviteReward");
        }
        // Issue group reward if completing a group of 3
        else if ((State.ValidInvites + 1) % 3 == 0)
        {
            await IssueReward(inviteeId, 100, "GroupInviteReward");
        }

        RaiseEvent(new UpdateValidInvitesLogEvent { ValidInvites = State.ValidInvites + 1 });
        await ConfirmEvents();
    }

    public async Task ProcessInviteeSubscriptionAsync(string inviteeId, string planType)
    {
        if (!State.Invitees.TryGetValue(inviteeId, out var invitee) || invitee.HasPaid)
        {
            return;
        }

        var credits = GetSubscriptionRewardCredits(planType);
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
            PaidAt = DateTime.UtcNow
        });

        // For annual plans, schedule the reward for 7 days later
        if (planType.StartsWith("Annual"))
        {
            // TODO: Implement delayed reward mechanism
            _logger.LogInformation($"Scheduled reward of {credits} credits for invitee {inviteeId} in 7 days");
        }
        else
        {
            await IssueReward(inviteeId, credits, "SubscriptionReward");
        }

        await ConfirmEvents();
    }

    private async Task IssueReward(string inviteeId, int credits, string rewardType)
    {
        var userQuotaGrain = GrainFactory.GetGrain<IUserQuotaGrain>(CommonHelper.GetUserQuotaGAgentId(this.GetPrimaryKey()));
        await userQuotaGrain.AddCreditsAsync(credits);

        RaiseEvent(new AddRewardLogEvent
        {
            InviteeId = inviteeId,
            Credits = credits,
            RewardType = rewardType
        });

        RaiseEvent(new UpdateTotalCreditsLogEvent
        {
            TotalCredits = State.TotalCreditsEarned + credits
        });
    }

    private string GenerateUniqueInviteCode()
    {
        // Generate a 6-character unique code
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 6)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    private int GetSubscriptionRewardCredits(string planType)
    {
        return planType switch
        {
            "WeeklyPremium" => 100,
            "WeeklyUltimate" => 500,
            "MonthlyPremium" => 400,
            "MonthlyUltimate" => 2000,
            "AnnualPremium" => 4000,
            "AnnualUltimate" => 20000,
            _ => 0
        };
    }

    private async Task<string> GenerateUniqueCodeAsync()
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        while (true)
        {
            string code = ToBase62(timestamp);
            var codeGrainId = CommonHelper.StringToGuid(code);
            var codeGrain = GrainFactory.GetGrain<IInviteCodeGAgent>(codeGrainId);
            var isUsed = await codeGrain.IsInitialized();

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
                    IssuedAt = DateTime.UtcNow
                });
                break;

            case UpdateValidInvitesLogEvent updateValidInvites:
                State.ValidInvites = updateValidInvites.ValidInvites;
                break;

            case UpdateTotalCreditsLogEvent updateTotalCredits:
                State.TotalCreditsEarned = updateTotalCredits.TotalCredits;
                break;
        }
    }
} 