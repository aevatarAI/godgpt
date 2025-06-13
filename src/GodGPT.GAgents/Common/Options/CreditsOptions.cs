namespace Aevatar.Application.Grains.Common.Options;

[GenerateSerializer]
public class CreditsOptions
{
    [Id(0)] public int InitialCreditsAmount { get; set; } = 320;
    [Id(1)] public int CreditsPerConversation { get; set; } = 10;
    [Id(2)] public List<string> OperatorUserId { get; set; } = new List<string>();
}