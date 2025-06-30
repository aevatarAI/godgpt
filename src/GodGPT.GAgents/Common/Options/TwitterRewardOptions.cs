namespace Aevatar.Application.Grains.Common.Options;

[GenerateSerializer]
public class TwitterRewardOptions
{
    public const string SectionName = "TwitterReward";
    
    // Twitter API 配置
    [Id(0)] public string BearerToken { get; set; } = string.Empty;
    [Id(1)] public string ApiKey { get; set; } = string.Empty;
    [Id(2)] public string ApiSecret { get; set; } = string.Empty;
    [Id(3)] public string MonitorHandle { get; set; } = "@GodGPT_";
    [Id(4)] public string ShareLinkDomain { get; set; } = "https://app.godgpt.fun";
    [Id(5)] public string SelfAccountId { get; set; } = string.Empty;
    
    // 定时任务配置
    [Id(6)] public int PullIntervalMinutes { get; set; } = 30;
    [Id(7)] public int PullBatchSize { get; set; } = 100;
    [Id(8)] public string PullSchedule { get; set; } = "*/30 * * * *";
    [Id(9)] public string RewardSchedule { get; set; } = "0 0 * * *";
    
    // 时间区间配置
    [Id(10)] public int TimeRangeStartOffsetMinutes { get; set; } = 2880; // 48小时
    [Id(11)] public int TimeRangeEndOffsetMinutes { get; set; } = 1440;   // 24小时
    [Id(12)] public int TimeOffsetMinutes { get; set; } = 2880;  // 48小时 (兼容性保留)
    [Id(13)] public int TimeWindowMinutes { get; set; } = 1440;  // 24小时 (兼容性保留)
    [Id(14)] public int TestTimeOffset { get; set; } = 0;
    
    // 数据管理配置
    [Id(15)] public int DataRetentionDays { get; set; } = 5;
    [Id(16)] public int DailyRewardLimit { get; set; } = 500;
    [Id(17)] public int MaxRetryAttempts { get; set; } = 3;
    [Id(18)] public int RetryDelayMinutes { get; set; } = 5;
    
    // 系统管理配置
    [Id(19)] public int ReminderTargetIdVersion { get; set; } = 1;
    [Id(20)] public string PullTaskTargetId { get; set; } = "12345678-1234-1234-1234-a00000000001";
    [Id(21)] public string RewardTaskTargetId { get; set; } = "12345678-1234-1234-1234-a00000000002";
    
    // 奖励规则配置
    [Id(22)] public int OriginalTweetReward { get; set; } = 2;
    [Id(23)] public int MaxTweetsPerUser { get; set; } = 10;
    [Id(24)] public int MaxUserReward { get; set; } = 20;
    [Id(25)] public double ShareLinkMultiplier { get; set; } = 1.1;
    
    // 奖励等级配置
    [Id(26)] public List<TwitterRewardTierOptions> RewardTiers { get; set; } = new()
    {
        new() { MinViews = 20, MinFollowers = 10, RewardCredits = 5 },
        new() { MinViews = 50, MinFollowers = 25, RewardCredits = 10 },
        new() { MinViews = 100, MinFollowers = 50, RewardCredits = 15 },
        new() { MinViews = 200, MinFollowers = 100, RewardCredits = 25 },
        new() { MinViews = 500, MinFollowers = 200, RewardCredits = 35 },
        new() { MinViews = 1000, MinFollowers = 500, RewardCredits = 50 },
        new() { MinViews = 5000, MinFollowers = 1000, RewardCredits = 80 },
        new() { MinViews = 10000, MinFollowers = 1000, RewardCredits = 120 }
    };
}

[GenerateSerializer]
public class TwitterRewardTierOptions
{
    [Id(0)] public int MinViews { get; set; }
    [Id(1)] public int MinFollowers { get; set; }
    [Id(2)] public int RewardCredits { get; set; }
} 