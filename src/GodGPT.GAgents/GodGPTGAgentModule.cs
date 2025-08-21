using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Agents.ChatManager.Options;
using Aevatar.Application.Grains.Agents.Anonymous.Options;
using Aevatar.Application.Grains.PaymentAnalytics.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AutoMapper;
using Volo.Abp.Modularity;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using GodGPT.GAgents.Awakening.Options;
using GodGPT.GAgents.SpeechChat;
using GodGPT.GAgents.DailyPush;
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
        Configure<LLMRegionOptions>(configuration.GetSection("LLMRegion"));
        Configure<GoogleAnalyticsOptions>(configuration.GetSection("GoogleAnalytics"));
        Configure<AwakeningOptions>(configuration.GetSection("Awakening"));

        Configure<SpeechOptions>(configuration.GetSection("Speech"));
        
        // Register speech services
        context.Services.AddSingleton<ISpeechService, SpeechService>();
        
        // Register daily push services
        context.Services.AddSingleton<DailyPushRedisService>();
        context.Services.AddSingleton<FirebaseService>();
        
        context.Services.AddHttpClient();
    }
}