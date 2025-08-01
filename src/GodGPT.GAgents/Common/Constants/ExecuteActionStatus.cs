namespace Aevatar.Application.Grains.ChatManager.UserQuota;

public static class ExecuteActionStatus
{
    public const int Success = 0;
    
    // Credits related status codes
    public const int InsufficientCredits = 20001;
    
    // Rate limiting status codes
    public const int RateLimitExceeded = 20002;
    
}