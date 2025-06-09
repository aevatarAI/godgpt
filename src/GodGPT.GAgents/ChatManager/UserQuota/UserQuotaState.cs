using Aevatar.Application.Grains.Common.Constants;

namespace Aevatar.Application.Grains.ChatManager.UserQuota;

[GenerateSerializer]
public class UserQuotaState
{
    [Id(0)] public int Credits { get; set; } = 0;
    [Id(1)] public bool HasInitialCredits { get; set; } = false;
    [Id(2)] public bool HasShownInitialCreditsToast { get; set; } = false;
    
    // Legacy single subscription (maintain compatibility)
    [Id(3)] public SubscriptionInfo Subscription { get; set; } = new SubscriptionInfo();
    [Id(4)] public Dictionary<string, RateLimitInfo> RateLimits { get; set; } = new Dictionary<string, RateLimitInfo>();
    
    // New: Dual subscription support
    [Id(5)] public SubscriptionInfo StandardSubscription { get; set; } = new SubscriptionInfo();
    [Id(6)] public SubscriptionInfo UltimateSubscription { get; set; } = new SubscriptionInfo();
    
    // New: Freeze/Unfreeze tracking
    [Id(7)] public DateTime? StandardSubscriptionFrozenAt { get; set; }
    [Id(8)] public TimeSpan AccumulatedFrozenTime { get; set; } = TimeSpan.Zero;
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