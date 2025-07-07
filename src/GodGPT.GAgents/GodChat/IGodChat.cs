using Aevatar.AI.Exceptions;
using Aevatar.AI.Feature.StreamSyncWoker;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.AIGAgent.Dtos;
using GodGPT.GAgents.SpeechChat;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Agents.ChatManager.Chat;

public interface IGodChat : IGAgent
{
    Task<string> GodChatAsync(string llm, string message, ExecutionPromptSettings? promptSettings = null);
    Task InitAsync(Guid ChatManagerGuid);

    Task<string> GodStreamChatAsync(Guid sessionId, string llm, bool streamingModeEnabled, string message,
        string chatId, ExecutionPromptSettings? promptSettings = null, bool isHttpRequest = false, string? region = null, bool addToHistory = true);

    [ReadOnly]
    Task<List<ChatMessage>> GetChatMessageAsync();

    Task StreamChatWithSessionAsync(Guid sessionId, string sysmLLM, string content, string chatId,
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null);
    
    Task StreamVoiceChatWithSessionAsync(Guid sessionId, string sysmLLM, string? voiceData, string fileName, string chatId,
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null, VoiceLanguageEnum voiceLanguage = VoiceLanguageEnum.English);
    
    Task SetUserProfileAsync(UserProfileDto? userProfileDto);
    Task<UserProfileDto?> GetUserProfileAsync();

    Task ChatMessageCallbackAsync(AIChatContextDto aiChatContextDto,
        AIExceptionEnum aiExceptionEnum, string? errorMessage,
        AIStreamChatContent? aiStreamChatContent);
    
    Task<List<ChatMessage>?> ChatWithHistory(Guid sessionId, string systemLLM, string content, string chatId, 
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null);

}