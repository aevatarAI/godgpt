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
    [Id(1)] public DateTime CreatedAt { get; set; }
    [Id(2)] public DateTime? CompletedAt { get; set; }
    [Id(3)] public PaymentStatus Status { get; set; }
    [Id(4)] public PaymentType Type { get; set; }
    [Id(5)] public PaymentMethod Method { get; set; }
    [Id(6)] public PaymentPlatform Platform {get; set;}
    [Id(7)] public decimal Amount { get; set; }
    [Id(8)] public string Currency { get; set; } = "USD";
    [Id(9)] public string Description { get; set; }
    [Id(10)] public bool IsSubscriptionRenewal { get; set; } = false;
    [Id(11)] public DateTime LastUpdated { get; set; }
}

