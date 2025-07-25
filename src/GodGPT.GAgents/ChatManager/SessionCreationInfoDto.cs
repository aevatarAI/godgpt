namespace Aevatar.Application.Grains.Agents.ChatManager;

[GenerateSerializer]
public class SessionCreationInfoDto
{
    [Id(0)] public Guid SessionId { get; set; }
    [Id(1)] public string Title { get; set; }
    [Id(2)] public DateTime CreateAt { get; set; }
    [Id(3)] public string? Guider { get; set; } // Role information for the conversation
} 