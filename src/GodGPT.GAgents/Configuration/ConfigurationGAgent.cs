using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Json.Schema.Generation;
using Microsoft.Extensions.Logging;
using Orleans.Providers;

namespace Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;

[Description("manage chat agent")]
[StorageProvider(ProviderName = "PubSubStore")]
[LogConsistencyProvider(ProviderName = "LogStorage")]
[GAgent(nameof(ConfigurationGAgent))]
public class ConfigurationGAgent : GAgentBase<ConfigurationState, ConfigurationLogEvent>, IConfigurationGAgent
{
    private IDisposable _timerHandle;
    private const string OpenAILatest = "OpenAILatest";

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Configuration GAgent");
    }

    [EventHandler]
    public async Task HandleEventAsync(SetLLMEvent @event)
    {
        RaiseEvent(new SetSystemLLMLogEvent()
        {
            SystemLLM = @event.LLM
        });

        await ConfirmEvents();
    }

    [EventHandler]
    public async Task HandleEventAsync(SetPromptEvent @event)
    {
        RaiseEvent(new SetPromptLogEvent()
        {
            Prompt = @event.Prompt
        });

        await ConfirmEvents();
    }

    [EventHandler]
    public async Task HandleEventAsync(SetStreamingModeEnabledEvent @event)
    {
        RaiseEvent(new SetStreamingModeEnabledLogEvent()
        {
            StreamingModeEnabled = @event.StreamingModeEnabled
        });

        await ConfirmEvents();
    }

    [EventHandler]
    public async Task HandleEventAsync(SetUserProfilePromptEvent @event)
    {
        RaiseEvent(new SetUserProfilePromptLogEvent
        {
            UserProfilePrompt = @event.UserProfilePrompt
        });
        await ConfirmEvents();
    }

    public Task<string> GetSystemLLM()
    {
        return Task.FromResult(State.SystemLLM);
    }

    public Task<bool> GetStreamingModeEnabled()
    {
        return Task.FromResult(State.StreamingModeEnabled);
    }

    public Task<string> GetPrompt()
    {
        // Task.FromResult(State.Prompt);
        var sysMessage = "如果没有特别说明你默认使用英语."+State.Prompt;

        // var formattedRequirement =
        //     """
        //     ### 如果有数学公式，按如下格式处理：
        //     1. 行内LaTeX公式使用@@@$和$@@@符号包裹,
        //         示例：@@@$LaTeX公式$@@@
        //         示例：@@@$E=mc^2$@@@
        //         注意：不是@@@E=mc^2@@@
        //     2. 块级LaTeX公式用 ===$$\LaTeX公式$$=== 包裹,
        //         示例：===$$\int_a^b f(x)dx$$===
        //         示例：===$$M = R \cdot (I + A)$$=== 
        //     """;
        //
        //
        // sysMessage += formattedRequirement;
        // Add a new field for the current date and time
        string currentTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var currentRequirement = $"\n当前 UTC 时间：{currentTime}, " +
                                 $"\n 回答有关时间的问题时,以UTC时间为基准 ";

        sysMessage += currentRequirement;
        Logger.LogDebug("[ConfigurationGAgent][GetPrompt] System prompt: {SysMessage}", sysMessage);

        return Task.FromResult(sysMessage);
    }

    public Task<string> GetUserProfilePromptAsync()
    {
        return Task.FromResult(State.UserProfilePrompt);
    }

    public async Task UpdateSystemPromptAsync(string systemPrompt)
    {
        const string logContext = "[ConfigurationGAgent][UpdateSystemPrompt]";

        Logger.LogDebug("[{LogContext}] Updating prompt from '{OldPrompt}' to '{NewPrompt}'.", logContext, State.Prompt,
            systemPrompt);

        // Raise an event to update the prompt
        RaiseEvent(new SetPromptLogEvent
        {
            Prompt = systemPrompt
        });

        // Confirm updates to persist the new state
        await ConfirmEvents();

        Logger.LogDebug("[{LogContext}] Prompt successfully updated to '{NewPrompt}'.", logContext, systemPrompt);
    }

    protected sealed override void GAgentTransitionState(ConfigurationState state,
        StateLogEventBase<ConfigurationLogEvent> @event)
    {
        switch (@event)
        {
            case SetSystemLLMLogEvent @systemLlmLogEvent:
                State.SystemLLM = @systemLlmLogEvent.SystemLLM;
                break;
            case SetPromptLogEvent @setPromptLogEvent:
                State.Prompt = @setPromptLogEvent.Prompt;
                break;
            case SetStreamingModeEnabledLogEvent @setStreamingModeEnabledLogEvent:
                State.StreamingModeEnabled = @setStreamingModeEnabledLogEvent.StreamingModeEnabled;
                break;
            case SetUserProfilePromptLogEvent @setUserProfilePromptLogEvent:
                State.UserProfilePrompt = @setUserProfilePromptLogEvent.UserProfilePrompt;
                break;
        }
    }

    protected override async Task OnGAgentActivateAsync(CancellationToken cancellationToken)
    {
        Logger.LogDebug("ConfigurationGAgent OnGAgentActivateAsync");

        // if (State.SystemLLM.IsNullOrEmpty())
        // {
        //     RaiseEvent(new SetSystemLLMLogEvent()
        //     {
        //         SystemLLM = OpenAILatest
        //     });
        // }
        RaiseEvent(new SetSystemLLMLogEvent()
        {
            SystemLLM = "OpenAI"
        });

        RaiseEvent(new SetStreamingModeEnabledLogEvent()
        {
            StreamingModeEnabled = true
        });

        await ConfirmEvents();

        // if (State.Prompt.IsNullOrEmpty())
        // {
        //     await UpdatePromptPeriodically(null);
        // }

        // Initialize a periodic task to update the prompt every 5 minutes
        // #pragma warning disable CS0618
        // _timerHandle = RegisterTimer(UpdatePromptPeriodically, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        // #pragma warning restore CS0618
    }

    private async Task UpdatePromptPeriodically(object? state)
    {
        const string logContext = "[ConfigurationGAgent][UpdatePromptPeriodically]";
        try
        {
            // Fetch a new prompt from a helper or external source
            var newPrompt = await GodPromptHelper.LoadNewGodPromptAsync();
            // var newPrompt =  GodPromptHelper.LoadGodPrompt();

            // Validate that the new prompt is not null or empty
            if (string.IsNullOrWhiteSpace(newPrompt))
            {
                Logger.LogDebug("[{LogContext}] Retrieved an empty or null prompt, skipping update.", logContext);
                return;
            }

            // Check if the new prompt is different from the currently stored prompt
            if (newPrompt != State.Prompt)
            {
                Logger.LogDebug("[{LogContext}] Updating prompt from '{OldPrompt}' to '{NewPrompt}'.", logContext,
                    State.Prompt, newPrompt);

                // Raise an event to update the prompt
                RaiseEvent(new SetPromptLogEvent
                {
                    Prompt = newPrompt
                });

                // Confirm updates to persist the new state
                await ConfirmEvents();

                Logger.LogDebug("[{LogContext}] Prompt successfully updated to '{NewPrompt}'.", logContext, newPrompt);
            }
            else
            {
                Logger.LogDebug("[{LogContext}] New prompt is identical to the current state, no update needed.",
                    logContext);
            }
        }
        catch (Exception ex)
        {
            // Log exceptions with the appropriate log level and context
            Logger.LogError(ex, "[{LogContext}] Failed to update the prompt due to an exception.", logContext);
        }
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        // Dispose of the timer to clean up resources on grain deactivation
        _timerHandle?.Dispose();
        await base.OnDeactivateAsync(reason, cancellationToken);
    }
}