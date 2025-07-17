using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Aevatar.AI.Exceptions;
using Aevatar.AI.Feature.StreamSyncWoker;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.Dtos;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.AIGAgent.Dtos;
using Aevatar.GAgents.ChatAgent.Dtos;
using Aevatar.GAgents.ChatAgent.GAgent;
using GodGPT.GAgents.SpeechChat;
using Json.Schema.Generation;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Agents.ChatManager.Chat;

[Description("god chat agent")]
[GAgent]
[Reentrant]
public class GodChatGAgent : ChatGAgentBase<GodChatState, GodChatEventLog, EventBase, ChatConfigDto>, IGodChat
{
    private static readonly Dictionary<string, List<string>> RegionToLLMsMap = new Dictionary<string, List<string>>()
    {
        //"SkyLark-Pro-250415"
        { "CN", new List<string> { "BytePlusDeepSeekV3" } },
        { "DEFAULT", new List<string>() { "OpenAILast", "OpenAI" } }
    };

    private static readonly TimeSpan RequestRecoveryDelay = TimeSpan.FromSeconds(600);
    private const string DefaultRegion = "DEFAULT";
    private const string ProxyGPTModelName = "HyperEcho";
    private readonly ISpeechService _speechService;
    
    // Dictionary to maintain text accumulator for voice chat sessions
    // Key: chatId, Value: accumulated text buffer for sentence detection
    private static readonly Dictionary<string, StringBuilder> VoiceTextAccumulators = new();
    // Voice synthesis sentence detection
    // Extended sentence ending characters including punctuation marks for semantic pauses
    private static readonly List<char> SentenceEnders =
    [
        '.', '?', '!', '。', '？', '！', // Complete sentence endings
        ',', ';', ':', '，', '；', '：', // Semantic pause markers
        '\n', '\r'
    ];
    
