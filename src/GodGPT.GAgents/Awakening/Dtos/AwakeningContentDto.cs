namespace GodGPT.GAgents.Awakening.Dtos;

/// <summary>
/// Data transfer object for awakening content
/// </summary>
[GenerateSerializer]
public class AwakeningContentDto
{
    [Id(0)] public int AwakeningLevel { get; set; }
    [Id(1)] public string AwakeningMessage { get; set; } = string.Empty;
    [Id(2)] public AwakeningStatus Status { get; set; } = AwakeningStatus.NotStarted;
}
