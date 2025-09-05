using Aevatar.Application.Grains.ChatManager.UserBilling.Payment;
using Aevatar.Application.Grains.Common.Constants;

namespace Aevatar.Application.Grains.ChatManager.Dtos;

[GenerateSerializer]
public class PaymentDetailsDto
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public Guid UserId { get; set; }        
    [Id(2)] public string PriceId { get; set; }
    [Id(3)] public decimal Amount { get; set; }          
    [Id(4)] public string Currency { get; set; } = "USD";
    [Id(5)] public PaymentType PaymentType { get; set; }
    [Id(6)] public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    [Id(7)] public PaymentMethod Method { get; set; } 
    [Id(8)] public PaymentPlatform Platform { get; set; } = PaymentPlatform.Stripe;
    [Id(9)] public string Mode { get; set; } = PaymentMode.SUBSCRIPTION;
    [Id(10)] public string Description { get; set; }
    [Id(11)] public DateTime CreatedAt { get; set; }
    [Id(12)] public DateTime? CompletedAt { get; set; }
    [Id(13)] public DateTime LastUpdated { get; set; }
    [Id(14)] public string OrderId { get; set; }
    [Id(15)] public string SubscriptionId { get; set; }
    [Id(16)] public string InvoiceId { get; set; }
    [Id(17)] public string SessionId { get; set; }
    [Id(18)] public List<PaymentInvoiceDetailDto> InvoiceDetails { get; set; }
    //Total after discounts and taxes.
    [Id(19)] public decimal AmountNetTotal { get; set; }
    [Id(20)] public List<DiscountDetails> Discounts { get; set; }
}

[GenerateSerializer]
public class PaymentInvoiceDetailDto
{
    [Id(0)] public string InvoiceId { get; set; }
    [Id(1)] public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    [Id(2)] public DateTime CreatedAt { get; set; }
    [Id(3)] public DateTime? CompletedAt { get; set; }
    [Id(4)] public decimal Amount { get; set; }   
    [Id(5)] public decimal AmountNetTotal { get; set; }
    [Id(6)] public List<DiscountDetails> Discounts { get; set; }
}
