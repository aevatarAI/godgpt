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
    [Id(2)] public string InviteCode { get; set; }
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
    [Id(0)] public string Code { get; set; }
    [Id(1)] public long BatchId { get; set; }
    [Id(2)] public int TrialDays { get; set; }
    [Id(3)] public string ProductId { get; set; }
    [Id(4)] public PlanType PlanType { get; set; }
    [Id(5)] public bool IsUltimate { get; set; }
    [Id(6)] public DateTime StartDate { get; set; }
    [Id(7)] public DateTime EndDate { get; set; }
    [Id(8)] public string InviteeId { get; set; }
    [Id(9)] public DateTime CreatedAt { get; set; }
    [Id(10)] public bool IsActive { get; set; }
    [Id(11)] public PaymentPlatform Platform { get; set; } = PaymentPlatform.Stripe;
    [Id(12)] public string SessionUrl { get; set; }
    [Id(13)] public DateTime SessionExpiresAt { get; set; }
}

[GenerateSerializer]
public class MarkCodeAsUsedLogEvent : InviteCodeLogEvent
{
    [Id(0)] public bool IsActive { get; set; }
    [Id(1)] public DateTime UsedAt { get; set; }
} 