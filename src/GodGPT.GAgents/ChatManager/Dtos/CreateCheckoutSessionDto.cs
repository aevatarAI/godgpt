using Aevatar.Application.Grains.Common.Constants;

namespace Aevatar.Application.Grains.ChatManager.Dtos;

[GenerateSerializer]
public class CreateCheckoutSessionDto
{
    [Id(0)] public string UserId { get; set; }
    [Id(1)] public string PriceId { get; set; }
    
    /// <summary>
    /// Payment mode: Use values from PaymentMode class (PAYMENT, SETUP, SUBSCRIPTION)
    /// </summary>
    [Id(2)] public string Mode { get; set; } = PaymentMode.PAYMENT;
    
    [Id(3)] public long Quantity { get; set; }
    [Id(4)] public string UiMode { get; set; } = StripeUiMode.HOSTED;
}