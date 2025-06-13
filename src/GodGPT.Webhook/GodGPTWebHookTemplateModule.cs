using Aevatar.Application.Grains.Http;
using Aevatar.Webhook.SDK.Handler;
using GodGPT.Webhook.Http;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Autofac;
using Volo.Abp.AutoMapper;
using Volo.Abp.Modularity;

namespace Aevatar.Application.Grains;

[DependsOn(typeof(AbpAutofacModule),
    typeof(AbpAutoMapperModule),
    typeof(AbpAspNetCoreSerilogModule)
)]
public class GodGPTWebHookTemplateModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpAutoMapperOptions>(options => { options.AddMaps<GodGPTWebHookTemplateModule>(); });
        var services = context.Services;
        services.AddSingleton<IWebhookHandler, GodGPTWebhookHandler>();
        services.AddSingleton<IWebhookHandler, AppleStoreWebhookHandler>();
    }
}