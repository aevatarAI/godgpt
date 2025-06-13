namespace Aevatar.Application.Grains.ChatManager.Dtos;

[GenerateSerializer]
public class CreatePaymentSheetDto
{
    [Id(0)] public Guid UserId { get; set; }
    [Id(1)] public string PriceId { get; set; }
    [Id(2)] public int Quantity { get; set; }
    [Id(3)] public long? Amount { get; set; }
    [Id(4)] public string Currency { get; set; } = "usd";
    /// Payment methods such as card, alipay, wechat_pay, paypal, etc.
    /// https://docs.stripe.com/api/payment_methods/object#payment_method_object-type
    [Id(5)] public List<string> PaymentMethodTypes { get; set; } = new List<string>() { "card", "link" };
    [Id(6)] public string Description { get; set; }
    [Id(7)] public bool AllowsDelayedPaymentMethods { get; set; } = false;
    [Id(8)] public string MerchantDisplayName { get; set; } = "Aevatar";
}

[GenerateSerializer]
public class PaymentSheetResponseDto
{
    [Id(0)] public string PaymentIntent { get; set; }
    [Id(1)] public string EphemeralKey { get; set; }
    [Id(2)] public string Customer { get; set; }
    [Id(3)] public string PublishableKey { get; set; }
}