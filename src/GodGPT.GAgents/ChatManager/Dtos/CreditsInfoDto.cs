namespace Aevatar.Application.Grains.ChatManager.UserQuota;

[GenerateSerializer]
public class CreditsInfoDto
{
    [Id(0)] public bool IsInitialized { get; set; }
    [Id(1)] public int Credits { get; set; }
    [Id(2)] public bool ShouldShowToast { get; set; }
} 