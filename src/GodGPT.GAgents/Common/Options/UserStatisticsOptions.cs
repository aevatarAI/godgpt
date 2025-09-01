namespace Aevatar.Application.Grains.Common.Options;

[GenerateSerializer]
public class UserStatisticsOptions
{
    [Id(0)] public int RatingIntervalMinutes { get; set; } = 10080; // 7 days = 7 * 24 * 60 = 10080 minutes
}
