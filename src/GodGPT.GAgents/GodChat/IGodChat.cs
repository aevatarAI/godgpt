using Aevatar.AI.Exceptions;
using Aevatar.AI.Feature.StreamSyncWoker;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.AIGAgent.Dtos;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Agents.ChatManager.Chat;

public interface IGodChat : IGAgent
{
    [Obsolete]
    Task<string> GodChatAsync(string llm, string message, ExecutionPromptSettings? promptSettings = null);
    [Obsolete]
    Task<string> GodStreamChatAsync(Guid sessionId, string llm, bool streamingModeEnabled, string message,
        string chatId, ExecutionPromptSettings? promptSettings = null, bool isHttpRequest = false, string? region = null, bool addToHistory = true);
    
    Task InitAsync(Guid ChatManagerGuid);
    Task SetUserProfileAsync(UserProfileDto? userProfileDto);
    Task<UserProfileDto?> GetUserProfileAsync();
    [ReadOnly]
    Task<List<ChatMessage>> GetChatMessageAsync();
    Task<List<ChatMessage>> ChatWithSessionAsync(string chatId, string prompt, bool includeHistory = false,
        ExecutionPromptSettings promptSettings = null, string? region = null, bool streamingModeEnabled = true);
    Task StreamChatWithSessionAsync(Guid sessionId, string sysmLLM, string content, string chatId,
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null);
    Task ChatMessageCallbackAsync(AIChatContextDto aiChatContextDto,
        AIExceptionEnum aiExceptionEnum, string? errorMessage,
        AIStreamChatContent? aiStreamChatContent);
}