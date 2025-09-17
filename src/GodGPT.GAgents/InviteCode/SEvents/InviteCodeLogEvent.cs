using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Agents.SEvents;

[GenerateSerializer]
public class InviteCodeLogEvent : StateLogEventBase<InviteCodeLogEvent>
{
}

[GenerateSerializer]
public class InitializeInviteCodeLogEvent : InviteCodeLogEvent
{
    [Id(0)] public string InviterId { get; set; }
    [Id(1)] public DateTime CreatedAt { get; set; }
}

[GenerateSerializer]
public class DeactivateInviteCodeLogEvent : InviteCodeLogEvent
{
}

[GenerateSerializer]
public class IncrementUsageCountLogEvent : InviteCodeLogEvent
{
}

[GenerateSerializer]
public class InitializeFreeTrialCodeLogEvent : InviteCodeLogEvent
{
    // [Id(0)] public string BatchId { get; set; }
    // [Id(1)] public int TrialDays { get; set; }
    // [Id(2)] public PlanType PlanType { get; set; }
    // [Id(3)] public bool IsUltimate { get; set; }
    // [Id(5)] public DateTime CreatedAt { get; set; }
}

[GenerateSerializer]
public class RedeemFreeTrialCodeLogEvent : InviteCodeLogEvent
{
    // [Id(0)] public string UserId { get; set; }
    // [Id(1)] public DateTime UsedAt { get; set; }
    // [Id(2)] public string SubscriptionId { get; set; }
} 