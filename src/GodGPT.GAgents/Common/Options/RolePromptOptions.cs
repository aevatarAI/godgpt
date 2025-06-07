namespace Aevatar.Application.Grains.Agents.ChatManager.Options;

[GenerateSerializer]
public class RolePromptOptions
{
    [Id(0)]
    public Dictionary<string, string> RolePrompts { get; set; } = new();
} 