using Aevatar.AI.Exceptions;
using Aevatar.AI.Feature.StreamSyncWoker;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.Dtos;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.ProxySEvents;
using Aevatar.Application.Grains.Common;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.AIGAgent.Agent;
using Aevatar.GAgents.AIGAgent.Dtos;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Orleans.Concurrency;
using Aevatar.GAgents.ChatAgent.Dtos;
using System.Diagnostics;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.GEvents;

namespace Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent;

[GAgent]
[Reentrant]
public class AIAgentStatusProxy :
    AIGAgentBase<AIAgentStatusProxyState, AIAgentStatusProxyLogEvent, EventBase, AIAgentStatusProxyConfig>,
    IAIAgentStatusProxy
{
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("AIGAgent supporting state management");
    }

    protected sealed override async Task PerformConfigAsync(AIAgentStatusProxyConfig configuration)
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.LogDebug($"[AIAgentStatusProxy][PerformConfigAsync] Start - SessionId: {this.GetPrimaryKey()}, ParentId: {configuration.ParentId}");
        /*
        var initializeStopwatch = Stopwatch.StartNew();
        Logger.LogDebug($"[AIAgentStatusProxy][PerformConfigAsync] Starting InitializeAsync - SessionId: {this.GetPrimaryKey()}");
       
        await PublishAsync(this.GetGrainId(),new AIAgentStatusProxyInitializeGEvent()
        {
            InitializeDto = new InitializeDto()
            {
                Instructions = configuration.Instructions,
                LLMConfig = configuration.LLMConfig,
                StreamingModeEnabled = configuration.StreamingModeEnabled,
                StreamingConfig = configuration.StreamingConfig
            }
        });
        
        initializeStopwatch.Stop();
        Logger.LogDebug($"[AIAgentStatusProxy][PerformConfigAsync] InitializeAsync completed - Duration: {initializeStopwatch.ElapsedMilliseconds}ms, SessionId: {this.GetPrimaryKey()}");
        */

        var raiseEventStopwatch = Stopwatch.StartNew();
        RaiseEvent(new SetStatusProxyConfigLogEvent
        {
            RecoveryDelay = configuration.RequestRecoveryDelay,
            ParentId = configuration.ParentId
        });
        await ConfirmEvents();
        raiseEventStopwatch.Stop();
        Logger.LogDebug($"[AIAgentStatusProxy][PerformConfigAsync] RaiseEvent and ConfirmEvents - Duration: {raiseEventStopwatch.ElapsedMilliseconds}ms, SessionId: {this.GetPrimaryKey()}");
        
        stopwatch.Stop();
        Logger.LogDebug($"[AIAgentStatusProxy][PerformConfigAsync] End - Total Duration: {stopwatch.ElapsedMilliseconds}ms, SessionId: {this.GetPrimaryKey()}, ParentId: {configuration.ParentId}");
       
    }
    /*private async Task PublishAsync<T>(GrainId grainId,T @event) where T : EventBase{
        var grainIdString = grainId.ToString();
        var streamId = StreamId.Create(AevatarOptions!.StreamNamespace, grainIdString);
        var stream = StreamProvider.GetStream<EventWrapperBase>(streamId);
        var eventWrapper = new EventWrapper<T>(@event, Guid.NewGuid(), this.GetGrainId());
        await stream.OnNextAsync(eventWrapper);
    }*/
    [EventHandler]
    private async Task HandlerEventAsync(AIAgentStatusProxyInitializeGEvent @event)
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.LogDebug($"[HandlerEventAsync][AIAgentStatusProxyInitializeGEvent] Start- SessionId:{this.GetPrimaryKey()}, event:{JsonConvert.SerializeObject(@event)}");
        await InitializeAsync(@event.InitializeDto);
        stopwatch.Stop();

        Logger.LogDebug($"[HandlerEventAsync][AIAgentStatusProxyInitializeGEvent] End - SessionId: {this.GetPrimaryKey()} ,Duration: {stopwatch.ElapsedMilliseconds}ms");

    }
    public async Task<List<ChatMessage>?> ChatWithHistory(string prompt, List<ChatMessage>? history = null,
        ExecutionPromptSettings? promptSettings = null, AIChatContextDto? context = null)
    {
        var systemPrompt = State.PromptTemplate;
        var selectedHistory = TokenHelper.SelectHistoryMessages(history, prompt, systemPrompt);
        Logger.LogDebug($"[AIAgentStatusProxy][ChatWithHistory] Original history count: {history?.Count ?? 0}, Selected history count: {selectedHistory.Count}");
        
        return await base.ChatWithHistory(prompt, selectedHistory, promptSettings, context: context);
    }

    public async Task<bool> PromptWithStreamAsync(string prompt, List<ChatMessage>? history = null,
        ExecutionPromptSettings? promptSettings = null, AIChatContextDto? context = null, List<string>? imageKeys = null)
    {
        // Get system prompt
        var systemPrompt = State.PromptTemplate;
        
        // Intelligently select historical messages
        var selectedHistory = TokenHelper.SelectHistoryMessages(history, prompt, systemPrompt);
        
        Logger.LogDebug($"[AIAgentStatusProxy][PromptWithStreamAsync] Original history count: {history?.Count ?? 0}, Selected history count: {selectedHistory.Count}");
        
        // Call base method with filtered history messages
        return await base.PromptWithStreamAsync(prompt, selectedHistory, promptSettings, context, imageKeys: imageKeys);
    }

    protected override async Task AIChatHandleStreamAsync(AIChatContextDto context, AIExceptionEnum errorEnum,
        string? errorMessage,
        AIStreamChatContent? content)
    {
        Logger.LogDebug(
            $"[AIAgentStatusProxy][AIChatHandleStreamAsync] sessionId {context?.RequestId.ToString()}, chatId {context?.ChatId}, errorEnum {errorEnum}, errorMessage {errorMessage}: {JsonConvert.SerializeObject(content)}");
        if (errorEnum == AIExceptionEnum.RequestLimitError)
        {
            RaiseEvent(new SetAvailableLogEvent
            {
                IsAvailable = false,
                ExceptionCount = 1
            });
            await ConfirmEvents();
        }
        
        var godChat = GrainFactory.GetGrain<IGodChat>(State.ParentId);
        await godChat.ChatMessageCallbackAsync(context, errorEnum, errorMessage, content);
    }

    public async Task<bool> IsAvailableAsync()
    {
        if (State.IsAvailable)
        {
            return true;
        }

        if (State.UnavailableSince == null)
        {
            Logger.LogDebug($"[AIAgentStatusProxy][IsAvailableAsync] State.UnavailableSince is null");
            return true;
        }

        var now = DateTime.UtcNow;
        var unavailableSince = State.UnavailableSince;
        var timeElapsed = now - unavailableSince;
        if (timeElapsed > State.RecoveryDelay)
        {
            RaiseEvent(new SetAvailableLogEvent
            {
                IsAvailable = true
            });
            await ConfirmEvents();
            return true;
        }

        return false;
    }

    protected override void AIGAgentTransitionState(AIAgentStatusProxyState state,
        StateLogEventBase<AIAgentStatusProxyLogEvent> @event)
    {
        switch (@event)
        {
            case SetStatusProxyConfigLogEvent setStatusProxyConfigLogEvent:
                if (setStatusProxyConfigLogEvent.RecoveryDelay != null)
                {
                    state.RecoveryDelay = (TimeSpan)setStatusProxyConfigLogEvent.RecoveryDelay;
                }

                state.ParentId = setStatusProxyConfigLogEvent.ParentId;
                break;
            case SetAvailableLogEvent setAvailableLogEvent:
                state.IsAvailable = setAvailableLogEvent.IsAvailable;
                if (state.IsAvailable)
                {
                    state.UnavailableSince = null;
                }
                else
                {
                    state.UnavailableSince = DateTime.UtcNow;
                    state.UnavailableCount += 1;
                    state.ExceptionCount += setAvailableLogEvent.ExceptionCount;
                }
                break;
        }
    }
}

public interface IAIAgentStatusProxy : IGAgent, IAIGAgent
{
    Task<bool> IsAvailableAsync();

    Task<List<ChatMessage>?> ChatWithHistory(string prompt, List<ChatMessage>? history = null,
        ExecutionPromptSettings? promptSettings = null, AIChatContextDto? context = null);

    Task<bool> PromptWithStreamAsync(string prompt, List<ChatMessage>? history = null,
        ExecutionPromptSettings? promptSettings = null, AIChatContextDto? context = null, List<string>? imageKeys = null);
}