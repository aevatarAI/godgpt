using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Agents.Invitation;

[GenerateSerializer]
public class InvitationState : StateBase
{
    [Id(0)] public string InviterId { get; set; }
    [Id(1)] public string CurrentInviteCode { get; set; }
    [Id(2)] public Dictionary<string, InviteeInfo> Invitees { get; set; } = new();
    [Id(3)] public int TotalInvites { get; set; }
    [Id(4)] public int ValidInvites { get; set; }
    [Id(5)] public int TotalCreditsEarned { get; set; }
    [Id(6)] public List<RewardRecord> RewardHistory { get; set; } = new();
    [Id(7)] public DateTime LastRewardTierUpdate { get; set; }
}

[GenerateSerializer]
public class InviteeInfo
{
    [Id(0)] public string InviteeId { get; set; }
    [Id(1)] public DateTime InvitedAt { get; set; }
    [Id(2)] public bool HasCompletedChat { get; set; }
    [Id(3)] public bool HasPaid { get; set; }
    [Id(4)] public PlanType PaidPlan { get; set; }
    [Id(5)] public DateTime? PaidAt { get; set; }
    [Id(6)] public bool RewardIssued { get; set; }
    [Id(7)] public bool IsValid { get; set; }
}

[GenerateSerializer]
public class RewardRecord
{
    [Id(0)] public string InviteeId { get; set; }
    [Id(1)] public int Credits { get; set; }
    [Id(2)] public RewardTypeEnum RewardType { get; set; }
    [Id(3)] public DateTime IssuedAt { get; set; }
} 