using Aevatar.Core.Abstractions;
using Aevatar.Core;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Aevatar.Application.Grains.Agents.ChatManager;
using GodGPT.GAgents.SpeechChat;
using GodGPT.GAgents.Awakening.Dtos;
using GodGPT.GAgents.Awakening.Options;
using GodGPT.GAgents.Awakening.SEvents;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.Dtos;
using Aevatar.GAgents.AIGAgent.Dtos;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Orleans;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GodGPT.GAgents.Awakening;

/// <summary>
/// Awakening system grain implementation
/// </summary>
[GAgent(nameof(AwakeningGAgent))]
public class AwakeningGAgent : GAgentBase<AwakeningState, AwakeningLogEvent>, IAwakeningGAgent
{
    private readonly IClusterClient _clusterClient;
    private readonly IOptionsMonitor<AwakeningOptions> _options;
    private readonly ILogger<AwakeningGAgent> _logger;

    public AwakeningGAgent(
        IClusterClient clusterClient,
        IOptionsMonitor<AwakeningOptions> options,
        ILogger<AwakeningGAgent> logger)
    {
        _clusterClient = clusterClient;
        _options = options;
        _logger = logger;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Personalized Awakening System GAgent");
    }

    public async Task<SessionContentDto?> GetLatestNonEmptySessionAsync()
    {
        try
        {
            // Get current user ID through Grain's Primary Key
            var userId = this.GetPrimaryKey();
            
            // Get ChatManagerGAgent for this user
            var chatManager = _clusterClient.GetGrain<IChatManagerGAgent>(userId);
            var sessionList = await chatManager.GetSessionListAsync();
            
            if (sessionList == null || sessionList.Count == 0)
            {
                return null;
            }
            
            // Traverse from end to find latest non-empty session
            for (int i = sessionList.Count - 1; i >= 0; i--)
            {
                var session = sessionList[i];
                var messages = await chatManager.GetSessionMessageListAsync(session.SessionId);
                
                if (messages != null && messages.Count > 0)
                {
                    // Found non-empty session, build return object
                    return new SessionContentDto
                    {
                        SessionId = session.SessionId,
                        Title = session.Title ?? string.Empty,
                        Messages = messages,
                        LastActivityTime = session.CreateAt,
                        ExtractedContent = ExtractCoreContent(messages)
                    };
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get latest non-empty session for user {UserId}", this.GetPrimaryKey());
            return null;
        }
    }

    public async Task<AwakeningResultDto> GenerateAwakeningContentAsync(SessionContentDto sessionContent, VoiceLanguageEnum language)
    {
        try
        {
            if (sessionContent == null)
            {
                return new AwakeningResultDto
                {
                    IsSuccess = false,
                    ErrorMessage = "Session content is null"
                };
            }

            var prompt = BuildPrompt(sessionContent, language);
            var result = await CallLLMWithRetry(prompt, language);
            
            if (result.IsSuccess)
            {
                // Save successful generation
                RaiseEvent(new GenerateAwakeningLogEvent
                {
                    Timestamp = result.Timestamp,
                    AwakeningLevel = result.AwakeningLevel,
                    AwakeningMessage = result.AwakeningMessage,
                    Language = language,
                    SessionId = sessionContent.SessionId.ToString(),
                    IsSuccess = true,
                    AttemptCount = State.GenerationAttempts + 1
                });

                await ConfirmEvents();
            }
            else
            {
                // Log failure (only in memory log, not persisted event)
                _logger.LogWarning("Awakening generation failed for user {UserId}: {ErrorMessage}", 
                    this.GetPrimaryKey(), result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate awakening content");
            return new AwakeningResultDto
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<AwakeningContentDto?> GetTodayAwakeningAsync(VoiceLanguageEnum language)
    {
        try
        {
            // Check if already generated today
            if (IsToday(State.LastGeneratedTimestamp))
            {
                return BuildAwakeningContentDto();
            }

            // Try to lock today's generation
            var lockSuccessful = await TryLockTodayGenerationAsync(language);
            if (!lockSuccessful)
            {
                return BuildAwakeningContentDto();
            }

            // Get latest session content
            var sessionContent = await GetLatestNonEmptySessionAsync();
            if (sessionContent == null)
            {
                // No session content, generate successful result with level 0
                RaiseEvent(new GenerateAwakeningLogEvent
                {
                    Timestamp = GetTodayTimestamp(),
                    AwakeningLevel = 0,
                    AwakeningMessage = string.Empty,
                    Language = language,
                    SessionId = string.Empty,
                    IsSuccess = true,
                    AttemptCount = 1
                });

                await SetStatusAsync(AwakeningStatus.Completed);
                await ConfirmEvents();
                
                return new AwakeningContentDto
                {
                    AwakeningLevel = 0,
                    AwakeningMessage = string.Empty,
                    Status = AwakeningStatus.Completed
                };
            }

            // Start asynchronous generation
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await GenerateAwakeningContentAsync(sessionContent, language);
                    await CompleteGenerationAsync(result.IsSuccess);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background awakening generation failed");
                    await CompleteGenerationAsync(false);
                }
            });

            // Return null with generating status immediately
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get today awakening");
            return null;
        }
    }

    #region Private Helper Methods

    private string ExtractCoreContent(List<ChatMessage> messages)
    {
        // Extract user messages and assistant replies' key content
        var userMessages = messages
            .Where(m => m.ChatRole == ChatRole.User)
            .Select(m => m.Content)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToList();
            
        var summary = string.Join(" | ", userMessages.Take(3));
        if (summary.Length > 500)
        {
            summary = summary.Substring(0, 500) + "...";
        }
        
        return summary;
    }

    private string BuildPrompt(SessionContentDto sessionContent, VoiceLanguageEnum language)
    {
        var languageCode = GetLanguageCode(language);
        var contentSummary = ExtractAndSummarizeContent(sessionContent);
        
        var template = _options.CurrentValue.PromptTemplate;
        var basePrompt = template
            .Replace("{LANGUAGE}", languageCode)
            .Replace("{CONTENT_SUMMARY}", contentSummary)
            .Replace("{USER_CONTEXT}", BuildUserContext(sessionContent))
            .Replace("{DATE}", DateTime.UtcNow.ToString("yyyy-MM-dd"));
        
        // Check multi-language switch
        if (_options.CurrentValue.EnableLanguageSpecificPrompt)
        {
            var languageInstructions = _options.CurrentValue.LanguageInstructions;
            if (languageInstructions.TryGetValue(language, out var instruction))
            {
                basePrompt += $"\n\n{instruction}";
            }
        }
        
        return basePrompt;
    }

    private string GetLanguageCode(VoiceLanguageEnum language)
    {
        return language switch
        {
            VoiceLanguageEnum.Chinese => "Chinese",
            VoiceLanguageEnum.English => "English", 
            VoiceLanguageEnum.Spanish => "Spanish",
            _ => "English"
        };
    }

    private string ExtractAndSummarizeContent(SessionContentDto sessionContent)
    {
        if (sessionContent.Messages.Count == 0)
            return string.Empty;
            
        // Extract key content from user messages and assistant replies
        var userMessages = sessionContent.Messages
            .Where(m => m.ChatRole == ChatRole.User)
            .Select(m => m.Content)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToList();
        
        // Limit content length to avoid exceeding LLM token limits
        var summary = string.Join(" | ", userMessages.Take(3));
        if (summary.Length > 500)
        {
            summary = summary.Substring(0, 500) + "...";
        }
        
        return summary;
    }

    private string BuildUserContext(SessionContentDto sessionContent)
    {
        return $"Session: {sessionContent.Title}, Messages: {sessionContent.Messages.Count}, Last Activity: {sessionContent.LastActivityTime:yyyy-MM-dd}";
    }

    private async Task<AwakeningResultDto> CallLLMWithRetry(string prompt, VoiceLanguageEnum language)
    {
        var maxAttempts = _options.CurrentValue.MaxRetryAttempts;
        var timeout = TimeSpan.FromSeconds(_options.CurrentValue.TimeoutSeconds);
        
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeout);
                
                // Get AIAgentStatusProxy for LLM calls
                var proxy = await GetAIAgentStatusProxyAsync();
                if (proxy == null)
                {
                    _logger.LogError("Failed to get AIAgentStatusProxy for awakening generation");
                    return new AwakeningResultDto
                    {
                        IsSuccess = false,
                        ErrorMessage = "Failed to get LLM service proxy"
                    };
                }

                var settings = new ExecutionPromptSettings
                {
                    Temperature = _options.CurrentValue.Temperature.ToString()
                };
                
                // Call LLM with empty history for awakening generation
                var response = await proxy.ChatWithHistory(prompt, new List<ChatMessage>(), settings);
                
                if (response != null && response.Count > 0)
                {
                    var content = response[0].Content;
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        var result = ParseAwakeningResponse(content, language);
                        if (result.IsSuccess)
                        {
                            return result;
                        }
                    }
                }
                
                _logger.LogWarning("Attempt {Attempt} failed: No valid response from LLM", attempt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception on attempt {Attempt}", attempt);
            }
            
            if (attempt < maxAttempts)
            {
                await Task.Delay(1000 * attempt); // Incremental delay
            }
        }
        
        // All retries failed, return failure result
        return new AwakeningResultDto 
        { 
            IsSuccess = false, 
            ErrorMessage = "Failed to generate awakening content after all retries" 
        };
    }

