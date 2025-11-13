using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Core.Abstractions;
using Aevatar.Application.Grains.ChatManager.UserBilling;

namespace Aevatar.Application.Grains.UserBilling.SEvents;

[GenerateSerializer]
public class UserBillingLogEvent : StateLogEventBase<UserBillingLogEvent>
{
}

[GenerateSerializer]
public class AddPaymentLogEvent : UserBillingLogEvent
{
    [Id(0)] public PaymentSummary PaymentSummary { get; set; }
}

[GenerateSerializer]
public class UpdatePaymentLogEvent : UserBillingLogEvent
{
    [Id(0)] public Guid PaymentId { get; set; }
    [Id(1)] public PaymentSummary PaymentSummary { get; set; }
}

[GenerateSerializer]
public class UpdatePaymentStatusLogEvent : UserBillingLogEvent
{
    [Id(0)] public Guid PaymentId { get; set; }
    [Id(1)] public PaymentStatus NewStatus { get; set; }
}

[GenerateSerializer]
public class ClearAllLogEvent : UserBillingLogEvent
{
    [Id(0)] public DateTime ClearTime { get; set; }
}

[GenerateSerializer]
public class UpdateExistingSubscriptionLogEvent : UserBillingLogEvent
{
    [Id(0)] public string SubscriptionId { get; set; }
    [Id(1)] public ChatManager.UserBilling.PaymentSummary ExistingSubscription { get; set; }
}

[GenerateSerializer]
public class UpdateCustomerIdLogEvent : UserBillingLogEvent
{
    [Id(0)] public string CustomerId { get; set; }
}

[GenerateSerializer]
public class UpdatePaymentBySubscriptionIdLogEvent : UserBillingLogEvent
{
    [Id(0)] public string SubscriptionId { get; set; }
    [Id(1)] public PaymentSummary PaymentSummary { get; set; }
}

[GenerateSerializer]
public class RemovePaymentHistoryLogEvent : UserBillingLogEvent
{
    [Id(0)] public List<PaymentSummary> RecordsToRemove { get; set; }
}

[GenerateSerializer]
public class InitializeFromGrainLogEvent : UserBillingLogEvent
{
    [Id(0)] public string CustomerId { get; set; }
    [Id(1)] public List<PaymentSummary> PaymentHistory { get; set; }
    [Id(2)] public int TotalPayments { get; set; }
    [Id(3)] public int RefundedPayments { get; set; }
}

[GenerateSerializer]
public class MarkInitializedLogEvent : UserBillingLogEvent
{
}