    // Regular expressions for cleaning text before speech synthesis
    private static readonly Regex MarkdownLinkRegex = new Regex(@"\[([^\]]+)\]\([^\)]+\)", RegexOptions.Compiled);
    private static readonly Regex MarkdownBoldRegex = new Regex(@"\*\*([^*]+)\*\*", RegexOptions.Compiled);
    private static readonly Regex MarkdownItalicRegex = new Regex(@"\*([^*]+)\*", RegexOptions.Compiled);
    private static readonly Regex MarkdownHeaderRegex = new Regex(@"^#+\s*(.+)$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex MarkdownCodeBlockRegex = new Regex(@"```[\s\S]*?```", RegexOptions.Compiled);
    private static readonly Regex MarkdownInlineCodeRegex = new Regex(@"`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex MarkdownTableRegex = new Regex(@"\|.*?\|", RegexOptions.Compiled);
    private static readonly Regex MarkdownStrikethroughRegex = new Regex(@"~~([^~]+)~~", RegexOptions.Compiled);
    private static readonly Regex EmojiRegex = new Regex(@"[\u2600-\u26FF]|[\u2700-\u27BF]", RegexOptions.Compiled);

    public GodChatGAgent(ISpeechService speechService)
    {
        _speechService = speechService;
    }

    protected override async Task ChatPerformConfigAsync(ChatConfigDto configuration)
    {
        if (RegionToLLMsMap.IsNullOrEmpty())
        {
            Logger.LogDebug($"[GodChatGAgent][ChatPerformConfigAsync] LLMConfigs is null or empty.");
            return;
        }

        var proxyIds = await InitializeRegionProxiesAsync(DefaultRegion);
        Dictionary<string, List<Guid>> regionProxies = new();
        regionProxies[DefaultRegion] = proxyIds;
        RaiseEvent(new UpdateRegionProxiesLogEvent
        {
            RegionProxies = regionProxies
        });
        await ConfirmEvents();
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

    public async Task StreamChatWithSessionAsync(Guid sessionId, string sysmLLM, string content, string chatId,
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null)
    {
        Logger.LogDebug($"[GodChatGAgent][StreamChatWithSession] {sessionId.ToString()} start.");
        var userQuotaGAgent = GrainFactory.GetGrain<IUserQuotaGAgent>(State.ChatManagerGuid);
        var actionResultDto =
            await userQuotaGAgent.ExecuteActionAsync(sessionId.ToString(), State.ChatManagerGuid.ToString());
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
                Content = content
            });
            chatMessages.Add(new ChatMessage
            {
                ChatRole = ChatRole.Assistant,
                Content = actionResultDto.Message
            });
            RaiseEvent(new AddChatHistoryLogEvent
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
                SessionId = sessionId
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

        var sw = new Stopwatch();
        sw.Start();
        var configuration = GetConfiguration();
        await GodStreamChatAsync(sessionId, await configuration.GetSystemLLM(),
            await configuration.GetStreamingModeEnabled(),
            content, chatId, promptSettings, isHttpRequest, region);
        sw.Stop();
        Logger.LogDebug($"StreamChatWithSessionAsync {sessionId.ToString()} - step4,time use:{sw.ElapsedMilliseconds}");
    }

    public async Task StreamVoiceChatWithSessionAsync(Guid sessionId, string sysmLLM, string? voiceData,
        string fileName, string chatId,
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null,
        VoiceLanguageEnum voiceLanguage = VoiceLanguageEnum.English, double voiceDurationSeconds = 0.0)
    {
        var totalStopwatch = Stopwatch.StartNew();
        Logger.LogInformation($"[PERF][VoiceChat] {sessionId} START - file: {fileName}, size: {voiceData?.Length ?? 0} chars, language: {voiceLanguage}, duration: {voiceDurationSeconds}s");

        // Validate voiceData
        if (string.IsNullOrEmpty(voiceData) || voiceLanguage == VoiceLanguageEnum.Unset)
        {
            Logger.LogError($"[GodChatGAgent][StreamVoiceChatWithSession] {sessionId.ToString()} Invalid voice data");
            var errMsg = "Invalid voice message. Please try again.";
            if (voiceLanguage == VoiceLanguageEnum.Unset)
            {
                errMsg = "Please set voice language.";
            }

            var errorMessage = new ResponseStreamGodChat()
            {
                Response = errMsg,
                ChatId = chatId,
                IsLastChunk = true,
                SerialNumber = -99,
                SessionId = sessionId,
                ErrorCode = ChatErrorCode.ParamInvalid
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
                voiceParseErrorMessage = "Speech recognition service timeout";
                voiceContent = "Transcript Unavailable";
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
            voiceParseErrorMessage = ex.Message.Contains("timeout") ? "Speech recognition service timeout" :
                ex.Message.Contains("format") ? "Audio file corrupted or unsupported format" :
                "Speech recognition service unavailable";
            voiceContent = "Transcript Unavailable";
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

            RaiseEvent(new AddChatHistoryLogEvent
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
                Response = $"Voice message processing failed: {voiceParseErrorMessage}",
                ChatId = chatId,
                IsLastChunk = true,
                SerialNumber = -99,
                SessionId = sessionId,
                ErrorCode = ChatErrorCode.VoiceParsingFailed

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

            RaiseEvent(new AddChatHistoryLogEvent
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
                ErrorCode = errorCode
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
        if (State.Title.IsNullOrEmpty())
        {
            var sw = Stopwatch.StartNew();
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

            await ConfirmEvents();

            sw.Stop();
            var chatManagerGAgent = GrainFactory.GetGrain<IChatManagerGAgent>((Guid)State.ChatManagerGuid);
            await chatManagerGAgent.RenameChatTitleAsync(new RenameChatTitleEvent()
            {
                SessionId = sessionId,
                Title = title
            });
            Logger.LogDebug(
                $"StreamChatWithSessionAsync {sessionId.ToString()} - step3,time use:{sw.ElapsedMilliseconds}");
        }
    }

    public async Task<string> GodStreamChatAsync(Guid sessionId, string llm, bool streamingModeEnabled, string message,
        string chatId, ExecutionPromptSettings? promptSettings = null, bool isHttpRequest = false,
        string? region = null, bool addToHistory = true)
    {
        var configuration = GetConfiguration();
        var sysMessage = await configuration.GetPrompt();

        await LLMInitializedAsync(llm, streamingModeEnabled, sysMessage);

        var aiChatContextDto =
            CreateAIChatContext(sessionId, llm, streamingModeEnabled, message, chatId, promptSettings, isHttpRequest,
                region);

        var aiAgentStatusProxy = await GetProxyByRegionAsync(region);
        if (aiAgentStatusProxy != null)
        {
            Logger.LogDebug(
                $"[GodChatGAgent][GodStreamChatAsync] agent {aiAgentStatusProxy.GetPrimaryKey().ToString()}, session {sessionId.ToString()}, chat {chatId}");
            var settings = promptSettings ?? new ExecutionPromptSettings();
            settings.Temperature = "0.9";
            var result = await aiAgentStatusProxy.PromptWithStreamAsync(message, State.ChatHistory, settings,
                context: aiChatContextDto);
            if (!result)
            {
                Logger.LogError($"Failed to initiate streaming response. {this.GetPrimaryKey().ToString()}");
            }

            if (addToHistory)
            {
                RaiseEvent(new AddChatHistoryLogEvent
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

                RaiseEvent(new UpdateChatTimeEventLog
                {
                    ChatTime = DateTime.UtcNow
                });
                
                RaiseEvent(new AddChatMessageMetasLogEvent
                {
                    ChatMessageMetas = new List<ChatMessageMeta>()
                });

                await ConfirmEvents();
            }
        }
        else
        {
            Logger.LogDebug(
                $"[GodChatGAgent][GodStreamChatAsync] history agent, session {sessionId.ToString()}, chat {chatId}");
            await ChatAsync(message, promptSettings, aiChatContextDto);
        }

        return string.Empty;
    }

    private async Task LLMInitializedAsync(string llm, bool streamingModeEnabled, string sysMessage)
    {
        if (State.SystemLLM != llm || State.StreamingModeEnabled != streamingModeEnabled)
        {
            var initializeDto = new InitializeDto()
            {
                Instructions = sysMessage, LLMConfig = new LLMConfigDto() { SystemLLM = llm },
                StreamingModeEnabled = true, StreamingConfig = new StreamingConfig()
                {
                    BufferingSize = 32
                }
            };
            Logger.LogDebug(
                $"[GodChatGAgent][GodStreamChatAsync] Detail : {JsonConvert.SerializeObject(initializeDto)}");

            await InitializeAsync(initializeDto);
        }
    }

    private AIChatContextDto CreateAIChatContext(Guid sessionId, string llm, bool streamingModeEnabled,
        string message, string chatId, ExecutionPromptSettings? promptSettings = null, bool isHttpRequest = false,
        string? region = null)
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
                { "Message", message }, { "Region", region }
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
        Logger.LogDebug(
            $"[GodChatGAgent][GetProxyByRegionAsync] session {this.GetPrimaryKey().ToString()}, Region: {region}");
        if (string.IsNullOrWhiteSpace(region))
        {
            return await GetProxyByRegionAsync(DefaultRegion);
        }

        if (State.RegionProxies == null || !State.RegionProxies.TryGetValue(region, out var proxyIds) ||
            proxyIds.IsNullOrEmpty())
        {
            Logger.LogDebug(
                $"[GodChatGAgent][GetProxyByRegionAsync] session {this.GetPrimaryKey().ToString()}, No proxies found for region {region}, initializing.");
            proxyIds = await InitializeRegionProxiesAsync(region);
            Dictionary<string, List<Guid>> regionProxies = new()
            {
                { region, proxyIds }
            };
            RaiseEvent(new UpdateRegionProxiesLogEvent
            {
                RegionProxies = regionProxies
            });
            await ConfirmEvents();
        }

        foreach (var proxyId in proxyIds)
        {
            var proxy = GrainFactory.GetGrain<IAIAgentStatusProxy>(proxyId);
            if (await proxy.IsAvailableAsync())
            {
                return proxy;
            }
        }

        Logger.LogDebug(
            $"[GodChatGAgent][GetProxyByRegionAsync] session {this.GetPrimaryKey().ToString()}, No proxies initialized for region {region}");
        if (region == DefaultRegion)
        {
            Logger.LogWarning($"[GodChatGAgent][GetProxyByRegionAsync] No available proxies for region {region}.");
            return null;
        }

        return await GetProxyByRegionAsync(DefaultRegion);
    }

    private async Task<List<Guid>> InitializeRegionProxiesAsync(string region)
    {
        var llmsForRegion = GetLLMsForRegion(region);
        if (llmsForRegion.IsNullOrEmpty())
        {
            Logger.LogDebug(
                $"[GodChatGAgent][InitializeRegionProxiesAsync] session {this.GetPrimaryKey().ToString()}, initialized proxy for region {region}, LLM not config");
            return new List<Guid>();
        }
        
        var oldSystemPrompt = await GetConfiguration().GetPrompt();
        //Logger.LogDebug($"[GodChatGAgent][InitializeRegionProxiesAsync] {this.GetPrimaryKey().ToString()} old system prompt: {oldSystemPrompt}");

        var proxies = new List<Guid>();
        foreach (var llm in llmsForRegion)
        {
            var systemPrompt = State.PromptTemplate;
            if (llm != ProxyGPTModelName)
            {
                systemPrompt = $"{oldSystemPrompt} {systemPrompt} {GetCustomPrompt()}";
            }
            else
            {
                systemPrompt = $"{systemPrompt} {GetCustomPrompt()}";
            }
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

            proxies.Add(proxy.GetPrimaryKey());
            Logger.LogDebug(
                $"[GodChatGAgent][InitializeRegionProxiesAsync] session {this.GetPrimaryKey().ToString()}, initialized proxy for region {region} with LLM {llm}. id {proxy.GetPrimaryKey().ToString()}");
        }

        return proxies;
    }

    private List<string> GetLLMsForRegion(string region)
    {
        return RegionToLLMsMap.TryGetValue(region, out var llms) ? llms : new List<string>();
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
        if (State.SystemLLM != llm)
        {
            await InitializeAsync(new InitializeDto()
                { Instructions = State.PromptTemplate, LLMConfig = new LLMConfigDto() { SystemLLM = llm } });
        }

        var response = await ChatAsync(message, promptSettings);
        if (response is { Count: > 0 })
        {
            return response[0].Content!;
        }

        return string.Empty;
    }


    public async Task InitAsync(Guid ChatManagerGuid)
    {
        RaiseEvent(new SetChatManagerGuidEventLog
        {
            ChatManagerGuid = ChatManagerGuid
        });

        await ConfirmEvents();
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
            GodStreamChatAsync(contextDto.RequestId,
                (string)dictionary.GetValueOrDefault("LLM", systemLlm),
                (bool)dictionary.GetValueOrDefault("StreamingModeEnabled", true),
                (string)dictionary.GetValueOrDefault("Message", string.Empty),
                contextDto.ChatId, null, (bool)dictionary.GetValueOrDefault("IsHttpRequest", true),
                (string)dictionary.GetValueOrDefault("Region", null),
                false);
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
                    bool isVoiceChat = messageData.ContainsKey("IsVoiceChat") && (bool)messageData["IsVoiceChat"];
                    if (isVoiceChat)
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
            RaiseEvent(new AddChatHistoryLogEvent
            {
                ChatList = new List<ChatMessage>()
                {
                    new ChatMessage
                    {
                        ChatRole = ChatRole.Assistant,
                        Content = chatContent?.AggregationMsg
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

            var chatManagerGAgent = GrainFactory.GetGrain<IChatManagerGAgent>(State.ChatManagerGuid);
            var inviterId = await chatManagerGAgent.GetInviterAsync();
            
            if (inviterId != null && inviterId != Guid.Empty)
            {
                var invitationGAgent = GrainFactory.GetGrain<IInvitationGAgent>((Guid)inviterId);
                await invitationGAgent.ProcessInviteeChatCompletionAsync(State.ChatManagerGuid.ToString());
            }
        }

        var partialMessage = new ResponseStreamGodChat()
        {
            Response = chatContent.ResponseContent,
            ChatId = contextDto.ChatId,
            IsLastChunk = chatContent.IsLastChunk,
            SerialNumber = chatContent.SerialNumber,
            SessionId = contextDto.RequestId
        };

        // Check if this is a voice chat and handle real-time voice synthesis
        Logger.LogDebug($"[ChatMessageCallbackAsync] MessageId: '{contextDto.MessageId}', ResponseContent: '{chatContent.ResponseContent}'");
        
        if (!contextDto.MessageId.IsNullOrWhiteSpace())
        {
            var messageData = JsonConvert.DeserializeObject<Dictionary<string, object>>(contextDto.MessageId);
            var isVoiceChat = messageData.ContainsKey("IsVoiceChat") && (bool)messageData["IsVoiceChat"];
            
            Logger.LogDebug($"[ChatMessageCallbackAsync] IsVoiceChat: {isVoiceChat}, HasResponseContent: {!string.IsNullOrEmpty(chatContent.ResponseContent)}");

            if (isVoiceChat && !string.IsNullOrEmpty(chatContent.ResponseContent))
            {
                Logger.LogDebug($"[ChatMessageCallbackAsync] Entering voice chat processing logic");
                
                // Safe type conversion to handle both int and long from JSON deserialization
                var voiceLanguageValue = messageData.GetValueOrDefault("VoiceLanguage", 0);
                var voiceLanguage = (VoiceLanguageEnum)Convert.ToInt32(voiceLanguageValue);
                
                Logger.LogDebug($"[ChatMessageCallbackAsync] VoiceLanguage: {voiceLanguage}, ChatId: {contextDto.ChatId}");
                
                // Get or create text accumulator for this chat session
                if (!VoiceTextAccumulators.ContainsKey(contextDto.ChatId))
                {
                    VoiceTextAccumulators[contextDto.ChatId] = new StringBuilder();
                    Logger.LogDebug($"[ChatMessageCallbackAsync] Created new text accumulator for chat: {contextDto.ChatId}");
                }
                else
                {
                    Logger.LogDebug($"[ChatMessageCallbackAsync] Using existing text accumulator for chat: {contextDto.ChatId}");
                }
                
                var textAccumulator = VoiceTextAccumulators[contextDto.ChatId];
                textAccumulator.Append(chatContent.ResponseContent);
                
                Logger.LogDebug($"[ChatMessageCallbackAsync] Appended text: '{chatContent.ResponseContent}', IsLastChunk: {chatContent.IsLastChunk}");
                
                // Check for complete sentences in accumulated text
                var accumulatedText = textAccumulator.ToString();
                Logger.LogDebug($"[ChatMessageCallbackAsync] Total accumulated text: '{accumulatedText}'");
                
                var completeSentence = ExtractCompleteSentence(accumulatedText, textAccumulator, chatContent.IsLastChunk);
                
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
                                var voiceResult = await _speechService.TextToSpeechWithMetadataAsync(cleanedText, voiceLanguage);
                                
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
                    Logger.LogDebug($"[ChatMessageCallbackAsync] No complete sentence extracted from accumulated text: '{textAccumulator.ToString()}'");
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
            return;
        }

        await PushMessageToClientAsync(partialMessage);
    }

    public async Task<List<ChatMessage>?> ChatWithHistory(Guid sessionId, string systemLLM, string content, string chatId,
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
        
        var aiChatContextDto = CreateAIChatContext(sessionId, llm, streamingModeEnabled, content, chatId, promptSettings, isHttpRequest, region);
        var response = await aiAgentStatusProxy.ChatWithHistory(content,  State.ChatHistory, settings, aiChatContextDto);
        sw.Stop();
        Logger.LogDebug($"[GodChatGAgent][ChatWithHistory] {sessionId.ToString()}, response:{JsonConvert.SerializeObject(response)} - step4,time use:{sw.ElapsedMilliseconds}");
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

    protected override async Task OnAIGAgentActivateAsync(CancellationToken cancellationToken)
    {
    }

    protected sealed override void AIGAgentTransitionState(GodChatState state,
        StateLogEventBase<GodChatEventLog> @event)
    {
        base.AIGAgentTransitionState(state, @event);

        switch (@event)
        {
            case UpdateUserProfileGodChatEventLog updateUserProfileGodChatEventLog:
                if (State.UserProfile == null)
                {
                    State.UserProfile = new UserProfile();
                }

                State.UserProfile.Gender = updateUserProfileGodChatEventLog.Gender;
                State.UserProfile.BirthDate = updateUserProfileGodChatEventLog.BirthDate;
                State.UserProfile.BirthPlace = updateUserProfileGodChatEventLog.BirthPlace;
                State.UserProfile.FullName = updateUserProfileGodChatEventLog.FullName;
                break;
            case RenameChatTitleEventLog renameChatTitleEventLog:
                State.Title = renameChatTitleEventLog.Title;
                break;
            case SetChatManagerGuidEventLog setChatManagerGuidEventLog:
                State.ChatManagerGuid = setChatManagerGuidEventLog.ChatManagerGuid;
                break;
            case SetAIAgentIdLogEvent setAiAgentIdLogEvent:
                State.AIAgentIds = setAiAgentIdLogEvent.AIAgentIds;
                break;
            case UpdateRegionProxiesLogEvent updateRegionProxiesLogEvent:
                foreach (var regionProxy in updateRegionProxiesLogEvent.RegionProxies)
                {
                    if (State.RegionProxies == null)
                    {
                        State.RegionProxies = new Dictionary<string, List<Guid>>();
                    }

                    State.RegionProxies[regionProxy.Key] = regionProxy.Value;
                }
                break;
            case UpdateChatTimeEventLog updateChatTimeEventLog:
                if (State.FirstChatTime == null)
                {
                    State.FirstChatTime = updateChatTimeEventLog.ChatTime;
                }
                State.LastChatTime = updateChatTimeEventLog.ChatTime;
                break;
            case AddChatMessageMetasLogEvent addChatMessageMetasLogEvent:
                if (addChatMessageMetasLogEvent.ChatMessageMetas != null && addChatMessageMetasLogEvent.ChatMessageMetas.Any())
                {
                    // Calculate the starting index for new metadata based on current ChatHistory count
                    // minus the number of new metadata items we're adding
                    int newMetadataCount = addChatMessageMetasLogEvent.ChatMessageMetas.Count;
                    int targetStartIndex = Math.Max(0, State.ChatHistory.Count - newMetadataCount);
                    
                    // Ensure we have enough default metadata up to the target start index
                    while (State.ChatMessageMetas.Count < targetStartIndex)
                    {
                        State.ChatMessageMetas.Add(new ChatMessageMeta
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
                        State.ChatMessageMetas.Add(meta);
                    }
                }
                
                // Final sync: ensure ChatMessageMetas matches ChatHistory count
                while (State.ChatMessageMetas.Count < State.ChatHistory.Count)
                {
                    State.ChatMessageMetas.Add(new ChatMessageMeta
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
        var cleanText = System.Text.RegularExpressions.Regex.Replace(text, @"[^\w\u4e00-\u9fff]", "");
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
    private string ExtractCompleteSentence(string accumulatedText, StringBuilder textAccumulator, bool isLastChunk = false)
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
            if (SentenceEnders.Contains(accumulatedText[i]))
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
        cleanText = MarkdownLinkRegex.Replace(cleanText, "$1");

        // Remove bold and italic formatting
        cleanText = MarkdownBoldRegex.Replace(cleanText, "$1");
        cleanText = MarkdownItalicRegex.Replace(cleanText, "$1");

        // Remove strikethrough formatting
        cleanText = MarkdownStrikethroughRegex.Replace(cleanText, "$1");

        // Remove header formatting
        cleanText = MarkdownHeaderRegex.Replace(cleanText, "$1");

        // Replace code blocks with speech-friendly text
        cleanText = MarkdownCodeBlockRegex.Replace(cleanText, language == VoiceLanguageEnum.Chinese ? "代码块" : "code block");

        // Remove inline code formatting but keep content
        cleanText = MarkdownInlineCodeRegex.Replace(cleanText, "$1");

        // Remove table formatting
        cleanText = MarkdownTableRegex.Replace(cleanText, " ");

        // Remove emojis completely (they don't speech-synthesize well)
        cleanText = EmojiRegex.Replace(cleanText, "");

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
        cleanText = Regex.Replace(cleanText, @"\s+", " ").Trim();

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
        Logger.LogDebug(
            $"[GodChatGAgent][GodVoiceStreamChatAsync] {sessionId.ToString()} start with message: {message}, language: {voiceLanguage}");

        // Step 1: Get configuration and system message (same as GodStreamChatAsync)
        var configuration = GetConfiguration();
        var sysMessage = await configuration.GetPrompt();

        // Step 2: Initialize LLM if needed (same as GodStreamChatAsync)
        await LLMInitializedAsync(llm, streamingModeEnabled, sysMessage);

        // Step 3: Create voice chat context with voice-specific metadata
        var aiChatContextDto = CreateVoiceChatContext(sessionId, llm, streamingModeEnabled, message, chatId, 
            promptSettings, isHttpRequest, region, voiceLanguage, voiceDurationSeconds);

        // Step 4: Get AI proxy and start streaming chat (same as GodStreamChatAsync)
        var aiAgentStatusProxy = await GetProxyByRegionAsync(region);
        if (aiAgentStatusProxy != null)
        {
            Logger.LogDebug(
                $"[GodChatGAgent][GodVoiceStreamChatAsync] agent {aiAgentStatusProxy.GetPrimaryKey().ToString()}, session {sessionId.ToString()}, chat {chatId}");
            
            // Set default temperature for voice chat
            var settings = promptSettings ?? new ExecutionPromptSettings();
            settings.Temperature = "0.9";
            
            // Start streaming with voice context
            var result = await aiAgentStatusProxy.PromptWithStreamAsync(message, State.ChatHistory, settings,
                context: aiChatContextDto);
            if (!result)
            {
                Logger.LogError($"[GodChatGAgent][GodVoiceStreamChatAsync] Failed to initiate voice streaming response. {this.GetPrimaryKey().ToString()}");
            }

            if (!addToHistory)
            {
                return string.Empty;
            }

            var userVoiceMeta = new ChatMessageMeta
            {
                IsVoiceMessage = true,
                VoiceLanguage = voiceLanguage,
                VoiceParseSuccess = true,
                VoiceParseErrorMessage = null,
                VoiceDurationSeconds = voiceDurationSeconds
            };

            RaiseEvent(new AddChatHistoryLogEvent
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

            await ConfirmEvents();
        }
        else
        {
            Logger.LogDebug(
                $"[GodChatGAgent][GodVoiceStreamChatAsync] fallback to history agent, session {sessionId.ToString()}, chat {chatId}");
            // Fallback to non-streaming chat if no proxy available
            await ChatAsync(message, promptSettings, aiChatContextDto);
        }

        // Voice synthesis and streaming handled in ChatMessageCallbackAsync
        return string.Empty;
    }
}