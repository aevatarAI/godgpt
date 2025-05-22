using System;
using System.Collections.Generic;

namespace Aevatar.Application.Grains.ChatManager.Dtos
{
    [GenerateSerializer]
    public class CreateSubscriptionDto
    {
        [Id(0)]
        public Guid UserId { get; set; }
        [Id(1)]
        public string PriceId { get; set; }
        [Id(2)]
        public int? Quantity { get; set; } = 1;
        [Id(3)]
        public string PaymentMethodId { get; set; }
        [Id(4)]
        public string Description { get; set; }
        [Id(5)]
        public Dictionary<string, string> Metadata { get; set; }
        [Id(6)] 
        public int? TrialPeriodDays { get; set; } = 0;
        [Id(7)] 
        public string? Platform { get; set; } = "android"; //android/ios
    }

    [GenerateSerializer]
    public class SubscriptionResponseDto
    {
        [Id(0)]
        public string SubscriptionId { get; set; }
        [Id(1)]
        public string CustomerId { get; set; }
        [Id(2)]
        public string ClientSecret { get; set; }
    }
    
    [GenerateSerializer]
    public class CancelSubscriptionDto
    {
        [Id(0)] 
        public Guid UserId { get; set; }
        [Id(1)] 
        public string SubscriptionId { get; set; }
        [Id(2)] 
        public string CancellationReason { get; set; }
        //Currently, only "true" has been implemented to cancel subscription renewal.
        [Id(3)] 
        public bool CancelAtPeriodEnd { get; set; } = true;
    }
    
    [GenerateSerializer]
    public class CancelSubscriptionResponseDto
    {
        [Id(0)] 
        public bool Success { get; set; }
        [Id(1)] 
        public string Message { get; set; }
        [Id(2)] 
        public string SubscriptionId { get; set; }
        [Id(3)] 
        public string Status { get; set; }
        [Id(4)] 
        public DateTime? CancelledAt { get; set; }
    }
} 