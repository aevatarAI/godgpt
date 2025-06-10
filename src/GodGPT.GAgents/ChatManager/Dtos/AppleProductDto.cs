namespace Aevatar.Application.Grains.ChatManager.Dtos;

[GenerateSerializer]
public class AppleProductDto
{
    [Id(0)] public int PlanType { get; set; }
    [Id(1)]  public string ProductId { get; set; }
    [Id(2)] public string Name { get; set; }
    [Id(3)] public string Description { get; set; }
    [Id(4)] public decimal Amount { get; set; }
    [Id(5)] public string Currency { get; set; } = "USD";
    [Id(6)] public string DailyAvgPrice { get; set; }
}