using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace Aevatar.Application.Grains.Tests;

[DependsOn(typeof(AevatarOrleansTestBaseModule))
]
public class AevatarGodGPTTestsMoudle : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        base.ConfigureServices(context);
        
        context.Services.AddSingleton<ISystemAuthenticationService, SystemAuthenticationService>();

        var configuration = context.Services.GetConfiguration();
        Configure<StripeOptions>(configuration.GetSection("Stripe"));
        Configure<SystemAuthenticationOptions>(configuration.GetSection("SystemAuth"));
    }
}