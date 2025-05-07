using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Orleans.TestingHost;
using Xunit;
using Aevatar.GAgents.AI.BrainFactory;
using System.IO;

namespace Aevatar.Application.Grains.Tests.Core
{
    // public abstract class GodGPTTestBase : IAsyncLifetime
    // {
    //     protected TestCluster Cluster { get; private set; }
    //     protected IGrainFactory GrainFactory => Cluster.GrainFactory;
    //     protected IClusterClient ClusterClient => Cluster.Client;
    //
    //     protected GodGPTTestBase()
    //     {
    //         var builder = new TestClusterBuilder();
    //         builder.AddSiloBuilderConfigurator<TestSiloConfigurations>();
    //         Cluster = builder.Build();
    //     }
    //
    //     public virtual async Task InitializeAsync()
    //     {
    //         await Cluster.DeployAsync();
    //     }
    //
    //     public virtual async Task DisposeAsync()
    //     {
    //         await Cluster.DisposeAsync();
    //     }
    // }

    // 简单的IBrainFactory实现，用于测试
    // public class MockBrainFactory : IBrainFactory
    // {
    //     public Task<string> GenerateResponseAsync(string prompt, string? llm = null, Dictionary<string, object>? options = null)
    //     {
    //         return Task.FromResult("Mock response for testing");
    //     }
    //
    //     // 必须实现这个方法，但我们返回null因为测试不会实际使用它
    //     public object GetBrain(object config)
    //     {
    //         return new object();
    //     }
    // }

    // public class TestSiloConfigurations : ISiloConfigurator
    // {
    //     public void Configure(ISiloBuilder siloBuilder)
    //     {
    //         // 加载配置文件
    //         IConfiguration configuration = new ConfigurationBuilder()
    //             .SetBasePath(Directory.GetCurrentDirectory())
    //             .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    //             .Build();
    //
    //         // 配置测试环境
    //         siloBuilder
    //             .AddMemoryGrainStorage("PubSubStore")
    //             .AddMemoryGrainStorage("LogStorage")
    //             .ConfigureServices(services => 
    //             {
    //                 // 注册配置
    //                 services.AddSingleton(configuration);
    //                 
    //                 // 注册IBrainFactory的简单实现
    //                 services.AddSingleton<IBrainFactory, MockBrainFactory>();
    //                 
    //                 // 添加内存缓存
    //                 services.AddMemoryCache();
    //                 
    //                 // 添加日志服务
    //                 services.AddLogging(logging => 
    //                 {
    //                     logging.AddConsole();
    //                 });
    //             });
    //     }
    // }
} 