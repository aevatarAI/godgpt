using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.UserBilling;

[GenerateSerializer]
public class UserBillingGAgentState : StateBase
{
    [Id(0)] public string UserId { get; set; }
    [Id(1)] public bool IsInitializedFromGrain { get; set; }
    [Id(2)] public string CustomerId { get; set; }
    [Id(3)] public List<PaymentSummary> PaymentHistory { get; set; } = new List<PaymentSummary>();
    [Id(4)] public int TotalPayments { get; set; }
    [Id(5)] public int RefundedPayments { get; set; }
    [Id(6)] public DateTime? LastClearTime { get; set; }
    
}