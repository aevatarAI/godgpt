using Aevatar.Application.Grains.ChatManager.UserBilling.Payment;
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
    [Obsolete]
    [Id(8)] public PaymentType PaymentType { get; set; }
    [Obsolete]
    [Id(9)] public PaymentMethod Method { get; set; }
    [Id(10)] public PaymentPlatform Platform {get; set;}
    [Obsolete]
    [Id(11)] public bool IsSubscriptionRenewal { get; set; } = false;
    [Id(12)] public string SubscriptionId { get; set; }
    [Id(13)] public DateTime SubscriptionStartDate { get; set; }
    [Id(14)] public DateTime SubscriptionEndDate { get; set; }
    [Obsolete]
    [Id(15)] public string SessionId { get; set; }
    [Id(16)] public Guid UserId { get; set; }
    [Id(17)] public string PriceId { get; set; }
    [Id(18)] public List<UserBillingInvoiceDetail> InvoiceDetails { get; set; } = new List<UserBillingInvoiceDetail>();
    [Id(19)] public string AppStoreEnvironment { get; set; }
    [Id(20)] public string MembershipLevel { get; set; }
    //Total after discounts and taxes.
    [Id(21)] public decimal? AmountNetTotal { get; set; }
}

[GenerateSerializer]
public class UserBillingInvoiceDetail
{
    [Id(0)] public string InvoiceId { get; set; }
    [Id(1)] public DateTime CreatedAt { get; set; }
    [Id(2)] public DateTime CompletedAt { get; set; }
    [Id(3)] public PaymentStatus Status { get; set; }
    [Id(4)] public DateTime SubscriptionStartDate { get; set; }
    [Id(5)] public DateTime SubscriptionEndDate { get; set; }
    [Id(6)] public string PriceId { get; set; }
    [Id(7)] public string MembershipLevel { get; set; }
    [Id(8)] public decimal? Amount { get; set; }
    [Id(9)] public PlanType PlanType { get; set; }
    [Id(10)] public string PurchaseToken { get; set; }
    [Id(11)] public string? Currency { get; set; }
    [Id(12)] public decimal? AmountNetTotal { get; set; }
    [Id(13)] public List<DiscountDetails>? Discounts { get; set; }
    [Id(14)] public bool IsTrial { get; set; }
    [Id(15)] public string TrialCode { get; set; }
}


[GenerateSerializer]
public class PaymentSummaryDto
{
    [Id(0)] public Guid PaymentGrainId { get; set; }
    [Id(1)] public string? OrderId { get; set; }
    [Id(2)] public PlanType PlanType { get; set; }
    [Id(3)] public decimal Amount { get; set; }
    [Id(4)] public string? Currency { get; set; } = "USD";
    [Id(5)] public DateTime CreatedAt { get; set; }
    [Id(6)] public DateTime? CompletedAt { get; set; }
    [Id(7)] public PaymentStatus Status { get; set; }
    [Id(8)] public PaymentPlatform Platform {get; set;}
    [Id(9)] public string? SubscriptionId { get; set; }
    [Id(10)] public DateTime SubscriptionStartDate { get; set; }
    [Id(11)] public DateTime SubscriptionEndDate { get; set; }
    [Id(12)] public Guid UserId { get; set; }
    [Id(13)] public string? PriceId { get; set; }
    [Id(14)] public string? AppStoreEnvironment { get; set; }
    [Id(15)] public string? MembershipLevel { get; set; }
    [Id(16)] public decimal? AmountNetTotal { get; set; }
    [Id(17)] public bool IsTrial { get; set; }
    [Id(18)] public string? TrialCode { get; set; }
    [Id(19)] public PaymentType PaymentType { get; set; }
}
