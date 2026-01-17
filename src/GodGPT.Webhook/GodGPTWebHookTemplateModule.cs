using Aevatar.Application.Grains.Http;
using Aevatar.Webhook.SDK.Handler;
using GodGPT.Webhook.Http;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Autofac;
using Volo.Abp.AutoMapper;
using Volo.Abp.Modularity;
using Aevatar.Application.Grains.Common.Security;
using Aevatar.Application.Grains.Common.Options;

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
        
        // Register webhook handlers
        services.AddSingleton<IWebhookHandler, GodGPTWebhookHandler>();
        services.AddSingleton<IWebhookHandler, AppleStoreWebhookHandler>();
        services.AddSingleton<IWebhookHandler, GooglePayWebhookHandler>();
        services.AddSingleton<IWebhookHandler, StripePriceWebhookHandler>();
        
        // Register Google Pay security validator
        services.AddSingleton<GooglePaySecurityValidator>();
        
        // Configure Google Pay options
        services.Configure<GooglePayOptions>(context.Services.GetConfiguration().GetSection("GooglePay"));
    }
}