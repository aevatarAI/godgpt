using Aevatar.Application.Grains.Common.Options;
using Aevatar.GAgents.Speech;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace Aevatar.Application.Grains.Tests;

[DependsOn(
    typeof(AevatarOrleansTestBaseModule),
    typeof(AevatarGAgentsSpeechModule)
)]
public class AevatarGodGPTTestsMoudle : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        base.ConfigureServices(context);
        var configuration = context.Services.GetConfiguration();
        Configure<StripeOptions>(configuration.GetSection("Stripe"));
        Configure<SpeechOptions>(configuration.GetSection("Speech"));
    }
}