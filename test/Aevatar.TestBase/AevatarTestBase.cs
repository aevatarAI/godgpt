using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.Modularity;
using Volo.Abp.Uow;
using Volo.Abp.Testing;

namespace Aevatar;

/* All test classes are derived from this class, directly or indirectly.
 */
public abstract class AevatarTestBase<TStartupModule> : AbpIntegratedTest<TStartupModule>
    where TStartupModule : IAbpModule
{
    protected override void SetAbpApplicationCreationOptions(AbpApplicationCreationOptions options)
    {
        options.UseAutofac();
    }
    
    protected override void BeforeAddApplication(IServiceCollection services)
    {
        var builder = new ConfigurationBuilder();
        
        // Use in-memory configuration for testing - no external file dependency
        var testConfiguration = new Dictionary<string, string>
        {
            // MongoDB connection
            ["ConnectionStrings:Default"] = "mongodb://127.0.0.1:27017/TestAevatar",
            
            // Chat configuration
            ["Chat:Model"] = "gpt-4o-mini",
            ["Chat:APIKey"] = "test-api-key",
            
            // RAG configuration
            ["Rag:Model"] = "gpt-4o-mini", 
            ["Rag:APIKey"] = "test-api-key",
            
            // Azure AI configuration
            ["AzureAI:Endpoint"] = "https://test-endpoint.cognitiveservices.azure.com/",
            ["AzureAI:ApiKey"] = "test-azure-key",
            
            // OpenAI configuration
            ["OpenAI:MaxTokensPerChunk"] = "512",
            ["OpenAI:Temperature"] = "0.7",
            ["OpenAI:ApiKey"] = "test-openai-key",
            
            // TwitterReward configuration for testing
            ["TwitterReward:BearerToken"] = "test-bearer-token",
            ["TwitterReward:ApiKey"] = "test-api-key",
            ["TwitterReward:ApiSecret"] = "test-api-secret",
            ["TwitterReward:MonitorHandle"] = "@GodGPT_",
            ["TwitterReward:ShareLinkDomain"] = "https://app.godgpt.fun",
            ["TwitterReward:SelfAccountId"] = "1234567890",
            ["TwitterReward:PullIntervalMinutes"] = "30",
            ["TwitterReward:PullBatchSize"] = "100",
            ["TwitterReward:PullSchedule"] = "*/30 * * * *",
            ["TwitterReward:RewardSchedule"] = "0 0 * * *",
            ["TwitterReward:EnablePullTask"] = "true",
            ["TwitterReward:EnableRewardTask"] = "true",
            ["TwitterReward:TimeOffsetMinutes"] = "2880",
            ["TwitterReward:TimeWindowMinutes"] = "1440",
            ["TwitterReward:TestTimeOffset"] = "0",
            ["TwitterReward:DataRetentionDays"] = "5",
            ["TwitterReward:MaxRetryAttempts"] = "3",
            ["TwitterReward:RetryDelayMinutes"] = "5",
            ["TwitterReward:PullTaskTargetId"] = "12345678-1234-1234-1234-a00000000001",
            ["TwitterReward:RewardTaskTargetId"] = "12345678-1234-1234-1234-a00000000002"
        };
        
        builder.AddInMemoryCollection(testConfiguration);
        builder.AddJsonFile("appsettings.json", false);
        builder.AddJsonFile("appsettings.secrets.json", true);
        services.ReplaceConfiguration(builder.Build());
    }

    protected virtual Task WithUnitOfWorkAsync(Func<Task> func)
    {
        return WithUnitOfWorkAsync(new AbpUnitOfWorkOptions(), func);
    }

    protected virtual async Task WithUnitOfWorkAsync(AbpUnitOfWorkOptions options, Func<Task> action)
    {
        using (var scope = ServiceProvider.CreateScope())
        {
            var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

            using (var uow = uowManager.Begin(options))
            {
                await action();

                await uow.CompleteAsync();
            }
        }
    }

    protected virtual Task<TResult> WithUnitOfWorkAsync<TResult>(Func<Task<TResult>> func)
    {
        return WithUnitOfWorkAsync(new AbpUnitOfWorkOptions(), func);
    }

    protected virtual async Task<TResult> WithUnitOfWorkAsync<TResult>(AbpUnitOfWorkOptions options, Func<Task<TResult>> func)
    {
        using (var scope = ServiceProvider.CreateScope())
        {
            var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

            using (var uow = uowManager.Begin(options))
            {
                var result = await func();
                await uow.CompleteAsync();
                return result;
            }
        }
    }
}
