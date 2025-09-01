using Newtonsoft.Json;
using System;

namespace Aevatar.Application.Grains.ChatManager.Dtos
{
    public class GooglePlayRTDNDto
    {
        [JsonProperty("message")]
        public GooglePlayMessage Message { get; set; }

        [JsonProperty("subscription")]
        public string Subscription { get; set; }
    }

    public class GooglePlayMessage
    {
        [JsonProperty("data")]
        public string Data { get; set; }

        [JsonProperty("messageId")]
        public string MessageId { get; set; }

        [JsonProperty("publishTime")]
        public DateTime PublishTime { get; set; }
    }

    public class DeveloperNotification
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("packageName")]
        public string PackageName { get; set; }

        [JsonProperty("eventTimeMillis")]
        public long EventTimeMillis { get; set; }

        [JsonProperty("subscriptionNotification")]
        public SubscriptionNotification SubscriptionNotification { get; set; }

        [JsonProperty("testNotification")]
        public TestNotification TestNotification { get; set; }
        
        [JsonProperty("voidedPurchaseNotification")]
        public VoidedPurchaseNotification VoidedPurchaseNotification { get; set; }
    }

    public class SubscriptionNotification
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("notificationType")]
        public int NotificationType { get; set; }

        [JsonProperty("purchaseToken")]
        public string PurchaseToken { get; set; }

        [JsonProperty("subscriptionId")]
        public string SubscriptionId { get; set; }
    }

    public class TestNotification
    {
        [JsonProperty("version")]
        public string Version { get; set; }
    }
    
    public class VoidedPurchaseNotification
    {
        [JsonProperty("purchaseToken")]
        public string PurchaseToken { get; set; }

        [JsonProperty("orderId")]
        public string OrderId { get; set; }

        [JsonProperty("productType")]
        public int ProductType { get; set; }

        [JsonProperty("refundTimeMillis")]
        public long RefundTimeMillis { get; set; }
    }
}

