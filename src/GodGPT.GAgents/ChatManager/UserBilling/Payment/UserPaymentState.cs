using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.ChatManager.Dtos;

namespace Aevatar.Application.Grains.ChatManager.UserBilling.Payment;

[GenerateSerializer]
public class UserPaymentState
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
    [Id(18)] public string PaymentIntentId { get; set; }
    [Id(19)] public List<PaymentInvoiceDetail> InvoiceDetails { get; set; } = new List<PaymentInvoiceDetail>();

    public PaymentDetailsDto ToDto()
    {
        return new PaymentDetailsDto
        {
            Id = this.Id,
            UserId = this.UserId,
            PriceId = this.PriceId,
            Amount = this.Amount,
            Currency = this.Currency,
            PaymentType = this.PaymentType,
            Status = this.Status,
            Method = this.Method,
            Platform = this.Platform,
            Mode = this.Mode,
            Description = this.Description,
            CreatedAt = this.CreatedAt,
            CompletedAt = this.CompletedAt,
            LastUpdated = this.LastUpdated,
            OrderId = this.OrderId,
            SubscriptionId = this.SubscriptionId,
            InvoiceId = this.InvoiceId,
            SessionId = this.SessionId,
            InvoiceDetails = ToInvoiceDetailDtos()
        };
    }

    public List<PaymentInvoiceDetailDto> ToInvoiceDetailDtos()
    {
        var paymentInvoiceDetailDtos = new List<PaymentInvoiceDetailDto>();
        if (this.InvoiceDetails.IsNullOrEmpty())
        {
            return paymentInvoiceDetailDtos;
        }

        foreach (var paymentInvoiceDetail in this.InvoiceDetails)
        {
            paymentInvoiceDetailDtos.Add(new PaymentInvoiceDetailDto
            {
                InvoiceId = paymentInvoiceDetail.InvoiceId,
                Status = paymentInvoiceDetail.Status,
                CreatedAt = paymentInvoiceDetail.CreatedAt,
                CompletedAt = paymentInvoiceDetail.CompletedAt
            });
        }

        return paymentInvoiceDetailDtos;
    }
    
    public static UserPaymentState FromDto(PaymentDetailsDto dto)
    {
        return new UserPaymentState
        {
            Id = dto.Id,
            UserId = dto.UserId,
            PriceId = dto.PriceId,
            Amount = dto.Amount,
            Currency = dto.Currency,
            PaymentType = dto.PaymentType,
            Status = dto.Status,
            Method = dto.Method,
            Platform = dto.Platform,
            Mode = dto.Mode,
            Description = dto.Description,
            CreatedAt = dto.CreatedAt,
            CompletedAt = dto.CompletedAt,
            LastUpdated = dto.LastUpdated,
            OrderId = dto.OrderId,
            SubscriptionId = dto.SubscriptionId,
            InvoiceId = dto.InvoiceId,
            SessionId = dto.SessionId,
            InvoiceDetails = FromInvoiceDetails(dto.InvoiceDetails),
        };
    }

    public static List<PaymentInvoiceDetail> FromInvoiceDetails(List<PaymentInvoiceDetailDto> detailDtos)
    {
        var paymentInvoiceDetails = new List<PaymentInvoiceDetail>();
        if (detailDtos.IsNullOrEmpty())
        {
            return paymentInvoiceDetails;
        }

        foreach (var invoiceDetailDto in detailDtos)
        {
            paymentInvoiceDetails.Add(new PaymentInvoiceDetail
            {
                InvoiceId = invoiceDetailDto.InvoiceId,
                Status = invoiceDetailDto.Status,
                CreatedAt = invoiceDetailDto.CreatedAt,
                CompletedAt = invoiceDetailDto.CompletedAt
            });
        }
        return paymentInvoiceDetails;
    }
}

[GenerateSerializer]
public class PaymentInvoiceDetail
{
    [Id(0)] public string InvoiceId { get; set; }
    [Id(1)] public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    [Id(2)] public DateTime CreatedAt { get; set; }
    [Id(3)] public DateTime? CompletedAt { get; set; }
}