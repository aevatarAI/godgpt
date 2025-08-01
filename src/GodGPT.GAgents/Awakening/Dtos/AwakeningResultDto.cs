namespace GodGPT.GAgents.Awakening.Dtos;

/// <summary>
/// Data transfer object for awakening generation result
/// </summary>
[GenerateSerializer]
public class AwakeningResultDto
{
    [Id(0)] public bool IsSuccess { get; set; }
    [Id(1)] public int AwakeningLevel { get; set; }
    [Id(2)] public string AwakeningMessage { get; set; } = string.Empty;
    [Id(3)] public long Timestamp { get; set; }
    [Id(4)] public string ErrorMessage { get; set; } = string.Empty;
}
