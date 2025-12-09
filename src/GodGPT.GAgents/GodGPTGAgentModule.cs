using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Common.Service;
using Aevatar.Application.Grains.Agents.ChatManager.Options;
using Aevatar.Application.Grains.Agents.Anonymous.Options;
using Aevatar.Application.Grains.Lumen.Helpers;
using Aevatar.Application.Grains.Lumen.Options;
using Aevatar.Application.Grains.PaymentAnalytics.Dtos;
using Aevatar.Application.Grains.UserFeedback.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.AutoMapper;
using Volo.Abp.Modularity;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using GodGPT.GAgents.Awakening.Options;
using GodGPT.GAgents.SpeechChat;
using GodGPT.GAgents.DailyPush;
using GodGPT.GAgents.DailyPush.Options;
using GodGPT.GAgents.DailyPush.Services;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

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
        Configure<GooglePayOptions>(configuration.GetSection("GooglePay"));
        Configure<UserStatisticsOptions>(configuration.GetSection("UserStatistics"));
        Configure<UserFeedbackOptions>(configuration.GetSection("UserFeedback"));
        Configure<GoogleAuthOptions>(configuration.GetSection("GoogleAuth"));
        
        // Register GooglePayOptions post processor for flat configuration support
        context.Services.AddSingleton<IPostConfigureOptions<GooglePayOptions>, GooglePayOptionsPostProcessor>();
        
        Configure<SpeechOptions>(configuration.GetSection("Speech"));
        Configure<DailyPushOptions>(configuration.GetSection("DailyPush"));
        
        // Configure Lumen options (backend)
        Configure<LumenPredictionOptions>(configuration.GetSection("Lumen:Prediction"));
        Configure<LumenUserProfileOptions>(configuration.GetSection("Lumen:UserProfile"));
        Configure<LumenSolarTermOptions>(configuration.GetSection("Lumen:SolarTerm"));
        
        // Configure Lumen feature flags (frontend only)
        Configure<LumenFeatureFlagsOptions>(configuration.GetSection("Lumen:FeatureFlags"));
        
        // Configure SolarTermData with file path from options
        var solarTermOptions = configuration.GetSection("Lumen:SolarTerm").Get<LumenSolarTermOptions>();
        SolarTermData.Configure(solarTermOptions?.DataFilePath);
        
        // Register speech services
        context.Services.AddSingleton<ISpeechService, SpeechService>();
        context.Services.AddSingleton<IGooglePayService, GooglePayService>();
        context.Services.AddSingleton<ILocalizationService, LocalizationService>();
        
        // Register HttpClient factory first
        context.Services.AddHttpClient();
        
        // Register Firebase and Daily Push services
        // Note: ILogger<T>, IConfiguration, IOptionsMonitor<T> are automatically registered by ABP/ASP.NET Core
        context.Services.AddSingleton<FirebaseService>();
        context.Services.AddSingleton<DailyPushContentService>();
        
        // Register Redis connection for push deduplication
        // Compatible with Station's Redis configuration format: "Redis:Configuration"
        // Fallback to "ConnectionStrings:Redis" for standalone deployment
        context.Services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var redisConfig = configuration["Redis:Configuration"] ?? 
                             configuration.GetConnectionString("Redis") ?? 
                             "localhost:6379";
            
            // Ensure port is included (Station config might be just "127.0.0.1")
            var connectionString = redisConfig.Contains(":") ? redisConfig : $"{redisConfig}:6379";
            var options = ConfigurationOptions.Parse(connectionString);
            
            // Configure Redis options for production resilience
            options.AbortOnConnectFail = false;
            options.ConnectRetry = 3;
            options.ConnectTimeout = 5000;
            options.SyncTimeout = 5000;
            
            return ConnectionMultiplexer.Connect(options);
        });
        
        // Register push deduplication service
        context.Services.AddSingleton<IPushDeduplicationService, PushDeduplicationService>();
    }
}