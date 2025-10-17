using Aevatar.GAgents.AI.Options;

namespace Aevatar.Application.Grains.GodChat.Dtos;

[GenerateSerializer]
public class StartStreamChatInput
{
    public Guid SessionId { get; set; }
    public string SysmLLM { get; set; }
    public string Content { get; set; }
    public string ChatId { get; set; }
    public ExecutionPromptSettings PromptSettings { get; set; } = null;
    public bool IsHttpRequest { get; set; } = false;
    public string? region { get; set; } = null;
    public List<string>? images { get; set; } = null;
    public DateTime? UserLocalTime { get; set; } = null;
    public string? UserTimeZoneId { get; set; } = string.Empty;
}