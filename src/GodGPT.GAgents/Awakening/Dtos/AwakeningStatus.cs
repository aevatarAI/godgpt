namespace GodGPT.GAgents.Awakening.Dtos;

/// <summary>
/// Awakening generation status enum
/// </summary>
[GenerateSerializer]
public enum AwakeningStatus
{
    NotStarted = 0,    // Not started
    Generating = 1,    // Generating in progress
    Completed = 2      // Generation completed (success or failure)
}
