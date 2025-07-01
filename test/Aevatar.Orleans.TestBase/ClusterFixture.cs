using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aevatar;
using Aevatar.Application.Grains;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Extensions;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.SemanticKernel.Extensions;
using Aevatar.Mock;
using Aevatar.PermissionManagement.Extensions;
using AutoMapper;
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
    public static MockLoggerProvider LoggerProvider { get; set; }

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

    private class TestSiloConfigurations : ISiloConfigurator
    {
        public void Configure(ISiloBuilder hostBuilder)
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
               // .AddJsonFile("/opt/evn/godgpt.appsettings.json")
                // .AddJsonFile("appsettings.secrets.json")
                .Build();

            hostBuilder.ConfigureServices(services =>
                {
                    services.AddAutoMapper(typeof(GodGPTGAgentModule).Assembly);
                    var mock = new Mock<ILocalEventBus>();
                    services.AddSingleton(typeof(ILocalEventBus), mock.Object);

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

                    services.AddSemanticKernel()
                        .AddQdrantVectorStore()
                        .AddAzureOpenAITextEmbedding()
                        .AddHttpClient();
                })
                .AddMemoryStreams("Aevatar")
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryGrainStorageAsDefault()
                .UseAevatar()
                .AddLogStorageBasedLogConsistencyProvider("LogStorage")
                .Configure<StripeOptions>(configuration.GetSection("Stripe"))
                .Configure<RateLimitOptions>(configuration.GetSection("RateLimit"))
                .Configure<ApplePayOptions>(configuration.GetSection("ApplePay"))
                .Configure<TwitterRewardOptions>(options =>
                {
                    options.BearerToken = "test-bearer-token";
                    options.ApiKey = "test-api-key";
                    options.ApiSecret = "test-api-secret";
                    options.MonitorHandle = "@GodGPT_";
                    options.ShareLinkDomain = "https://app.godgpt.fun";
                    options.SelfAccountId = "test-self-account";
                    options.PullIntervalMinutes = 30;
                    options.PullBatchSize = 100;
                    options.TimeRangeStartOffsetMinutes = 2880;
                    options.TimeRangeEndOffsetMinutes = 1440;
                    options.DataRetentionDays = 5;
                    options.DailyRewardLimit = 500;
                    options.OriginalTweetReward = 2;
                    options.MaxTweetsPerUser = 10;
                    options.MaxUserReward = 20;
                    options.ShareLinkMultiplier = 1.1;
                });
        }
    }

    public class MapperAccessor : IMapperAccessor
    {
        public IMapper Mapper { get; set; }
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