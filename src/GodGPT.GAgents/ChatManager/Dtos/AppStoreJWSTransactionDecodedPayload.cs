using System.Text.Json.Serialization;

namespace Aevatar.Application.Grains.ChatManager.Dtos;

[GenerateSerializer]
public class AppStoreJWSTransactionDecodedPayload
{
    [JsonPropertyName("transactionId")]
    [Id(0)] public string TransactionId { get; set; }
    
    [JsonPropertyName("originalTransactionId")]
    [Id(1)] public string OriginalTransactionId { get; set; }
    
    [JsonPropertyName("webOrderLineItemId")]
    [Id(2)] public string WebOrderLineItemId { get; set; }
    
    [JsonPropertyName("bundleId")]
    [Id(3)] public string BundleId { get; set; }
    
    [JsonPropertyName("productId")]
    [Id(4)] public string ProductId { get; set; }
    
    [JsonPropertyName("subscriptionGroupIdentifier")]
    [Id(5)] public string SubscriptionGroupIdentifier { get; set; }
    
    [JsonPropertyName("purchaseDate")]
    [Id(6)] public long PurchaseDate { get; set; }
    
    [JsonPropertyName("originalPurchaseDate")]
    [Id(7)] public long OriginalPurchaseDate { get; set; }
    
    [JsonPropertyName("expiresDate")]
    [Id(8)] public long? ExpiresDate { get; set; }
    
    [JsonPropertyName("quantity")]
    [Id(9)] public int Quantity { get; set; }
    
    [JsonPropertyName("type")]
    [Id(10)] public string Type { get; set; }
    
    [JsonPropertyName("inAppOwnershipType")]
    [Id(11)] public string InAppOwnershipType { get; set; }
    
    [JsonPropertyName("signedDate")]
    [Id(12)] public long SignedDate { get; set; }
    
    [JsonPropertyName("environment")]
    [Id(13)] public string Environment { get; set; }
    
    [JsonPropertyName("transactionReason")]
    [Id(14)] public string TransactionReason { get; set; }
    
    [JsonPropertyName("storefront")]
    [Id(15)] public string Storefront { get; set; }
    
    [JsonPropertyName("storefrontId")]
    [Id(16)] public string StorefrontId { get; set; }
    
    [JsonPropertyName("price")]
    [Id(17)] public decimal Price { get; set; }
    
    [JsonPropertyName("currency")]
    [Id(18)] public string Currency { get; set; }
    
    [JsonPropertyName("appTransactionId")]
    [Id(19)] public string AppTransactionId { get; set; }
}