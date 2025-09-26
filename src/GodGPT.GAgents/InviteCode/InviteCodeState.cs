using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Agents.Invitation;

[GenerateSerializer]
public class InviteCodeState : StateBase
{
    [Id(0)] public string InviterId { get; set; }
    [Id(1)] public DateTime CreatedAt { get; set; }
    [Id(2)] public bool IsActive { get; set; }
    [Id(3)] public int UsageCount { get; set; }
    
    // New fields to support free trial reward codes
    [Id(4)] public string InviteCode { get; set; }
    [Id(5)] public InvitationCodeType CodeType { get; set; }
    [Id(6)] public long BatchId { get; set; }
    [Id(7)] public int TrialDays { get; set; }
    [Id(8)] public string ProductId { get; set; }
    [Id(9)] public PlanType PlanType { get; set; }
    [Id(10)] public bool IsUltimate { get; set; }
    [Id(11)] public PaymentPlatform Platform { get; set; } = PaymentPlatform.Stripe;
    [Id(12)] public string InviteeId { get; set; }
    [Id(13)] public DateTime? UsedAt { get; set; }
    [Id(14)] public string SessionUrl { get; set; }
    [Id(15)] public DateTime SessionExpiresAt { get; set; }
} 