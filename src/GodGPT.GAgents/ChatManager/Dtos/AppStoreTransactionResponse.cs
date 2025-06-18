using System.Text.Json.Serialization;

namespace Aevatar.Application.Grains.ChatManager.Dtos;

[GenerateSerializer]
public class AppStoreTransactionResponse
{
    [JsonPropertyName("signedTransactionInfo")]
    [Id(0)] public string SignedTransactionInfo { get; set; }
}