using Aevatar.Core.Abstractions;
using GodGPT.GAgents.SpeechChat;
using GodGPT.GAgents.Awakening.Dtos;
using Orleans.Concurrency;

namespace GodGPT.GAgents.Awakening;

/// <summary>
/// AwakeningGAgent - Personalized awakening system
/// Note: This Grain uses userId (Guid) as Primary Key, each user has an independent instance
/// Client calling method: var agent = _clusterClient.GetGrain<IAwakeningGAgent>(userId);
/// </summary>
public interface IAwakeningGAgent : IGAgent
{
    /// <summary>
    /// Get the user's latest non-empty session record
    /// Internally gets current user ID through this.GetPrimaryKey(), then calls corresponding ChatManagerGAgent
    /// </summary>
    /// <returns>Latest session content and related information</returns>
    [ReadOnly]
    Task<SessionContentDto?> GetLatestNonEmptySessionAsync();
    
    /// <summary>
    /// Generate awakening level and sentence based on session content and language type
    /// </summary>
    /// <param name="sessionContent">Session content</param>
    /// <param name="language">Language type</param>
    /// <param name="region">Region parameter for LLM service</param>
    /// <returns>Generated awakening content</returns>
    Task<AwakeningResultDto> GenerateAwakeningContentAsync(SessionContentDto sessionContent, VoiceLanguageEnum language, string? region = "");
    
    /// <summary>
    /// Get today's awakening level and quote, if not generated then generate asynchronously and return null
    /// The returned DTO contains status field, frontend can determine whether to continue polling based on this
    /// Status of Generating means generation is in progress, Completed means generation is finished (success or failure)
    /// </summary>
    /// <param name="language">Language type</param>
    /// <returns>Today's awakening content, return null if not generated, includes generation status</returns>
    Task<AwakeningContentDto?> GetTodayAwakeningAsync(VoiceLanguageEnum language, string? region = "");
    
    /// <summary>
    /// Reset awakening generation state for testing purposes
    /// This will clear the generated timestamp and allow regeneration of awakening content
    /// </summary>
    /// <returns>True if reset was successful</returns>
    Task<bool> ResetAwakeningStateForTestingAsync();
}
