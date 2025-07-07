using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace Aevatar.Application.Grains.Tests;

[DependsOn(typeof(AevatarOrleansTestBaseModule))]
public class AevatarGodGPTTestsMoudle : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        base.ConfigureServices(context);
        var configuration = context.Services.GetConfiguration();
        
        // 配置Stripe选项
        Configure<StripeOptions>(configuration.GetSection("Stripe"));
        
        // 配置TwitterRewardOptions用于测试
        Configure<TwitterRewardOptions>(options =>
        {
            options.BearerToken = "test-bearer-token";
            options.ApiKey = "test-api-key";
            options.ApiSecret = "test-api-secret";
            options.MonitorHandle = "@GodGPT_";
            options.ShareLinkDomain = "https://app.godgpt.fun";
            options.SelfAccountId = "test-self-account";
            options.PullIntervalMinutes = 30;
            options.PullBatchSize = 100;
            options.TimeRangeStartOffsetMinutes = 2880;
            options.TimeRangeEndOffsetMinutes = 1440;
            options.DataRetentionDays = 5;
            options.DailyRewardLimit = 500;
            options.OriginalTweetReward = 2;
            options.MaxTweetsPerUser = 10;
            options.MaxUserReward = 20;
            options.ShareLinkMultiplier = 1.1;
        });
    }
}