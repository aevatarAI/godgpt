using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Invitation.SEvents;

[GenerateSerializer]
public class InvitationLogEvent : StateLogEventBase<InvitationLogEvent>
{
}

[GenerateSerializer]
public class SetInviteCodeLogEvent : InvitationLogEvent
{
    [Id(0)] public string InviteCode { get; set; }
}

[GenerateSerializer]
public class AddInviteeLogEvent : InvitationLogEvent
{
    [Id(0)] public string InviteeId { get; set; }
    [Id(1)] public DateTime InvitedAt { get; set; }
}

[GenerateSerializer]
public class UpdateInviteeStatusLogEvent : InvitationLogEvent
{
    [Id(0)] public string InviteeId { get; set; }
    [Id(1)] public bool HasCompletedChat { get; set; }
    [Id(2)] public bool HasPaid { get; set; }
    [Id(3)] public PlanType PaidPlan { get; set; }
    [Id(4)] public DateTime? PaidAt { get; set; }
    [Id(5)] public string MembershipLevel { get; set; }
}

[GenerateSerializer]
public class AddRewardLogEvent : InvitationLogEvent
{
    [Id(0)] public string InviteeId { get; set; }
    [Id(1)] public int Credits { get; set; }
    [Id(2)] public RewardTypeEnum RewardType { get; set; }
}

[GenerateSerializer]
public class UpdateValidInvitesLogEvent : InvitationLogEvent
{
    [Id(0)] public int ValidInvites { get; set; }
}