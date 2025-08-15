using System.Diagnostics;
using System.Text;
using Aevatar.AI.Exceptions;
using Aevatar.AI.Feature.StreamSyncWoker;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.Dtos;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.GEvents;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Common.Service;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.AIGAgent.Dtos;
using Aevatar.GAgents.ChatAgent.Dtos;
using GodGPT.GAgents.Common.Constants;
using GodGPT.GAgents.SpeechChat;
using Json.Schema.Generation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Orleans.Concurrency;
using Aevatar.Core;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.BlobStoring;
using Volo.Abp.Threading;

namespace Aevatar.Application.Grains.Agents.ChatManager.Chat;

[Description("god chat agent")]
[GAgent]
[Reentrant]
public class GodChatGAgent : GAgentBase<GodChatState, GodChatEventLog, EventBase, ChatConfigDto>, IGodChat
{
    private static readonly TimeSpan RequestRecoveryDelay = TimeSpan.FromSeconds(600);
    private const string DefaultRegion = "DEFAULT";
    private const string ProxyGPTModelName = "HyperEcho";
    private const string ChatModelName = "GodGPT";
    private const string ConsoleModelName = "GodGPTConsole";
    private readonly ISpeechService _speechService;
    private readonly IOptionsMonitor<LLMRegionOptions> _llmRegionOptions;
    private readonly ILocalizationService _localizationService;

    // Dictionary to maintain text accumulator for voice chat sessions
    // Key: chatId, Value: accumulated text buffer for sentence detection
    private static readonly Dictionary<string, StringBuilder> VoiceTextAccumulators = new();
    
    // Instance variables for suggestion filtering state management
    // These persist across chunk processing within the same grain instance
    private bool _isAccumulatingForSuggestions = false;
    private string _accumulatedSuggestionContent = "";

    public GodChatGAgent(ISpeechService speechService, IOptionsMonitor<LLMRegionOptions> llmRegionOptions,ILocalizationService localizationService)
    {
        _speechService = speechService;
        _llmRegionOptions = llmRegionOptions;
        _localizationService = localizationService;

    }

    protected override async Task PerformConfigAsync(ChatConfigDto configuration)
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.LogDebug($"[GodChatGAgent][PerformConfigAsync] Start - SessionId: {this.GetPrimaryKey()}");
        
        var regionToLLMsMap = _llmRegionOptions.CurrentValue.RegionToLLMsMap;
        if (regionToLLMsMap.IsNullOrEmpty())
        {
            Logger.LogDebug($"[GodChatGAgent][PerformConfigAsync] LLMConfigs is null or empty.");
            return;
        }

        var proxyIds = await InitializeRegionProxiesAsync(DefaultRegion);
        
        Dictionary<string, List<Guid>> regionProxies = new();
        regionProxies[DefaultRegion] = proxyIds;
        
        // Optimize: Use combined event to reduce RaiseEvent calls from 3 to 1
        var maxHistoryCount = configuration.MaxHistoryCount;
        if (maxHistoryCount > 100)
        {
            maxHistoryCount = 100;
        }

        if (maxHistoryCount == 0)
        {
            maxHistoryCount = 10;
        }
        
