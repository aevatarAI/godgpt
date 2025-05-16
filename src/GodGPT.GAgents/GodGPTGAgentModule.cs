using Aevatar.Application.Grains.Common.Options;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AutoMapper;
using Volo.Abp.Modularity;

namespace Aevatar.Application.Grains;

[DependsOn(
    typeof(AbpAutoMapperModule)
)]
public class GodGPTGAgentModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpAutoMapperOptions>(options => { options.AddMaps<GodGPTGAgentModule>(); });
        
        var configuration = context.Services.GetConfiguration();
        Configure<CreditsOptions>(configuration.GetSection("Credits"));
        Configure<RateLimiterOptions>(configuration.GetSection("RateLimit"));
        Configure<StripeOptions>(configuration.GetSection("Stripe"));
    }
}