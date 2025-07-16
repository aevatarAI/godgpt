namespace Aevatar.Application.Grains.Common.Options;

[GenerateSerializer]
public class RateLimitOptions
{
    // Text conversation rate limits
    [Id(0)] public int UserMaxRequests { get; set; } = 10;//25;
    [Id(1)] public int UserTimeWindowSeconds { get; set; } = 60 * 20;//60 * 60 * 3; //s
    [Id(2)] public int SubscribedUserMaxRequests { get; set; } = 15;//50;
    [Id(3)] public int SubscribedUserTimeWindowSeconds { get; set; } = 60 * 10;//60 * 60 * 3; //s
    
    // Voice conversation rate limits
    [Id(4)] public int VoiceUserMaxRequests { get; set; } = 5;//10; 
    [Id(5)] public int VoiceUserTimeWindowSeconds { get; set; } = 60 * 10;//60 * 60 * 3; //s
    [Id(6)] public int VoiceSubscribedUserMaxRequests { get; set; } = 8;//50; 
    [Id(7)] public int VoiceSubscribedUserTimeWindowSeconds { get; set; } =  60 * 10;//60 * 60 * 3; //s
}