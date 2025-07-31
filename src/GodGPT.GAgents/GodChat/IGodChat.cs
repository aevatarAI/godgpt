using Aevatar.AI.Exceptions;
using Aevatar.AI.Feature.StreamSyncWoker;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
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
        string chatId, ExecutionPromptSettings? promptSettings = null, bool isHttpRequest = false, string? region = null,
        bool addToHistory = true, List<string>? images = null);

    [ReadOnly]
    Task<List<ChatMessage>> GetChatMessageAsync();
    
    [ReadOnly]
    Task<List<ChatMessageWithMetaDto>> GetChatMessageWithMetaAsync();

    Task StreamChatWithSessionAsync(Guid sessionId, string sysmLLM, string content, string chatId,
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null,
        List<string>? images = null);
    
    Task StreamVoiceChatWithSessionAsync(Guid sessionId, string sysmLLM, string? voiceData, string fileName, string chatId,
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null, VoiceLanguageEnum voiceLanguage = VoiceLanguageEnum.English, double voiceDurationSeconds = 0.0);
    
    Task SetUserProfileAsync(UserProfileDto? userProfileDto);
    Task<UserProfileDto?> GetUserProfileAsync();

    Task ChatMessageCallbackAsync(AIChatContextDto aiChatContextDto,
        AIExceptionEnum aiExceptionEnum, string? errorMessage,
        AIStreamChatContent? aiStreamChatContent);
    
    Task<List<ChatMessage>?> ChatWithHistory(Guid sessionId, string systemLLM, string content, string chatId, 
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null);


    [ReadOnly]
    Task<DateTime?> GetFirstChatTimeAsync();

    [ReadOnly]
    Task<DateTime?> GetLastChatTimeAsync();
    
    Task<GodChatState?> GetStateAsync();

}