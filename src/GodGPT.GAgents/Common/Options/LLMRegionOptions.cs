namespace Aevatar.Application.Grains.Common.Options;

[GenerateSerializer]
public class LLMRegionOptions
{
    [Id(0)]
    public Dictionary<string, List<string>> RegionToLLMsMap { get; set; } = new Dictionary<string, List<string>>();
} 