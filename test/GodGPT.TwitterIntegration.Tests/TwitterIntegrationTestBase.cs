using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.TestingHost;
using Orleans.Hosting;
using GodGPT.TwitterIntegration.Tests.Helpers;

namespace GodGPT.TwitterIntegration.Tests;

/// <summary>
/// Test silo configurator
/// </summary>
public class TestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.UseInMemoryReminderService();
        
        // Build configuration first
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
            
        // Use MongoDB storage instead of in-memory storage to achieve data persistence
        var connectionString = configuration.GetConnectionString("Default") ?? "mongodb://127.0.0.1:27017/GodGPT_TwitterIntegration_Tests";
        siloBuilder.UseMongoDBClient(connectionString);
        siloBuilder.AddMongoDBGrainStorage("DefaultGrainStorage", options =>
        {
            options.DatabaseName = "GodGPT_TwitterIntegration_Tests";
        })
        // ✅ Add EventSourcing services - CRITICAL for GAgentBase classes
        .AddLogStorageBasedLogConsistencyProvider("LogStorage")
        .AddMongoDBGrainStorage("PubSubStore", options =>
        {
            options.DatabaseName = "GodGPT_TwitterIntegration_Tests";
        });
        
        siloBuilder.ConfigureServices(services =>
        {
            // Add HttpClient
            services.AddHttpClient();
                
            // Add test configuration
            services.AddSingleton<IConfiguration>(configuration);
            
            // Configure TwitterRewardOptions from configuration
            services.Configure<Aevatar.Application.Grains.Common.Options.TwitterRewardOptions>(
                configuration.GetSection("TwitterReward"));
        });
    }
}

/// <summary>
/// Test client configurator
/// </summary>
public class TestClientConfigurator : IClientBuilderConfigurator
{
    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
    {
        // Client configuration if needed
    }
}

/// <summary>
/// Base class for Twitter integration tests
/// </summary>
public abstract class TwitterIntegrationTestBase : IDisposable
{
    protected TestCluster Cluster { get; private set; } = null!;
    protected IClusterClient ClusterClient => Cluster.Client;
    protected IServiceProvider ServiceProvider { get; private set; } = null!;
    protected ILogger Logger { get; private set; } = null!;

    protected TwitterIntegrationTestBase()
    {
        InitializeTestEnvironment();
    }

    private void InitializeTestEnvironment()
    {
        // Create test cluster
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<TestClientConfigurator>();
        
        Cluster = builder.Build();
        Cluster.Deploy();

        // Create service provider for test dependencies
        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();

        Logger = ServiceProvider.GetRequiredService<ILogger<TwitterIntegrationTestBase>>();
        Logger.LogInformation("Twitter integration test environment initialized");
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Add configuration
        services.AddSingleton<IConfiguration>(provider =>
        {
            return new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables()
                .Build();
        });

        // Add test helpers
        services.AddSingleton<TwitterIntegrationTestHelper>();
    }



    public virtual void Dispose()
    {
        try
        {
            Logger?.LogInformation("Disposing Twitter integration test environment");
            
            if (ServiceProvider is IDisposable disposableServiceProvider)
            {
                disposableServiceProvider.Dispose();
            }
            Cluster?.StopAllSilos();
            Cluster?.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during test cleanup: {ex.Message}");
        }
    }
} 