    private async Task<IAIAgentStatusProxy?> GetAIAgentStatusProxyAsync()
    {
        try
        {
            // Create a new AIAgentStatusProxy instance
            var proxy = GrainFactory.GetGrain<IAIAgentStatusProxy>(Guid.NewGuid());
            
            // Configure the proxy with awakening-specific settings
            await proxy.ConfigAsync(new AIAgentStatusProxyConfig
            {
                Instructions = "You are an AI assistant that generates personalized awakening messages.",
                LLMConfig = new LLMConfigDto { SystemLLM = _options.CurrentValue.LLMModel },
                StreamingModeEnabled = false,
                RequestRecoveryDelay = TimeSpan.FromSeconds(30),
                ParentId = this.GetPrimaryKey()
            });

            return proxy;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create and configure AIAgentStatusProxy");
            return null;
        }
    }

    private AwakeningResultDto ParseAwakeningResponse(string responseContent, VoiceLanguageEnum language)
    {
        try
        {
            // Try to parse JSON response format
            var jsonResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent);
            
            if (jsonResponse != null && 
                jsonResponse.TryGetValue("level", out var levelObj) && 
                jsonResponse.TryGetValue("message", out var messageObj))
            {
                var level = Convert.ToInt32(levelObj);
                var message = messageObj?.ToString();

                // Validate that we have a meaningful message (following "empty is empty" principle)
                if (string.IsNullOrWhiteSpace(message))
                {
                    return new AwakeningResultDto
                    {
                        IsSuccess = false,
                        ErrorMessage = "Empty or invalid awakening message in JSON response"
                    };
                }

                // Validate level range
                if (level < 1 || level > 10)
                {
                    level = Math.Max(1, Math.Min(10, level));
                }

                return new AwakeningResultDto
                {
                    IsSuccess = true,
                    AwakeningLevel = level,
                    AwakeningMessage = message,
                    Timestamp = GetTodayTimestamp()
                };
            }
        }
        catch (JsonException)
        {
            // If JSON parsing fails, try to extract level and message from text
        }

