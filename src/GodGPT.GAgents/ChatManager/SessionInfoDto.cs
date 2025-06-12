namespace Aevatar.Application.Grains.Agents.ChatManager;

[GenerateSerializer]
public class SessionInfoDto
{
    [Id(0)] public Guid SessionId { get; set; }
    [Id(1)] public string Title { get; set; }
    [Id(2)] public DateTime CreateAt { get; set; }
    [Id(3)] public string? Guider { get; set; } // Role information for the conversation
    [Id(4)] public string Content { get; set; } = string.Empty;  // Chat content preview (first 60 characters)
    [Id(5)] public bool IsMatch { get; set; } = false;    // Whether this is a search result, used for frontend distinction
} 