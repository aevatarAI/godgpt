using Aevatar.Application.Grains.Common.Constants;
using System;
using System.Collections.Generic;

namespace Aevatar.Application.Grains.ChatManager.UserQuota;


[GenerateSerializer]
public class UserQuotaState
{
    [Id(0)] public int Credits { get; set; } = 0;
    [Id(1)] public bool HasInitialCredits { get; set; } = false;
    [Id(2)] public bool HasShownInitialCreditsToast { get; set; } = false;
    [Id(3)] public SubscriptionInfo Subscription { get; set; } = new SubscriptionInfo();
    [Id(4)] public Dictionary<string, RateLimitInfo> RateLimits { get; set; } = new Dictionary<string, RateLimitInfo>();
    [Id(5)] public SubscriptionInfo UltimateSubscription { get; set; } = new SubscriptionInfo();
    [Id(6)] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Id(7)] public bool CanReceiveInviteReward { get; set; } = true;
}

[GenerateSerializer]
public class SubscriptionInfo
{
    [Id(0)] public bool IsActive { get; set; } = false;
    [Id(1)] public PlanType PlanType { get; set; }
    [Id(2)] public PaymentStatus Status { get; set; }
    [Id(3)] public DateTime StartDate { get; set; }
    [Id(4)] public DateTime EndDate { get; set; }
    [Id(5)] public List<string> SubscriptionIds { get; set; } = new List<string>();
    [Id(6)] public List<string> InvoiceIds { get; set; } = new List<string>();
}

[GenerateSerializer]
public class RateLimitInfo
{
    [Id(0)] public int Count { get; set; }
    [Id(1)] public DateTime LastTime { get; set; }
}