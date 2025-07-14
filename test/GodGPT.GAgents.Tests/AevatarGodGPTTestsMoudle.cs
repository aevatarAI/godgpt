using Aevatar.Application.Grains.Common.Options;
using GodGPT.GAgents.SpeechChat;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace Aevatar.Application.Grains.Tests;

[DependsOn(
    typeof(AevatarOrleansTestBaseModule),
    typeof(GodGPTGAgentModule)
)]
public class AevatarGodGPTTestsMoudle : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        base.ConfigureServices(context);
        var configuration = context.Services.GetConfiguration();
        Configure<StripeOptions>(configuration.GetSection("Stripe"));
        
        //TwitterRewardOptions
        Configure<TwitterRewardOptions>(configuration.GetSection("TwitterReward"));
        Configure<SpeechOptions>(configuration.GetSection("Speech"));
    }
}