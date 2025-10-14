using Aevatar.AI.Exceptions;
using Aevatar.AI.Feature.StreamSyncWoker;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.AIGAgent.Agent;
using Aevatar.GAgents.AIGAgent.Dtos;
using Aevatar.GAgents.AIGAgent.State;
using Microsoft.Extensions.Logging;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent;

/// <summary>
/// GodGPT AI Agent Status Proxy with cache monitoring
/// </summary>
[GAgent]
[Reentrant]
public class GodAIAgentStatusProxy : 
    AIGAgentBase<GodAIAgentStatusProxyState, GodAIAgentStatusProxyLogEvent, EventBase, GodAIAgentStatusProxyConfig>,
    IGodAIAgentStatusProxy
{
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("GodGPT AIGAgent with cache monitoring");
    }

    protected sealed override async Task PerformConfigAsync(GodAIAgentStatusProxyConfig configuration)
    {
        await InitializeAsync(
            new InitializeDto()
            {
                Instructions = configuration.Instructions,
                LLMConfig = configuration.LLMConfig,
                StreamingModeEnabled = configuration.StreamingModeEnabled,
                StreamingConfig = configuration.StreamingConfig
            });
        RaiseEvent(new SetGodStatusProxyConfigLogEvent
        {
            ParentId = configuration.ParentId
        });
        await ConfirmEvents();
    }

    public new async Task HandleStreamAsync(AIStreamChatResponseEvent arg)
    {
        // Log cache monitoring information BEFORE calling base
        if (arg.TokenUsageStatistics != null && arg.TokenUsageStatistics.InputToken > 0)
        {
            double cacheHitRate = arg.TokenUsageStatistics.CachedTokens > 0 
                ? (arg.TokenUsageStatistics.CachedTokens * 100.0 / arg.TokenUsageStatistics.InputToken) 
                : 0;
            
            if (arg.TokenUsageStatistics.CachedTokens > 0)
            {
                Logger.LogInformation($"[GodGPT][CacheMonitor] ✅ Cache Hit! Input: {arg.TokenUsageStatistics.InputToken}, Output: {arg.TokenUsageStatistics.OutputToken}, Cached: {arg.TokenUsageStatistics.CachedTokens}, Hit Rate: {cacheHitRate:F1}%");
            }
            else
            {
                Logger.LogInformation($"[GodGPT][CacheMonitor] ❌ Cache Miss. Input: {arg.TokenUsageStatistics.InputToken}, Output: {arg.TokenUsageStatistics.OutputToken}");
            }
        }
        
        // Call base implementation
        await base.HandleStreamAsync(arg);
    }

    protected override void AIGAgentTransitionState(GodAIAgentStatusProxyState state,
        StateLogEventBase<GodAIAgentStatusProxyLogEvent> @event)
    {
        if (@event is SetGodStatusProxyConfigLogEvent setConfig)
        {
            state.ParentId = setConfig.ParentId;
        }
    }
}

// State
[GenerateSerializer]
public class GodAIAgentStatusProxyState : AIGAgentStateBase
{
    [Id(0)] public Guid ParentId { get; set; }
}

// Events
[GenerateSerializer]
public class GodAIAgentStatusProxyLogEvent : StateLogEventBase<GodAIAgentStatusProxyLogEvent>
{
}

[GenerateSerializer]
public class SetGodStatusProxyConfigLogEvent : StateLogEventBase<GodAIAgentStatusProxyLogEvent>
{
    [Id(0)] public Guid ParentId { get; set; }
}

// Config
[GenerateSerializer]
public class GodAIAgentStatusProxyConfig : ConfigurationBase
{
    [Id(0)] public required string Instructions { get; set; }
    [Id(1)] public required LLMConfigDto LLMConfig { get; set; }
    [Id(2)] public bool StreamingModeEnabled { get; set; }
    [Id(3)] public StreamingConfig? StreamingConfig { get; set; }
    [Id(4)] public Guid ParentId { get; set; }
}

// Interface
public interface IGodAIAgentStatusProxy : IGAgent, IAIGAgent
{
}

