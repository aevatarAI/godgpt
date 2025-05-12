using System;
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
    [Obsolete("This method is deprecated, please use GodStreamChatAsync or StreamChatWithSessionAsync instead, which provide richer functionality and better performance")]
    Task<string> GodChatAsync(string llm, string message, ExecutionPromptSettings? promptSettings = null);
    /// <summary>
    /// Set associated ChatManager reference
    /// </summary>
    /// <param name="chatManagerGuid">ChatManager Guid</param>
    Task SetChatManagerReferenceAsync(Guid chatManagerGuid);
    Task StreamChatWithSessionAsync(Guid sessionId, string sysmLLM, string content, string chatId,
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null);
    Task<string> GodStreamChatAsync(Guid sessionId, string llm, bool streamingModeEnabled, string message,
        string chatId, ExecutionPromptSettings? promptSettings = null, bool isHttpRequest = false, string? region = null);

    [ReadOnly]
    Task<List<ChatMessage>> GetChatMessageAsync();
    
    /// <summary>
    /// Get message list with metadata
    /// </summary>
    /// <returns>Message list with metadata</returns>
    [ReadOnly]
    Task<List<ChatMessageWithInfo>> GetEnhancedChatMessagesAsync();
    
    /// <summary>
    /// Find message by message ID
    /// </summary>
    /// <param name="messageId">Message unique identifier</param>
    /// <returns>Message with metadata, or null if not found</returns>
    [ReadOnly]
    Task<ChatMessageWithInfo?> FindMessageByMessageIdAsync(long messageId);
    
    Task SetUserProfileAsync(UserProfileDto? userProfileDto);
    Task<UserProfileDto?> GetUserProfileAsync();
    Task ChatMessageCallbackAsync(AIChatContextDto aiChatContextDto,
        AIExceptionEnum aiExceptionEnum, string? errorMessage,
        AIStreamChatContent? aiStreamChatContent);
}