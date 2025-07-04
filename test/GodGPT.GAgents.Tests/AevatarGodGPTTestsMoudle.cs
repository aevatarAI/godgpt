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
        
        //TwitterRewardOptions
        Configure<TwitterRewardOptions>(configuration.GetSection("TwitterReward"));
    }
}