namespace Aevatar.Application.Grains.Agents.Anonymous;

/// <summary>
/// Guest session information for anonymous users
/// </summary>
[GenerateSerializer]
public class GuestSessionInfo
{
    [Id(0)] public Guid SessionId { get; set; }
    [Id(1)] public string? Guider { get; set; }
    [Id(2)] public DateTime CreatedAt { get; set; }
    [Id(3)] public int ChatCount { get; set; }
    [Id(4)] public int RemainingChats { get; set; }
} 