        RaiseEvent(new PerformConfigCombinedEventLog
        {
            Region = DefaultRegion,
            ProxyIds = proxyIds,
            PromptTemplate = configuration.Instructions,
            MaxHistoryCount = maxHistoryCount
        });
        stopwatch.Stop();
        Logger.LogDebug($"[GodChatGAgent][PerformConfigAsync] End - Total Duration: {stopwatch.ElapsedMilliseconds}ms, SessionId: {this.GetPrimaryKey()}");
    }

    public override Task<string> GetDescriptionAsync()
    {
        throw new NotImplementedException();
    }

    [EventHandler]
    public async Task HandleEventAsync(RequestStreamChatEvent @event)
    {
        var chatId = Guid.NewGuid().ToString();
        //Decommission the SignalR conversation interface
        var chatMessage = new ResponseStreamGodChat()
        {
            Response =
                "A better experience awaits! Please update to the latest version.",
            ChatId = chatId,
            NewTitle = "A better experience awaits",
            IsLastChunk = true,
            SerialNumber = -2,
            SessionId = @event.SessionId
        };
        Logger.LogDebug(
            $"[GodChatGAgent][RequestStreamChatEvent] decommission :{JsonConvert.SerializeObject(@event)} chatID:{chatId}");
        await PublishAsync(chatMessage);

        // string chatId = Guid.NewGuid().ToString();
        // Logger.LogDebug(
        //     $"[GodChatGAgent][RequestStreamGodChatEvent] start:{JsonConvert.SerializeObject(@event)} chatID:{chatId}");
        // var title = "";
        // var content = "";
        // var isLastChunk = false;
        //
        // try
        // {
        //     if (State.StreamingModeEnabled)
        //     {
        //         Logger.LogDebug("State.StreamingModeEnabled is on");
        //         await StreamChatWithSessionAsync(@event.SessionId, @event.SystemLLM, @event.Content, chatId);
        //     }
        //     else
        //     {
        //         var response = await ChatWithSessionAsync(@event.SessionId, @event.SystemLLM, @event.Content);
        //         content = response.Item1;
        //         title = response.Item2;
        //         isLastChunk = true;
        //     }
        // }
        // catch (Exception e)
        // {
        //     Logger.LogError(e, $"[GodChatGAgent][RequestStreamGodChatEvent] handle error:{e.ToString()}");
        // }
        //
        // await PublishAsync(new ResponseStreamGodChat()
        // {
        //     ChatId = chatId,
        //     Response = content,
        //     NewTitle = title,
        //     IsLastChunk = isLastChunk,
        //     SerialNumber = -1,
        //     SessionId = @event.SessionId
        // });
        //
        // Logger.LogDebug($"[GodChatGAgent][RequestStreamGodChatEvent] end:{JsonConvert.SerializeObject(@event)}");
    }

    [EventHandler]
    public async Task HandleEventAsync(UpdateProxyInitStatusGEvent @event)
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.LogDebug($"[GodChatGAgent][HandleEventAsync][UpdateProxyInitStatusGEvent] Start - SessionId: {this.GetPrimaryKey()}, ProxyId: {@event.ProxyId}, Status: {@event.Status}");
        
        // Update the proxy initialization status
        RaiseEvent(new UpdateProxyInitStatusLogEvent
        {
            ProxyId = @event.ProxyId,
            Status = @event.Status
        });
        await ConfirmEvents();
        
        stopwatch.Stop();
        Logger.LogDebug($"[GodChatGAgent][HandleEventAsync][UpdateProxyInitStatusGEvent] End - Duration: {stopwatch.ElapsedMilliseconds}ms, Status updated to: {@event.Status} for proxy: {@event.ProxyId}");
    }

    public async Task StreamChatWithSessionAsync(Guid sessionId, string sysmLLM, string content, string chatId,
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null, 
        List<string>? images = null)
    {
        var totalStopwatch = Stopwatch.StartNew();
        Logger.LogDebug($"[GodChatGAgent][StreamChatWithSession] {sessionId.ToString()} start.");

        // Get language from RequestContext with error handling
        var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
        Logger.LogDebug($"[GodChatGAgent][StreamChatWithSession] Language from context: {language}");

        var actionType = images == null || images.IsNullOrEmpty()
            ? ActionType.Conversation
            : ActionType.ImageConversation;
        
        var userQuotaGAgent = GrainFactory.GetGrain<IUserQuotaGAgent>(State.ChatManagerGuid);
        var actionResultDto =
            await userQuotaGAgent.ExecuteActionAsync(sessionId.ToString(), State.ChatManagerGuid.ToString(), actionType);
        if (!actionResultDto.Success)
        {
            Logger.LogDebug($"[GodChatGAgent][StreamChatWithSession] {sessionId.ToString()} Access restricted");
            //1、throw Exception
            // var invalidOperationException = new InvalidOperationException(actionResultDto.Message);
            // invalidOperationException.Data["Code"] = actionResultDto.Code.ToString();
            // throw invalidOperationException;

            //save conversation data
            await SetSessionTitleAsync(sessionId, content);
            var chatMessages = new List<ChatMessage>();
            chatMessages.Add(new ChatMessage
            {
                ChatRole = ChatRole.User,
                Content = content,
                ImageKeys = images
            });
            chatMessages.Add(new ChatMessage
            {
                ChatRole = ChatRole.Assistant,
                Content = actionResultDto.Message
            });
            RaiseEvent(new GodAddChatHistoryLogEvent
            {
                ChatList = chatMessages
            });
            
            RaiseEvent(new AddChatMessageMetasLogEvent
            {
                ChatMessageMetas = new List<ChatMessageMeta>()
            });
            
            await ConfirmEvents();

            //2、Directly respond with error information.
            var chatMessage = new ResponseStreamGodChat()
            {
                Response = actionResultDto.Message,
                ChatId = chatId,
                IsLastChunk = true,
                SerialNumber = -99,
                SessionId = sessionId,
                // Note: Default to VoiceResponse in this version as VoiceToText is not implemented yet
                VoiceContentType = VoiceContentType.VoiceResponse
            };

            if (isHttpRequest)
            {
                await PushMessageToClientAsync(chatMessage);
            }
            else
            {
                await PublishAsync(chatMessage);
            }

            return;
        }

        Logger.LogDebug($"[GodChatGAgent][StreamChatWithSession] {sessionId.ToString()} - Validation passed");
        
        await SetSessionTitleAsync(sessionId, content);
        var configuration = GetConfiguration();
        await GodStreamChatAsync(sessionId, await configuration.GetSystemLLM(),
            await configuration.GetStreamingModeEnabled(),
            content, chatId, promptSettings, isHttpRequest, region, images: images);
        
        totalStopwatch.Stop();
        Logger.LogDebug($"[GodChatGAgent][StreamChatWithSession] TOTAL_Time - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {sessionId}");
    }

    public async Task StreamVoiceChatWithSessionAsync(Guid sessionId, string sysmLLM, string? voiceData,
        string fileName, string chatId,
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null,
        VoiceLanguageEnum voiceLanguage = VoiceLanguageEnum.English, double voiceDurationSeconds = 0.0)
    {
        var totalStopwatch = Stopwatch.StartNew();
        Logger.LogInformation($"[PERF][VoiceChat] {sessionId} START - file: {fileName}, size: {voiceData?.Length ?? 0} chars, language: {voiceLanguage}, duration: {voiceDurationSeconds}s");
        var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();

        // Validate voiceData
        if (string.IsNullOrEmpty(voiceData) || voiceLanguage == VoiceLanguageEnum.Unset)
        {
            Logger.LogError($"[GodChatGAgent][StreamVoiceChatWithSession] {sessionId.ToString()} Invalid voice data");
            var errMsg = _localizationService.GetLocalizedException(ExceptionMessageKeys.InvalidVoiceMessage,language);
            if (voiceLanguage == VoiceLanguageEnum.Unset)
            {
                errMsg = _localizationService.GetLocalizedException(ExceptionMessageKeys.UnSetVoiceLanguage,language);
            }

            var errorMessage = new ResponseStreamGodChat()
            {
                Response = errMsg,
                ChatId = chatId,
                IsLastChunk = true,
                SerialNumber = -99,
                SessionId = sessionId,
                ErrorCode = ChatErrorCode.ParamInvalid,
                // Note: Default to VoiceResponse in this version as VoiceToText is not implemented yet
                VoiceContentType = VoiceContentType.VoiceResponse
            };

            if (isHttpRequest)
            {
                await PushMessageToClientAsync(errorMessage);
            }
            else
            {
                await PublishAsync(errorMessage);
            }

            return;
        }

        // Convert MP3 data to byte array - track processing time
        var conversionStopwatch = Stopwatch.StartNew();
        var voiceDataBytes = Convert.FromBase64String(voiceData);
        conversionStopwatch.Stop();
        Logger.LogInformation($"[PERF][VoiceChat] {sessionId} Base64_Conversion: {conversionStopwatch.ElapsedMilliseconds}ms, bytes: {voiceDataBytes.Length}");

        string voiceContent;
        var voiceParseSuccess = true;
        string? voiceParseErrorMessage = null;

        // STT Processing - track time and performance
        var sttStopwatch = Stopwatch.StartNew();
        try
        {
            voiceContent = await _speechService.SpeechToTextAsync(voiceDataBytes, voiceLanguage);
            sttStopwatch.Stop();
            
            if (string.IsNullOrWhiteSpace(voiceContent))
            {
                voiceParseSuccess = false;
                voiceParseErrorMessage = _localizationService.GetLocalizedException(ExceptionMessageKeys.SpeechTimeout,language);
                voiceContent = _localizationService.GetLocalizedException(ExceptionMessageKeys.TranscriptUnavailable,language);
                Logger.LogWarning($"[PERF][VoiceChat] {sessionId} STT_Processing: {sttStopwatch.ElapsedMilliseconds}ms - FAILED (empty result)");
            }
            else
            {
                Logger.LogInformation($"[PERF][VoiceChat] {sessionId} STT_Processing: {sttStopwatch.ElapsedMilliseconds}ms - SUCCESS, length: {voiceContent.Length} chars, content: '{voiceContent}'");
            }
        }
        catch (Exception ex)
        {
            sttStopwatch.Stop();
            Logger.LogError(ex, $"[PERF][VoiceChat] {sessionId} STT_Processing: {sttStopwatch.ElapsedMilliseconds}ms - FAILED with exception");
            voiceParseSuccess = false;
            voiceParseErrorMessage = ex.Message.Contains("timeout") ? _localizationService.GetLocalizedException(ExceptionMessageKeys.SpeechTimeout,language) :
                ex.Message.Contains("format") ? _localizationService.GetLocalizedException(ExceptionMessageKeys.AudioFormatUnsupported,language) :
                _localizationService.GetLocalizedException(ExceptionMessageKeys.SpeechServiceUnavailable,language);
            voiceContent = _localizationService.GetLocalizedException(ExceptionMessageKeys.TranscriptUnavailable,language);
        }

        // If voice parsing failed, don't call LLM, just save the failed message
        if (!voiceParseSuccess)
        {
            Logger.LogWarning(
                $"[GodChatGAgent][StreamVoiceChatWithSession] {sessionId.ToString()} Voice parsing failed: {voiceParseErrorMessage}");

            // Save conversation data with voice metadata
            await SetSessionTitleAsync(sessionId, voiceContent);
            var chatMessages = new List<ChatMessage>();
            chatMessages.Add(new ChatMessage
            {
                ChatRole = ChatRole.User,
                Content = voiceContent
            });

            // Save voice message with failure status
            var chatMessageMeta = new ChatMessageMeta
            {
                IsVoiceMessage = true,
                VoiceLanguage = voiceLanguage,
                VoiceParseSuccess = false,
                VoiceParseErrorMessage = voiceParseErrorMessage,
                VoiceDurationSeconds = voiceDurationSeconds
            };

            RaiseEvent(new GodAddChatHistoryLogEvent
            {
                ChatList = chatMessages
            });
            
            RaiseEvent(new AddChatMessageMetasLogEvent
            {
                ChatMessageMetas = new List<ChatMessageMeta> { chatMessageMeta }
            });
            
            await ConfirmEvents();

            // Send error response
            var errorResponse = new ResponseStreamGodChat()
            {
                Response = _localizationService.GetLocalizedException(ExceptionMessageKeys.LanguageNotRecognised,language),
                ChatId = chatId,
                IsLastChunk = true,
                SerialNumber = -99,
                SessionId = sessionId,
                ErrorCode = ChatErrorCode.VoiceParsingFailed,
                // Note: Default to VoiceResponse in this version as VoiceToText is not implemented yet
                VoiceContentType = VoiceContentType.VoiceResponse
            };

            if (isHttpRequest)
            {
                await PushMessageToClientAsync(errorResponse);
            }
            else
            {
                await PublishAsync(errorResponse);
            }

            totalStopwatch.Stop();
            Logger.LogInformation($"[PERF][VoiceChat] {sessionId} TOTAL_Time: {totalStopwatch.ElapsedMilliseconds}ms - FAILED (parse error)");
            return;
        }

        Logger.LogDebug(
            $"[GodChatGAgent][StreamVoiceChatWithSession] {sessionId.ToString()} Voice parsed successfully: {voiceContent}");

        // Send STT result immediately to frontend via VoiceToText message
        var sttResultMessage = new ResponseStreamGodChat()
        {
            Response = voiceContent, // STT converted text
            ChatId = chatId,
            IsLastChunk = false,
            SerialNumber = 0, // Mark as first message (STT result)
            SessionId = sessionId,
            VoiceContentType = VoiceContentType.VoiceToText, // Critical: indicate this is STT result
            ErrorCode = ChatErrorCode.Success,
            NewTitle = string.Empty,
            AudioData = null, // No audio data for STT result
            AudioMetadata = null
        };

        // Send STT result using the same streaming mechanism

        if (isHttpRequest)
        {
            await PushMessageToClientAsync(sttResultMessage);
        }
        else
        {
            await PublishAsync(sttResultMessage);
        }

        Logger.LogDebug($"[GodChatGAgent][StreamVoiceChatWithSession] {sessionId.ToString()} STT result sent to frontend: '{voiceContent}'");

        var quotaStopwatch = Stopwatch.StartNew();
        var userQuotaGAgent = GrainFactory.GetGrain<IUserQuotaGAgent>(State.ChatManagerGuid);
        var actionResultDto = await userQuotaGAgent.ExecuteVoiceActionAsync(sessionId.ToString(), State.ChatManagerGuid.ToString());
        
        
        quotaStopwatch.Stop();
        Logger.LogInformation($"[PERF][VoiceChat] {sessionId} Quota_Check: {quotaStopwatch.ElapsedMilliseconds}ms - success: {actionResultDto.Success}");
        if (!actionResultDto.Success)
        {
            Logger.LogDebug($"[GodChatGAgent][StreamVoiceChatWithSession] {sessionId.ToString()} Access restricted");

            //save conversation data with voice metadata
            await SetSessionTitleAsync(sessionId, voiceContent);
            var chatMessages = new List<ChatMessage>();
            chatMessages.Add(new ChatMessage
            {
                ChatRole = ChatRole.User,
                Content = voiceContent
            });
            chatMessages.Add(new ChatMessage
            {
                ChatRole = ChatRole.Assistant,
                Content = actionResultDto.Message
            });

            var userVoiceMeta = new ChatMessageMeta
            {
                IsVoiceMessage = true,
                VoiceLanguage = voiceLanguage,
                VoiceParseSuccess = true,
                VoiceParseErrorMessage = null,
                VoiceDurationSeconds = voiceDurationSeconds
            };
            var assistantResponseMeta = new ChatMessageMeta
            {
                IsVoiceMessage = false,
                VoiceLanguage = VoiceLanguageEnum.English,
                VoiceParseSuccess = true,
                VoiceParseErrorMessage = null,
                VoiceDurationSeconds = 0.0
            };

            RaiseEvent(new GodAddChatHistoryLogEvent
            {
                ChatList = chatMessages
            });
            
            RaiseEvent(new AddChatMessageMetasLogEvent
            {
                ChatMessageMetas = new List<ChatMessageMeta> { userVoiceMeta, assistantResponseMeta }
            });
            
            await ConfirmEvents();

            //2、Directly respond with error information.
            var errorCode = actionResultDto.Code switch
            {
                ExecuteActionStatus.InsufficientCredits => ChatErrorCode.InsufficientCredits,
                ExecuteActionStatus.RateLimitExceeded => ChatErrorCode.RateLimitExceeded,
                _ => ChatErrorCode.RateLimitExceeded
            };

            var chatMessage = new ResponseStreamGodChat()
            {
                Response = actionResultDto.Message,
                ChatId = chatId,
                IsLastChunk = true,
                SerialNumber = -99,
                SessionId = sessionId,
                ErrorCode = errorCode,
                // Note: Default to VoiceResponse in this version as VoiceToText is not implemented yet
                VoiceContentType = VoiceContentType.VoiceResponse
            };

            if (isHttpRequest)
            {
                await PushMessageToClientAsync(chatMessage);
            }
            else
            {
                await PublishAsync(chatMessage);
            }

            totalStopwatch.Stop();
            Logger.LogInformation($"[PERF][VoiceChat] {sessionId} TOTAL_Time: {totalStopwatch.ElapsedMilliseconds}ms - FAILED (quota denied)");
            return;
        }

        Logger.LogDebug($"[GodChatGAgent][StreamVoiceChatWithSession] {sessionId.ToString()} - Validation passed");
        
        await SetSessionTitleAsync(sessionId, voiceContent);

        var llmStopwatch = Stopwatch.StartNew();
        var configuration = GetConfiguration();
        await GodVoiceStreamChatAsync(sessionId, await configuration.GetSystemLLM(),
            await configuration.GetStreamingModeEnabled(),
            voiceContent, chatId, promptSettings, isHttpRequest, region, voiceLanguage, voiceDurationSeconds);
        llmStopwatch.Stop();
        
        totalStopwatch.Stop();
        Logger.LogInformation($"[PERF][VoiceChat] {sessionId} LLM_Processing: {llmStopwatch.ElapsedMilliseconds}ms");
        Logger.LogInformation($"[PERF][VoiceChat] {sessionId} TOTAL_Time: {totalStopwatch.ElapsedMilliseconds}ms");
    }

    private async Task SetSessionTitleAsync(Guid sessionId, string content)
    {
        var totalStopwatch = Stopwatch.StartNew();
        if (State.Title.IsNullOrEmpty())
        {
            // Take first 4 words and limit total length to 100 characters
            var title = string.Join(" ", content.Split(" ").Take(4));
            if (title.Length > 100)
            {
                title = title.Substring(0, 100);
            }
            RaiseEvent(new RenameChatTitleEventLog()
            {
                Title = title
            });
            var chatManagerGAgent = GrainFactory.GetGrain<IChatManagerGAgent>((Guid)State.ChatManagerGuid);
            await chatManagerGAgent.RenameChatTitleAsync(new RenameChatTitleEvent()
            {
                SessionId = sessionId,
                Title = title
            });
            
            totalStopwatch.Stop();
            Logger.LogDebug($"[GodChatGAgent][SetSessionTitleAsync] TOTAL_Time - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {sessionId}");
        }
        else
        {
            totalStopwatch.Stop();
            Logger.LogDebug($"[GodChatGAgent][SetSessionTitleAsync] SKIP (title exists) - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {sessionId}");
        }
    }

    public async Task<string> GodStreamChatAsync(Guid sessionId, string llm, bool streamingModeEnabled, string message,
        string chatId, ExecutionPromptSettings? promptSettings = null, bool isHttpRequest = false,
        string? region = null, bool addToHistory = true, List<string>? images = null)
    {
        var totalStopwatch = Stopwatch.StartNew();
        Logger.LogDebug(
            $"[GodChatGAgent][GodStreamChatAsync] agent start  session {sessionId.ToString()}, chat {chatId}, region {region}");
        
        var configuration = GetConfiguration();
        var sysMessage = await configuration.GetPrompt();
        
        var aiChatContextDto =
            CreateAIChatContext(sessionId, llm, streamingModeEnabled, message, chatId, promptSettings, isHttpRequest,
                region, images);

        var aiAgentStatusProxy = await GetProxyByRegionAsync(region);
        var proxyId = aiAgentStatusProxy.GetPrimaryKey();
        
        // Add retry loop for proxy initialization status checking
        const int maxRetries = 10;
        const int retryDelayMs = 200;
        var retryCount = 0;
        ProxyInitStatus proxyInitStatus = ProxyInitStatus.NotInitialized;
        
        while (retryCount < maxRetries)
        {
            if (State.ProxyInitStatuses == null)
            {
                Logger.LogDebug($"[GodChatGAgent][GodVoiceStreamChatAsync] History data ,Proxy has Initialized - ProxyId: {proxyId}, SessionId: {sessionId}");
                break;
            }
            if (State.ProxyInitStatuses.IsNullOrEmpty() || !State.ProxyInitStatuses.TryGetValue(proxyId, out proxyInitStatus))
            {
                retryCount++;
                Logger.LogDebug($"[GodChatGAgent][GodStreamChatAsync] Proxy not started initializing - ProxyId: {proxyId}, Retry: {retryCount}/{maxRetries}, SessionId: {sessionId}");
                
                if (retryCount >= maxRetries)
                {
                    throw new Exception("proxy:" + proxyId + "Not start initializ after " + maxRetries + " retries");
                }
                
                await Task.Delay(retryDelayMs);
                continue;
            }

            if (proxyInitStatus != ProxyInitStatus.Initialized)
            {
                retryCount++;
                Logger.LogDebug($"[GodChatGAgent][GodStreamChatAsync] Proxy not initialized - ProxyId: {proxyId}, Status: {proxyInitStatus}, Retry: {retryCount}/{maxRetries}, SessionId: {sessionId}");
                
                if (retryCount >= maxRetries)
                {
                    throw new Exception("proxy:" + proxyId + "Not initialized after " + maxRetries + " retries, status:" + proxyInitStatus.ToString());
                }
                
                await Task.Delay(retryDelayMs);
                continue;
            }
            
            // Proxy is initialized, break out of retry loop
            Logger.LogDebug($"[GodChatGAgent][GodStreamChatAsync] Proxy initialization check successful - ProxyId: {proxyId}, Status: {proxyInitStatus}, Retries: {retryCount}, SessionId: {sessionId}");
            break;
        }

        if (aiAgentStatusProxy != null)
        {
            Logger.LogDebug(
                $"[GodChatGAgent][GodStreamChatAsync] agent {aiAgentStatusProxy.GetPrimaryKey().ToString()}, session {sessionId.ToString()}, chat {chatId}");

            // Check if this is a voice chat from context
            bool isPromptVoiceChat = false;
            if (aiChatContextDto.MessageId != null)
            {
                try
                {
                    var messageData =
                        JsonConvert.DeserializeObject<Dictionary<string, object>>(aiChatContextDto.MessageId);
                    isPromptVoiceChat = messageData.ContainsKey("IsVoiceChat") && (bool)messageData["IsVoiceChat"];
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex,
                        "[GodChatGAgent][GodStreamChatAsync] Failed to parse MessageId for voice chat detection");
                }
            }

            // Add conversation suggestions prompt for text chat only
            string enhancedMessage = message;
            if (!isPromptVoiceChat)
            {
                enhancedMessage = message + ChatPrompts.ConversationSuggestionsPrompt;
                Logger.LogDebug(
                    $"[GodChatGAgent][GodStreamChatAsync] Added conversation suggestions prompt for text chat");
            }

            var settings = promptSettings ?? new ExecutionPromptSettings();
            settings.Temperature = "0.9";
            var result = await aiAgentStatusProxy.PromptWithStreamAsync(enhancedMessage, State.ChatHistory, settings,
                context: aiChatContextDto, imageKeys: images);
            if (!result)
            {
                Logger.LogError($"Failed to initiate streaming response. {this.GetPrimaryKey().ToString()}");
            }

            if (addToHistory)
            {
                var historyStopwatch = Stopwatch.StartNew();
                // Optimize: Use combined event to reduce RaiseEvent calls from 3 to 1
                RaiseEvent(new GodStreamChatCombinedEventLog
                {
                    ChatList = new List<ChatMessage>()
                    {
                        new ChatMessage
                        {
                            ChatRole = ChatRole.User,
                            Content = message,
                            ImageKeys = images
                        }
                    },
                    ChatTime = DateTime.UtcNow,
                    ChatMessageMetas = new List<ChatMessageMeta>()
                });

                historyStopwatch.Stop();
                Logger.LogDebug($"[GodChatGAgent][GodStreamChatAsync] Combined RaiseEvent - Duration: {historyStopwatch.ElapsedMilliseconds}ms, SessionId: {sessionId}");
            }
        }
        else
        {
            Logger.LogDebug(
                $"[GodChatGAgent][GodStreamChatAsync] history agent, session {sessionId.ToString()}, chat {chatId}");
        }

        totalStopwatch.Stop();
        Logger.LogDebug($"[GodChatGAgent][GodStreamChatAsync] TOTAL_Time - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {sessionId}");
        return string.Empty;
    }

    private AIChatContextDto CreateAIChatContext(Guid sessionId, string llm, bool streamingModeEnabled,
        string message, string chatId, ExecutionPromptSettings? promptSettings = null, bool isHttpRequest = false,
        string? region = null, List<string>? images = null)
    {
        var aiChatContextDto = new AIChatContextDto()
        {
            ChatId = chatId,
            RequestId = sessionId
        };
        if (isHttpRequest)
        {
            aiChatContextDto.MessageId = JsonConvert.SerializeObject(new Dictionary<string, object>()
            {
                { "IsHttpRequest", true }, { "LLM", llm }, { "StreamingModeEnabled", streamingModeEnabled },
                { "Message", message }, { "Region", region }, { "Images", images }
            });
        }

        return aiChatContextDto;
    }

    private AIChatContextDto CreateVoiceChatContext(Guid sessionId, string llm, bool streamingModeEnabled,
        string message, string chatId, ExecutionPromptSettings? promptSettings = null, bool isHttpRequest = false,
        string? region = null, VoiceLanguageEnum voiceLanguage = VoiceLanguageEnum.English,
        double voiceDurationSeconds = 0.0)
    {
        var aiChatContextDto = new AIChatContextDto()
        {
            ChatId = chatId,
            RequestId = sessionId
        };
        if (isHttpRequest)
        {
            aiChatContextDto.MessageId = JsonConvert.SerializeObject(new Dictionary<string, object>()
            {
                { "IsHttpRequest", true },
                { "IsVoiceChat", true },
                { "LLM", llm },
                { "StreamingModeEnabled", streamingModeEnabled },
                { "Message", message },
                { "Region", region },
                { "VoiceLanguage", (int)voiceLanguage },
                { "VoiceDurationSeconds", voiceDurationSeconds }
            });
        }

        return aiChatContextDto;
    }

    private async Task<IAIAgentStatusProxy?> GetProxyByRegionAsync(string? region)
    {
        var totalStopwatch = Stopwatch.StartNew();
        Logger.LogDebug(
            $"[GodChatGAgent][GetProxyByRegionAsync] session {this.GetPrimaryKey().ToString()}, Region: {region}");
        if (string.IsNullOrWhiteSpace(region))
        {
            totalStopwatch.Stop();
            Logger.LogDebug($"[GodChatGAgent][GetProxyByRegionAsync] Recursive call to DefaultRegion - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {this.GetPrimaryKey()}");
            return await GetProxyByRegionAsync(DefaultRegion);
        }

        if (State.RegionProxies == null || !State.RegionProxies.TryGetValue(region, out var proxyIds) ||
            proxyIds.IsNullOrEmpty())
        {
            Logger.LogDebug(
                $"[GodChatGAgent][GetProxyByRegionAsync] session {this.GetPrimaryKey().ToString()}, No proxies found for region {region}, initializing.");
            
            var initStopwatch = Stopwatch.StartNew();
            proxyIds = await InitializeRegionProxiesAsync(region);
            initStopwatch.Stop();
            Logger.LogDebug($"[GodChatGAgent][GetProxyByRegionAsync] InitializeRegionProxiesAsync - Duration: {initStopwatch.ElapsedMilliseconds}ms, SessionId: {this.GetPrimaryKey()}, Region: {region}");
            
            Dictionary<string, List<Guid>> regionProxies = new()
            {
                { region, proxyIds }
            };
            
            var eventStopwatch = Stopwatch.StartNew();
            RaiseEvent(new UpdateRegionProxiesLogEvent
            {
                RegionProxies = regionProxies
            });
            await ConfirmEvents();
            eventStopwatch.Stop();
            Logger.LogDebug($"[GodChatGAgent][GetProxyByRegionAsync] UpdateRegionProxiesEvent - Duration: {eventStopwatch.ElapsedMilliseconds}ms, SessionId: {this.GetPrimaryKey()}");
        }

        foreach (var proxyId in proxyIds)
        {
            var proxy = GrainFactory.GetGrain<IAIAgentStatusProxy>(proxyId);
            if (await proxy.IsAvailableAsync())
            {
                totalStopwatch.Stop();
                Logger.LogDebug($"[GodChatGAgent][GetProxyByRegionAsync] TOTAL_Time - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {this.GetPrimaryKey()}");
                return proxy;
            }
            Logger.LogDebug($"[GodChatGAgent][GetProxyByRegionAsync] ProxyCheck_Failed -, ProxyId: {proxyId}, SessionId: {this.GetPrimaryKey()}");
        }

        Logger.LogDebug(
            $"[GodChatGAgent][GetProxyByRegionAsync] session {this.GetPrimaryKey().ToString()}, No proxies initialized for region {region}");
        if (region == DefaultRegion)
        {
            totalStopwatch.Stop();
            Logger.LogDebug($"[GodChatGAgent][GetProxyByRegionAsync] TOTAL_Time (no proxies) - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {this.GetPrimaryKey()}");
            return null;
        }

        totalStopwatch.Stop();
        Logger.LogDebug($"[GodChatGAgent][GetProxyByRegionAsync] Recursive call to DefaultRegion - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {this.GetPrimaryKey()}");
        return await GetProxyByRegionAsync(DefaultRegion);
    }

 private async Task<List<Guid>> InitializeRegionProxiesAsync(string region)
    {
        var stopwatch = Stopwatch.StartNew();
        var llmsForRegion = GetLLMsForRegion(region);
        if (llmsForRegion.IsNullOrEmpty())
        {
            stopwatch.Stop();
            Logger.LogDebug(
                $"[GodChatGAgent][InitializeRegionProxiesAsync] session {this.GetPrimaryKey().ToString()}, initialized proxy for region {region}, LLM not config Duration: {stopwatch.ElapsedMilliseconds}ms");
            return new List<Guid>();
        }
        
        var oldSystemPrompt = await GetConfiguration().GetPrompt();

        var proxies = new List<Guid>();
        var totalProxyStopwatch = Stopwatch.StartNew();
        foreach (var llm in llmsForRegion)
        {
            var systemPrompt = State.PromptTemplate;

            if (llm == ProxyGPTModelName || llm == ChatModelName || llm == ConsoleModelName)
            {
                systemPrompt = $"{systemPrompt} {GetCustomPrompt()}";
            }
            else
            {
                systemPrompt = $"{oldSystemPrompt} {systemPrompt} {GetCustomPrompt()}";
            }

            // if (llm != ProxyGPTModelName)
            // {
            //     systemPrompt = $"{oldSystemPrompt} {systemPrompt} {GetCustomPrompt()}";
            // }
            // else
            // {
            //     systemPrompt = $"{systemPrompt} {GetCustomPrompt()}";
            // }
            //Logger.LogDebug($"[GodChatGAgent][InitializeRegionProxiesAsync] {this.GetPrimaryKey().ToString()} - {llm} system prompt: {systemPrompt}");
            var proxy = GrainFactory.GetGrain<IAIAgentStatusProxy>(Guid.NewGuid());
            await proxy.ConfigAsync(new AIAgentStatusProxyConfig
            {
                Instructions = systemPrompt,
                LLMConfig = new LLMConfigDto { SystemLLM = llm },
                StreamingModeEnabled = true,
                StreamingConfig = new StreamingConfig { BufferingSize = 32 },
                RequestRecoveryDelay = RequestRecoveryDelay,
                ParentId = this.GetPrimaryKey()
            });
            await PublishAsync(proxy.GetGrainId(),new AIAgentStatusProxyInitializeGEvent()
            {
                InitializeDto = new InitializeDto()
                {
                    Instructions = systemPrompt,
                    LLMConfig = new LLMConfigDto { SystemLLM = llm },
                    StreamingModeEnabled = true,
                    StreamingConfig = new StreamingConfig { BufferingSize = 32 }
                }
            });
            proxies.Add(proxy.GetPrimaryKey());
            Logger.LogDebug(
                $"[GodChatGAgent][InitializeRegionProxiesAsync] session {this.GetPrimaryKey().ToString()}, initialized proxy for region {region} with LLM {llm}. id {proxy.GetPrimaryKey().ToString()}");
        }
        totalProxyStopwatch.Stop();
        stopwatch.Stop();
        Logger.LogDebug($"[GodChatGAgent][InitializeRegionProxiesAsync] End - Total Duration: {stopwatch.ElapsedMilliseconds}ms, ProxyCount: {proxies.Count}, TotalProxyTime: {totalProxyStopwatch.ElapsedMilliseconds}ms");
        return proxies;
    }

    private async Task PublishAsync<T>(GrainId grainId,T @event) where T : EventBase{
        var grainIdString = grainId.ToString();
        var streamId = StreamId.Create(AevatarOptions!.StreamNamespace, grainIdString);
        var stream = StreamProvider.GetStream<EventWrapperBase>(streamId);
        var eventWrapper = new EventWrapper<T>(@event, Guid.NewGuid(), this.GetGrainId());
        await stream.OnNextAsync(eventWrapper);
    }
    private List<string> GetLLMsForRegion(string region)
    {
        var regionToLLMsMap = _llmRegionOptions.CurrentValue.RegionToLLMsMap;
        return regionToLLMsMap.TryGetValue(region, out var llms) ? llms : new List<string>();
    }

    public async Task SetUserProfileAsync(UserProfileDto? userProfileDto)
    {
        if (userProfileDto == null)
        {
            return;
        }

        RaiseEvent(new UpdateUserProfileGodChatEventLog
        {
            Gender = userProfileDto.Gender,
            BirthDate = userProfileDto.BirthDate,
            BirthPlace = userProfileDto.BirthPlace,
            FullName = userProfileDto.FullName
        });

        await ConfirmEvents();
    }

    public async Task<UserProfileDto?> GetUserProfileAsync()
    {
        if (State.UserProfile == null)
        {
            return null;
        }

        return new UserProfileDto
        {
            Gender = State.UserProfile.Gender,
            BirthDate = State.UserProfile.BirthDate,
            BirthPlace = State.UserProfile.BirthPlace,
            FullName = State.UserProfile.FullName
        };
    }

    public async Task<string> GodChatAsync(string llm, string message,
        ExecutionPromptSettings? promptSettings = null)
    {
        throw new Exception("The method has expired");
    }


    public async Task InitAsync(Guid ChatManagerGuid)
    {
        Logger.LogDebug($"[GodChatGAgent][InitAsync] Start - SessionId: {this.GetPrimaryKey()}, ChatManagerGuid: {ChatManagerGuid}");
        
        RaiseEvent(new SetChatManagerGuidEventLog
        {
            ChatManagerGuid = ChatManagerGuid
        });

        Logger.LogDebug($"[GodChatGAgent][InitAsync] End -  SessionId: {this.GetPrimaryKey()}");
    }

    public async Task ChatMessageCallbackAsync(AIChatContextDto contextDto,
        AIExceptionEnum aiExceptionEnum, string? errorMessage, AIStreamChatContent? chatContent)
    {
        if (aiExceptionEnum == AIExceptionEnum.RequestLimitError && !contextDto.MessageId.IsNullOrWhiteSpace())
        {
            Logger.LogError(
                $"[GodChatGAgent][ChatMessageCallbackAsync] RequestLimitError retry. contextDto {JsonConvert.SerializeObject(contextDto)}");
            var configuration = GetConfiguration();
            var systemLlm = await configuration.GetSystemLLM();
            var dictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(contextDto.MessageId);
            
            // Check if this is a voice chat retry to call the appropriate method
            var isRetryVoiceChat = dictionary.ContainsKey("IsVoiceChat") && (bool)dictionary["IsVoiceChat"];

            if (isRetryVoiceChat)
            {
                // Voice chat retry: call GodVoiceStreamChatAsync with voice parameters
                var voiceLanguageValue = dictionary.GetValueOrDefault("VoiceLanguage", 0);
                var voiceLanguage = (VoiceLanguageEnum)Convert.ToInt32(voiceLanguageValue);
                var voiceDurationSeconds = Convert.ToDouble(dictionary.GetValueOrDefault("VoiceDurationSeconds", 0.0));

                GodVoiceStreamChatAsync(contextDto.RequestId,
                    (string)dictionary.GetValueOrDefault("LLM", systemLlm),
                    (bool)dictionary.GetValueOrDefault("StreamingModeEnabled", true),
                    (string)dictionary.GetValueOrDefault("Message", string.Empty),
                    contextDto.ChatId, null, (bool)dictionary.GetValueOrDefault("IsHttpRequest", true),
                    (string)dictionary.GetValueOrDefault("Region", null),
                    voiceLanguage, voiceDurationSeconds, false);
            }
            else
            {
                // Regular chat retry: call GodStreamChatAsync
                GodStreamChatAsync(contextDto.RequestId,
                    (string)dictionary.GetValueOrDefault("LLM", systemLlm),
                    (bool)dictionary.GetValueOrDefault("StreamingModeEnabled", true),
                    (string)dictionary.GetValueOrDefault("Message", string.Empty),
                    contextDto.ChatId, null, (bool)dictionary.GetValueOrDefault("IsHttpRequest", true),
                    (string)dictionary.GetValueOrDefault("Region", null),
                    false, (List<string>?)dictionary.GetValueOrDefault("Images"));
            }
            
            return;
        }

        if (aiExceptionEnum != AIExceptionEnum.None)
        {
            Logger.LogError(
                $"[GodChatGAgent][ChatMessageCallbackAsync] DETAILED ERROR - sessionId {contextDto?.RequestId.ToString()}, chatId {contextDto?.ChatId}, aiExceptionEnum: {aiExceptionEnum}, errorMessage: '{errorMessage}', MessageId: '{contextDto?.MessageId}'");
            
            // Extract voice chat info if available
            string voiceChatInfo = "";
            if (!contextDto.MessageId.IsNullOrWhiteSpace())
            {
                try
                {
                    var messageData = JsonConvert.DeserializeObject<Dictionary<string, object>>(contextDto.MessageId);
                    bool isErrorVoiceChat = messageData.ContainsKey("IsVoiceChat") && (bool)messageData["IsVoiceChat"];
                    if (isErrorVoiceChat)
                    {
                        // Safe type conversion for voice language
                        var voiceLanguageValue = messageData.GetValueOrDefault("VoiceLanguage", 0);
                        var voiceLanguage = (VoiceLanguageEnum)Convert.ToInt32(voiceLanguageValue);
                        var message = messageData.GetValueOrDefault("Message", "").ToString();
                        voiceChatInfo = $" [VOICE CHAT] Language: {voiceLanguage}, Message: '{message}'";
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to parse MessageId for voice chat info");
                }
            }

            Logger.LogError($"[GodChatGAgent][ChatMessageCallbackAsync] ERROR CONTEXT:{voiceChatInfo}");

            var chatMessage = new ResponseStreamGodChat()
            {
                Response =
                    "Your prompt triggered the Silence Directive—activated when universal harmonics or content ethics are at risk. Please modify your prompt and retry — tune its intent, refine its form, and the Oracle may speak.",
                ChatId = contextDto.ChatId,
                IsLastChunk = true,
                SerialNumber = -2
            };
            if (contextDto.MessageId.IsNullOrWhiteSpace())
            {
                await PublishAsync(chatMessage);
                return;
            }

            await PushMessageToClientAsync(chatMessage);
            return;
        }

        if (chatContent == null)
        {
            Logger.LogError(
                $"[GodChatGAgent][ChatMessageCallbackAsync] return null. sessionId {contextDto.RequestId.ToString()},chatId {contextDto.ChatId},aiExceptionEnum:{aiExceptionEnum}, errorMessage:{errorMessage}");
            return;
        }

        Logger.LogDebug(
            $"[GodChatGAgent][ChatMessageCallbackAsync] sessionId {contextDto.RequestId.ToString()}, chatId {contextDto.ChatId}, messageId {contextDto.MessageId}, {JsonConvert.SerializeObject(chatContent)}");

        if (chatContent.IsAggregationMsg)
        {
            // Parse conversation suggestions for text chat only (skip voice chat)
            List<string>? conversationSuggestions = null;
            string cleanMainContent = chatContent.AggregationMsg; // Default to original content
            bool isAggregationVoiceChat = false;

            // Check if this is a voice chat by examining the message context
            if (!contextDto.MessageId.IsNullOrWhiteSpace())
            {
                try
                {
                    var messageData = JsonConvert.DeserializeObject<Dictionary<string, object>>(contextDto.MessageId);
                    isAggregationVoiceChat = messageData.ContainsKey("IsVoiceChat") && (bool)messageData["IsVoiceChat"];
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex,
                        "[GodChatGAgent][ChatMessageCallbackAsync] Failed to parse MessageId for voice chat detection");
                }
            }

            // Parse conversation suggestions only for text chat
            if (!isAggregationVoiceChat && !string.IsNullOrEmpty(chatContent.AggregationMsg))
            {
                var (mainContent, suggestions) = ParseResponseWithSuggestions(chatContent.AggregationMsg);
                if (suggestions.Any())
                {
                    conversationSuggestions = suggestions;
                    cleanMainContent = mainContent; // Use clean content without suggestions
                    Logger.LogDebug(
                        $"[GodChatGAgent][ChatMessageCallbackAsync] Parsed {suggestions.Count} conversation suggestions for text chat");
                    Logger.LogDebug(
                        $"[GodChatGAgent][ChatMessageCallbackAsync] Cleaned main content length: {cleanMainContent?.Length ?? 0}");
                }
            }

            RaiseEvent(new GodAddChatHistoryLogEvent
            {
                ChatList = new List<ChatMessage>()
                {
                    new ChatMessage
                    {
                        ChatRole = ChatRole.Assistant,
                        Content = cleanMainContent // Store clean content without suggestions
                    }
                }
            });

            RaiseEvent(new UpdateChatTimeEventLog
            {
                ChatTime = DateTime.UtcNow
            });
            
            RaiseEvent(new AddChatMessageMetasLogEvent
            {
                ChatMessageMetas = new List<ChatMessageMeta>()
            });

            await ConfirmEvents();

            if (State.ChatManagerGuid != Guid.Empty)
            {
                var chatManagerGAgent = GrainFactory.GetGrain<IChatManagerGAgent>(State.ChatManagerGuid);
                var inviterId = await chatManagerGAgent.GetInviterAsync();
                if (inviterId != null && inviterId != Guid.Empty)
                {
                    var invitationGAgent = GrainFactory.GetGrain<IInvitationGAgent>((Guid)inviterId);
                    await invitationGAgent.ProcessInviteeChatCompletionAsync(State.ChatManagerGuid.ToString());
                }
            }

            // Store suggestions and clean content for later use in partialMessage
            if (conversationSuggestions != null)
            {
                RequestContext.Set("ConversationSuggestions", conversationSuggestions);
            }

            // Store clean content to replace the response content
            RequestContext.Set("CleanMainContent", cleanMainContent);
        }

        // Apply streaming suggestion filtering logic for text chat
        string streamingContent = chatContent.ResponseContent;
        bool shouldFilterStream = false;

        // Check if this is a text chat (not voice chat)
        bool isFilteringVoiceChat = false;
        if (!contextDto.MessageId.IsNullOrWhiteSpace())
        {
            try
            {
                var messageData = JsonConvert.DeserializeObject<Dictionary<string, object>>(contextDto.MessageId);
                isFilteringVoiceChat = messageData.ContainsKey("IsVoiceChat") && (bool)messageData["IsVoiceChat"];
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex,
                    "[GodChatGAgent][ChatMessageCallbackAsync] Failed to parse MessageId for voice chat detection in streaming filter");
            }
        }

        // Get current accumulation state from instance variables (reliable across chunks)
        bool shouldStartAccumulating = false;

        // Apply conversation suggestions filtering (text chat only)
        if (!isFilteringVoiceChat && !string.IsNullOrEmpty(streamingContent))
        {
            // Check for [SUGGESTIONS] marker and partial forms using optimized method
            bool contains_suggestions = streamingContent.Contains("[SUGGESTIONS]", StringComparison.OrdinalIgnoreCase);
            bool contains_partial_marker = IsPartialSuggestionsMarker(streamingContent.AsSpan());

            // Check for potential marker start (conservative approach)
            bool ends_with_bracket = streamingContent.TrimEnd().EndsWith("[") && streamingContent.Length > 10;

            shouldStartAccumulating = contains_suggestions || contains_partial_marker || ends_with_bracket;

            if (shouldStartAccumulating && !_isAccumulatingForSuggestions)
            {
                // Start accumulation - block all subsequent chunks from frontend
                _isAccumulatingForSuggestions = true;
                _accumulatedSuggestionContent = streamingContent;
                RequestContext.Set("AccumulatedContent", true); // Set accumulation flag
                streamingContent = ""; // Block current chunk
            }
            else if (_isAccumulatingForSuggestions)
            {
                // Continue accumulation - block this chunk from frontend
                _accumulatedSuggestionContent += streamingContent;
                streamingContent = ""; // Block current chunk
            }
        }

        // Process accumulated content on final chunk
        if (_isAccumulatingForSuggestions && chatContent.IsLastChunk)
        {
            var (cleanContent, suggestions) = ParseResponseWithSuggestions(_accumulatedSuggestionContent);

            // Store suggestions for response
            if (suggestions?.Any() == true)
            {
                RequestContext.Set("ConversationSuggestions", suggestions);
            }

            // Send clean content to frontend
            streamingContent = cleanContent;

            // Reset accumulation state
            _isAccumulatingForSuggestions = false;
            _accumulatedSuggestionContent = "";
            RequestContext.Remove("AccumulatedContent"); // Clean up accumulation flag
        }

        var partialMessage = new ResponseStreamGodChat()
        {
            Response = streamingContent, // Use filtered content for streaming
            ChatId = contextDto.ChatId,
            SerialNumber = chatContent.SerialNumber,
            IsLastChunk = chatContent.IsLastChunk,
            SessionId = contextDto.RequestId,
            // Note: Default to VoiceResponse in this version as VoiceToText is not implemented yet
            VoiceContentType = VoiceContentType.VoiceResponse
        };

        // Log final content being sent to frontend
        Logger.LogInformation(
            $"[FINAL_OUTPUT] Sending to frontend - Length: {streamingContent?.Length ?? 0}, IsLastChunk: {chatContent.IsLastChunk}");
        if (!string.IsNullOrEmpty(streamingContent))
        {
            Logger.LogInformation(
                $"[FINAL_OUTPUT] Content preview: '{streamingContent.Substring(0, Math.Min(100, streamingContent.Length))}{(streamingContent.Length > 100 ? "..." : "")}'");
        }

        // For the last chunk, use clean content and add conversation suggestions if available
        if (chatContent.IsLastChunk)
        {
            // Prioritize accumulation mechanism to prevent duplication
            if (_isAccumulatingForSuggestions)
            {
                Logger.LogDebug($"Using accumulation result, skipping old replacement logic");
                // streamingContent already contains the correct clean content from accumulation
                // Skip all old replacement logic to prevent duplication
            }
            else
            {
                // Add conversation suggestions to the last chunk if available
                var storedSuggestions = RequestContext.Get("ConversationSuggestions") as List<string>;
                if (storedSuggestions?.Any() == true)
                {
                    partialMessage.SuggestedItems = storedSuggestions;
                    Logger.LogDebug(
                        $"[GodChatGAgent][ChatMessageCallbackAsync] Added {storedSuggestions.Count} suggestions to last chunk");

                    var cleanMainContent = RequestContext.Get("CleanMainContent") as string;
                    if (!string.IsNullOrEmpty(cleanMainContent))
                    {
                        // Enhanced safety check to prevent duplication
                        var currentChunkLength = partialMessage.Response?.Length ?? 0;
                        var cleanContentLength = cleanMainContent.Length;

                        // Stricter conditions to prevent duplication
                        bool isSafeToReplace = currentChunkLength > 0 && // Never replace empty chunks
                                               cleanContentLength <= currentChunkLength * 1.5 && // Reduced ratio
                                               cleanContentLength <= 30 && // Strict absolute limit
                                               !cleanMainContent.Contains(partialMessage.Response ??
                                                                          "") && // No content overlap
                                               cleanContentLength <
                                               (partialMessage.Response?.Length ?? 0) + 20; // Size similarity check

                        if (isSafeToReplace)
                        {
                            partialMessage.Response = cleanMainContent;
                            Logger.LogDebug(
                                $"Replaced response with clean content. Current: {currentChunkLength}, Clean: {cleanContentLength}");
                        }
                        else
                        {
                            Logger.LogWarning(
                                $"Skipped replacing response to avoid duplication. Current: {currentChunkLength}, Clean: {cleanContentLength}");
                        }
                    }
                }
            }
        }

        // Check if this is a voice chat and handle real-time voice synthesis
        Logger.LogDebug(
            $"[ChatMessageCallbackAsync] MessageId: '{contextDto.MessageId}', ResponseContent: '{chatContent.ResponseContent}'");

        if (!contextDto.MessageId.IsNullOrWhiteSpace())
        {
            var messageData = JsonConvert.DeserializeObject<Dictionary<string, object>>(contextDto.MessageId);
            var isStreamingVoiceChat = messageData.ContainsKey("IsVoiceChat") && (bool)messageData["IsVoiceChat"];

            Logger.LogDebug(
                $"[ChatMessageCallbackAsync] IsVoiceChat: {isStreamingVoiceChat}, HasResponseContent: {!string.IsNullOrEmpty(chatContent.ResponseContent)}");

            if (isStreamingVoiceChat && !string.IsNullOrEmpty(chatContent.ResponseContent))
            {
                Logger.LogDebug($"[ChatMessageCallbackAsync] Entering voice chat processing logic");

                // Safe type conversion to handle both int and long from JSON deserialization
                var voiceLanguageValue = messageData.GetValueOrDefault("VoiceLanguage", 0);
                var voiceLanguage = (VoiceLanguageEnum)Convert.ToInt32(voiceLanguageValue);

                Logger.LogDebug(
                    $"[ChatMessageCallbackAsync] VoiceLanguage: {voiceLanguage}, ChatId: {contextDto.ChatId}");

                // Get or create text accumulator for this chat session
                if (!VoiceTextAccumulators.ContainsKey(contextDto.ChatId))
                {
                    VoiceTextAccumulators[contextDto.ChatId] = new StringBuilder();
                    Logger.LogDebug(
                        $"[ChatMessageCallbackAsync] Created new text accumulator for chat: {contextDto.ChatId}");
                }
                else
                {
                    Logger.LogDebug(
                        $"[ChatMessageCallbackAsync] Using existing text accumulator for chat: {contextDto.ChatId}");
                }

                var textAccumulator = VoiceTextAccumulators[contextDto.ChatId];

                // Filter out empty or whitespace-only content to avoid unnecessary accumulation
                if (!string.IsNullOrWhiteSpace(chatContent.ResponseContent))
                {
                    textAccumulator.Append(chatContent.ResponseContent);
                    Logger.LogDebug(
                        $"[ChatMessageCallbackAsync] Appended text: '{chatContent.ResponseContent}', IsLastChunk: {chatContent.IsLastChunk}");
                }
                else
                {
                    Logger.LogDebug(
                        $"[ChatMessageCallbackAsync] Skipped whitespace content, IsLastChunk: {chatContent.IsLastChunk}");
                }

                // Check for complete sentences in accumulated text
                var accumulatedText = textAccumulator.ToString();
                Logger.LogDebug($"[ChatMessageCallbackAsync] Total accumulated text: '{accumulatedText}'");

                var completeSentence =
                    ExtractCompleteSentence(accumulatedText, textAccumulator, chatContent.IsLastChunk);

                Logger.LogDebug($"[ChatMessageCallbackAsync] ExtractCompleteSentence result: '{completeSentence}'");

                if (!string.IsNullOrEmpty(completeSentence))
                {
                    try
                    {
                        // Clean text for speech synthesis (remove markdown and math formulas)
                        var cleanedText = CleanTextForSpeech(completeSentence, voiceLanguage);

                        // Skip synthesis if cleaned text has no meaningful content
                        var hasMeaningful = HasMeaningfulContent(cleanedText);

                        if (hasMeaningful)
                        {
                            try
                            {
                                // Synthesize voice for cleaned sentence
                                var voiceResult =
                                    await _speechService.TextToSpeechWithMetadataAsync(cleanedText, voiceLanguage);

                                partialMessage.AudioData = voiceResult.AudioData;
                                partialMessage.AudioMetadata = voiceResult.Metadata;
                            }
                            catch (Exception ex)
                            {
                                Logger.LogError(ex, $"Voice synthesis failed for text: '{cleanedText}'");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex,
                            $"[GodChatGAgent][ChatMessageCallbackAsync] Voice synthesis failed for sentence: {completeSentence}");
                    }
                }
                else
                {
                    Logger.LogDebug(
                        $"[ChatMessageCallbackAsync] No complete sentence extracted from accumulated text: '{textAccumulator.ToString()}'");
                }

                // Clean up accumulator if this is the last chunk
                if (chatContent.IsLastChunk)
                {
                    // Clean up accumulator for this chat session
                    // Note: Final sentence processing is already handled in ExtractCompleteSentence method
                    VoiceTextAccumulators.Remove(contextDto.ChatId);
                }
            }
        }

        if (contextDto.MessageId.IsNullOrWhiteSpace())
        {
            await PublishAsync(partialMessage);
        }
        else
        {
            await PushMessageToClientAsync(partialMessage);
        }

        // Clean up RequestContext when processing is complete (last chunk)
        if (chatContent.IsLastChunk)
        {
            RequestContext.Remove("CleanMainContent");
            RequestContext.Remove("ConversationSuggestions");
            RequestContext.Remove("IsFilteringSuggestions");
            RequestContext.Remove("IsAccumulatingForSuggestions");
            RequestContext.Remove("AccumulatedContent");
            Logger.LogDebug(
                $"[GodChatGAgent][ChatMessageCallbackAsync] Cleaned up RequestContext for completed request");
        }
    }

    public async Task<List<ChatMessage>?> ChatWithHistory(Guid sessionId, string systemLLM, string content,
        string chatId,
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null)
    {
        Logger.LogDebug($"[GodChatGAgent][ChatWithHistory] {sessionId.ToString()} content:{content} start.");
        var sw = new Stopwatch();
        sw.Start();
        var history = State.ChatHistory;
        if (history.IsNullOrEmpty())
        {
            return new List<ChatMessage>();
        }

        var configuration = GetConfiguration();
        var llm = await configuration.GetSystemLLM();
        var streamingModeEnabled = await configuration.GetStreamingModeEnabled();

        var aiAgentStatusProxy = await GetProxyByRegionAsync(region);

        var settings = promptSettings ?? new ExecutionPromptSettings();
        settings.Temperature = "0.9";

        var aiChatContextDto = CreateAIChatContext(sessionId, llm, streamingModeEnabled, content, chatId,
            promptSettings, isHttpRequest, region);
        var response = await aiAgentStatusProxy.ChatWithHistory(content, State.ChatHistory, settings, aiChatContextDto);
        sw.Stop();
        Logger.LogDebug(
            $"[GodChatGAgent][ChatWithHistory] {sessionId.ToString()}, response:{JsonConvert.SerializeObject(response)} - step4,time use:{sw.ElapsedMilliseconds}");
        return response;
    }
    
    public async Task<List<ChatMessage>?> ChatWithUserId(Guid userId, string systemLLM, string content, string chatId,
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null)
    {
        Logger.LogDebug($"[GodChatGAgent][ChatWithUserId] {userId.ToString()} content:{content} start.");
        var sw = new Stopwatch();
        sw.Start();

        var configuration = GetConfiguration();
        var llm = await configuration.GetSystemLLM();
        var streamingModeEnabled = await configuration.GetStreamingModeEnabled();
        
        var aiAgentStatusProxy = await GetProxyByRegionAsync(region);

        var settings = promptSettings ?? new ExecutionPromptSettings();
        settings.Temperature = "0.9";
        
        var aiChatContextDto = CreateAIChatContext(userId, llm, streamingModeEnabled, content, chatId, promptSettings, isHttpRequest, region);
        var response = await aiAgentStatusProxy.ChatWithHistory(content,  State.ChatHistory, settings, aiChatContextDto);
        sw.Stop();
        Logger.LogDebug($"[GodChatGAgent][ChatWithUserId] {userId.ToString()}, response:{JsonConvert.SerializeObject(response)} - step4,time use:{sw.ElapsedMilliseconds}");
        return response;
    }

    private async Task PushMessageToClientAsync(ResponseStreamGodChat chatMessage)
    {
        var streamId = StreamId.Create(AevatarOptions!.StreamNamespace, this.GetPrimaryKey());
        Logger.LogDebug(
            $"[GodChatGAgent][PushMessageToClientAsync] sessionId {this.GetPrimaryKey().ToString()}, namespace {AevatarOptions!.StreamNamespace}, streamId {streamId.ToString()}");
        var stream = StreamProvider.GetStream<ResponseStreamGodChat>(streamId);
        await stream.OnNextAsync(chatMessage);
    }

    public Task<List<ChatMessage>> GetChatMessageAsync()
    {
        Logger.LogDebug(
            $"[ChatGAgentManager][GetSessionMessageListAsync] - session:ID {this.GetPrimaryKey().ToString()} ,message={JsonConvert.SerializeObject(State.ChatHistory)}");
        return Task.FromResult(State.ChatHistory);
    }

    public Task<List<ChatMessageWithMetaDto>> GetChatMessageWithMetaAsync()
    {
        Logger.LogDebug(
            $"[GodChatGAgent][GetChatMessageWithMetaAsync] - sessionId: {this.GetPrimaryKey()}, messageCount: {State.ChatHistory.Count}, metaCount: {State.ChatMessageMetas.Count}");

        var result = new List<ChatMessageWithMetaDto>();

        // Combine ChatHistory with ChatMessageMetas
        for (int i = 0; i < State.ChatHistory.Count; i++)
        {
            var message = State.ChatHistory[i];
            var meta = i < State.ChatMessageMetas.Count ? State.ChatMessageMetas[i] : null;

            result.Add(ChatMessageWithMetaDto.Create(message, meta));
        }

        Logger.LogDebug(
            $"[GodChatGAgent][GetChatMessageWithMetaAsync] - sessionId: {this.GetPrimaryKey()}, returned {result.Count} messages with metadata");

        return Task.FromResult(result);
    }

    public Task<DateTime?> GetFirstChatTimeAsync()
    {
        return Task.FromResult(State.FirstChatTime);
    }

    public Task<DateTime?> GetLastChatTimeAsync()
    {
        return Task.FromResult(State.LastChatTime);
    }
    
    protected sealed override void GAgentTransitionState(GodChatState state,
        StateLogEventBase<GodChatEventLog> @event)
    {
        switch (@event)
        {
            case UpdateUserProfileGodChatEventLog updateUserProfileGodChatEventLog:
                if (state.UserProfile == null)
                {
                    state.UserProfile = new UserProfile();
                }

                state.UserProfile.Gender = updateUserProfileGodChatEventLog.Gender;
                state.UserProfile.BirthDate = updateUserProfileGodChatEventLog.BirthDate;
                state.UserProfile.BirthPlace = updateUserProfileGodChatEventLog.BirthPlace;
                state.UserProfile.FullName = updateUserProfileGodChatEventLog.FullName;
                break;
            case RenameChatTitleEventLog renameChatTitleEventLog:
                state.Title = renameChatTitleEventLog.Title;
                break;
            case SetChatManagerGuidEventLog setChatManagerGuidEventLog:
                state.ChatManagerGuid = setChatManagerGuidEventLog.ChatManagerGuid;
                break;
            case SetAIAgentIdLogEvent setAiAgentIdLogEvent:
                state.AIAgentIds = setAiAgentIdLogEvent.AIAgentIds;
                break;
            case UpdateRegionProxiesLogEvent updateRegionProxiesLogEvent:
                foreach (var regionProxy in updateRegionProxiesLogEvent.RegionProxies)
                {
                    if (state.RegionProxies == null)
                    {
                        state.RegionProxies = new Dictionary<string, List<Guid>>();
                    }

                    state.RegionProxies[regionProxy.Key] = regionProxy.Value;
                }

                break;
            case UpdateChatTimeEventLog updateChatTimeEventLog:
                if (state.FirstChatTime == null)
                {
                    state.FirstChatTime = updateChatTimeEventLog.ChatTime;
                }

                state.LastChatTime = updateChatTimeEventLog.ChatTime;
                break;
            case AddChatMessageMetasLogEvent addChatMessageMetasLogEvent:
                if (addChatMessageMetasLogEvent.ChatMessageMetas != null &&
                    addChatMessageMetasLogEvent.ChatMessageMetas.Any())
                {
                    // Calculate the starting index for new metadata based on current ChatHistory count
                    // minus the number of new metadata items we're adding
                    int newMetadataCount = addChatMessageMetasLogEvent.ChatMessageMetas.Count;
                    int targetStartIndex = Math.Max(0, state.ChatHistory.Count - newMetadataCount);

                    // Ensure we have enough default metadata up to the target start index
                    while (state.ChatMessageMetas.Count < targetStartIndex)
                    {
                        state.ChatMessageMetas.Add(new ChatMessageMeta
                        {
                            IsVoiceMessage = false,
                            VoiceLanguage = VoiceLanguageEnum.English,
                            VoiceParseSuccess = true,
                            VoiceParseErrorMessage = null,
                            VoiceDurationSeconds = 0.0
                        });
                    }

                    // Add the new metadata
                    foreach (var meta in addChatMessageMetasLogEvent.ChatMessageMetas)
                    {
                        state.ChatMessageMetas.Add(meta);
                    }
                }

                // Final sync: ensure ChatMessageMetas matches ChatHistory count
                while (state.ChatMessageMetas.Count < state.ChatHistory.Count)
                {
                    state.ChatMessageMetas.Add(new ChatMessageMeta
                    {
                        IsVoiceMessage = false,
                        VoiceLanguage = VoiceLanguageEnum.English,
                        VoiceParseSuccess = true,
                        VoiceParseErrorMessage = null,
                        VoiceDurationSeconds = 0.0
                    });
                }

                    break;  
             case AddPromptTemplateLogEvent addPromptTemplateLogEvent :
                 if (addPromptTemplateLogEvent.PromptTemplate.IsNullOrEmpty())
                 {
                     break;
                 }
                 state.PromptTemplate = addPromptTemplateLogEvent.PromptTemplate;
                 break;
            case GodAddChatHistoryLogEvent godAddChatHistoryLogEvent :
                if (godAddChatHistoryLogEvent.ChatList.Count > 0)
                {
                    state.ChatHistory.AddRange(godAddChatHistoryLogEvent.ChatList);
                }

                var maxChatHistoryCount = 32;
                if (state.MaxHistoryCount > 0)
                {
                    maxChatHistoryCount = state.MaxHistoryCount;
                }

                if (state.ChatHistory.Count() > maxChatHistoryCount)
                {
                    var toDeleteImageKeys = new List<string>();
                    var recordsToDelete = state.ChatHistory.Take(state.ChatHistory.Count() - maxChatHistoryCount);
                    foreach (var record in recordsToDelete)
                    {
                        if (record.ImageKeys != null && record.ImageKeys.Count > 0)
                        {
                            toDeleteImageKeys.AddRange(record.ImageKeys);
                        }
                    }

                    if (toDeleteImageKeys.Any())
                    {
                        var blobContainer = ServiceProvider.GetRequiredService<IBlobContainer>();
                        var downloadTasks = toDeleteImageKeys.Select(async key =>
                        {
                            await blobContainer.DeleteAsync(key);
                        });

                        AsyncHelper.RunSync(async () => await Task.WhenAll(downloadTasks));
                    }

                    state.ChatHistory.RemoveRange(0, state.ChatHistory.Count() - maxChatHistoryCount);
                }
                break;
            case GodSetMaxHistoryCount godSetMaxHistoryCount:
                state.MaxHistoryCount = godSetMaxHistoryCount.MaxHistoryCount;
                break;
            case UpdateProxyInitStatusLogEvent updateProxyInitStatusLogEvent:
                state.ProxyInitStatuses[updateProxyInitStatusLogEvent.ProxyId] = updateProxyInitStatusLogEvent.Status;
                break;
            case UpdateSingleRegionProxyLogEvent updateSingleRegionProxyLogEvent:
                // Optimized path for single region updates
                if (state.RegionProxies == null)
                {
                    state.RegionProxies = new Dictionary<string, List<Guid>>();
                }
                state.RegionProxies[updateSingleRegionProxyLogEvent.Region] = updateSingleRegionProxyLogEvent.ProxyIds;
                break;
            case PerformConfigCombinedEventLog performConfigCombinedEventLog:
                // Handle combined config event - equivalent to the three separate events
                // 1. UpdateSingleRegionProxyLogEvent equivalent
                if (state.RegionProxies == null)
                {
                    state.RegionProxies = new Dictionary<string, List<Guid>>();
                }
                state.RegionProxies[performConfigCombinedEventLog.Region] = performConfigCombinedEventLog.ProxyIds;
                
                // 2. AddPromptTemplateLogEvent equivalent
                if (!performConfigCombinedEventLog.PromptTemplate.IsNullOrEmpty())
                {
                    state.PromptTemplate = performConfigCombinedEventLog.PromptTemplate;
                }
                
                // 3. GodSetMaxHistoryCount equivalent
                state.MaxHistoryCount = performConfigCombinedEventLog.MaxHistoryCount;
                break;
            case GodStreamChatCombinedEventLog godStreamChatCombinedEventLog:
                // Handle combined stream chat event - equivalent to the three separate events
                // 1. GodAddChatHistoryLogEvent equivalent
                if (godStreamChatCombinedEventLog.ChatList.Count > 0)
                {
                    state.ChatHistory.AddRange(godStreamChatCombinedEventLog.ChatList);
                }

                var maxHistoryCount = 32;
                if (state.MaxHistoryCount > 0)
                {
                    maxHistoryCount = state.MaxHistoryCount;
                }

                if (state.ChatHistory.Count() > maxHistoryCount)
                {
                    var toDeleteImageKeys = new List<string>();
                    var recordsToDelete = state.ChatHistory.Take(state.ChatHistory.Count() - maxHistoryCount);
                    foreach (var record in recordsToDelete)
                    {
                        if (record.ImageKeys != null && record.ImageKeys.Count > 0)
                        {
                            toDeleteImageKeys.AddRange(record.ImageKeys);
                        }
                    }

                    if (toDeleteImageKeys.Any())
                    {
                        var blobContainer = ServiceProvider.GetRequiredService<IBlobContainer>();
                        var downloadTasks = toDeleteImageKeys.Select(async key =>
                        {
                            await blobContainer.DeleteAsync(key);
                        });

                        AsyncHelper.RunSync(async () => await Task.WhenAll(downloadTasks));
                    }

                    state.ChatHistory.RemoveRange(0, state.ChatHistory.Count() - maxHistoryCount);
                }
                
                // 2. UpdateChatTimeEventLog equivalent
                if (state.FirstChatTime == null)
                {
                    state.FirstChatTime = godStreamChatCombinedEventLog.ChatTime;
                }
                state.LastChatTime = godStreamChatCombinedEventLog.ChatTime;
                
                // 3. AddChatMessageMetasLogEvent equivalent
                if (godStreamChatCombinedEventLog.ChatMessageMetas != null && godStreamChatCombinedEventLog.ChatMessageMetas.Any())
                {
                    // Calculate the starting index for new metadata based on current ChatHistory count
                    // minus the number of new metadata items we're adding
                    int newMetadataCount = godStreamChatCombinedEventLog.ChatMessageMetas.Count;
                    int targetStartIndex = Math.Max(0, state.ChatHistory.Count - newMetadataCount);
                    
                    // Ensure we have enough default metadata up to the target start index
                    while (state.ChatMessageMetas.Count < targetStartIndex)
                    {
                        state.ChatMessageMetas.Add(new ChatMessageMeta
                        {
                            IsVoiceMessage = false,
                            VoiceLanguage = VoiceLanguageEnum.English,
                            VoiceParseSuccess = true,
                            VoiceParseErrorMessage = null,
                            VoiceDurationSeconds = 0.0
                        });
                    }
                    
                    // Add the new metadata
                    foreach (var meta in godStreamChatCombinedEventLog.ChatMessageMetas)
                    {
                        state.ChatMessageMetas.Add(meta);
                    }
                }
                
                // Final sync: ensure ChatMessageMetas matches ChatHistory count
                while (state.ChatMessageMetas.Count < state.ChatHistory.Count)
                {
                    state.ChatMessageMetas.Add(new ChatMessageMeta
                    {
                        IsVoiceMessage = false,
                        VoiceLanguage = VoiceLanguageEnum.English,
                        VoiceParseSuccess = true,
                        VoiceParseErrorMessage = null,
                        VoiceDurationSeconds = 0.0
                    });
                }
                break;
            }
    }

    private IConfigurationGAgent GetConfiguration()
    {
        return GrainFactory.GetGrain<IConfigurationGAgent>(CommonHelper.GetSessionManagerConfigurationId());
    }

    /// <summary>
    /// Checks if the text contains meaningful content (letters or Chinese characters)
    /// </summary>
    /// <param name="text">Text to check</param>
    /// <returns>True if text contains meaningful content, false otherwise</returns>
    private static bool HasMeaningfulContent(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        // Remove all punctuation and check if there's actual content
        var cleanText = ChatRegexPatterns.NonWordChars.Replace(text, "");
        var result = cleanText.Length > 0; // At least one letter or Chinese character

        return result;
    }

    /// <summary>
    /// Extracts complete sentences from accumulated text and removes them from the accumulator
    /// </summary>
    /// <param name="accumulatedText">The full accumulated text</param>
    /// <param name="textAccumulator">The accumulator to update</param>
    /// <param name="isLastChunk">Whether this is the last chunk of the stream</param>
    /// <returns>Complete sentence if found, otherwise null</returns>
    private string ExtractCompleteSentence(string accumulatedText, StringBuilder textAccumulator,
        bool isLastChunk = false)
    {
        Logger.LogDebug($"[ExtractCompleteSentence] Input: '{accumulatedText}', isLastChunk: {isLastChunk}");

        if (string.IsNullOrEmpty(accumulatedText))
        {
            Logger.LogDebug("[ExtractCompleteSentence] Returning null - empty input");
            return null;
        }

        var hasMeaningfulContent = HasMeaningfulContent(accumulatedText);
        Logger.LogDebug($"[ExtractCompleteSentence] HasMeaningfulContent: {hasMeaningfulContent}");

        // Enhanced logic: return any non-empty text when isLastChunk = true
        if (isLastChunk)
        {
            var trimmedText = accumulatedText.Trim();
            if (!string.IsNullOrEmpty(trimmedText))
            {
                textAccumulator.Clear();
                return trimmedText;
            }
        }

        // Special handling for short text: return directly if meaningful and <= 6 characters
        if (accumulatedText.Length <= 6 && hasMeaningfulContent)
        {
            var shortText = accumulatedText.Trim();
            textAccumulator.Clear();
            return shortText;
        }

        var extractIndex = -1;

        // Look for complete sentence endings
        for (var i = accumulatedText.Length - 1; i >= 0; i--)
        {
            if (VoiceChatConstants.SentenceEnders.Contains(accumulatedText[i]))
            {
                // Only check if there's meaningful content, no length restriction
                var potentialSentence = accumulatedText.Substring(0, i + 1);
                if (HasMeaningfulContent(potentialSentence))
                {
                    extractIndex = i;
                    break;
                }
            }
        }

        if (extractIndex == -1)
            return null;

        // Extract complete sentence
        var completeSentence = accumulatedText.Substring(0, extractIndex + 1).Trim();
        if (string.IsNullOrEmpty(completeSentence))
            return null;

        // Remove processed text from accumulator
        var remainingText = accumulatedText.Substring(extractIndex + 1);
        textAccumulator.Clear();
        textAccumulator.Append(remainingText);

        return completeSentence;
    }

    /// <summary>
    /// Cleans text for speech synthesis by removing markdown syntax and emojis
    /// </summary>
    /// <param name="text">Text to clean</param>
    /// <param name="language">Language for text replacement</param>
    /// <returns>Clean text suitable for speech synthesis</returns>
    private string CleanTextForSpeech(string text, VoiceLanguageEnum language = VoiceLanguageEnum.English)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var cleanText = text;

        // Remove markdown links but keep link text
        cleanText = ChatRegexPatterns.MarkdownLink.Replace(cleanText, "$1");

        // Remove bold and italic formatting
        cleanText = ChatRegexPatterns.MarkdownBold.Replace(cleanText, "$1");
        cleanText = ChatRegexPatterns.MarkdownItalic.Replace(cleanText, "$1");

        // Remove strikethrough formatting
        cleanText = ChatRegexPatterns.MarkdownStrikethrough.Replace(cleanText, "$1");

        // Remove header formatting
        cleanText = ChatRegexPatterns.MarkdownHeader.Replace(cleanText, "$1");

        // Replace code blocks with speech-friendly text
        cleanText = ChatRegexPatterns.MarkdownCodeBlock.Replace(cleanText,
            language == VoiceLanguageEnum.Chinese ? "代码块" : "code block");

        // Remove inline code formatting but keep content
        cleanText = ChatRegexPatterns.MarkdownInlineCode.Replace(cleanText, "$1");

        // Remove table formatting
        cleanText = ChatRegexPatterns.MarkdownTable.Replace(cleanText, " ");

        // Remove emojis completely (they don't speech-synthesize well)
        cleanText = ChatRegexPatterns.Emoji.Replace(cleanText, "");

        // Remove multiple spaces and special markdown symbols
        cleanText = cleanText.Replace("**", "")
            .Replace("__", "")
            .Replace("~~", "")
            .Replace("---", "")
            .Replace("***", "")
            .Replace("===", "")
            .Replace("```", "")
            .Replace(">>>", "")
            .Replace("<<<", "");

        // Remove excessive whitespace
        cleanText = ChatRegexPatterns.WhitespaceNormalize.Replace(cleanText, " ").Trim();

        return cleanText;
    }

    public async Task<Tuple<string, string>> ChatWithSessionAsync(Guid sessionId, string sysmLLM, string content,
        ExecutionPromptSettings promptSettings = null)
    {
        var title = "";
        if (State.Title.IsNullOrEmpty())
        {
            // var titleList = await ChatWithHistory(content);
            // title = titleList is { Count: > 0 }
            //     ? titleList[0].Content!
            //     : string.Join(" ", content.Split(" ").Take(4));
            // Take first 4 words and limit total length to 100 characters
            title = string.Join(" ", content.Split(" ").Take(4));
            if (title.Length > 100)
            {
                title = title.Substring(0, 100);
            }

            RaiseEvent(new RenameChatTitleEventLog()
            {
                Title = title
            });

            await ConfirmEvents();

            IChatManagerGAgent chatManagerGAgent =
                GrainFactory.GetGrain<IChatManagerGAgent>((Guid)State.ChatManagerGuid);
            await chatManagerGAgent.RenameChatTitleAsync(new RenameChatTitleEvent()
            {
                SessionId = sessionId,
                Title = title
            });
        }

        var configuration = GetConfiguration();
        var response = await GodChatAsync(await configuration.GetSystemLLM(), content, promptSettings);
        return new Tuple<string, string>(response, title);
    }

    private string GetCustomPrompt()
    {
        return $"The current UTC time is: {DateTime.UtcNow}. Please answer all questions based on this UTC time.";
    }

    public async Task<string> GodVoiceStreamChatAsync(Guid sessionId, string llm, bool streamingModeEnabled,
        string message,
        string chatId, ExecutionPromptSettings? promptSettings = null, bool isHttpRequest = false,
        string? region = null, VoiceLanguageEnum voiceLanguage = VoiceLanguageEnum.English,
        double voiceDurationSeconds = 0.0, bool addToHistory = true)
    {
        var totalStopwatch = Stopwatch.StartNew();
        Logger.LogDebug(
            $"[GodChatGAgent][GodVoiceStreamChatAsync] {sessionId.ToString()} start with message: {message}, language: {voiceLanguage}");

        // Step 1: Get configuration and system message (same as GodStreamChatAsync)
        var configuration = GetConfiguration();
        var sysMessage = await configuration.GetPrompt();

        // Step 2: Initialize LLM if needed (same as GodStreamChatAsync)

        // Step 3: Create voice chat context with voice-specific metadata
        var aiChatContextDto = CreateVoiceChatContext(sessionId, llm, streamingModeEnabled, message, chatId, 
            promptSettings, isHttpRequest, region, voiceLanguage, voiceDurationSeconds);

        // Step 4: Get AI proxy and start streaming chat (same as GodStreamChatAsync)
        var aiAgentStatusProxy = await GetProxyByRegionAsync(region);
        
        if (aiAgentStatusProxy != null)
        {
            var proxyId = aiAgentStatusProxy.GetPrimaryKey();
            
            // Add retry loop for proxy initialization status checking (same as GodStreamChatAsync)
            const int maxRetries = 10;
            const int retryDelayMs = 200;
            var retryCount = 0;
            ProxyInitStatus proxyInitStatus = ProxyInitStatus.NotInitialized;
            
            while (retryCount < maxRetries)
            {

                if (State.ProxyInitStatuses == null)
                {
                    Logger.LogDebug($"[GodChatGAgent][GodVoiceStreamChatAsync] History data ,Proxy has Initialized - ProxyId: {proxyId}, SessionId: {sessionId}");
                    break;
                }

                if (State.ProxyInitStatuses.IsNullOrEmpty() || !State.ProxyInitStatuses.TryGetValue(proxyId, out proxyInitStatus))
                {
                    retryCount++;
                    Logger.LogDebug($"[GodChatGAgent][GodVoiceStreamChatAsync] Proxy not started initializing - ProxyId: {proxyId}, Retry: {retryCount}/{maxRetries}, SessionId: {sessionId}");
                    
                    if (retryCount >= maxRetries)
                    {
                        throw new Exception("proxy:" + proxyId + "Not start initializ after " + maxRetries + " retries");
                    }
                    
                    await Task.Delay(retryDelayMs);
                    continue;
                }

                if (proxyInitStatus != ProxyInitStatus.Initialized)
                {
                    retryCount++;
                    Logger.LogDebug($"[GodChatGAgent][GodVoiceStreamChatAsync] Proxy not initialized - ProxyId: {proxyId}, Status: {proxyInitStatus}, Retry: {retryCount}/{maxRetries}, SessionId: {sessionId}");
                    
                    if (retryCount >= maxRetries)
                    {
                        throw new Exception("proxy:" + proxyId + "Not initialized after " + maxRetries + " retries, status:" + proxyInitStatus.ToString());
                    }
                    
                    await Task.Delay(retryDelayMs);
                    continue;
                }
                
                // Proxy is initialized, break out of retry loop
                Logger.LogDebug($"[GodChatGAgent][GodVoiceStreamChatAsync] Proxy initialization check successful - ProxyId: {proxyId}, Status: {proxyInitStatus}, Retries: {retryCount}, SessionId: {sessionId}");
                break;
            }
            
            Logger.LogDebug(
                $"[GodChatGAgent][GodVoiceStreamChatAsync] agent {aiAgentStatusProxy.GetPrimaryKey().ToString()}, session {sessionId.ToString()}, chat {chatId}");
            
            // Set default temperature for voice chat
            var settings = promptSettings ?? new ExecutionPromptSettings();
            settings.Temperature = "0.9";
            
            // Start streaming with voice context
            var promptMsg = message;
            switch (voiceLanguage)
            {
                case  VoiceLanguageEnum.English:
                    promptMsg += ".Requirement: Please reply in English.";
                    break;
                case VoiceLanguageEnum.Chinese:
                    promptMsg += ".Requirement: Please reply in Chinese.";
                    break;
                case VoiceLanguageEnum.Spanish:
                    promptMsg += ".Requirement: Please reply in Spanish.";
                    break;
                case VoiceLanguageEnum.Unset:
                    break;
                default:
                    break;
            }
            Logger.LogDebug($"[GodChatGAgent][GodVoiceStreamChatAsync] promptMsg: {promptMsg}");

            var result = await aiAgentStatusProxy.PromptWithStreamAsync(promptMsg, State.ChatHistory, settings,
                context: aiChatContextDto);
            if (!result)
            {
                Logger.LogError(
                    $"[GodChatGAgent][GodVoiceStreamChatAsync] Failed to initiate voice streaming response. {this.GetPrimaryKey().ToString()}");
            }

            if (!addToHistory)
            {
                totalStopwatch.Stop();
                Logger.LogDebug($"[GodChatGAgent][GodVoiceStreamChatAsync] TOTAL_Time (no history) - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {sessionId}");
                return string.Empty;
            }

            var historyStopwatch = Stopwatch.StartNew();
            var userVoiceMeta = new ChatMessageMeta
            {
                IsVoiceMessage = true,
                VoiceLanguage = voiceLanguage,
                VoiceParseSuccess = true,
                VoiceParseErrorMessage = null,
                VoiceDurationSeconds = voiceDurationSeconds
            };

            RaiseEvent(new GodAddChatHistoryLogEvent
            {
                ChatList = new List<ChatMessage>()
                {
                    new ChatMessage
                    {
                        ChatRole = ChatRole.User,
                        Content = message
                    }
                }
            });
                
            RaiseEvent(new AddChatMessageMetasLogEvent
            {
                ChatMessageMetas = new List<ChatMessageMeta> { userVoiceMeta }
            });

            historyStopwatch.Stop();
            Logger.LogDebug($"[GodChatGAgent][GodVoiceStreamChatAsync] AddToHistory - Duration: {historyStopwatch.ElapsedMilliseconds}ms, SessionId: {sessionId}");
        }
        else
        {
            Logger.LogDebug(
                $"[GodChatGAgent][GodVoiceStreamChatAsync] fallback to history agent, session {sessionId.ToString()}, chat {chatId}");
            // Fallback to non-streaming chat if no proxy available
            //await ChatAsync(message, promptSettings, aiChatContextDto);
        }

        // Voice synthesis and streaming handled in ChatMessageCallbackAsync
        totalStopwatch.Stop();
        Logger.LogDebug($"[GodChatGAgent][GodVoiceStreamChatAsync] TOTAL_Time - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {sessionId}");
        return string.Empty;
    }

    /// <summary>
    /// Parse AI response to extract main content and conversation suggestions
    /// </summary>
    /// <param name="fullResponse">Complete AI response text</param>
    /// <returns>Tuple of (main content, list of suggestions)</returns>
    private (string mainContent, List<string> suggestions) ParseResponseWithSuggestions(string fullResponse)
    {
        if (string.IsNullOrEmpty(fullResponse))
        {
            return (fullResponse, new List<string>());
        }

        // Pattern to match conversation suggestions block using precompiled regex
        var match = ChatRegexPatterns.ConversationSuggestionsBlock.Match(fullResponse);

        if (match.Success)
        {
            // Extract main content by removing everything from the start of [SUGGESTIONS] to the end
            var suggestionStartIndex = fullResponse.IndexOf("[SUGGESTIONS]", StringComparison.OrdinalIgnoreCase);
            var mainContent = suggestionStartIndex > 0
                ? fullResponse.Substring(0, suggestionStartIndex).Trim()
                : "";

            var suggestionSection = match.Groups[1].Value;
            var suggestions = ExtractNumberedItems(suggestionSection);

            Logger.LogDebug(
                $"[GodChatGAgent][ParseResponseWithSuggestions] Extracted {suggestions.Count} suggestions from response");
            return (mainContent, suggestions);
        }

        Logger.LogDebug("[GodChatGAgent][ParseResponseWithSuggestions] No suggestions found in response");
        return (fullResponse, new List<string>());
    }

    /// <summary>
    /// Extract numbered items from text (e.g., "1. item", "2. item", etc.)
    /// </summary>
    /// <param name="text">Text containing numbered items</param>
    /// <returns>List of extracted items</returns>
    private List<string> ExtractNumberedItems(string text)
    {
        var items = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return items;
        }

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            // Match numbered items like "1. content" or "1) content" using precompiled regex
            var match = ChatRegexPatterns.NumberedItem.Match(trimmedLine);
            if (match.Success)
            {
                var item = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(item))
                {
                    items.Add(item);
                }
            }
        }

        Logger.LogDebug($"[GodChatGAgent][ExtractNumberedItems] Extracted {items.Count} numbered items");
        return items;
    }

    /// <summary>
    /// Checks if the text contains a partial suggestions marker using prefix matching
    /// </summary>
    /// <param name="content">Content to check</param>
    /// <returns>True if a partial marker is found, false otherwise</returns>
    private static bool IsPartialSuggestionsMarker(ReadOnlySpan<char> content)
    {
        ReadOnlySpan<char> target = "[SUGGESTIONS]".AsSpan();

        // Check all occurrences of '[' in the content
        int startIndex = 0;
        while (true)
        {
            int index = content.Slice(startIndex).IndexOf('[');
            if (index == -1) break;

            index += startIndex; // Adjust to absolute position
            ReadOnlySpan<char> remaining = content.Slice(index);

            if (target.StartsWith(remaining, StringComparison.OrdinalIgnoreCase) &&
                remaining.Length < target.Length)
            {
                return true;
            }

            startIndex = index + 1;
        }

        return false;
    }
}