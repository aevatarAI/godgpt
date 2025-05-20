namespace Aevatar.Application.Grains.ChatManager.Dtos;

[GenerateSerializer]
public class GetCustomerResponseDto
{
    [Id(0)] public string EphemeralKey { get; set; }
    [Id(1)] public string Customer { get; set; }
    [Id(2)] public string PublishableKey { get; set; }
}