using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.UserQuota;


[GenerateSerializer]
public class UserQuotaGAgentState : StateBase
{
    [Id(0)] public string UserId { get; set; }
    [Id(1)] public int Credits { get; set; } = 0;
    [Id(2)] public bool HasInitialCredits { get; set; } = false;
    [Id(3)] public bool HasShownInitialCreditsToast { get; set; } = false;
    [Id(4)] public SubscriptionInfo Subscription { get; set; } = new SubscriptionInfo();
    [Id(5)] public Dictionary<string, RateLimitInfo> RateLimits { get; set; } = new Dictionary<string, RateLimitInfo>();
    [Id(6)] public SubscriptionInfo UltimateSubscription { get; set; } = new SubscriptionInfo();
    [Id(7)] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Id(8)] public bool CanReceiveInviteReward { get; set; } = true;
    [Id(9)] public bool IsInitializedFromGrain { get; set; } = false;
    [Id(10)] public DailyImageConversationInfo DailyImageConversation { get; set; } = new DailyImageConversationInfo();
    [Id(11)] public FreeTrialInfo? FreeTrialInfo { get; set; }
}

[GenerateSerializer]
public class FreeTrialInfo
{
    [Id(1)] public string FreeTrialCode { get; set; }
    [Id(2)] public int TrialDays { get; set; }
    [Id(3)] public PlanType PlanType { get; set; }
    [Id(4)] public bool IsUltimate { get; set; }
    [Id(5)] public string TransactionId { get; set; }
}