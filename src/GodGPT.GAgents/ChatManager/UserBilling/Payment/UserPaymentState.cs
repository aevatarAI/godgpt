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
    //Total after discounts and taxes.
    [Id(20)] public decimal? AmountNetTotal { get; set; }
    [Id(21)] public List<DiscountDetails>? Discounts { get; set; }
    [Id(22)] public bool IsTrial { get; set; }
    [Id(23)] public string TrialCode { get; set; }

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
            InvoiceDetails = ToInvoiceDetailDtos(),
            AmountNetTotal = this.AmountNetTotal,
            Discounts = this.Discounts,
            IsTrial = this.IsTrial,
            TrialCode = this.TrialCode,
            PaymentIntentId = PaymentIntentId
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
                CompletedAt = paymentInvoiceDetail.CompletedAt,
                Amount = paymentInvoiceDetail.Amount,
                AmountNetTotal = paymentInvoiceDetail.AmountNetTotal,
                Discounts = paymentInvoiceDetail.Discounts,
                IsTrial = paymentInvoiceDetail.IsTrial,
                TrialCode = paymentInvoiceDetail.TrialCode
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
            AmountNetTotal = dto.AmountNetTotal,
            Discounts = dto.Discounts,
            IsTrial = dto.IsTrial,
            TrialCode = dto.TrialCode,
            PaymentIntentId = dto.PaymentIntentId
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
                CompletedAt = invoiceDetailDto.CompletedAt,
                Amount = invoiceDetailDto.Amount,
                AmountNetTotal = invoiceDetailDto.AmountNetTotal,
                Discounts = invoiceDetailDto.Discounts,
                IsTrial = invoiceDetailDto.IsTrial,
                TrialCode = invoiceDetailDto.TrialCode
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
    [Id(4)] public decimal Amount { get; set; }   
    [Id(5)] public decimal? AmountNetTotal { get; set; }
    [Id(6)] public List<DiscountDetails>? Discounts { get; set; }
    [Id(7)] public bool IsTrial { get; set; }
    [Id(8)] public string TrialCode { get; set; }
}

[GenerateSerializer]
public class DiscountDetails
{
    [Id(0)] public string DiscountId { get; set; }
    [Id(1)] public string CouponId { get; set; }
    [Id(2)] public string CouponName { get; set; }
    [Id(3)] public long? AmountOff { get; set; }
    [Id(4)] public decimal? PercentOff { get; set; }
    [Id(5)] public string PromotionCodeId { get; set; }
    [Id(6)] public string PromotionCode { get; set; }
}