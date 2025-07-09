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
    [Id(1)] public string InviterId { get; set; }
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
    [Id(3)] public bool IsScheduled { get; set; }
    [Id(4)] public DateTime? ScheduledDate { get; set; }
    [Id(5)] public string InvoiceId { get; set; }
    [Id(6)] public string TweetId { get; set; }
    [Id(7)] public DateTime IssueAt { get; set; }
}

[GenerateSerializer]
public class UpdateValidInvitesLogEvent : InvitationLogEvent
{
    [Id(0)] public int ValidInvites { get; set; }
}

[GenerateSerializer]
public class MarkRewardIssuedLogEvent : InvitationLogEvent
{
    [Id(0)] public string InviteeId { get; set; }
    [Id(1)] public string InvoiceId { get; set; }
}