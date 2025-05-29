namespace Aevatar.Application.Grains.ChatManager.UserQuota;

[GenerateSerializer]
public class ExecuteActionResultDto
{
    [Id(0)] public bool Success { get; set; } = false;
    [Id(1)] public int Code { get; set; }
    [Id(2)] public string Message { get; set; } = string.Empty;
}