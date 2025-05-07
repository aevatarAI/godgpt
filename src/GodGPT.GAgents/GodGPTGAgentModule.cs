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
    }
}