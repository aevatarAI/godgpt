using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.UserQuota.SEvents;

[GenerateSerializer]
public class UserQuotaLogEvent : StateLogEventBase<UserQuotaLogEvent>
{
}

[GenerateSerializer]
public class InitializeCreditsLogEvent : UserQuotaLogEvent
{
    [Id(0)] public int InitialCredits { get; set; }
}

[GenerateSerializer]
public class SetShownCreditsToastLogEvent : UserQuotaLogEvent
{
    [Id(0)] public bool HasShownInitialCreditsToast { get; set; }
}

[GenerateSerializer]
public class UpdateRateLimitLogEvent : UserQuotaLogEvent
{
    [Id(0)] public string ActionType { get; set; }
    [Id(1)] public RateLimitInfo RateLimitInfo { get; set; }
}

[GenerateSerializer]
public class ClearRateLimitLogEvent : UserQuotaLogEvent
{
    [Id(0)] public string ActionType { get; set; }
}

[GenerateSerializer]
public class UpdateSubscriptionLogEvent : UserQuotaLogEvent
{
    [Id(0)] public SubscriptionInfoDto SubscriptionInfo { get; set; }
    [Id(1)] public bool IsUltimate { get; set; }
}

[GenerateSerializer]
public class CancelSubscriptionLogEvent : UserQuotaLogEvent
{
    [Id(0)] public bool IsUltimate { get; set; }
}

[GenerateSerializer]
public class UpdateCreditsLogEvent : UserQuotaLogEvent
{
    [Id(0)] public int NewCredits { get; set; }
}

[GenerateSerializer]
public class UpdateCanReceiveInviteRewardLogEvent : UserQuotaLogEvent
{
    [Id(0)] public bool CanReceiveInviteReward { get; set; }
}

[GenerateSerializer]
public class ClearAllLogEvent : UserQuotaLogEvent
{
    [Id(0)] public bool CanReceiveInviteReward { get; set; }
}

[GenerateSerializer]
public class InitializeFromGrainLogEvent : UserQuotaLogEvent
{
    [Id(0)] public int Credits { get; set; }
    [Id(1)] public bool HasInitialCredits { get; set; }
    [Id(2)] public bool HasShownInitialCreditsToast { get; set; }
    [Id(3)] public SubscriptionInfo Subscription { get; set; }
    [Id(4)] public Dictionary<string, RateLimitInfo> RateLimits { get; set; }
    [Id(5)] public SubscriptionInfo UltimateSubscription { get; set; }
    [Id(6)] public DateTime CreatedAt { get; set; }
    [Id(7)] public bool CanReceiveInviteReward { get; set; }
}

[GenerateSerializer]
public class MarkInitializedLogEvent : UserQuotaLogEvent
{
}

[GenerateSerializer]
public class UpdateDailyImageConversationLogEvent : UserQuotaLogEvent
{
    [Id(0)] public DailyImageConversationInfo DailyImageConversation { get; set; }
}

[GenerateSerializer]
public class ActivateFreeTrialLogEvent : UserQuotaLogEvent
{
    [Id(0)] public int TrialDays { get; set; }
    [Id(1)] public PlanType PlanType { get; set; }
    [Id(2)] public bool IsUltimate { get; set; }
    [Id(3)] public DateTime StartDate { get; set; }
    [Id(4)] public DateTime EndDate { get; set; }
}