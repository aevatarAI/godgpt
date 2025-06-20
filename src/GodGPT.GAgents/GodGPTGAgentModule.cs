using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Agents.ChatManager.Options;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AutoMapper;
using Volo.Abp.Modularity;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.Common.Services;
using Microsoft.Extensions.Configuration;

namespace Aevatar.Application.Grains;

[DependsOn(
    typeof(AbpAutoMapperModule)
)]
public class GodGPTGAgentModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpAutoMapperOptions>(options => { options.AddMaps<GodGPTGAgentModule>(); });

        context.Services.AddSingleton<ISystemAuthenticationService, SystemAuthenticationService>();
        
        var configuration = context.Services.GetConfiguration();
        Configure<CreditsOptions>(configuration.GetSection("Credits"));
        Configure<RateLimitOptions>(configuration.GetSection("RateLimit"));
        Configure<StripeOptions>(configuration.GetSection("Stripe"));
        Configure<RolePromptOptions>(configuration.GetSection("RolePrompts"));
        Configure<ApplePayOptions>(configuration.GetSection("ApplePay"));
        Configure<SystemAuthenticationOptions>(configuration.GetSection("SystemAuth"));

        context.Services.AddHttpClient();
    }
}