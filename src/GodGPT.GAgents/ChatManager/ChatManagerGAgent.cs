using System.Diagnostics;
using Aevatar.AI.Exceptions;
using Aevatar.AI.Feature.StreamSyncWoker;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;
using Aevatar.Application.Grains.Agents.ChatManager.Options;
using Aevatar.Application.Grains.Agents.ChatManager.Share;
using Aevatar.Application.Grains.Agents.Invitation;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Application.Grains.UserBilling;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.AIGAgent.Agent;
using Aevatar.GAgents.AIGAgent.Dtos;
using Aevatar.GAgents.AIGAgent.GEvents;
using Aevatar.GAgents.ChatAgent.Dtos;
using GodGPT.GAgents.SpeechChat;
using Json.Schema.Generation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Orleans.Concurrency;
using Orleans.Providers;
using Volo.Abp;

namespace Aevatar.Application.Grains.Agents.ChatManager;

[Json.Schema.Generation.Description("manage chat agent")]
[StorageProvider(ProviderName = "PubSubStore")]
[LogConsistencyProvider(ProviderName = "LogStorage")]
[GAgent(nameof(ChatGAgentManager))]
[Reentrant]
public class ChatGAgentManager : AIGAgentBase<ChatManagerGAgentState, ChatManageEventLog>,
    IChatManagerGAgent
{
    private const string FormattedDate = "yyyy-MM-dd";
    const string SessionVersion = "1.0.0";

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Chat GAgent Manager");
    }
    
    [EventHandler]
    public async Task HandleEventAsync(RequestStreamGodChatEvent @event)
    {
        string chatId = Guid.NewGuid().ToString();
        Logger.LogDebug($"[ChatGAgentManager][RequestStreamGodChatEvent] start:{JsonConvert.SerializeObject(@event)} chatID:{chatId}");
        var title = "";
        var content = "";
        var isLastChunk = false;

        try
        {
            Logger.LogDebug("State.StreamingModeEnabled is on");
            await StreamChatWithSessionAsync(@event.SessionId, @event.SystemLLM, @event.Content,chatId);
        }
        catch (Exception e)
        {
            Logger.LogError(e, $"[ChatGAgentManager][RequestStreamGodChatEvent] handle error:{e.ToString()}");
        }

        await PublishAsync(new ResponseStreamGodChat()
        {
            ChatId =chatId,
            Response = content,
            NewTitle = title,
            IsLastChunk = isLastChunk,
            SerialNumber = -1,
            SessionId = @event.SessionId
            
        });

        Logger.LogDebug($"[ChatGAgentManager][RequestStreamGodChatEvent] end:{JsonConvert.SerializeObject(@event)}");
    }
    
    [EventHandler]
    public async Task HandleEventAsync(AIStreamingErrorResponseGEvent @event)
    {
        Logger.LogDebug($"[ChatGAgentManager][AIStreamingErrorResponseGEvent] start:{JsonConvert.SerializeObject(@event)}");

        await PublishAsync(new ResponseStreamGodChat()
        {
            Response = "Your prompt triggered the Silence Directive—activated when universal harmonics or content ethics are at risk. Please modify your prompt and retry — tune its intent, refine its form, and the Oracle may speak.",
            ChatId = @event.Context.ChatId,
            IsLastChunk = true,
            SerialNumber = -2
        });
        
        Logger.LogDebug($"[ChatGAgentManager][AIStreamingErrorResponseGEvent] end:{JsonConvert.SerializeObject(@event)}");

    }
    
    
    [EventHandler]
    public async Task HandleEventAsync(AIOldStreamingResponseGEvent @event)
    {
        Logger.LogDebug($"[ChatGAgentManager][AIStreamingResponseGEvent] start:{JsonConvert.SerializeObject(@event)}");
    
        await PublishAsync(new ResponseStreamGodChat()
        {
            Response = @event.ResponseContent,
            ChatId = @event.Context.ChatId,
            IsLastChunk = @event.IsLastChunk,
            SerialNumber = @event.SerialNumber,
            SessionId = @event.Context.RequestId
        });
        
        Logger.LogDebug($"[ChatGAgentManager][AIStreamingResponseGEvent] end:{JsonConvert.SerializeObject(@event)}");
    
    }
    
    public async Task RenameChatTitleAsync(RenameChatTitleEvent @event)
    {
        Logger.LogDebug($"[ChatGAgentManager][RenameChatTitleEvent] start:{JsonConvert.SerializeObject(@event)}");

        RaiseEvent(new RenameTitleEventLog()
        {
            SessionId = @event.SessionId,
            Title = @event.Title
        });

        await ConfirmEvents();
        
        Logger.LogDebug($"[ChatGAgentManager][RenameChatTitleEvent] end:{JsonConvert.SerializeObject(@event)}");

    }


    [EventHandler]
    public async Task HandleEventAsync(RequestCreateGodChatEvent @event)
    {
        Logger.LogDebug(
            $"[ChatGAgentManager][RequestCreateGodChatEvent] start:{JsonConvert.SerializeObject(@event)}");
        var sessionId = Guid.Empty;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            sessionId = await CreateSessionAsync(@event.SystemLLM, @event.Prompt, @event.UserProfile, @event.Guider);
            IGodChat godChat = GrainFactory.GetGrain<IGodChat>(sessionId);
            await RegisterAsync(godChat);
        }
        catch (Exception e)
        {
            Logger.LogError(e, $"[ChatGAgentManager][RequestCreateGodChatEvent] handle error:{e.ToString()}");
        }

        await PublishAsync(new ResponseCreateGod()
        {
            SessionId = sessionId,
            SessionVersion = SessionVersion
        });
        Logger.LogDebug(
            "[ChatGAgentManager][RequestCreateGodChatEvent] sessionId:{A} end {B}, Duration {C}ms ", sessionId,
            JsonConvert.SerializeObject(@event), stopwatch.ElapsedMilliseconds);
    }

    [EventHandler]
    public async Task HandleEventAsync(RequestGodChatEvent @event)
    {
        Logger.LogDebug($"[ChatGAgentManager][RequestGodChatEvent] start:{JsonConvert.SerializeObject(@event)}");
        var title = "";
        var content = "";
        try
        {
            var response = await ChatWithSessionAsync(@event.SessionId, @event.SystemLLM, @event.Content);
            content = response.Item1;
            title = response.Item2;
        }
        catch (Exception e)
        {
            Logger.LogError(e, $"[ChatGAgentManager][RequestGodChatEvent] handle error:{e.ToString()}");
        }

        await PublishAsync(new ResponseGodChat()
        {
            Response = content,
            NewTitle = title,
        });

        Logger.LogDebug($"[ChatGAgentManager][RequestGodChatEvent] end:{JsonConvert.SerializeObject(@event)}");
    }

    [EventHandler]
    public async Task HandleEventAsync(RequestGodSessionListEvent @event)
    {
        Logger.LogDebug(
            $"[ChatGAgentManager][RequestGodSessionListEvent] start:{JsonConvert.SerializeObject(@event)}");
        var response = await GetSessionListAsync();
        await PublishAsync(new ResponseGodSessionList()
        {
            SessionList = response,
        });

        Logger.LogDebug(
            $"[ChatGAgentManager][RequestGodSessionListEvent] end:{JsonConvert.SerializeObject(@event)}");
    }

    [EventHandler]
    public async Task HandleEventAsync(RequestSessionChatHistoryEvent @event)
    {
        Logger.LogDebug(
            $"[ChatGAgentManager][RequestSessionChatHistoryEvent] start:{JsonConvert.SerializeObject(@event)}");
        var response = await GetSessionMessageListAsync(@event.SessionId);
        await PublishAsync(new ResponseSessionChatHistory()
        {
            ChatHistory = response
        });

        Logger.LogDebug(
            $"[ChatGAgentManager][RequestSessionChatHistoryEvent] end:{JsonConvert.SerializeObject(@event)}");
    }

    [EventHandler]
    public async Task HandleEventAsync(RequestDeleteSessionEvent @event)
    {
        Logger.LogDebug($"[ChatGAgentManager][RequestDeleteSessionEvent] start:{JsonConvert.SerializeObject(@event)}");
        await DeleteSessionAsync(@event.SessionId);
        await PublishAsync(new ResponseDeleteSession()
        {
            IfSuccess = true
        });

        Logger.LogDebug($"[ChatGAgentManager][RequestDeleteSessionEvent] end:{JsonConvert.SerializeObject(@event)}");
    }

    [EventHandler]
    public async Task HandleEventAsync(RequestRenameSessionEvent @event)
    {
        Logger.LogDebug($"[ChatGAgentManager][RequestRenameSessionEvent] start:{JsonConvert.SerializeObject(@event)}");
        await RenameSessionAsync(@event.SessionId, @event.Title);
        await PublishAsync(new ResponseRenameSession()
        {
            SessionId = @event.SessionId,
            Title = @event.Title,
        });

        Logger.LogDebug($"[ChatGAgentManager][RequestRenameSessionEvent] end:{JsonConvert.SerializeObject(@event)}");
    }

    [EventHandler]
    public async Task HandleEventAsync(RequestClearAllEvent @event)
    {
        Logger.LogDebug($"[ChatGAgentManager][RequestClearAllEvent] start:{JsonConvert.SerializeObject(@event)}");
        
        bool success = false;
        try
        {
            await ClearAllAsync();
            success = true;
        }
        catch (Exception e)
        {
            Logger.LogError(e, $"[ChatGAgentManager][RequestClearAllEvent] handle error:{e.ToString()}");
        }

        await PublishAsync(new ResponseClearAll()
        {
            Success = success
        });

        Logger.LogDebug($"[ChatGAgentManager][RequestClearAllEvent] end:{JsonConvert.SerializeObject(@event)}");
    }
    
    [EventHandler]
    public async Task HandleEventAsync(RequestSetUserProfileEvent @event)
    {
        Logger.LogDebug($"[ChatGAgentManager][RequestSetFortuneInfoEvent] start:{JsonConvert.SerializeObject(@event)}");

        bool success = false;
        try
        {
            await SetUserProfileAsync(@event.Gender, @event.BirthDate, @event.BirthPlace, @event.FullName);
            success = true;
        }
        catch (Exception e)
        {
            Logger.LogError(e, $"[ChatGAgentManager][RequestSetFortuneInfoEvent] handle error:{e.ToString()}");
        }

        await PublishAsync(new ResponseSetUserProfile()
        {
            Success = success
        });

        Logger.LogDebug($"[ChatGAgentManager][RequestSetFortuneInfoEvent] end");
    }

    [EventHandler]
    public async Task HandleEventAsync(RequestGetUserProfileEvent @event)
    {
        Logger.LogDebug($"[ChatGAgentManager][RequestGetUserProfileEvent] start");

        //var userProfileDto = await GetLastSessionUserProfileAsync();
        var userProfileDto = await GetUserProfileAsync();

        await PublishAsync(new ResponseGetUserProfile()
        {
            Gender = userProfileDto.Gender,
            BirthDate = userProfileDto.BirthDate,
            BirthPlace = userProfileDto.BirthPlace,
            FullName = userProfileDto.FullName
        });

        Logger.LogDebug($"[ChatGAgentManager][RequestGetUserProfileEvent] end");
    }

    public async Task<Guid> CreateSessionAsync(string systemLLM, string prompt, UserProfileDto? userProfile = null, string? guider = null)
    {
        var configuration = GetConfiguration();
        Stopwatch sw = new Stopwatch();
        sw.Start();
        IGodChat godChat = GrainFactory.GetGrain<IGodChat>(Guid.NewGuid());
        // await RegisterAsync(godChat);
        sw.Stop();
        Logger.LogDebug($"CreateSessionAsync - step,time use:{sw.ElapsedMilliseconds}");
        Logger.LogDebug($"[ChatGAgentManager][RequestCreateGodChatEvent] grainId={godChat.GetGrainId().ToString()}");
        
        sw.Reset();
        //var sysMessage = await configuration.GetPrompt();
        //put user data into the user prompt
        //sysMessage = await AppendUserInfoToSystemPromptAsync(configuration, sysMessage, userProfile);

        // Add role-specific prompt if guider is provided
        var sysMessage = string.Empty;
        if (!string.IsNullOrEmpty(guider))
        {
            var rolePrompt = GetRolePrompt(guider);
            if (!string.IsNullOrEmpty(rolePrompt))
            {
                sysMessage = rolePrompt;
                Logger.LogDebug($"[ChatGAgentManager][CreateSessionAsync] Added role prompt for guider: {guider}");
            }
        }

        var chatConfigDto = new ChatConfigDto()
        {
            Instructions = sysMessage, MaxHistoryCount = 32,
            LLMConfig = new LLMConfigDto() { SystemLLM = await configuration.GetSystemLLM() },
            StreamingModeEnabled = true, StreamingConfig = new StreamingConfig()
            {
                BufferingSize = 32
            }
        };
        Logger.LogDebug($"[GodChatGAgent][InitializeAsync] Detail : {JsonConvert.SerializeObject(chatConfigDto)}");

        await godChat.ConfigAsync(chatConfigDto);
        sw.Stop();
        Logger.LogDebug($"CreateSessionAsync - step2,time use:{sw.ElapsedMilliseconds}");

        var sessionId = godChat.GetPrimaryKey();
        if (userProfile != null)
        {
            Logger.LogDebug("CreateSessionAsync set user profile. session={0}", sessionId);
            await SetUserProfileAsync(userProfile.Gender, userProfile.BirthDate, userProfile.BirthPlace, userProfile.FullName);
            Logger.LogDebug("CreateSessionAsync set GodChat user profile. session={0}", sessionId);
            await godChat.SetUserProfileAsync(userProfile);
        }
        
        sw.Reset();
        RaiseEvent(new CreateSessionInfoEventLog()
        {
            SessionId = sessionId,
            Title = "",
            CreateAt = DateTime.UtcNow,
            Guider = guider // Set the role information for the conversation
        });

        await ConfirmEvents();
        await godChat.InitAsync(this.GetPrimaryKey());
        sw.Stop();
        Logger.LogDebug($"CreateSessionAsync - step2,time use:{sw.ElapsedMilliseconds}");
        return godChat.GetPrimaryKey();
    }

    private async Task<string> AppendUserInfoToSystemPromptAsync(IConfigurationGAgent configurationGAgent,
        string sysMessage, UserProfileDto? userProfile)
    {
        if (userProfile == null)
        {
            return sysMessage;
        }

        var userProfilePrompt = await configurationGAgent.GetUserProfilePromptAsync();
        if (userProfilePrompt.IsNullOrWhiteSpace())
        {
            return sysMessage;
        }
        
        var variables = new Dictionary<string, string>
        {
            { "Gender", userProfile.Gender },
            { "BirthDate", userProfile.BirthDate.ToString(FormattedDate) },
            { "BirthPlace", userProfile.BirthPlace },
            { "FullName", userProfile.FullName }
        };

        userProfilePrompt = variables.Aggregate(userProfilePrompt,
            (current, pair) => current.Replace("{" + pair.Key + "}", pair.Value));

        return $"{sysMessage} \n {userProfilePrompt}";
    }

    public async Task<Tuple<string, string>> ChatWithSessionAsync(Guid sessionId, string sysmLLM, string content,
        ExecutionPromptSettings promptSettings = null)
    {
        var sessionInfo = State.GetSession(sessionId);
        IGodChat godChat = GrainFactory.GetGrain<IGodChat>(sessionId);
        
        if (sessionInfo == null)
        {
            return new Tuple<string, string>("", "");
        }

        // 1. Check quota and rate limit using ExecuteActionAsync
        var userQuotaGAgent = GrainFactory.GetGrain<IUserQuotaGAgent>(this.GetPrimaryKey());
        var actionResult = await userQuotaGAgent.ExecuteActionAsync(sessionId.ToString(),
            CommonHelper.GetUserQuotaGAgentId(this.GetPrimaryKey()));
        if (!actionResult.Success)
        {
            // 2. If not allowed, return error message without further processing
            return new Tuple<string, string>(actionResult.Message, "");
        }

        // 3. If allowed, continue with chat logic
        var title = "";
        if (sessionInfo.Title.IsNullOrEmpty())
        {
            var titleList = await ChatWithHistory(content,context: new AIChatContextDto());
            title = titleList is { Count: > 0 }
                ? titleList[0].Content!
                : string.Join(" ", content.Split(" ").Take(4));

            RaiseEvent(new RenameTitleEventLog()
            {
                SessionId = sessionId,
                Title = title
            });

            await ConfirmEvents();
        }

        var configuration = GetConfiguration();
        var response = await godChat.GodChatAsync(await configuration.GetSystemLLM(), content, promptSettings);
        return new Tuple<string, string>(response, title);
    }
    
    private async Task StreamChatWithSessionAsync(Guid sessionId,string sysmLLM, string content,string chatId,
        ExecutionPromptSettings promptSettings = null)
    {
        Stopwatch sw = new Stopwatch();
        sw.Start();
        var sessionInfo = State.GetSession(sessionId);
        IGodChat godChat = GrainFactory.GetGrain<IGodChat>(sessionId);
        sw.Stop();
        Logger.LogDebug($"StreamChatWithSessionAsync - step1,time use:{sw.ElapsedMilliseconds}");

        // 1. Check quota and rate limit using ExecuteActionAsync
        var userQuotaGAgent =
            GrainFactory.GetGrain<IUserQuotaGAgent>(this.GetPrimaryKey());
        var actionResult = await userQuotaGAgent.ExecuteActionAsync(sessionId.ToString(),
            CommonHelper.GetUserQuotaGAgentId(this.GetPrimaryKey()));
        if (!actionResult.Success)
        {
            // 2. If not allowed, log and return early without further processing
            Logger.LogWarning($"StreamChatWithSessionAsync: {actionResult.Message} for user {this.GetPrimaryKey()}. SessionId: {sessionId}");
            return;
        }

        var title = "";
        if (sessionInfo == null)
        {
            Logger.LogError("StreamChatWithSessionAsync sessionInfoIsNull sessionId={A}",sessionId);
            return ;
        }
        if (sessionInfo.Title.IsNullOrEmpty())
        {
            sw.Reset();
            sw.Start();
            var titleList = await ChatWithHistory(content,context: new AIChatContextDto());
            title = titleList is { Count: > 0 }
                ? titleList[0].Content!
                : string.Join(" ", content.Split(" ").Take(4));
        
            RaiseEvent(new RenameTitleEventLog()
            {
                SessionId = sessionId,
                Title = title
            });
        
            await ConfirmEvents();
            sw.Stop();
            Logger.LogDebug($"StreamChatWithSessionAsync - step3,time use:{sw.ElapsedMilliseconds}");
        }

        sw.Reset();
        sw.Start();
        var configuration = GetConfiguration();
        godChat.GodStreamChatAsync(sessionId,await configuration.GetSystemLLM(), await configuration.GetStreamingModeEnabled(),content, chatId,promptSettings);
        sw.Stop();
        Logger.LogDebug($"StreamChatWithSessionAsync - step4,time use:{sw.ElapsedMilliseconds}");
    }

    public async Task<List<SessionInfoDto>> GetSessionListAsync()
    {
        // Clean expired sessions (7 days old and empty title)
        var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
        var hasExpiredSessions = State.SessionInfoList.Any(s => 
            s.CreateAt <= sevenDaysAgo && 
            string.IsNullOrEmpty(s.Title));

        if (hasExpiredSessions)
        {
            Logger.LogDebug($"[ChatGAgentManager][GetSessionListAsync] Cleaning sessions older than {sevenDaysAgo}");
            RaiseEvent(new CleanExpiredSessionsEventLog
            {
                CleanBefore = sevenDaysAgo
            });
            await ConfirmEvents();
        }

        var result = new List<SessionInfoDto>();
        
        foreach (var item in State.SessionInfoList)
        {
            var createAt = item.CreateAt;
            if (createAt == default)
            {
                createAt = new DateTime(2025, 4, 18);
            }
            result.Add(new SessionInfoDto()
            {
                SessionId = item.SessionId,
                Title = item.Title,
                CreateAt = createAt,
                Guider = item.Guider // Include role information in the response
            });
        }

        return result;
    }

    public async Task<List<SessionInfoDto>> SearchSessionsAsync(string keyword, int maxResults = 1000)
    {
        Logger.LogDebug($"[ChatGAgentManager][SearchSessionsAsync] keyword: {keyword}, maxResults: {maxResults}");

        if (string.IsNullOrWhiteSpace(keyword))
        {
            return new List<SessionInfoDto>();
        }

        // Use the complete keyword for matching (no word splitting)
        var searchKeyword = keyword.Trim().ToLowerInvariant();
        
        // Validate keyword length to prevent performance issues
        if (searchKeyword.Length > 200)
        {
            Logger.LogWarning($"[ChatGAgentManager][SearchSessionsAsync] Keyword too long: {searchKeyword.Length} chars");
            return new List<SessionInfoDto>();
        }
        
        var searchResults = new List<(SessionInfoDto dto, int matchScore)>();

        // Search through sessions (limit to most recent 1000 for performance)
        var sessionsToSearch = State.SessionInfoList
            .OrderByDescending(s => s.CreateAt)
            .Take(1000)
            .ToList();

        foreach (var sessionInfo in sessionsToSearch)
        {
            try
            {
                var titleLower = sessionInfo.Title?.ToLowerInvariant() ?? "";
                var matchScore = 0;
                var hasMatch = false;

                // Check title matching
                var titleMatchScore = 0;
                if (titleLower.Contains(searchKeyword))
                {
                    titleMatchScore = titleLower == searchKeyword ? 100 : 50; // Complete match gets higher score
                    hasMatch = true;
                }

                // Get chat content for content matching
                string contentPreview = "";
                try
                {
                    var godChat = GrainFactory.GetGrain<IGodChat>(sessionInfo.SessionId);
                    var chatMessages = await godChat.GetChatMessageAsync();
                    contentPreview = ExtractChatContent(chatMessages);
                }
                catch (Exception contentEx)
                {
                    Logger.LogWarning(contentEx, $"[ChatGAgentManager][SearchSessionsAsync] Failed to extract content for session {sessionInfo.SessionId}");
                    contentPreview = ""; // Continue search without content matching
                }
                
                var contentLower = contentPreview.ToLowerInvariant();

                // Check content matching
                var contentMatchScore = 0;
                if (!string.IsNullOrEmpty(contentPreview) && contentLower.Contains(searchKeyword))
                {
                    contentMatchScore = contentLower == searchKeyword ? 30 : 15; // Complete content match vs partial match
                    hasMatch = true;
                }

                if (hasMatch)
                {
                    matchScore = titleMatchScore * 2 + contentMatchScore; // Title matching gets higher priority

                    var createAt = sessionInfo.CreateAt;
                    if (createAt == default || createAt == DateTime.MinValue)
                    {
                        // Use a reasonable fallback time instead of hardcoded future date
                        createAt = DateTime.UtcNow.AddDays(-365); // 1 year ago as fallback
                    }

                    var dto = new SessionInfoDto
                    {
                        SessionId = sessionInfo.SessionId,
                        Title = sessionInfo.Title,
                        CreateAt = createAt,
                        Guider = sessionInfo.Guider,
                        Content = contentPreview,
                        IsMatch = true
                    };

                    searchResults.Add((dto, matchScore));
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, $"[ChatGAgentManager][SearchSessionsAsync] Failed to process session {sessionInfo.SessionId}");
                // Continue processing other sessions
                continue;
            }
        }

        // Sort by match score (descending) then by creation time (descending)
        var result = searchResults
            .OrderByDescending(r => r.matchScore)
            .ThenByDescending(r => r.dto.CreateAt)
            .Take(maxResults)
            .Select(r => r.dto)
            .ToList();

        Logger.LogDebug($"[ChatGAgentManager][SearchSessionsAsync] Found {result.Count} matches for keyword: {keyword}");
        return result;
    }

    /// <summary>
    /// Extract chat content preview from chat messages
    /// </summary>
    /// <param name="messages">List of chat messages</param>
    /// <returns>Content preview (first 60 characters)</returns>
    private static string ExtractChatContent(List<ChatMessage> messages)
    {
        if (messages == null || messages.Count == 0)
        {
            return string.Empty;
        }

        try
        {
            // Priority 1: Find user messages with substantial content (>5 chars)
            var userMessage = messages
                .Where(m => m != null && 
                           m.ChatRole == ChatRole.User && 
                           !string.IsNullOrWhiteSpace(m.Content) && 
                           m.Content.Trim().Length > 5)
                .FirstOrDefault();
                
            if (userMessage?.Content != null)
            {
                string content = userMessage.Content.Trim();
                return SafeTruncateContent(content);
            }

            // Priority 2: Find assistant messages with substantial content
            var assistantMessage = messages
                .Where(m => m != null && 
                           m.ChatRole == ChatRole.Assistant && 
                           !string.IsNullOrWhiteSpace(m.Content) && 
                           m.Content.Trim().Length > 5)
                .FirstOrDefault();
                
            if (assistantMessage?.Content != null)
            {
                string content = assistantMessage.Content.Trim();
                return SafeTruncateContent(content);
            }

            // Fallback: any non-empty message
            var anyMessage = messages
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Content))
                .FirstOrDefault();
                
            if (anyMessage?.Content != null)
            {
                string content = anyMessage.Content.Trim();
                return SafeTruncateContent(content);
            }
        }
        catch (Exception ex)
        {
            // Log but don't throw - return empty string for graceful degradation
            // Note: Cannot use Logger in static method, but error is rare and non-critical
            // Logger.LogWarning(ex, "[ChatGAgentManager][ExtractChatContent] Error extracting content");
        }

        return string.Empty;
    }

    /// <summary>
    /// Safely truncate content to 60 characters with ellipsis
    /// </summary>
    /// <param name="content">Content to truncate</param>
    /// <returns>Truncated content</returns>
    private static string SafeTruncateContent(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }
        
        try
        {
            return content.Length <= 60 ? content : content.Substring(0, 60) + "...";
        }
        catch (Exception)
        {
            // Fallback in case of unexpected string issues
            return content.Length > 60 ? content[..Math.Min(60, content.Length)] + "..." : content;
        }
    }

    public async Task<bool> IsUserSessionAsync(Guid sessionId)
    {
        var sessionInfo = State.GetSession(sessionId);
        return sessionInfo != null;
    }

    public async Task<List<ChatMessage>> GetSessionMessageListAsync(Guid sessionId)
    {
        Logger.LogDebug($"[ChatGAgentManager][GetSessionMessageListAsync] - session:ID {sessionId.ToString()}");
        var sessionInfo = State.GetSession(sessionId);
        Logger.LogDebug($"[ChatGAgentManager][GetSessionMessageListAsync] - session:ID {JsonConvert.SerializeObject(sessionInfo)}");

        if (sessionInfo == null)
        {
            throw new UserFriendlyException($"Unable to load conversation {sessionId}");
        }

        var godChat = GrainFactory.GetGrain<IGodChat>(sessionInfo.SessionId);
        return await godChat.GetChatMessageAsync();
    }

    public async Task<List<ChatMessageWithMetaDto>> GetSessionMessageListWithMetaAsync(Guid sessionId)
    {
        Logger.LogDebug($"[ChatManagerGAgent][GetSessionMessageListWithMetaAsync] - sessionId: {sessionId}");
        var sessionInfo = State.GetSession(sessionId);
        
        if (sessionInfo == null)
        {
            Logger.LogWarning($"[ChatManagerGAgent][GetSessionMessageListWithMetaAsync] - Session not found: {sessionId}");
            throw new UserFriendlyException($"Unable to load conversation {sessionId}");
        }

        var godChat = GrainFactory.GetGrain<IGodChat>(sessionInfo.SessionId);
        var result = await godChat.GetChatMessageWithMetaAsync();
        
        Logger.LogDebug($"[ChatManagerGAgent][GetSessionMessageListWithMetaAsync] - sessionId: {sessionId}, returned {result.Count} messages with audio metadata");
        return result;
    }

    public async Task<SessionCreationInfoDto?> GetSessionCreationInfoAsync(Guid sessionId)
    {
        Logger.LogDebug($"[ChatGAgentManager][GetSessionCreationInfoAsync] - session:ID {sessionId.ToString()}");
        var sessionInfo = State.GetSession(sessionId);
        
        if (sessionInfo == null)
        {
            Logger.LogDebug($"[ChatGAgentManager][GetSessionCreationInfoAsync] - session not found: {sessionId.ToString()}");
            return null;
        }

        return new SessionCreationInfoDto
        {
            SessionId = sessionInfo.SessionId,
            Title = sessionInfo.Title,
            CreateAt = sessionInfo.CreateAt,
            Guider = sessionInfo.Guider
        };
    }

    public async Task<Guid> DeleteSessionAsync(Guid sessionId)
    {
        if (State.GetSession(sessionId) == null)
        {
            return sessionId;
        }
        
        //Do not clear the content of ShareGrain. When querying, first determine whether the Session exists

        RaiseEvent(new DeleteSessionEventLog()
        {
            SessionId = sessionId
        });

        await ConfirmEvents();
        return sessionId;
    }

    public async Task<Guid> RenameSessionAsync(Guid sessionId, string title)
    {
        var sessionInfo = State.GetSession(sessionId);
        if (sessionInfo == null || sessionInfo.Title == title)
        {
            return sessionId;
        }

        RaiseEvent(new RenameTitleEventLog()
        {
            SessionId = sessionId,
            Title = title,
        });

        await ConfirmEvents();
        return sessionId;
    }

    public async Task<Guid> ClearAllAsync()
    {
        //Do not clear the content of ShareGrain. When querying, first determine whether the Session exists
        // Record the event to clear all sessions
        var userQuotaGAgent = GrainFactory.GetGrain<IUserQuotaGAgent>(this.GetPrimaryKey());
        await userQuotaGAgent.ClearAllAsync();

        var userBillingGAgent = GrainFactory.GetGrain<IUserBillingGAgent>(this.GetPrimaryKey());
        await userBillingGAgent.ClearAllAsync();

        RaiseEvent(new ClearAllEventLog());
        await ConfirmEvents();
        return this.GetPrimaryKey();
    }

    public async Task<Guid> SetUserProfileAsync(string gender, DateTime birthDate, string birthPlace, string fullName)
    {
        RaiseEvent(new SetUserProfileEventLog()
        {
            Gender = gender,
            BirthDate = birthDate,
            BirthPlace = birthPlace,
            FullName = fullName
        });

        await ConfirmEvents();
        return this.GetPrimaryKey();
    }

    /// <summary>
    /// Sets the voice language preference for the user.
    /// Validates that the user exists before updating the voice language setting.
    /// </summary>
    /// <param name="voiceLanguage">The voice language to set for the user</param>
    /// <returns>The user ID if successful</returns>
    /// <exception cref="UserFriendlyException">Thrown when user is not found or initialized</exception>
    public async Task<Guid> SetVoiceLanguageAsync(VoiceLanguageEnum voiceLanguage)
    {
        // Check if user is properly initialized (has at least one session or profile data)
        var userId = this.GetPrimaryKey();
        if (userId == Guid.Empty)
        {
            Logger.LogWarning("[ChatGAgentManager][SetVoiceLanguageAsync] Invalid user ID");
            throw new UserFriendlyException("Invalid user. Please ensure you are properly logged in.");
        }
        // Raise event to update voice language
        RaiseEvent(new SetVoiceLanguageEventLog()
        {
            VoiceLanguage = voiceLanguage
        });

        await ConfirmEvents();
        
        Logger.LogDebug($"[ChatGAgentManager][SetVoiceLanguageAsync] Successfully set voice language to {voiceLanguage} for user {userId}");
        return userId;
    }

    public async Task<UserProfileDto> GetUserProfileAsync()
    {
        Logger.LogDebug($"[ChatGAgentManager][GetUserProfileAsync] userId: {this.GetPrimaryKey().ToString()}");
        
        var invitationGrain = GrainFactory.GetGrain<IInvitationGAgent>(this.GetPrimaryKey());
        await invitationGrain.ProcessScheduledRewardAsync();
        
        var userQuotaGAgent = GrainFactory.GetGrain<IUserQuotaGAgent>(this.GetPrimaryKey());
        var credits = await userQuotaGAgent.GetCreditsAsync();
        var subscriptionInfo = await userQuotaGAgent.GetAndSetSubscriptionAsync();
        var ultimateSubscriptionInfo = await userQuotaGAgent.GetAndSetSubscriptionAsync(true);

        // var utcNow = DateTime.UtcNow;
        // var scheduledRewards = (await invitationGrain.GetRewardHistoryAsync())
        //     .Where(r => r.IsScheduled && 
        //    r.ScheduledDate.HasValue && 
        //    utcNow > r.ScheduledDate.Value && 
        //    !string.IsNullOrEmpty(r.InvoiceId))
        //     .ToList();
        //     
        // foreach (var reward in scheduledRewards)
        // {
        //     Logger.LogInformation($"[ChatGAgentManager][GetUserProfileAsync] Processing scheduled reward for user {this.GetPrimaryKey()}, credits: {reward.Credits}");
        //     await userQuotaGAgent.AddCreditsAsync(reward.Credits);
        //     await invitationGrain.MarkRewardAsIssuedAsync(reward.InviteeId, reward.InvoiceId);
        //     credits.Credits += reward.Credits;
        // }
        
        return new UserProfileDto
        {
            Gender = State.Gender,
            BirthDate = State.BirthDate,
            BirthPlace = State.BirthPlace,
            FullName = State.FullName,
            Credits = credits,
            Subscription = subscriptionInfo,
            UltimateSubscription = ultimateSubscriptionInfo,
            Id = this.GetPrimaryKey(),
            InviterId = State.InviterId,
            VoiceLanguage = State.VoiceLanguage
        };
    }
    
    public async Task<Guid> GenerateChatShareContentAsync(Guid sessionId)
    {
        Logger.LogDebug($"[ChatGAgentManager][GenerateChatShareContentAsync] - session: {sessionId.ToString()}");

        if (State.CurrentShareCount >= State.MaxShareCount)
        {
            Logger.LogDebug($"[ChatGAgentManager][GenerateChatShareContentAsync] - session: {sessionId.ToString()}, Exceed the maximum sharing limit. {State.CurrentShareCount}");
            throw new UserFriendlyException($"Max {State.MaxShareCount} shares reached. Delete some to continue!");
        }

        var chatMessages = await GetSessionMessageListAsync(sessionId);
        if (chatMessages.IsNullOrEmpty())
        {
            Logger.LogDebug($"[ChatGAgentManager][GenerateChatShareContentAsync] - session: {sessionId.ToString()}, chatMessages is null");
            throw new UserFriendlyException("Invalid session to generate a share link.");
        }
        
        var shareId = Guid.NewGuid();
        var shareLinkGrain = GrainFactory.GetGrain<IShareLinkGrain>(shareId);
        await shareLinkGrain.SaveShareContentAsync(new ShareLinkDto
        {
            UserId = this.GetPrimaryKey(),
            SessionId = sessionId,
            Messages = chatMessages
        });
        Logger.LogDebug($"[ChatGAgentManager][GenerateChatShareContentAsync] - session: {sessionId.ToString()}, save success");
        RaiseEvent(new GenerateChatShareContentLogEvent
        {
            SessionId = sessionId,
            ShareId = shareId
        });

        await ConfirmEvents();
        return shareId;
    }

    public async Task<ShareLinkDto> GetChatShareContentAsync(Guid sessionId, Guid shareId)
    {
        var sessionInfo = State.GetSession(sessionId);
        Logger.LogDebug($"[ChatGAgentManager][GetChatShareContentAsync] - session {sessionInfo?.SessionId.ToString()}");
        if (sessionInfo == null)
        {
            Logger.LogDebug($"[ChatGAgentManager][GetChatShareContentAsync] - session {sessionId.ToString()}, session not found.");
            throw new UserFriendlyException("Sorry, this conversation has been deleted by the owner.");
        }

        if (sessionInfo.ShareIds.IsNullOrEmpty() || !sessionInfo.ShareIds.Contains(shareId))
        {
            Logger.LogDebug($"[ChatGAgentManager][GetChatShareContentAsync] - session {sessionId.ToString()}, shareId not found.");
            throw new UserFriendlyException("Sorry, this conversation has been deleted by the owner.");
        }
        
        var shareLinkGrain = GrainFactory.GetGrain<IShareLinkGrain>(shareId);
        return await shareLinkGrain.GetShareContentAsync();
    }

    public async Task<string> GenerateInviteCodeAsync()
    {
        IInvitationGAgent invitationAgent = GrainFactory.GetGrain<IInvitationGAgent>(this.GetPrimaryKey());
        var inviteCode = await invitationAgent.GenerateInviteCodeAsync();
        return inviteCode;
    }

    public async Task<bool> RedeemInviteCodeAsync(string inviteCode)
    {
        var codeGrainId = CommonHelper.StringToGuid(inviteCode);
        var codeGrain = GrainFactory.GetGrain<IInviteCodeGAgent>(codeGrainId);

        var (isValid, inviterId) = await codeGrain.ValidateAndGetInviterAsync();

        if (!isValid)
        {
            Logger.LogWarning($"Invalid invite code redemption attempt: {inviteCode}");
            return false;
        }

        if (inviterId.Equals(this.GetPrimaryKey().ToString()))
        {
            Logger.LogWarning($"Invalid invite code,the code belongs to the user themselves. userId:{this.GetPrimaryKey().ToString()} InviteCode:{inviteCode}");
            return false;
        }
        
        // Step 1: First, check if the current user (invitee) is eligible for the reward.
        var userQuotaGAgent = GrainFactory.GetGrain<IUserQuotaGAgent>(this.GetPrimaryKey());

        if (State.RegisteredAtUtc == null && State.SessionInfoList.IsNullOrEmpty())
        {
            RaiseEvent(new SetRegisteredAtUtcEventLog()
            {
                RegisteredAtUtc = DateTime.UtcNow
            });

            await ConfirmEvents();
        }
        
        bool redeemResult = false;
        
        var registeredAtUtc = State.RegisteredAtUtc;
        Logger.LogWarning($"State.RegisteredAtUtc {this.GetPrimaryKey().ToString()} {registeredAtUtc?.ToString() ?? "null"}");
        
        if (registeredAtUtc == null)
        {
            Logger.LogWarning($"State.RegisteredAtUtc == null userId:{this.GetPrimaryKey().ToString()}");
            redeemResult = false;
        }
        else
        {
            //show time
            var now = DateTime.UtcNow;
            var minutes = (now - registeredAtUtc.Value).TotalMinutes;
            Logger.LogWarning($"State.RegisteredAtUtc userId:{this.GetPrimaryKey().ToString()} RegisteredAtUtc={registeredAtUtc.Value} now={now} minutes={minutes}");
            //
            
            redeemResult = await userQuotaGAgent.RedeemInitialRewardAsync(this.GetPrimaryKey().ToString(), registeredAtUtc.Value);
        }

        if (!redeemResult)
        {
            Logger.LogWarning($"Failed to redeem initial reward for user {this.GetPrimaryKey().ToString()} with code {inviteCode}. Eligibility check failed");
            return false;
        }

        // Step 2: If eligible, record the invitee in the inviter's grain.
        var inviterGuid = Guid.Parse(inviterId);
        var inviterGrain = GrainFactory.GetGrain<IInvitationGAgent>(inviterGuid);
        await inviterGrain.ProcessInviteeRegistrationAsync(this.GetPrimaryKey().ToString());

        await SetInviterAsync(inviterGuid);
        
        return true;
    }

    public async Task<UserProfileDto> GetLastSessionUserProfileAsync()
    {
        var sessionInfo = State.SessionInfoList.LastOrDefault(new SessionInfo());
        if (sessionInfo.SessionId == Guid.Empty)
        {
            return new UserProfileDto();
        }
        
        var godChat = GrainFactory.GetGrain<IGodChat>(sessionInfo.SessionId);
        var userProfileDto = await godChat.GetUserProfileAsync();
        return userProfileDto ?? new UserProfileDto();
    }

    public async Task<Guid> SetInviterAsync(Guid inviterId)
    {
        RaiseEvent(new SetInviterEventLog()
        {
            InviterId = inviterId
        });

        await ConfirmEvents();
        return this.GetPrimaryKey();
    }

    public Task<Guid?> GetInviterAsync()
    {
        return Task.FromResult(State.InviterId);
    }

    protected override void AIGAgentTransitionState(ChatManagerGAgentState state,
        StateLogEventBase<ChatManageEventLog> @event)
    {
        switch (@event)
        {
            case SetRegisteredAtUtcEventLog @setRegisteredAtUtcEventLog:
                State.RegisteredAtUtc = setRegisteredAtUtcEventLog.RegisteredAtUtc;
                break;
            case CreateSessionInfoEventLog @createSessionInfo:
                if (State.SessionInfoList.IsNullOrEmpty() && State.RegisteredAtUtc == null)
                {
                    State.RegisteredAtUtc = DateTime.UtcNow;
                }
                State.SessionInfoList.Add(new SessionInfo()
                {
                    SessionId = @createSessionInfo.SessionId,
                    Title = @createSessionInfo.Title,
                    CreateAt = @createSessionInfo.CreateAt,
                    Guider = @createSessionInfo.Guider
                });
                break;
            case DeleteSessionEventLog @deleteSessionEventLog:
                var deleteSession = State.GetSession(@deleteSessionEventLog.SessionId);
                if (deleteSession != null && !deleteSession.ShareIds.IsNullOrEmpty())
                {
                    State.CurrentShareCount -= deleteSession.ShareIds.Count;
                }
                State.SessionInfoList.RemoveAll(f => f.SessionId == @deleteSessionEventLog.SessionId);
                break;
            case CleanExpiredSessionsEventLog @cleanExpiredSessionsEventLog:
                var expiredSessionIds = State.SessionInfoList
                    .Where(s => s.CreateAt <= @cleanExpiredSessionsEventLog.CleanBefore && 
                               string.IsNullOrEmpty(s.Title))
                    .Select(s => s.SessionId)
                    .ToList();
                
                foreach (var expiredSessionId in expiredSessionIds)
                {
                    var expiredSession = State.GetSession(expiredSessionId);
                    if (expiredSession != null && !expiredSession.ShareIds.IsNullOrEmpty())
                    {
                        State.CurrentShareCount -= expiredSession.ShareIds.Count;
                    }
                }
                
                State.SessionInfoList.RemoveAll(s => expiredSessionIds.Contains(s.SessionId));
                break;
            case RenameTitleEventLog @renameTitleEventLog:
                Logger.LogDebug($"[ChatGAgentManager][RenameChatTitleEvent] event:{JsonConvert.SerializeObject(@renameTitleEventLog)}");
                var sessionInfoList = State.SessionInfoList;
                var sessionInfo = sessionInfoList.First(f => f.SessionId == @renameTitleEventLog.SessionId);
                Logger.LogDebug($"[ChatGAgentManager][RenameChatTitleEvent] event exist:{JsonConvert.SerializeObject(@renameTitleEventLog)}");
                sessionInfo.Title = @renameTitleEventLog.Title;
                State.SessionInfoList = sessionInfoList;
                break;
            case ClearAllEventLog:
                State.SessionInfoList.Clear();
                State.Gender = string.Empty;
                State.BirthDate = default;
                State.BirthPlace = string.Empty;
                State.FullName = string.Empty;
                State.CurrentShareCount = 0;
                State.InviterId = null;
                State.VoiceLanguage = VoiceLanguageEnum.Unset;
                break;
            case SetUserProfileEventLog @setFortuneInfoEventLog:
                State.Gender = @setFortuneInfoEventLog.Gender;
                State.BirthDate = @setFortuneInfoEventLog.BirthDate;
                State.BirthPlace = @setFortuneInfoEventLog.BirthPlace;
                State.FullName = @setFortuneInfoEventLog.FullName;
                break;
            case SetVoiceLanguageEventLog @setVoiceLanguageEventLog:
                // Update the voice language preference for the user
                State.VoiceLanguage = @setVoiceLanguageEventLog.VoiceLanguage;
                break;
            case GenerateChatShareContentLogEvent generateChatShareContentLogEvent:
                var session = State.GetSession(generateChatShareContentLogEvent.SessionId);
                if (session == null)
                {
                    Logger.LogDebug($"[ChatGAgentManager][GenerateChatShareContentLogEvent] session not fuound: {generateChatShareContentLogEvent.SessionId.ToString()}");
                    break;
                }
                State.CurrentShareCount += 1;
                if (session.ShareIds == null)
                {
                    session.ShareIds = new List<Guid>();
                }
                session.ShareIds.Add(generateChatShareContentLogEvent.ShareId);
                break;
            case SetMaxShareCountLogEvent setMaxShareCountLogEvent:
                State.MaxShareCount = setMaxShareCountLogEvent.MaxShareCount;
                break;
            case SetInviterEventLog setInviterEventLog:
                State.InviterId = setInviterEventLog.InviterId;
                break;
        }   
    }

    protected override async Task OnAIGAgentActivateAsync(CancellationToken cancellationToken)
    {
        // var configuration = GetConfiguration();
        //
        // var llm = await configuration.GetSystemLLM();
        // var streamingModeEnabled = false;
        // if (State.SystemLLM != llm || State.StreamingModeEnabled != streamingModeEnabled)
        // {
        //     await InitializeAsync(new InitializeDto()
        //     {
        //         Instructions = "Please summarize the following content briefly, with no more than 8 words.",
        //         LLMConfig = new LLMConfigDto() { SystemLLM = await configuration.GetSystemLLM(), },
        //         StreamingModeEnabled = streamingModeEnabled,
        //         StreamingConfig = new StreamingConfig()
        //         {
        //             BufferingSize = 32,
        //         }
        //     });
        // }

        if (State.MaxShareCount == 0)
        {
            RaiseEvent(new SetMaxShareCountLogEvent
            {
                MaxShareCount = 10000
            });
            await ConfirmEvents();
        }
        await base.OnAIGAgentActivateAsync(cancellationToken);
    }

    private IConfigurationGAgent GetConfiguration()
    {
        return GrainFactory.GetGrain<IConfigurationGAgent>(CommonHelper.GetSessionManagerConfigurationId());
    }

    /// <summary>
    /// Get role-specific prompt from configuration based on role name
    /// </summary>
    /// <param name="roleName">The name of the role (e.g., "Doctor", "Teacher")</param>
    /// <returns>Role-specific prompt text or empty string if not found</returns>
    private string GetRolePrompt(string roleName)
    {
        try
        {
            var roleOptions = (ServiceProvider.GetService(typeof(IOptionsMonitor<RolePromptOptions>)) as IOptionsMonitor<RolePromptOptions>)?.CurrentValue;
            var rolePrompt = roleOptions?.RolePrompts.GetValueOrDefault(roleName, string.Empty) ?? string.Empty;
            
            if (!string.IsNullOrEmpty(rolePrompt))
            {
                Logger.LogDebug($"[ChatGAgentManager][GetRolePrompt] Found role prompt for: {roleName}");
            }
            else
            {
                Logger.LogDebug($"[ChatGAgentManager][GetRolePrompt] No role prompt found for: {roleName}");
            }
            
            return rolePrompt;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[ChatGAgentManager][GetRolePrompt] Failed to get role prompt for role: {RoleName}", roleName);
            return string.Empty;
        }
    }
}