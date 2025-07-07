namespace Aevatar.Application.Grains.Agents.Anonymous.Options;

/// <summary>
/// Configuration options for Anonymous GodGPT service
/// </summary>
[GenerateSerializer]
public class AnonymousGodGPTOptions
{
    /// <summary>
    /// Maximum chat count for anonymous users (default: 3)
    /// </summary>
    [Id(0)]
    public int MaxChatCount { get; set; } = 3;
    
    /// <summary>
    /// Whether anonymous chat feature is enabled (default: true)
    /// </summary>
    [Id(1)]
    public bool Enabled { get; set; } = true;
} 