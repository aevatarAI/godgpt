using System;
using System.Collections.Generic;

namespace Aevatar.Application.Grains.ChatManager.Dtos
{
    [GenerateSerializer]
    public class CreateSubscriptionDto
    {
        [Id(0)] public Guid UserId { get; set; }
        [Id(1)] public string PriceId { get; set; }
        [Id(2)] public int? Quantity { get; set; }
        [Id(3)] public string PaymentMethodId { get; set; }
        [Id(4)] public string Description { get; set; }
        [Id(5)] public Dictionary<string, string> Metadata { get; set; }
        [Id(6)] public int? TrialPeriodDays { get; set; }
    }

    [GenerateSerializer]
    public class SubscriptionResponseDto
    {
        [Id(0)] public string SubscriptionId { get; set; }
        [Id(1)] public string CustomerId { get; set; }
        [Id(2)] public string ClientSecret { get; set; }
    }
} 