        // Fallback: try to parse from natural text response
        return ParseAwakeningFromText(responseContent, language);
    }

    private AwakeningResultDto ParseAwakeningFromText(string responseContent, VoiceLanguageEnum language)
    {
        try
        {
            // Extract level (look for numbers 1-10)
            var levelMatch = Regex.Match(responseContent, @"\b([1-9]|10)\b");
            if (!levelMatch.Success)
            {
                return new AwakeningResultDto
                {
                    IsSuccess = false,
                    ErrorMessage = "No valid awakening level found in LLM response"
                };
            }
            
            var level = int.Parse(levelMatch.Value);

            // Extract message (take the longest meaningful sentence)
            var sentences = responseContent.Split(new[] { '.', '!', '?', '。', '！', '？' }, StringSplitOptions.RemoveEmptyEntries);
            var message = sentences
                .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length > 10)
                .OrderByDescending(s => s.Length)
                .FirstOrDefault()?.Trim();

            // If no valid message found, return failure (following "empty is empty" principle)
            if (string.IsNullOrWhiteSpace(message))
            {
                return new AwakeningResultDto
                {
                    IsSuccess = false,
                    ErrorMessage = "No valid awakening message found in LLM response"
                };
            }

            return new AwakeningResultDto
            {
                IsSuccess = true,
                AwakeningLevel = level,
                AwakeningMessage = message,
                Timestamp = GetTodayTimestamp()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse awakening response from text");
            return new AwakeningResultDto
            {
                IsSuccess = false,
                ErrorMessage = "Failed to parse awakening content from LLM response"
            };
        }
    }

    private bool IsToday(long timestamp)
    {
        if (timestamp == 0) return false;
        
        var dateTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
        var today = DateTime.UtcNow.Date;
        
        return dateTime.Date == today;
    }

    private long GetTodayTimestamp()
    {
        return ((DateTimeOffset)DateTime.UtcNow.Date).ToUnixTimeSeconds();
    }

    private async Task<bool> TryLockTodayGenerationAsync(VoiceLanguageEnum language)
    {
        var todayTimestamp = GetTodayTimestamp();
        
        // If it's already today's timestamp, it means it's locked or generated
        if (IsToday(State.LastGeneratedTimestamp))
        {
            return false; // Already locked, no need to regenerate
        }
        
        // Atomically update timestamp to today and reset content
        RaiseEvent(new LockGenerationTimestampLogEvent 
        { 
            Timestamp = todayTimestamp 
        });
        
        // Reset awakening content for new day
        RaiseEvent(new ResetAwakeningContentLogEvent
        {
            Timestamp = todayTimestamp,
            Language = language
        });
        
        // Set status to generating
        RaiseEvent(new UpdateAwakeningStatusLogEvent
        {
            Status = AwakeningStatus.Generating
        });
        
        await ConfirmEvents();
        return true; // Successfully locked and initialized
    }

    private async Task SetStatusAsync(AwakeningStatus status)
    {
        RaiseEvent(new UpdateAwakeningStatusLogEvent
        {
            Status = status
        });
        
        await ConfirmEvents();
    }

    private AwakeningContentDto? BuildAwakeningContentDto()
    {
        // If content hasn't been generated today, return null
        if (!IsToday(State.LastGeneratedTimestamp))
        {
            return null;
        }
        
        // Return current content with status
        return new AwakeningContentDto
        {
            AwakeningLevel = State.AwakeningLevel,
            AwakeningMessage = State.AwakeningMessage,
            Status = State.Status
        };
    }

    private async Task CompleteGenerationAsync(bool isSuccess)
    {
        // Always set status to completed regardless of success/failure
        await SetStatusAsync(AwakeningStatus.Completed);
        
        if (!isSuccess)
        {
            _logger.LogWarning("Awakening generation completed with failure for user {UserId}", this.GetPrimaryKey());
        }
    }

    #endregion
}
