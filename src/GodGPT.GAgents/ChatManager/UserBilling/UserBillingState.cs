using Aevatar.Application.Grains.Common.Constants;

namespace Aevatar.Application.Grains.ChatManager.UserBilling;

[GenerateSerializer]
public class UserBillingState
{
    [Id(0)] public string CustomerId { get; set; }
    [Id(1)] public List<PaymentSummary> PaymentHistory { get; set; } = new List<PaymentSummary>();
    [Id(2)] public int TotalPayments { get; set; }
    [Id(3)] public int RefundedPayments { get; set; }
}

[GenerateSerializer]
public class PaymentSummary
{
    [Id(0)] public Guid PaymentGrainId { get; set; }
    [Id(1)] public string OrderId { get; set; }
    [Id(2)] public PlanType PlanType { get; set; }
    [Id(3)] public decimal Amount { get; set; }
    [Id(4)] public string Currency { get; set; } = "USD";
    [Id(5)] public DateTime CreatedAt { get; set; }
    [Id(6)] public DateTime? CompletedAt { get; set; }
    [Id(7)] public PaymentStatus Status { get; set; }
    [Id(8)] public PaymentType PaymentType { get; set; }
    [Id(9)] public PaymentMethod Method { get; set; }
    [Id(10)] public PaymentPlatform Platform {get; set;}
    [Id(11)] public bool IsSubscriptionRenewal { get; set; } = false;
    [Id(12)] public string SubscriptionId { get; set; }
    [Id(13)] public DateTime SubscriptionStartDate { get; set; }
    [Id(14)] public DateTime SubscriptionEndDate { get; set; }
    [Id(15)] public string SessionId { get; set; }
    [Id(16)] public Guid UserId { get; set; }
}

