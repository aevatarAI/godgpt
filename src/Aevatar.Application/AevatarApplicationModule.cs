﻿using System;
using System.Linq;
using Aevatar.Account;
using Aevatar.Application.Grains;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Aevatar.CQRS;
using Aevatar.Notification;
using Aevatar.Options;
using Aevatar.Schema;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Volo.Abp.Account;
using Volo.Abp.AspNetCore.Mvc.Dapr;
using Volo.Abp.AutoMapper;
using Volo.Abp.Dapr;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.EventBus;
using Volo.Abp.VirtualFileSystem;

namespace Aevatar;

[DependsOn(
    typeof(AevatarDomainModule),
    typeof(AbpAccountApplicationModule),
    typeof(AevatarApplicationContractsModule),
    typeof(AbpIdentityApplicationModule),
    typeof(AbpPermissionManagementApplicationModule),
    typeof(AbpDaprModule),
    typeof(AbpAspNetCoreMvcDaprModule),
    typeof(AIApplicationGrainsModule),
    typeof(AevatarCQRSModule),
    typeof(AbpAutoMapperModule),
    typeof(AbpEventBusModule)
)]
public class AevatarApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpAutoMapperOptions>(options =>
        {
            options.AddMaps<AevatarApplicationModule>();
        });
        
        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<AevatarApplicationModule>();
        });
        
        var configuration = context.Services.GetConfiguration();
        Configure<NameContestOptions>(configuration.GetSection("NameContest"));
        context.Services.AddSingleton<IGAgentFactory>(sp => new GAgentFactory(context.Services.GetRequiredService<IClusterClient>()));
        context.Services.AddSingleton<IGAgentManager>(sp => new GAgentManager(context.Services.GetRequiredService<IClusterClient>()));
        context.Services.AddSingleton<ISchemaProvider, SchemaProvider>();
        Configure<WebhookDeployOptions>(configuration.GetSection("WebhookDeploy"));
        Configure<AgentOptions>(configuration.GetSection("Agent"));
        context.Services.AddSingleton<INotificationHandlerFactory, NotificationProcessorFactory>();
        Configure<HostDeployOptions>(configuration.GetSection("HostDeploy"));
        context.Services.Configure<HostOptions>(configuration.GetSection("Host"));
        context.Services.Configure<AppleAuthOption>(configuration.GetSection("AppleAuth"));
        
        Configure<AccountOptions>(configuration.GetSection("Account"));
    }
}
