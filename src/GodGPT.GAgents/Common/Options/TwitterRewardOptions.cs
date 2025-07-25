namespace Aevatar.Application.Grains.Common.Options;

[GenerateSerializer]
public class TwitterRewardOptions
{
    public const string SectionName = "TwitterReward";
    
    // Twitter API Configuration
    [Id(0)] public string BearerToken { get; set; } = string.Empty;
    [Id(1)] public string ApiKey { get; set; } = string.Empty;
    [Id(2)] public string ApiSecret { get; set; } = string.Empty;
    [Id(3)] public string MonitorHandle { get; set; } = "@godgpt_";
    [Id(4)] public string ShareLinkDomain { get; set; } = "https://app.godgpt.fun";
    [Id(5)] public List<string> ExcludedAccountIds { get; set; } = new();

    // Scheduled task configuration
    [Id(6)] public int PullIntervalMinutes { get; set; } = 20; // Reduced from 30 to 20 minutes for more frequent pulls
    [Id(7)] public int PullBatchSize { get; set; } = 100;
    [Id(8)] public string PullSchedule { get; set; } = "*/20 * * * *"; // Updated to match PullIntervalMinutes
    [Id(9)] public string RewardSchedule { get; set; } = "0 0 * * *";
    
    // Time range configuration
    [Id(10)] public int TimeOffsetMinutes { get; set; } = 2880; // 48 hours
    [Id(11)] public int TimeWindowMinutes { get; set; } = 1440;   // 24 hours
    // Compatibility preservation - deprecated field, but maintains serialization compatibility
    [Id(12)] public string SelfAccountId { get; set; } = string.Empty;
    // Reward tier configuration
    [Id(13)] public List<TwitterRewardTierOptions> RewardTiers { get; set; } = new()
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
    [Id(14)] public int TestTimeOffset { get; set; } = 0;
    
    // Data management configuration
    [Id(15)] public int DataRetentionDays { get; set; } = 5;
    [Id(16)] public int DailyRewardLimit { get; set; } = 500;
    [Id(17)] public int MaxRetryAttempts { get; set; } = 3;
    [Id(18)] public int RetryDelayMinutes { get; set; } = 5;
    
    // System management configuration
    [Id(19)] public int ReminderTargetIdVersion { get; set; } = 1;
    [Id(20)] public string PullTaskTargetId { get; set; } = string.Empty;
    [Id(21)] public string RewardTaskTargetId { get; set; } = string.Empty;
    
    // Reward rules configuration
    [Id(22)] public int OriginalTweetReward { get; set; } = 2;
    [Id(23)] public int MaxTweetsPerUser { get; set; } = 10;
    [Id(24)] public int MaxUserReward { get; set; } = 20;
    [Id(25)] public double ShareLinkMultiplier { get; set; } = 1.1;
    [Id(28)] public int MinViewsForReward { get; set; } = 20; // Minimum views required for reward eligibility

    /// <summary>
    /// Tweet monitoring related configuration
    /// </summary>
    [Id(26)]
    public int MonitoringIntervalMinutes { get; set; } = 30;
    [Id(27)]
    public int BatchFetchSize { get; set; } = 15; // Further reduced from 25 to 15 to minimize API calls per batch
    
    // Twitter API Rate Limiting Configuration
    /// <summary>
    /// Delay in milliseconds between processing each tweet to avoid API rate limiting
    /// Default: 6000ms (6 seconds) - calculated for Twitter API safety: 300 requests/15min ÷ 2 requests/user = 10 users/min = 6s/user
    /// </summary>
    [Id(29)] public int TweetProcessingDelayMs { get; set; } = 6000;
    
    /// <summary>
    /// Maximum number of users to process in a single batch for reward calculation
    /// Default: 50 users - ensures API safety: 50 users × 2 requests = 100 requests in ~5 minutes, well within 300/15min limit
    /// </summary>
    [Id(30)] public int RewardCalculationBatchSize { get; set; } = 50;
    
    /// <summary>
    /// Delay in milliseconds between individual API calls within the same tweet analysis
    /// Default: 1000ms (1 second) - adds small delay between GetTweetDetails and GetUserInfo calls
    /// </summary>
    [Id(34)] public int ApiCallDelayMs { get; set; } = 1000;
    
    // Time Window Management Configuration
    /// <summary>
    /// Default time window in hours for background tweet fetching
    /// Default: 1 hour - reduces API calls per window to prevent rate limiting
    /// </summary>
    [Id(31)] public int TimeWindowHours { get; set; } = 1;
    
    /// <summary>
    /// Maximum tweets to process per time window
    /// Default: 15 - reduced to match BatchFetchSize and minimize API calls
    /// </summary>
    [Id(32)] public int MaxTweetsPerWindow { get; set; } = 15;
    
    /// <summary>
    /// Minimum time window in minutes for dynamic adjustment
    /// Default: 15 minutes - matches Twitter's 15-minute rolling window for optimal API rate limit recovery
    /// </summary>
    [Id(33)] public int MinTimeWindowMinutes { get; set; } = 15;
    
    /// <summary>
    /// Get all account IDs that need to be excluded (including compatibility handling)
    /// </summary>
    public List<string> GetExcludedAccountIds()
    {
        var excludedIds = new List<string>(ExcludedAccountIds);
        
        // Compatibility handling: if the old SelfAccountId has a value, also add it to the exclusion list
        if (!string.IsNullOrEmpty(SelfAccountId) && !excludedIds.Contains(SelfAccountId))
        {
            excludedIds.Add(SelfAccountId);
        }
        
        return excludedIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
    }
}

[GenerateSerializer]
public class TwitterRewardTierOptions
{
    [Id(0)] public int MinViews { get; set; }
    [Id(1)] public int MinFollowers { get; set; }
    [Id(2)] public int RewardCredits { get; set; }
} 