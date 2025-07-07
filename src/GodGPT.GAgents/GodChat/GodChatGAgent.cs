using System.Diagnostics;
using System.Text;
using Aevatar.AI.Exceptions;
using Aevatar.AI.Feature.StreamSyncWoker;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.Dtos;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.AIGAgent.Dtos;
using Aevatar.GAgents.ChatAgent.Dtos;
using Aevatar.GAgents.ChatAgent.GAgent;
using GodGPT.GAgents.Common;
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
    private readonly ISpeechService _speechService;

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
        var userQuotaGrain =
            GrainFactory.GetGrain<IUserQuotaGrain>(CommonHelper.GetUserQuotaGAgentId(State.ChatManagerGuid));
        var actionResultDto =
            await userQuotaGrain.ExecuteActionAsync(sessionId.ToString(), State.ChatManagerGuid.ToString());
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
        Logger.LogDebug(
            $"[GodChatGAgent][StreamVoiceChatWithSession] {sessionId.ToString()} start with voice file: {fileName}, size: {voiceData?.Length ?? 0} bytes, voiceLanguage: {voiceLanguage}, duration: {voiceDurationSeconds}s");

        // Validate voiceData
        if (string.IsNullOrEmpty(voiceData))
        {
            Logger.LogError($"[GodChatGAgent][StreamVoiceChatWithSession] {sessionId.ToString()} Invalid voice data");
            var errorMessage = new ResponseStreamGodChat()
            {
                Response = "Invalid voice message. Please try again.",
                ChatId = chatId,
                IsLastChunk = true,
                SerialNumber = -99,
                SessionId = sessionId
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

        // Convert MP3 data to byte array (already byte[] but log for confirmation)
        var voiceDataBytes = Convert.FromBase64String(voiceData);
        Logger.LogDebug(
            $"[GodChatGAgent][StreamVoiceChatWithSession] {sessionId.ToString()} Processed MP3 data: {voiceDataBytes.Length} bytes");

        string voiceContent;
        var voiceParseSuccess = true;
        string? voiceParseErrorMessage = null;

        try
        {
            voiceContent = await _speechService.SpeechToTextAsync(voiceDataBytes, voiceLanguage);
            if (string.IsNullOrWhiteSpace(voiceContent))
            {
                voiceParseSuccess = false;
                voiceParseErrorMessage = "Speech recognition service timeout";
                voiceContent = "Transcript Unavailable";
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                $"[GodChatGAgent][StreamVoiceChatWithSession] {sessionId.ToString()} Voice parsing failed");
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

            // Add to state directly
            State.ChatMessageMetas.Add(chatMessageMeta);

            RaiseEvent(new AddChatHistoryLogEvent
            {
                ChatList = chatMessages
            });
            await ConfirmEvents();

            // Send error response
            var errorResponse = new ResponseStreamGodChat()
            {
                Response = $"Voice message processing failed: {voiceParseErrorMessage}",
                ChatId = chatId,
                IsLastChunk = true,
                SerialNumber = -99,
                SessionId = sessionId
            };

            if (isHttpRequest)
            {
                await PushMessageToClientAsync(errorResponse);
            }
            else
            {
                await PublishAsync(errorResponse);
            }

            return;
        }

        Logger.LogDebug(
            $"[GodChatGAgent][StreamVoiceChatWithSession] {sessionId.ToString()} Voice parsed successfully: {voiceContent}");

        var userQuotaGrain =
            GrainFactory.GetGrain<IUserQuotaGrain>(CommonHelper.GetUserQuotaGAgentId(State.ChatManagerGuid));
        var actionResultDto =
            await userQuotaGrain.ExecuteVoiceActionAsync(sessionId.ToString(), State.ChatManagerGuid.ToString());
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

            RaiseEvent(new AddChatHistoryLogEvent
            {
                ChatList = chatMessages
            });
            
            // Add metadata for the two messages: user voice message + assistant response
            // We need two metadata entries because we added two chat messages above
            State.ChatMessageMetas.Add(new ChatMessageMeta // For user's voice message
            {
                IsVoiceMessage = true,
                VoiceLanguage = voiceLanguage,
                VoiceParseSuccess = true,
                VoiceParseErrorMessage = null,
                VoiceDurationSeconds = voiceDurationSeconds
            });
            State.ChatMessageMetas.Add(new ChatMessageMeta // For assistant's error response
            {
                IsVoiceMessage = false,
                VoiceLanguage = VoiceLanguageEnum.English,
                VoiceParseSuccess = true,
                VoiceParseErrorMessage = null,
                VoiceDurationSeconds = 0.0
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

        Logger.LogDebug($"[GodChatGAgent][StreamVoiceChatWithSession] {sessionId.ToString()} - Validation passed");
        await SetSessionTitleAsync(sessionId, voiceContent);

        var sw = new Stopwatch();
        sw.Start();
        var configuration = GetConfiguration();
        await GodVoiceStreamChatAsync(sessionId, await configuration.GetSystemLLM(),
            await configuration.GetStreamingModeEnabled(),
            voiceContent, chatId, promptSettings, isHttpRequest, region, voiceLanguage, voiceDurationSeconds);
        sw.Stop();
        Logger.LogDebug(
            $"StreamVoiceChatWithSessionAsync {sessionId.ToString()} - step4,time use:{sw.ElapsedMilliseconds}");
    }

    private async Task SetSessionTitleAsync(Guid sessionId, string content)
    {
        var title = "";

        if (State.Title.IsNullOrEmpty())
        {
            var sw = Stopwatch.StartNew();
            title = string.Join(" ", content.Split(" ").Take(4));

            RaiseEvent(new RenameChatTitleEventLog()
            {
                Title = title
            });

            await ConfirmEvents();

            sw.Stop();
            IChatManagerGAgent chatManagerGAgent =
                GrainFactory.GetGrain<IChatManagerGAgent>((Guid)State.ChatManagerGuid);
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

        var proxies = new List<Guid>();
        foreach (var llm in llmsForRegion)
        {
            var proxy = GrainFactory.GetGrain<IAIAgentStatusProxy>(Guid.NewGuid());
            await proxy.ConfigAsync(new AIAgentStatusProxyConfig
            {
                Instructions = await GetConfiguration().GetPrompt(),
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
        else if (aiExceptionEnum != AIExceptionEnum.None)
        {
            Logger.LogError(
                $"[GodChatGAgent][ChatMessageCallbackAsync] stream error. sessionId {contextDto?.RequestId.ToString()}, chatId {contextDto?.ChatId}, error {aiExceptionEnum}");
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

        // Check if this is a voice chat and handle voice synthesis
        if (!contextDto.MessageId.IsNullOrWhiteSpace())
        {
            var messageData = JsonConvert.DeserializeObject<Dictionary<string, object>>(contextDto.MessageId);
            bool isVoiceChat = messageData.ContainsKey("IsVoiceChat") && (bool)messageData["IsVoiceChat"];

            if (isVoiceChat && !string.IsNullOrEmpty(chatContent.ResponseContent))
            {
                var voiceLanguage = (VoiceLanguageEnum)(int)messageData.GetValueOrDefault("VoiceLanguage", 0);

                // Use simple sentence detection for voice synthesis
                if (IsSentenceComplete(chatContent.ResponseContent))
                {
                    try
                    {
                        var voiceResult =
                            await _speechService.TextToSpeechWithMetadataAsync(chatContent.ResponseContent,
                                voiceLanguage);
                        partialMessage.AudioData = voiceResult.AudioData;
                        partialMessage.AudioMetadata = voiceResult.Metadata;
                        Logger.LogDebug(
                            $"[GodChatGAgent][ChatMessageCallbackAsync] Added voice data for: {chatContent.ResponseContent}");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex,
                            $"[GodChatGAgent][ChatMessageCallbackAsync] Voice synthesis failed for: {chatContent.ResponseContent}");
                    }
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
            case AddChatHistoryLogEvent addChatHistoryLogEvent:
                // Ensure ChatMessageMetas list has the same count as ChatHistory
                // Fill with default metadata for messages without explicit metadata
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
    /// Check if the text chunk contains a complete sentence
    /// </summary>
    /// <param name="text">Text to check</param>
    /// <returns>True if text ends with sentence punctuation</returns>
    private bool IsSentenceComplete(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        var trimmedText = text.Trim();
        if (trimmedText.Length < 3) // Minimum sentence length
            return false;

        // Check for sentence endings in multiple languages
        var sentenceEndings = new[] { '.', '!', '?', '。', '！', '？' };
        return sentenceEndings.Any(ending => trimmedText.EndsWith(ending));
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

            title = string.Join(" ", content.Split(" ").Take(4));

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

    public async Task<string> GodVoiceStreamChatAsync(Guid sessionId, string llm, bool streamingModeEnabled,
        string message,
        string chatId, ExecutionPromptSettings? promptSettings = null, bool isHttpRequest = false,
        string? region = null, VoiceLanguageEnum voiceLanguage = VoiceLanguageEnum.English,
        double voiceDurationSeconds = 0.0, bool addToHistory = true)
    {
        Logger.LogDebug(
            $"[GodChatGAgent][GodVoiceStreamChatAsync] {sessionId.ToString()} start with message: {message}, language: {voiceLanguage}");

        // Initialize sentence collector for real-time voice synthesis
        var sentenceCollector = new SentenceCollector(voiceLanguage, async (text, lang) =>
        {
            try
            {
                return await _speechService.TextToSpeechWithMetadataAsync(text, lang);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    $"[GodChatGAgent][GodVoiceStreamChatAsync] Voice synthesis failed for text: {text}");
                return (new byte[0],
                    new AudioMetadata { Duration = 0, SizeBytes = 0, Format = "mp3", LanguageType = lang });
            }
        });

        var stringBuilder = new StringBuilder();
        int serialNumber = 0;
        var contextDto = CreateVoiceChatContext(sessionId, llm, streamingModeEnabled, message, chatId, promptSettings,
            isHttpRequest, region, voiceLanguage, voiceDurationSeconds);

        var proxy = await GetProxyByRegionAsync(region);
        if (proxy == null)
        {
            throw new InvalidOperationException("No available AI proxy found");
        }

        var result = await proxy.PromptWithStreamAsync(message, State.ChatHistory, promptSettings, contextDto);
        if (!result)
        {
            Logger.LogError($"Failed to initiate voice streaming response. {this.GetPrimaryKey().ToString()}");
        }

        // Save user voice message metadata
        if (addToHistory)
        {
            var userChatMessage = new ChatMessage
            {
                ChatRole = ChatRole.User,
                Content = message
            };

            // Add metadata for voice message
            State.ChatMessageMetas.Add(new ChatMessageMeta
            {
                IsVoiceMessage = true,
                VoiceLanguage = voiceLanguage,
                VoiceParseSuccess = true,
                VoiceParseErrorMessage = null,
                VoiceDurationSeconds = voiceDurationSeconds
            });

            RaiseEvent(new AddChatHistoryLogEvent
            {
                ChatList = new List<ChatMessage> { userChatMessage }
            });
            await ConfirmEvents();
        }

        // For voice chat, the response processing happens in ChatMessageCallbackAsync
        // Return empty string as this is handled asynchronously
        return string.Empty;
    }
}