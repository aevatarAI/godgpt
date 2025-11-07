using Aevatar.GAgents.AI.Options;

namespace Aevatar.Application.Grains.GodChat.Dtos;

[GenerateSerializer]
public class StartStreamChatInput
{
    [Id(0)] public Guid SessionId { get; set; }
    [Id(1)] public string SysmLLM { get; set; }
    [Id(2)] public string Content { get; set; }
    [Id(3)] public string ChatId { get; set; }
    [Id(4)] public ExecutionPromptSettings PromptSettings { get; set; } = null;
    [Id(5)] public bool IsHttpRequest { get; set; } = false;
    [Id(6)] public string? region { get; set; } = null;
    [Id(7)] public List<string>? images { get; set; } = null;
    [Id(8)] public DateTime? UserLocalTime { get; set; } = null;
    [Id(9)] public string? UserTimeZoneId { get; set; } = string.Empty;
    [Id(10)] public string? Context { get; set; } = null;
}