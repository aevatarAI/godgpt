using System.Text.Json.Serialization;

namespace Aevatar.Application.Grains.ChatManager.Dtos;

[GenerateSerializer]
public class AppStoreTransactionInfoDto
{
    [JsonPropertyName("transactionId")]
    [Id(0)] public string TransactionId { get; set; }
    
    [JsonPropertyName("originalTransactionId")]
    [Id(1)] public string OriginalTransactionId { get; set; }
    
    [JsonPropertyName("productId")]
    [Id(2)] public string ProductId { get; set; }
    
    [JsonPropertyName("subscriptionGroupIdentifier")]
    [Id(3)] public string SubscriptionGroupIdentifier { get; set; }
    
    [JsonPropertyName("purchaseDate")]
    [Id(4)] public long PurchaseDate { get; set; }
    
    [JsonPropertyName("expiresDate")]
    [Id(5)] public long? ExpiresDate { get; set; }
    
    [JsonPropertyName("quantity")]
    [Id(6)] public int Quantity { get; set; }
    
    [JsonPropertyName("type")]
    [Id(7)] public string Type { get; set; }
    
    [JsonPropertyName("inAppOwnershipType")]
    [Id(8)] public string InAppOwnershipType { get; set; }
    
    [JsonPropertyName("signedDate")]
    [Id(9)] public long SignedDate { get; set; }
    
    [JsonPropertyName("environment")]
    [Id(10)] public string Environment { get; set; }
    
    [JsonPropertyName("price")]
    [Id(11)] public decimal Price { get; set; }
    
    [JsonPropertyName("currency")]
    [Id(12)] public string Currency { get; set; }
    
    [JsonPropertyName("status")]
    [Id(13)] public int Status { get; set; }
}