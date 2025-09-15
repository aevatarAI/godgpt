using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aevatar;
using Aevatar.Application.Grains;
using Aevatar.Application.Grains.Agents.ChatManager.Options;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Common.Service;
using Aevatar.Application.Grains.PaymentAnalytics.Dtos;
using Aevatar.Extensions;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.SemanticKernel.Extensions;
using Aevatar.Mock;
using Aevatar.PermissionManagement.Extensions;
using AutoMapper;
using GodGPT.GAgents.Awakening.Options;
using GodGPT.GAgents.SpeechChat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver.Core.Configuration;
using Moq;
using Orleans.Hosting;
using Orleans.TestingHost;
using Volo.Abp.AutoMapper;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Local;
using Volo.Abp.ObjectMapping;
using Volo.Abp.Reflection;

public class ClusterFixture : IDisposable, ISingletonDependency
{
    public static MockLoggerProvider LoggerProvider { get; set; } = new MockLoggerProvider("TestCluster");

    public ClusterFixture()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurations>();
        builder.AddClientBuilderConfigurator<TestClientBuilderConfigurator>();
        Cluster = builder.Build();
        Cluster.Deploy();
    }

    public void Dispose()
    {
        Cluster.StopAllSilos();
    }

    public TestCluster Cluster { get; private set; }

    private static void ConfigureGooglePayMock(Mock<Aevatar.Application.Grains.Common.Service.IGooglePayService> mock)
    {
        // Setup for successful subscription verification
        mock.Setup(x => x.VerifyGooglePlayPurchaseAsync(It.Is<
        GooglePlayVerificationDto>(dto => dto.PurchaseToken.Contains("valid_subscription_token"))))
            .ReturnsAsync(new PaymentVerificationResultDto { IsValid = true, Message = "Subscription verified successfully" });

        // Setup for successful product verification
        mock.Setup(x => x.VerifyGooglePlayPurchaseAsync(It.Is<GooglePlayVerificationDto>(dto => dto.PurchaseToken.Contains("valid_product_token"))))
            .ReturnsAsync(new PaymentVerificationResultDto { IsValid = true, Message = "Product purchase verified successfully" });
        
        // Setup for not found error
        mock.Setup(x => x.VerifyGooglePlayPurchaseAsync(It.Is<GooglePlayVerificationDto>(dto => dto.PurchaseToken.Contains("not_found_token"))))
            .ReturnsAsync(new PaymentVerificationResultDto { IsValid = false, ErrorCode = "INVALID_PURCHASE_TOKEN", Message = "Purchase token not found" });

        // Setup for API error
        mock.Setup(x => x.VerifyGooglePlayPurchaseAsync(It.Is<GooglePlayVerificationDto>(dto => dto.PurchaseToken.Contains("api_error_token"))))
            .ReturnsAsync(new PaymentVerificationResultDto { IsValid = false, ErrorCode = "API_ERROR", Message = "API error occurred" });

        // Default setup for any other request
        mock.Setup(x => x.VerifyGooglePlayPurchaseAsync(It.IsAny<GooglePlayVerificationDto>()))
            .ReturnsAsync(new PaymentVerificationResultDto { IsValid = false, ErrorCode = "INVALID_TOKEN", Message = "Invalid token" });

        // Setup for web payment verification - removed to allow individual tests to set their own behavior
        // mock.Setup(x => x.VerifyGooglePayPaymentAsync(It.IsAny<GooglePayVerificationDto>()))
        //     .ReturnsAsync(new PaymentVerificationResultDto { IsValid = false, ErrorCode = "NOT_IMPLEMENTED", Message = "Google Pay web payment verification requires separate implementation" });
    }

    private class TestSiloConfigurations : ISiloConfigurator
    {
        public void Configure(ISiloBuilder hostBuilder)
        {
            var configuration = new ConfigurationBuilder()
                //.AddJsonFile("appsettings.json")
                .AddJsonFile("/opt/evn/godgpt.appsettings.json")
                // .AddJsonFile("appsettings.secrets.json")
                .Build();

            hostBuilder.ConfigureServices(services =>
                {
                    services.AddAutoMapper(typeof(GodGPTGAgentModule).Assembly);
                    var mock = new Mock<ILocalEventBus>();
                    services.AddSingleton(typeof(ILocalEventBus), mock.Object);
                    
                    // Mock IBlobContainer for testing
                    var mockBlobContainer = new Mock<Volo.Abp.BlobStoring.IBlobContainer>();
                    services.AddSingleton(mockBlobContainer.Object);

                    services.AddMemoryCache();
                    // Configure logging
                    var loggerProvider = new MockLoggerProvider("Aevatar");
                    services.AddSingleton<ILoggerProvider>(loggerProvider);
                    LoggerProvider = loggerProvider;
                    services.AddLogging(logging =>
                    {
                        //logging.AddProvider(loggerProvider);
                        logging.AddConsole(); // Adds console logger
                    });
                    services.OnExposing(onServiceExposingContext =>
                    {
                        var implementedTypes = ReflectionHelper.GetImplementedGenericTypes(
                            onServiceExposingContext.ImplementationType,
                            typeof(IObjectMapper<,>)
                        );
                    });

                    services.AddTransient(typeof(IObjectMapper<>), typeof(DefaultObjectMapper<>));
                    services.AddTransient(typeof(IObjectMapper), typeof(DefaultObjectMapper));
                    services.AddTransient(typeof(IAutoObjectMappingProvider),
                        typeof(AutoMapperAutoObjectMappingProvider));
                    services.AddTransient(sp => new MapperAccessor()
                    {
                        Mapper = sp.GetRequiredService<IMapper>()
                    });

                    //services.AddMediatR(typeof(TestSiloConfigurations).Assembly);

                    services.AddTransient<IMapperAccessor>(provider => provider.GetRequiredService<MapperAccessor>());
                    // services.AddSingleton<IIndexingService, MockElasticIndexingService>();
                    //
                    // services.AddSingleton<ElasticsearchClient>(sp =>
                    // {
                    //     var response =
                    //         TestableResponseFactory.CreateSuccessfulResponse<SearchResponse<Document>>(new(), 200);
                    //     var mock = new Mock<ElasticsearchClient>();
                    //     mock
                    //         .Setup(m => m.SearchAsync<Document>(It.IsAny<SearchRequest>(),
                    //             It.IsAny<CancellationToken>()))
                    //         .ReturnsAsync(response);
                    //     return mock.Object;
                    // });
                    // services.AddMediatR(cfg =>
                    //     cfg.RegisterServicesFromAssembly(typeof(SaveStateBatchCommandHandler).Assembly)
                    // );
                    // services.AddSingleton(typeof(ICQRSProvider), typeof(CQRSProvider));
                    // services.AddSingleton(typeof(ICqrsService), typeof(CqrsService));
                    //
                    services.Configure<QdrantConfig>(configuration.GetSection("VectorStores:Qdrant"));
                    services.Configure<AzureOpenAIEmbeddingsConfig>(
                        configuration.GetSection("AIServices:AzureOpenAIEmbeddings"));
                    services.Configure<RagConfig>(configuration.GetSection("Rag"));
                    services.Configure<SystemLLMConfigOptions>(configuration);
                    services.Configure<SpeechOptions>(configuration.GetSection("Speech"));
                    services.AddSingleton<ISpeechService, SpeechService>();
                    services.AddTransient<ILocalizationService, LocalizationService>();
                    services.AddSemanticKernel()
                        .AddQdrantVectorStore()
                        .AddAzureOpenAITextEmbedding()
                        .AddHttpClient();
                    // Register Google Pay mock service for testing
                    var googlePayServiceMock = new Mock<Aevatar.Application.Grains.Common.Service.IGooglePayService>();
                    ConfigureGooglePayMock(googlePayServiceMock);
                    services.AddSingleton(googlePayServiceMock.Object);
                    services.AddSingleton(googlePayServiceMock); // Register the mock itself for tests
                })
                .AddMemoryStreams("Aevatar")
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("DefaultGrainStorage")
                .UseAevatar()
                .AddLogStorageBasedLogConsistencyProvider("LogStorage")
                .Configure<StripeOptions>(configuration.GetSection("Stripe"))
                .Configure<RateLimitOptions>(configuration.GetSection("RateLimit"))
                .Configure<ApplePayOptions>(configuration.GetSection("ApplePay"))
                .Configure<RolePromptOptions>(configuration.GetSection("RolePrompts"))
                .Configure<GoogleAnalyticsOptions>(configuration.GetSection("GoogleAnalytics"))
                .Configure<TwitterAuthOptions>(configuration.GetSection("TwitterAuth"))
                .Configure<TwitterRewardOptions>(configuration.GetSection("TwitterReward"))
                .Configure<AwakeningOptions>(configuration.GetSection("Awakening"))
                .Configure<LLMRegionOptions>(configuration.GetSection("LLMRegion"))
                .Configure<Aevatar.Application.Grains.Common.Options.GooglePayOptions>(configuration.GetSection("GooglePay"))
                .Configure<UserStatisticsOptions>(configuration.GetSection("UserStatistics"));
                    
                    
        }
    }

    public class MapperAccessor : IMapperAccessor
    {
        public IMapper Mapper { get; set; } = null!; // Will be set during DI container setup
    }

    private class TestClientBuilderConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) => clientBuilder
            .AddMemoryStreams("Aevatar");
    }

    public static async Task WaitLogAsync(string log)
    {
        var timeout = TimeSpan.FromSeconds(15);
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (LoggerProvider.Logs.Any(l => l.Contains(log)))
            {
                break;
            }

            await Task.Delay(1000);
        }
    }
}