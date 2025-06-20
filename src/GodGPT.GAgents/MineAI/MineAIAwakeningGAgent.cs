using System.Security.Cryptography;
using System.Text;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;
using Aevatar.Application.Grains.Common.Services;
using Aevatar.Application.Grains.MineAI.Events;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.AIGAgent.Dtos;
using Aevatar.GAgents.ChatAgent.Dtos;
using GodGPT.GAgents.MineAI.Dtos;
using Microsoft.Extensions.Logging;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.MineAI;

/// <summary>
/// MineAI Awakening Score GAgent
/// </summary>
[GAgent]
[Reentrant]
public class MineAIAwakeningGAgent :
    GAgentBase<MineAIAwakeningState, MineAIAwakeningEventLog, EventBase, ConfigurationBase>, IMineAIAwakening
{
    private readonly ILogger<MineAIAwakeningGAgent> _logger;
    private readonly ISystemAuthenticationService _systemAuthenticationService;
    private const long TokenExpireTime = 300;

    /// <summary>
    /// Initializes a new instance of the <see cref="MineAIAwakeningGAgent"/> class.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="grainFactory">Orleans grain factory</param>
    /// <param name="systemAuthenticationService">System authentication service</param>
    public MineAIAwakeningGAgent(
        ILogger<MineAIAwakeningGAgent> logger,
        ISystemAuthenticationService systemAuthenticationService)
    {
        _logger = logger;
        _systemAuthenticationService = systemAuthenticationService;
    }

    /// <summary>
    /// Calculate awakening score for the given request
    /// </summary>
    public async Task<AwakeningScoreResponse> CalculateScoreAsync(AwakeningScoreRequest request)
    {
        try
        {
            // 1. Validate request
            if (!await ValidateSystemRequestAsync(request))
            {
                _logger.LogWarning("[MineAIAwakeningGAgent][CalculateScoreAsync] Invalid request authentication for request {RequestId}", request.Id);
                throw new UnauthorizedAccessException("Invalid system authentication");
            }

            // 2. Calculate score
            var score = await CalculateScoreInternalAsync(request.Prompt);

            // 4. Return result
            return new AwakeningScoreResponse
            {
                Id = request.Id,
                Score = score,
                Message = "Success"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MineAIAwakeningGAgent][CalculateScoreAsync] Error calculating awakening score for request {RequestId}", request.Id);
            throw;
        }
    }

    private async Task<bool> ValidateSystemRequestAsync(AwakeningScoreRequest request)
    {
        try
        {
            // 1. Validate required parameters
            if (string.IsNullOrEmpty(request.SystemId) ||
                string.IsNullOrEmpty(request.Timestamp) ||
                string.IsNullOrEmpty(request.Signature))
            {
                _logger.LogDebug(
                    "[MineAIAwakeningGAgent][ValidateSystemRequestAsync] Required parameters missing - SystemId: {HasSystemId}, Timestamp: {HasTimestamp}, Signature: {HasSignature}",
                    !string.IsNullOrEmpty(request.SystemId),
                    !string.IsNullOrEmpty(request.Timestamp),
                    !string.IsNullOrEmpty(request.Signature)
                );
                return false;
            }

            // 2. Validate timestamp (prevent replay attacks)
            if (!long.TryParse(request.Timestamp, out long ts) ||
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ts > TokenExpireTime)
            {
                _logger.LogDebug(
                    "[MineAIAwakeningGAgent][ValidateSystemRequestAsync] Token expired or invalid - Timestamp");
                return false;
            }

            // 3. Get system public key
            var publicKey = await _systemAuthenticationService.GetPublicKeyAsync(request.SystemId);
            if (string.IsNullOrEmpty(publicKey))
            {
                _logger.LogDebug(
                    "[MineAIAwakeningGAgent][ValidateSystemRequestAsync] Public key not found for system {SystemId}",
                    request.SystemId
                );
                return false;
            }

            // 4. Verify signature
            var contentToVerify = $"{request.SystemId}{request.Timestamp}{request.Id}{request.Prompt}";
            var signatureBytes = Convert.FromBase64String(request.Signature);
            var dataToVerify = Encoding.UTF8.GetBytes(contentToVerify);

            try
            {
                using var rsa = RSA.Create();
                // Convert base64 string to byte array and import as PKCS#8
                var publicKeyBytes = Convert.FromBase64String(publicKey);
                rsa.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);
                return rsa.VerifyData(
                    dataToVerify,
                    signatureBytes,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MineAIAwakeningGAgent][ValidateSystemRequestAsync] Error importing public key or verifying signature");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MineAIAwakeningGAgent][ValidateSystemRequestAsync] Error validating system request for {SystemId}", request.SystemId);
            return false;
        }
    }

    private async Task<int> CalculateScoreInternalAsync(string prompt)
    {
        var sessionIds = State.SessionIds;
        if (sessionIds.IsNullOrEmpty())
        {
            var configuration = GetConfiguration();
            var sysMessage = await configuration.GetPrompt();
            var sessionId = Guid.NewGuid();
            var newGodChat = GrainFactory.GetGrain<IGodChat>(sessionId);
            var chatConfigDto = new ChatConfigDto()
            {
                Instructions = string.Empty, 
                MaxHistoryCount = 32,
                LLMConfig = new LLMConfigDto() { SystemLLM = await configuration.GetSystemLLM() },
                StreamingModeEnabled = false, StreamingConfig = new StreamingConfig()
                {
                    BufferingSize = 32
                }
            };
            Logger.LogDebug($"[MineAIAwakeningGAgent][CalculateScoreInternalAsync] init godchat : {sessionId.ToString()}");
            await newGodChat.ConfigAsync(chatConfigDto);
            
            RaiseEvent(new MineAIAwakeningUpdateSessionidsEventLog
            {
                SessionIds = new List<Guid>() {sessionId}
            });
            await ConfirmEvents();
        }

        var godChat = GrainFactory.GetGrain<IGodChat>(State.SessionIds.First());
        var chatMessages = await godChat.ChatWithSessionAsync(Guid.NewGuid().ToString(), prompt, region: "CN", streamingModeEnabled: false);
        if (chatMessages.IsNullOrEmpty())
        {
            return 0;
        }
        var chatMessage = chatMessages.FirstOrDefault(t => t.ChatRole == ChatRole.Assistant);
        return chatMessage == null ? 0 : ParseScoreFromResponse(chatMessage.Content);
    }

    private int ParseScoreFromResponse(string? response)
    {
        if (response == null || response.IsNullOrWhiteSpace())
        {
            return 0;
        }
        // Simple implementation: extract number between 1-100 from response
        try
        {
            var match = System.Text.RegularExpressions.Regex.Match(response, @"\b([1-9][0-9]?|100)\b");
            if (match.Success && int.TryParse(match.Value, out int score))
            {
                return score;
            }

            return 1; // Default minimum score
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MineAIAwakeningGAgent][ParseScoreFromResponse] Error parsing score from response");
            return 1;
        }
    }

    /// <summary>
    /// Get agent description
    /// </summary>
    /// <returns>Agent description</returns>
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("MineAI awakening score calculation agent");
    }

    private IConfigurationGAgent GetConfiguration()
    {
        return GrainFactory.GetGrain<IConfigurationGAgent>(
            CommonHelper.GetSessionManagerConfigurationId()
        );
    }

    protected override void GAgentTransitionState(MineAIAwakeningState state,
        StateLogEventBase<MineAIAwakeningEventLog> @event)
    {
        switch (@event)
        {
            case MineAIAwakeningUpdateSessionidsEventLog mineAiAwakeningUpdateSessionidsEventLog:
                State.SessionIds = mineAiAwakeningUpdateSessionidsEventLog.SessionIds;
                break;
        }
    }
}