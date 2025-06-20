using Aevatar.Core.Abstractions;
using GodGPT.GAgents.MineAI.Dtos;

namespace Aevatar.Application.Grains.MineAI;

/// <summary>
/// MineAI awakening score calculation interface
/// </summary>
public interface IMineAIAwakening : IGAgent
{
    /// <summary>
    /// Calculate awakening score based on user prompt
    /// </summary>
    /// <param name="request">Awakening score calculation request</param>
    /// <returns>Calculation result containing score and original ID</returns>
    Task<AwakeningScoreResponse> CalculateScoreAsync(AwakeningScoreRequest request);
}