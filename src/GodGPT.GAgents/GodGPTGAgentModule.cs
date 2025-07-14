using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Agents.ChatManager.Options;
using Aevatar.Application.Grains.Agents.Anonymous.Options;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AutoMapper;
using Volo.Abp.Modularity;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using GodGPT.GAgents.SpeechChat;
using Microsoft.Extensions.Configuration;

namespace Aevatar.Application.Grains;

[DependsOn(
    typeof(AbpAutoMapperModule))]
public class GodGPTGAgentModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpAutoMapperOptions>(options => { options.AddMaps<GodGPTGAgentModule>(); });
        
        var configuration = context.Services.GetConfiguration();
        Configure<CreditsOptions>(configuration.GetSection("Credits"));
        Configure<RateLimitOptions>(configuration.GetSection("RateLimit"));
        Configure<StripeOptions>(configuration.GetSection("Stripe"));
        Configure<RolePromptOptions>(configuration.GetSection("RolePrompts"));
        Configure<ApplePayOptions>(configuration.GetSection("ApplePay"));
        Configure<AnonymousGodGPTOptions>(configuration.GetSection("AnonymousGodGPT"));
        Configure<TwitterAuthOptions>(configuration.GetSection("TwitterAuth"));
        Configure<TwitterRewardOptions>(configuration.GetSection("TwitterReward"));

        Configure<SpeechOptions>(configuration.GetSection("Speech"));
        // Register speech services
        context.Services.AddSingleton<ISpeechService, SpeechService>();
        context.Services.AddHttpClient();
    }
}