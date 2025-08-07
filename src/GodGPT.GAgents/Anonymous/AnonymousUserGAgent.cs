using System.Diagnostics;
using Aevatar.Application.Grains.Agents.Anonymous.Options;
using Aevatar.Application.Grains.Agents.Anonymous.SEvents;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;
using Aevatar.Application.Grains.Agents.ChatManager.Options;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Aevatar.Core.Placement;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.AIGAgent.Agent;
using Aevatar.GAgents.AIGAgent.Dtos;
using Aevatar.GAgents.ChatAgent.Dtos;
using Json.Schema.Generation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;
using Orleans.Providers;
using Volo.Abp;

namespace Aevatar.Application.Grains.Agents.Anonymous;

/// <summary>
/// Anonymous User GAgent - manages IP-based guest chat sessions with limited usage
/// Follows the same patterns as ChatManagerGAgent but simplified for anonymous users
/// </summary>
[Description("Anonymous user chat agent for guest access")]
[StorageProvider(ProviderName = "PubSubStore")]
[LogConsistencyProvider(ProviderName = "LogStorage")]
[GAgent(nameof(AnonymousUserGAgent))]
[SiloNamePatternPlacement("User")]
[Reentrant]
public class AnonymousUserGAgent : GAgentBase<AnonymousUserState, AnonymousUserEventLog>, 
    IAnonymousUserGAgent
{
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Anonymous User GAgent for guest chat sessions");
    }

    public async Task<int> GetChatCountAsync()
    {
        await EnsureInitializedAsync();
        return State.ChatCount;
    }

    public async Task<bool> CanChatAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        await EnsureInitializedAsync();
        var maxCount = GetMaxChatCount();
        var canChat = State.ChatCount < maxCount;
        Logger.LogDebug("[AnonymousUserGAgent][CanChatAsync] Total duration: {0}ms", stopwatch.ElapsedMilliseconds);
        return canChat;
    }

    public async Task<int> GetRemainingChatsAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        await EnsureInitializedAsync();
        var maxCount = GetMaxChatCount();
        var remainingChats = Math.Max(0, maxCount - State.ChatCount);
        Logger.LogDebug("[AnonymousUserGAgent][GetRemainingChatsAsync] Total duration: {0}ms", stopwatch.ElapsedMilliseconds);
        return remainingChats;
    }

    public async Task<int> GetMaxChatCountAsync()
    {
        return GetMaxChatCount();
    }

    public async Task<Guid> CreateGuestSessionAsync(string? guider = null)
    {
        var stopwatch = Stopwatch.StartNew();
        //await EnsureInitializedAsync();
        
        // Check if user has exceeded chat limit
        var canChatStopwatch = Stopwatch.StartNew();
        if (!await CanChatAsync())
        {
            Logger.LogWarning($"[AnonymousUserGAgent][CreateGuestSessionAsync] Chat limit exceeded for user: {State.UserHashId}");
            throw new InvalidOperationException("Daily chat limit exceeded for guest users");
        }
        Logger.LogDebug("[AnonymousUserGAgent][CreateGuestSessionAsync] CanChatAsync duration: {0}ms", canChatStopwatch.ElapsedMilliseconds);

        // Check if existing session can be reused (same guider and not yet used)
        if (State.CurrentSessionId.HasValue && !State.CurrentSessionUsed)
        {
            var existingGuider = State.CurrentGuider ?? string.Empty;
            var newGuider = guider ?? string.Empty;
            
            if (existingGuider.Equals(newGuider, StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogDebug($"[AnonymousUserGAgent][CreateGuestSessionAsync] Reusing existing session: {State.CurrentSessionId.Value} for user: {State.UserHashId}");
                Logger.LogDebug("[AnonymousUserGAgent][CreateGuestSessionAsync] Total duration: {0}ms", stopwatch.ElapsedMilliseconds);
                return State.CurrentSessionId.Value;
            }
        }

        var configuration = GetConfiguration();
        var createGodChatStopwatch = Stopwatch.StartNew();
        
        // Create new GodChat session (mimic ChatManagerGAgent.CreateSessionAsync)
        IGodChat godChat = GrainFactory.GetGrain<IGodChat>(Guid.NewGuid());
        createGodChatStopwatch.Stop();
        Logger.LogDebug("[AnonymousUserGAgent][CreateGuestSessionAsync] Create GodChat: {0}ms", createGodChatStopwatch.ElapsedMilliseconds);

        // Get system prompt and append role prompt if provided (exact copy from ChatManagerGAgent)
        var getPromptStopwatch = Stopwatch.StartNew();
        var sysMessage = await configuration.GetPrompt();
        
        if (!string.IsNullOrEmpty(guider))
        {
            var rolePrompt = GetRolePrompt(guider);
            if (!string.IsNullOrEmpty(rolePrompt))
            {
                sysMessage += $"You should follow the rules below. 1. {rolePrompt}. 2. {sysMessage}";
                Logger.LogDebug($"[AnonymousUserGAgent][CreateGuestSessionAsync] Added role prompt for guider: {guider}");
            }
        }
        getPromptStopwatch.Stop();
        Logger.LogDebug("[AnonymousUserGAgent][CreateGuestSessionAsync] GetPrompt duration: {0}ms", getPromptStopwatch.ElapsedMilliseconds);

        // Configure GodChat with same settings as regular users (exact copy from ChatManagerGAgent)
        var configGodChatStopwatch = Stopwatch.StartNew();
        var chatConfigDto = new ChatConfigDto()
        {
            Instructions = sysMessage, 
            MaxHistoryCount = 32,
            LLMConfig = new LLMConfigDto() { SystemLLM = await configuration.GetSystemLLM() },
            StreamingModeEnabled = true, 
            StreamingConfig = new StreamingConfig()
            {
                BufferingSize = 32
            }
        };
        
        Logger.LogDebug($"[AnonymousUserGAgent][CreateGuestSessionAsync] Config: {Newtonsoft.Json.JsonConvert.SerializeObject(chatConfigDto)}");

        await godChat.ConfigAsync(chatConfigDto);
        configGodChatStopwatch.Stop();
        Logger.LogDebug("[AnonymousUserGAgent][CreateGuestSessionAsync] Config GodChat: {0}ms", configGodChatStopwatch.ElapsedMilliseconds);

        // Record session creation event
        var sessionId = godChat.GetPrimaryKey();
        RaiseEvent(new CreateGuestSessionEventLog()
        {
            SessionId = sessionId,
            Guider = guider,
            CreateAt = DateTime.UtcNow
        });

        await godChat.InitAsync(this.GetPrimaryKey()); // Initialize with AnonymousUserGAgent ID
        //await ConfirmEvents();

        // Update state
        State.CurrentSessionId = sessionId;
        State.CurrentGuider = guider;
        State.CurrentSessionUsed = false; // Mark as unused initially
        
        Logger.LogDebug($"[AnonymousUserGAgent][CreateGuestSessionAsync] Session created: {sessionId} for user: {State.UserHashId}");
        Logger.LogDebug("[AnonymousUserGAgent][CreateGuestSessionAsync] Total duration: {0}ms", stopwatch.ElapsedMilliseconds);
        
        return sessionId;
    }

    public async Task GuestChatAsync(string content, string chatId)
    {
        await EnsureInitializedAsync();
        
        // Check chat limits
        if (!await CanChatAsync())
        {
            Logger.LogWarning($"[AnonymousUserGAgent][GuestChatAsync] Chat limit exceeded for user: {State.UserHashId}");
            throw new InvalidOperationException("Daily chat limit exceeded for guest users");
        }

        // Validate current session
        if (!State.CurrentSessionId.HasValue)
        {
            Logger.LogWarning($"[AnonymousUserGAgent][GuestChatAsync] No active guest session for user: {State.UserHashId}");
            throw new InvalidOperationException("No active guest session. Please create a session first.");
        }

        IGodChat godChat = GrainFactory.GetGrain<IGodChat>(State.CurrentSessionId.Value);
        var configuration = GetConfiguration();

        // Execute streaming chat (exact copy from ChatManagerGAgent.StreamChatWithSessionAsync)
        var stopwatch = Stopwatch.StartNew();
        await godChat.GodStreamChatAsync(
            State.CurrentSessionId.Value,
            await configuration.GetSystemLLM(), 
            await configuration.GetStreamingModeEnabled(),
            content, 
            chatId,
            null, isHttpRequest: true);
        stopwatch.Stop();
        Logger.LogDebug($"[AnonymousUserGAgent][GuestChatAsync] Chat execution: {stopwatch.ElapsedMilliseconds}ms");

        // Mark session as used and increment chat count
        RaiseEvent(new GuestChatEventLog()
        {
            ChatCount = State.ChatCount + 1,
            ChatAt = DateTime.UtcNow,
            SessionUsed = true // Mark session as used
        });

        await ConfirmEvents();
        
        Logger.LogDebug($"[AnonymousUserGAgent][GuestChatAsync] Chat completed for user: {State.UserHashId}, new count: {State.ChatCount + 1}");
    }

    public async Task<GuestSessionInfo?> GetCurrentSessionAsync()
    {
        await EnsureInitializedAsync();
        
        if (!State.CurrentSessionId.HasValue)
        {
            return null;
        }

        return new GuestSessionInfo
        {
            SessionId = State.CurrentSessionId.Value,
            Guider = State.CurrentGuider,
            CreatedAt = State.CreatedAt,
            ChatCount = State.ChatCount,
            RemainingChats = await GetRemainingChatsAsync(),
            SessionUsed = State.CurrentSessionUsed
        };
    }

    /// <summary>
    /// Get configuration agent (exact copy from ChatManagerGAgent)
    /// </summary>
    private IConfigurationGAgent GetConfiguration()
    {
        return GrainFactory.GetGrain<IConfigurationGAgent>(CommonHelper.GetSessionManagerConfigurationId());
    }

    /// <summary>
    /// Get role-specific prompt from configuration (exact copy from ChatManagerGAgent)
    /// </summary>
    private string GetRolePrompt(string roleName)
    {
        try
        {
            var roleOptions = (ServiceProvider.GetService(typeof(IOptionsMonitor<RolePromptOptions>)) as IOptionsMonitor<RolePromptOptions>)?.CurrentValue;
            var rolePrompt = roleOptions?.RolePrompts.GetValueOrDefault(roleName, string.Empty) ?? string.Empty;
            
            if (!string.IsNullOrEmpty(rolePrompt))
            {
                Logger.LogDebug($"[AnonymousUserGAgent][GetRolePrompt] Found role prompt for: {roleName}");
            }
            else
            {
                Logger.LogDebug($"[AnonymousUserGAgent][GetRolePrompt] No role prompt found for: {roleName}");
            }
            
            return rolePrompt;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[AnonymousUserGAgent][GetRolePrompt] Failed to get role prompt for role: {RoleName}", roleName);
            return string.Empty;
        }
    }

    /// <summary>
    /// Get maximum chat count from configuration
    /// </summary>
    private int GetMaxChatCount()
    {
        try
        {
            var options = (ServiceProvider.GetService(typeof(IOptionsMonitor<AnonymousGodGPTOptions>)) as IOptionsMonitor<AnonymousGodGPTOptions>)?.CurrentValue;
            return options?.MaxChatCount ?? 3;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[AnonymousUserGAgent][GetMaxChatCount] Failed to get max chat count, using default: 3");
            return 3;
        }
    }

    /// <summary>
    /// Ensure the anonymous user record is initialized with hashed user identifier
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (string.IsNullOrEmpty(State.UserHashId))
        {
            var grainId = this.GetPrimaryKey();
            var userHashId = grainId.ToString("N")[..16]; // Use first 16 chars as hash ID
            
            RaiseEvent(new InitializeAnonymousUserEventLog()
            {
                UserHashId = userHashId,
                CreatedAt = DateTime.UtcNow
            });

            await ConfirmEvents();
            
            Logger.LogDebug($"[AnonymousUserGAgent][EnsureInitializedAsync] Initialized for user: {userHashId}");
        }
    }

    /// <summary>
    /// Handle state transitions for events (mimic ChatManagerGAgent.AIGAgentTransitionState)
    /// </summary>
    protected override void GAgentTransitionState(AnonymousUserState state, StateLogEventBase<AnonymousUserEventLog> @event)
    {
        switch (@event)
        {
            case InitializeAnonymousUserEventLog initEvent:
                state.UserHashId = initEvent.UserHashId;
                state.CreatedAt = initEvent.CreatedAt;
                state.ChatCount = 0;
                break;
                
            case CreateGuestSessionEventLog createSessionEvent:
                state.CurrentSessionId = createSessionEvent.SessionId;
                state.CurrentGuider = createSessionEvent.Guider;
                state.CurrentSessionUsed = false; // Reset session used flag for new session
                // Note: Don't increment chat count on session creation, only on actual chat
                break;
                
            case GuestChatEventLog chatEvent:
                state.ChatCount = chatEvent.ChatCount;
                state.LastChatTime = chatEvent.ChatAt;
                state.CurrentSessionUsed = chatEvent.SessionUsed;
                break;
        }
    }

    /// <summary>
    /// Initialization on grain activation (mimic ChatManagerGAgent.OnAIGAgentActivateAsync)
    /// </summary>
    protected override async Task OnGAgentActivateAsync(CancellationToken cancellationToken)
    {
        Logger.LogDebug("[AnonymousUserGAgent][OnAIGAgentActivateAsync] Activating anonymous user grain");
        await EnsureInitializedAsync();
        await base.OnGAgentActivateAsync(cancellationToken);
    }
}
 