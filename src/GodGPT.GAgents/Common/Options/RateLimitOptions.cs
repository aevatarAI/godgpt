namespace Aevatar.Application.Grains.Common.Options;

[GenerateSerializer]
public class RateLimitOptions
{
    [Id(0)] public int UserMaxRequests { get; set; } = 25;
    [Id(1)] public int UserTimeWindowSeconds { get; set; } = 60 * 60 * 3; //s
    [Id(2)] public int SubscribedUserMaxRequests { get; set; } = 50;
    [Id(3)] public int SubscribedUserTimeWindowSeconds { get; set; } = 60 * 60 * 3; //s
}