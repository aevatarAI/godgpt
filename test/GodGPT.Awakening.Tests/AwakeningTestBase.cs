using Aevatar;
using Aevatar.GAgents.AI.Common;
using GodGPT.GAgents.Awakening;
using GodGPT.GAgents.Awakening.Dtos;
using Microsoft.Extensions.Logging;

namespace GodGPT.Awakening.Tests;

/// <summary>
/// Base class for AwakeningGAgent tests
/// </summary>
public class AwakeningTestBase : AevatarOrleansTestBase<AwakeningTestModule>
{
    protected ILogger Logger => GetService<ILogger<AwakeningTestBase>>();

    /// <summary>
    /// Get AwakeningGAgent instance for testing
    /// </summary>
    /// <param name="userId">User ID for the grain, uses default if not provided</param>
    /// <returns>AwakeningGAgent instance</returns>
    protected async Task<IAwakeningGAgent> GetAwakeningGAgentAsync(Guid? userId = null)
    {
        var id = userId ?? Guid.NewGuid();
        var grain = Cluster.GrainFactory.GetGrain<IAwakeningGAgent>(id);
        
        // Ensure grain is activated
        await Task.Delay(10); // Small delay to ensure activation
        
        return grain;
    }

    /// <summary>
    /// Create test session content for testing
    /// </summary>
    /// <param name="messageCount">Number of messages to create</param>
    /// <returns>SessionContentDto for testing</returns>
    protected SessionContentDto CreateTestSessionContent(int messageCount = 3)
    {
        var messages = new List<ChatMessage>();
        
        for (int i = 0; i < messageCount; i++)
        {
            messages.Add(new ChatMessage
            {
                ChatRole = i % 2 == 0 ? ChatRole.User : ChatRole.Assistant,
                Content = $"Test message {i + 1}"
            });
        }

        return new SessionContentDto
        {
            SessionId = Guid.NewGuid(),
            Title = "Test Session",
            Messages = messages,
            LastActivityTime = DateTime.UtcNow,
            ExtractedContent = "Test content summary"
        };
    }
}
