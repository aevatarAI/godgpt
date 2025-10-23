using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Helpers;

namespace Aevatar.Application.Grains.ChatManager.Dtos;

[GenerateSerializer]
public class StripeProductDto
{
    [Id(0)] public PlanType PlanType { get; set; }
    [Id(1)] public string PriceId { get; set; }
    
    /// <summary>
    /// Payment mode: Uses values from PaymentMode class (PAYMENT, SETUP, SUBSCRIPTION)
    /// </summary>
    [Id(2)] public string Mode { get; set; }
    
    [Id(3)] public decimal Amount { get; set; }
    [Id(4)] public string DailyAvgPrice { get; set; }
    [Id(5)] public string Currency { get; set; }
    
    /// <summary>
    /// Indicates if this is an Ultimate subscription plan
    /// </summary>
    [Id(6)] public bool IsUltimate { get; set; }
    [Id(7)] public int Credits { get; set; } = 0;
}