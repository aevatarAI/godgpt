using Aevatar.Application.Grains.Common.Constants;
using Stripe;

namespace Aevatar.Application.Grains.ChatManager.Dtos;

[GenerateSerializer]
public class CreateCheckoutSessionDto
{
    [Id(0)] public string UserId { get; set; }
    [Id(1)] public string PriceId { get; set; }
    
    /// <summary>
    /// Payment mode: Use values from PaymentMode class (PAYMENT, SETUP, SUBSCRIPTION)
    /// </summary>
    [Id(2)] public string Mode { get; set; } = PaymentMode.SUBSCRIPTION;
    
    [Id(3)] public long Quantity { get; set; }
    
    /// HOSTED, EMBEDDED、CUSTOM
    [Id(4)] public string UiMode { get; set; } = StripeUiMode.HOSTED;
    
    /// <summary>
    /// - card、 alipay、wechat_pay、paypal: PayPal、apple_pay: Apple Pay、google_pay: Google Pay、link: Stripe Link
    /// https://docs.stripe.com/api/payment_methods/object#payment_method_object-type
    /// </summary>
    [Id(5)] public List<string> PaymentMethodTypes { get; set; } = new List<string>() { "card", "link" };
    
    /// always、automatic
    [Id(6)] public string PaymentMethodCollection { get; set; } = "always";
    
    [Id(7)] public string PaymentMethodConfiguration { get; set; }
    [Id(8)] public string CancelUrl { get; set; }
}