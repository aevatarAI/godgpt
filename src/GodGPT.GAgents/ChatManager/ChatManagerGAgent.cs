using System.Diagnostics;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;
using Aevatar.Application.Grains.Agents.ChatManager.Options;
using Aevatar.Application.Grains.Agents.ChatManager.Share;
using Aevatar.Application.Grains.Agents.Invitation;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Observability;
using Aevatar.Application.Grains.Common.Service;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Application.Grains.UserBilling;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.AIGAgent.Dtos;
using Aevatar.GAgents.AIGAgent.GEvents;
using Aevatar.GAgents.ChatAgent.Dtos;
using GodGPT.GAgents.SpeechChat;
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
public class ChatGAgentManager : GAgentBase<ChatManagerGAgentState, ChatManageEventLog>,
    IChatManagerGAgent
{
    private const string FormattedDate = "yyyy-MM-dd";
    const string SessionVersion = "1.0.0";
    private readonly ILocalizationService _localizationService;

    public ChatGAgentManager(ILocalizationService localizationService)
    {
        _localizationService = localizationService;

    }
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
    
    
    // [EventHandler]
    // public async Task HandleEventAsync(AIOldStreamingResponseGEvent @event)
    // {
    //     Logger.LogDebug($"[ChatGAgentManager][AIStreamingResponseGEvent] start:{JsonConvert.SerializeObject(@event)}");
    //
    //     await PublishAsync(new ResponseStreamGodChat()
    //     {
    //         Response = @event.ResponseContent,
    //         ChatId = @event.Context.ChatId,
    //         IsLastChunk = @event.IsLastChunk,
    //         SerialNumber = @event.SerialNumber,
    //         SessionId = @event.Context.RequestId
    //     });
    //     
    //     Logger.LogDebug($"[ChatGAgentManager][AIStreamingResponseGEvent] end:{JsonConvert.SerializeObject(@event)}");
    //
    // }
    
    public async Task RenameChatTitleAsync(RenameChatTitleEvent @event)
    {
        Logger.LogDebug($"[ChatGAgentManager][RenameChatTitleEvent] start:{JsonConvert.SerializeObject(@event)}");

        RaiseEvent(new RenameTitleEventLog()
        {
            SessionId = @event.SessionId,
            Title = @event.Title
        });

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
        Logger.LogDebug($"[ChatManagerGAgent][CreateSessionAsync] Start - UserId: {this.GetPrimaryKey()}");
        
        var configuration = GetConfiguration();
        Stopwatch sw = new Stopwatch();
        sw.Start();
        IGodChat godChat = GrainFactory.GetGrain<IGodChat>(Guid.NewGuid());
        // await RegisterAsync(godChat);
        sw.Stop();
        Logger.LogDebug($"CreateSessionAsync - step,time use:{sw.ElapsedMilliseconds}");
        Logger.LogDebug($"[ChatManagerGAgent][RequestCreateGodChatEvent] grainId={godChat.GetGrainId().ToString()}");
        
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
                Logger.LogDebug($"[ChatManagerGAgent][CreateSessionAsync] Added role prompt for guider: {guider}");
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
        
        // Record user activity metrics for retention analysis (before RaiseEvent to ensure proper deduplication)
        await RecordUserActivityMetricsAsync();
        RaiseEvent(new CreateSessionInfoEventLog()
        {
            SessionId = sessionId,
            Title = "",
            CreateAt = DateTime.UtcNow,
            Guider = guider // Set the role information for the conversation
        });
        
        var initStopwatch = Stopwatch.StartNew();
        await godChat.InitAsync(this.GetPrimaryKey());
        initStopwatch.Stop();
        Logger.LogDebug($"[ChatManagerGAgent][CreateSessionAsync] InitAsync completed - Duration: {initStopwatch.ElapsedMilliseconds}ms");
        
        sw.Stop();
        Logger.LogDebug($"CreateSessionAsync - step2,time use:{sw.ElapsedMilliseconds}");
        return godChat.GetPrimaryKey();
    }

    /// <summary>
    /// Records user activity metrics for retention analysis
    /// Application-level deduplication: Check SessionInfoList to determine if today's activity was already reported
    /// </summary>
    private async Task RecordUserActivityMetricsAsync()
    {
        try
        {
            var today = DateTime.UtcNow.Date;
            
            // Check SessionInfoList for the last session's creation time
            var lastSession = State.SessionInfoList?.LastOrDefault();
            if (lastSession != null && lastSession.CreateAt.Date == today)
            {
                // Today already has session creation, skip duplicate reporting
                Logger.LogDebug("[GodChatGAgent][RecordUserActivityMetricsAsync] {UserId} User activity metrics already recorded today", this.GetPrimaryKey().ToString());
                return;
            }
            
            // First session creation today, need to record metrics
            var todayString = today.ToString("yyyy-MM-dd");
            var userRegistrationDate = State.RegisteredAtUtc?.ToString("yyyy-MM-dd") ?? todayString;

            // Get user membership level
            var membershipLevel = await DetermineMembershipLevelAsync();

            // Calculate days since registration
            var registrationDate = State.RegisteredAtUtc?.Date ?? today;
            var daysSinceRegistration = (int)(today - registrationDate).TotalDays;

            // Record user activity metrics (ensure each user is counted only once per day)
            UserLifecycleTelemetryMetrics.RecordUserActivityByCohort(
                daysSinceRegistration: daysSinceRegistration,
                membershipLevel: membershipLevel,
                logger: Logger);
                
            Logger.LogInformation("User activity metrics recorded: daysSinceRegistration={DaysSinceRegistration}, membership={MembershipLevel}", 
                daysSinceRegistration, membershipLevel);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to record user activity metrics for retention analysis");
        }
    }

    /// <summary>
    /// Determines user membership level based on UserQuotaGAgent subscription information
    /// Returns one of 9 levels defined in UserMembershipTier constants:
    /// Free, PremiumDay, PremiumWeek, PremiumMonth, PremiumYear,
    /// UltimateDay, UltimateWeek, UltimateMonth, UltimateYear
    /// </summary>
    private async Task<string> DetermineMembershipLevelAsync()
    {
        try
        {
            var userQuotaGrain = GrainFactory.GetGrain<IUserQuotaGAgent>(this.GetPrimaryKey());
            
            // Check Ultimate subscription first (higher priority)
            var ultimateSubscription = await userQuotaGrain.GetSubscriptionAsync(ultimate: true);
            if (ultimateSubscription.IsActive)
            {
                return ultimateSubscription.PlanType switch
                {
                    PlanType.Day => UserMembershipTier.UltimateDay,
                    PlanType.Week => UserMembershipTier.UltimateWeek,
                    PlanType.Month => UserMembershipTier.UltimateMonth,
                    PlanType.Year => UserMembershipTier.UltimateYear,
                    _ => UserMembershipTier.UltimateMonth // Default fallback for unknown plan types
                };
            }
            
            // Check Premium subscription
            var premiumSubscription = await userQuotaGrain.GetSubscriptionAsync(ultimate: false);
            if (premiumSubscription.IsActive)
            {
                return premiumSubscription.PlanType switch
                {
                    PlanType.Day => UserMembershipTier.PremiumDay,
                    PlanType.Week => UserMembershipTier.PremiumWeek,
                    PlanType.Month => UserMembershipTier.PremiumMonth,
                    PlanType.Year => UserMembershipTier.PremiumYear,
                    _ => UserMembershipTier.PremiumMonth // Default fallback for unknown plan types
                };
            }
            
            // No active subscription
            return UserMembershipTier.Free;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to determine membership level from UserQuotaGAgent, defaulting to 'free'");
            return UserMembershipTier.Free;
        }
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
        throw new Exception("The method is outdated");
    }
    
    private async Task StreamChatWithSessionAsync(Guid sessionId,string sysmLLM, string content,string chatId,
        ExecutionPromptSettings promptSettings = null)
    {
        throw new Exception("The method is outdated");

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
            var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
            var parameters = new Dictionary<string, string>
            {
                ["SessionId"] = sessionId.ToString()
            };
            var localizedMessage = _localizationService.GetLocalizedException(ExceptionMessageKeys.InvalidConversation,language, parameters);
            Logger.LogWarning($"[ChatManagerGAgent][GetSessionMessageListWithMetaAsync] - Session not found: {sessionId} ,language:{language}");
            throw new UserFriendlyException(localizedMessage);
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
        
        // Sync latest subscription status from UserBillingGAgent before getting user profile
        // This ensures Google Pay and other platform subscriptions are up-to-date
        var userBillingGAgent = GrainFactory.GetGrain<IUserBillingGAgent>(this.GetPrimaryKey());
        var activeSubscriptionStatus = await userBillingGAgent.GetActiveSubscriptionStatusAsync();
        
        Logger.LogDebug($"[ChatGAgentManager][GetUserProfileAsync] Active subscription status - Apple: {activeSubscriptionStatus.HasActiveAppleSubscription}, Stripe: {activeSubscriptionStatus.HasActiveStripeSubscription}, GooglePlay: {activeSubscriptionStatus.HasActiveGooglePlaySubscription}");
        
        var userQuotaGAgent = GrainFactory.GetGrain<IUserQuotaGAgent>(this.GetPrimaryKey());
        
        // Check if we need to sync subscription status between UserBillingGAgent and UserQuotaGAgent
        // This is particularly important for Google Pay subscriptions that might not be reflected in UserQuotaGAgent yet
        await SyncSubscriptionStatusIfNeeded(userBillingGAgent, userQuotaGAgent, activeSubscriptionStatus);
        
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
        var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
        if (State.CurrentShareCount >= State.MaxShareCount)
        {
            Logger.LogDebug($"[ChatGAgentManager][GenerateChatShareContentAsync] - session: {sessionId.ToString()}, Exceed the maximum sharing limit. {State.CurrentShareCount}");
            var parameters = new Dictionary<string, string>
            {
                ["MaxShareCount"] = State.MaxShareCount.ToString()
            };
            var localizedMessage = _localizationService.GetLocalizedException(ExceptionMessageKeys.SharesReached,language, parameters);

            throw new UserFriendlyException(localizedMessage);
        }

        var chatMessages = await GetSessionMessageListAsync(sessionId);
        if (chatMessages.IsNullOrEmpty())
        {
            Logger.LogDebug($"[ChatGAgentManager][GenerateChatShareContentAsync] - session: {sessionId.ToString()}, chatMessages is null");
            var localizedMessage = _localizationService.GetLocalizedException(ExceptionMessageKeys.InvalidSession, language);
            throw new UserFriendlyException(localizedMessage);
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
        var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
        if (sessionInfo == null)
        {
            Logger.LogDebug($"[ChatGAgentManager][GetChatShareContentAsync] - session {sessionId.ToString()}, session not found.");
            var localizedMessage = _localizationService.GetLocalizedException(ExceptionMessageKeys.ConversationDeleted,language);
            throw new UserFriendlyException(localizedMessage);
        }

        if (sessionInfo.ShareIds.IsNullOrEmpty() || !sessionInfo.ShareIds.Contains(shareId))
        {
            Logger.LogDebug($"[ChatGAgentManager][GetChatShareContentAsync] - session {sessionId.ToString()}, shareId not found.");
            var localizedMessage = _localizationService.GetLocalizedException(ExceptionMessageKeys.ConversationDeleted,language);
            throw new UserFriendlyException(localizedMessage);
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

    protected override void GAgentTransitionState(ChatManagerGAgentState state,
        StateLogEventBase<ChatManageEventLog> @event)
    {
        switch (@event)
        {
            case SetRegisteredAtUtcEventLog @setRegisteredAtUtcEventLog:
                state.RegisteredAtUtc = setRegisteredAtUtcEventLog.RegisteredAtUtc;
                break;
            case CreateSessionInfoEventLog @createSessionInfo:
                if (state.SessionInfoList.IsNullOrEmpty() && state.RegisteredAtUtc == null)
                {
                    state.RegisteredAtUtc = DateTime.UtcNow;
                }
                state.SessionInfoList.Add(new SessionInfo()
                {
                    SessionId = @createSessionInfo.SessionId,
                    Title = @createSessionInfo.Title,
                    CreateAt = @createSessionInfo.CreateAt,
                    Guider = @createSessionInfo.Guider
                });
                break;
            case DeleteSessionEventLog @deleteSessionEventLog:
                var deleteSession = state.GetSession(@deleteSessionEventLog.SessionId);
                if (deleteSession != null && !deleteSession.ShareIds.IsNullOrEmpty())
                {
                    state.CurrentShareCount -= deleteSession.ShareIds.Count;
                }
                state.SessionInfoList.RemoveAll(f => f.SessionId == @deleteSessionEventLog.SessionId);
                break;
            case CleanExpiredSessionsEventLog @cleanExpiredSessionsEventLog:
                var expiredSessionIds = state.SessionInfoList
                    .Where(s => s.CreateAt <= @cleanExpiredSessionsEventLog.CleanBefore && 
                               string.IsNullOrEmpty(s.Title))
                    .Select(s => s.SessionId)
                    .ToList();
                
                foreach (var expiredSessionId in expiredSessionIds)
                {
                    var expiredSession = state.GetSession(expiredSessionId);
                    if (expiredSession != null && !expiredSession.ShareIds.IsNullOrEmpty())
                    {
                        state.CurrentShareCount -= expiredSession.ShareIds.Count;
                    }
                }
                
                state.SessionInfoList.RemoveAll(s => expiredSessionIds.Contains(s.SessionId));
                break;
            case RenameTitleEventLog @renameTitleEventLog:
                Logger.LogDebug($"[ChatGAgentManager][RenameChatTitleEvent] event:{JsonConvert.SerializeObject(@renameTitleEventLog)}");
                var sessionInfoList = state.SessionInfoList;
                var sessionInfo = sessionInfoList.First(f => f.SessionId == @renameTitleEventLog.SessionId);
                Logger.LogDebug($"[ChatGAgentManager][RenameChatTitleEvent] event exist:{JsonConvert.SerializeObject(@renameTitleEventLog)}");
                sessionInfo.Title = @renameTitleEventLog.Title;
                state.SessionInfoList = sessionInfoList;
                break;
            case ClearAllEventLog:
                state.SessionInfoList.Clear();
                state.Gender = string.Empty;
                state.BirthDate = default;
                state.BirthPlace = string.Empty;
                state.FullName = string.Empty;
                state.CurrentShareCount = 0;
                state.InviterId = null;
                state.VoiceLanguage = VoiceLanguageEnum.Unset;
                break;
            case SetUserProfileEventLog @setFortuneInfoEventLog:
                state.Gender = @setFortuneInfoEventLog.Gender;
                state.BirthDate = @setFortuneInfoEventLog.BirthDate;
                state.BirthPlace = @setFortuneInfoEventLog.BirthPlace;
                state.FullName = @setFortuneInfoEventLog.FullName;
                break;
            case SetVoiceLanguageEventLog @setVoiceLanguageEventLog:
                // Update the voice language preference for the user
                state.VoiceLanguage = @setVoiceLanguageEventLog.VoiceLanguage;
                break;
            case GenerateChatShareContentLogEvent generateChatShareContentLogEvent:
                var session = state.GetSession(generateChatShareContentLogEvent.SessionId);
                if (session == null)
                {
                    Logger.LogDebug($"[ChatGAgentManager][GenerateChatShareContentLogEvent] session not fuound: {generateChatShareContentLogEvent.SessionId.ToString()}");
                    break;
                }
                state.CurrentShareCount += 1;
                if (session.ShareIds == null)
                {
                    session.ShareIds = new List<Guid>();
                }
                session.ShareIds.Add(generateChatShareContentLogEvent.ShareId);
                break;
            case SetMaxShareCountLogEvent setMaxShareCountLogEvent:
                state.MaxShareCount = setMaxShareCountLogEvent.MaxShareCount;
                break;
            case SetInviterEventLog setInviterEventLog:
                state.InviterId = setInviterEventLog.InviterId;
                break;
            case InitializeNewUserStatusLogEvent initializeNewUserStatusLogEvent:
                state.IsFirstConversation = initializeNewUserStatusLogEvent.IsFirstConversation;
                state.UserId = initializeNewUserStatusLogEvent.UserId;
                if (initializeNewUserStatusLogEvent.RegisteredAtUtc != null)
                {
                    state.RegisteredAtUtc = initializeNewUserStatusLogEvent.RegisteredAtUtc;
                }
                state.MaxShareCount = initializeNewUserStatusLogEvent.MaxShareCount;
                break;
        }   
    }

    /// <summary>
    /// Check and initialize first access status based on version history.
    /// Uses IsFirstConversation field to mark whether this is the first access to ChatManagerGAgent.
    /// If the field has a value, it means this is not the first access.
    /// </summary>
    private async Task<bool> CheckAndInitializeFirstAccessStatus()
    {
        // If IsFirstConversation is already set, no need to set it again
        if (State.IsFirstConversation != null)
        {
            return false;
        }

        // Use Version property to determine if this is a historical user or new user
        // Version > 0 means there are existing events, so it's a historical user
        // Version == 0 means no events yet, so it's a new user
        var isFirstAccess = Version == 0;
        var userId = this.GetPrimaryKey();

        if (isFirstAccess)
        {
            // For new users: initialize all fields in one combined event
            RaiseEvent(new InitializeNewUserStatusLogEvent
            {
                IsFirstConversation = true,
                UserId = userId,
                RegisteredAtUtc = DateTime.UtcNow,
                MaxShareCount = 10000 
            });
            await ConfirmEvents();
            return true;
        }
        else
        {
            // For historical users: use separate events to maintain backward compatibility
            // Don't set RegisteredAtUtc and MaxShareCount for historical users here
            // as they should be handled by existing logic if needed
            RaiseEvent(new InitializeNewUserStatusLogEvent
            {
                IsFirstConversation = false,
                UserId = userId,
                RegisteredAtUtc = null,
                MaxShareCount = 10000
            });
            await ConfirmEvents();
            return false;
        }
    }

    protected override async Task OnGAgentActivateAsync(CancellationToken cancellationToken)
    {
        // Check and initialize first access status if needed
        var firstAccess = await CheckAndInitializeFirstAccessStatus();
        if (firstAccess)
        {
            // Record signup success event via OpenTelemetry
            var userId = this.GetPrimaryKey().ToString();
            UserLifecycleTelemetryMetrics.RecordSignupSuccess(userId: userId, logger: Logger);
        }

        if (State.MaxShareCount == 0)
        {
            RaiseEvent(new SetMaxShareCountLogEvent
            {
                MaxShareCount = 10000
            });
        }
        await base.OnGAgentActivateAsync(cancellationToken);
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

    /// <summary>
    /// Sync subscription status between UserBillingGAgent and UserQuotaGAgent if needed
    /// This ensures Google Pay and other platform subscriptions are properly reflected in user quota
    /// </summary>
    private async Task SyncSubscriptionStatusIfNeeded(IUserBillingGAgent userBillingGAgent, IUserQuotaGAgent userQuotaGAgent, ActiveSubscriptionStatusDto activeSubscriptionStatus)
    {
        try
        {
            // Get current quota subscription status
            var quotaSubscription = await userQuotaGAgent.GetSubscriptionAsync(false);
            var quotaUltimateSubscription = await userQuotaGAgent.GetSubscriptionAsync(true);

            Logger.LogDebug($"[ChatGAgentManager][SyncSubscriptionStatusIfNeeded] Current quota subscription status - Premium: {quotaSubscription.IsActive}, Ultimate: {quotaUltimateSubscription.IsActive}");

            // Check if there's any mismatch between billing and quota status
            bool needsSync = false;

            // If billing shows active subscriptions but quota doesn't, we need to sync
            if (activeSubscriptionStatus.HasActiveSubscription && !quotaSubscription.IsActive && !quotaUltimateSubscription.IsActive)
            {
                needsSync = true;
                Logger.LogInformation($"[ChatGAgentManager][SyncSubscriptionStatusIfNeeded] Subscription status mismatch detected. Billing shows active subscription but quota shows inactive. Syncing...");
            }

            // Additional check: If Google Play specifically shows active but neither quota subscription is active
            if (activeSubscriptionStatus.HasActiveGooglePlaySubscription && !quotaSubscription.IsActive && !quotaUltimateSubscription.IsActive)
            {
                needsSync = true;
                Logger.LogInformation($"[ChatGAgentManager][SyncSubscriptionStatusIfNeeded] Google Play subscription detected but not reflected in quota. Syncing...");
            }

            if (needsSync)
            {
                // Get latest payment history to sync subscription status
                var paymentHistory = await userBillingGAgent.GetPaymentHistoryAsync(1, 10); // Get recent payments
                
                foreach (var payment in paymentHistory.Where(p => p.Status == PaymentStatus.Completed && p.Platform == PaymentPlatform.GooglePlay))
                {
                    Logger.LogInformation($"[ChatGAgentManager][SyncSubscriptionStatusIfNeeded] Found Google Play payment {payment.PaymentGrainId} with PlanType {payment.PlanType}, syncing to UserQuotaGAgent");
                    
                    // Determine if this is ultimate subscription based on membership level
                    bool isUltimate = payment.MembershipLevel == MembershipLevel.Membership_Level_Ultimate;
                    
                    // Create subscription info to sync
                    var subscriptionToSync = new SubscriptionInfoDto
                    {
                        IsActive = true,
                        PlanType = payment.PlanType,
                        Status = payment.Status,
                        StartDate = payment.SubscriptionStartDate,
                        EndDate = payment.SubscriptionEndDate,
                        SubscriptionIds = new List<string> { payment.SubscriptionId },
                        InvoiceIds = new List<string>()
                    };

                    await userQuotaGAgent.UpdateSubscriptionAsync(subscriptionToSync, isUltimate);
                    Logger.LogInformation($"[ChatGAgentManager][SyncSubscriptionStatusIfNeeded] Successfully synced Google Play subscription to UserQuotaGAgent - Ultimate: {isUltimate}");
                    break; // Only sync the most recent active subscription
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"[ChatGAgentManager][SyncSubscriptionStatusIfNeeded] Error syncing subscription status");
            // Don't throw - this is a best-effort sync operation
        }
    }
}