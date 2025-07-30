using Aevatar.GAgents.AI.Common;

namespace GodGPT.GAgents.Awakening.Dtos;

/// <summary>
/// Data transfer object for session content
/// </summary>
[GenerateSerializer]
public class SessionContentDto
{
    [Id(0)] public Guid SessionId { get; set; }
    [Id(1)] public string Title { get; set; } = string.Empty;
    [Id(2)] public List<ChatMessage> Messages { get; set; } = new();
    [Id(3)] public DateTime LastActivityTime { get; set; }
    [Id(4)] public string ExtractedContent { get; set; } = string.Empty;
}
