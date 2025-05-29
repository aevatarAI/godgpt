using Aevatar.Application.Grains.Common.Constants;

namespace Aevatar.Application.Grains.ChatManager.UserBilling.Payment;

public static class PaymentMethodHelper
{
    private static readonly Dictionary<string, PaymentMethod> _paymentMethodMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "card", PaymentMethod.Card },
        { "link", PaymentMethod.Link },
        { "apple_pay", PaymentMethod.ApplePay },
        { "google_pay", PaymentMethod.GooglePay },
        { "paypal", PaymentMethod.PayPal },
        { "alipay", PaymentMethod.Other },
        { "wechat_pay", PaymentMethod.Other }
    };
    
    public static PaymentMethod MapToPaymentMethod(this IEnumerable<string> paymentMethodTypes)
    {
        if (paymentMethodTypes == null || !paymentMethodTypes.Any())
            return PaymentMethod.Other;
        
        if (paymentMethodTypes.Any(t => _paymentMethodMap.TryGetValue(t, out var m) && m == PaymentMethod.Card))
            return PaymentMethod.Card;
            
        foreach (var type in paymentMethodTypes)
        {
            if (_paymentMethodMap.TryGetValue(type, out var method))
                return method;
        }
        
        return PaymentMethod.Other;
    }
} 