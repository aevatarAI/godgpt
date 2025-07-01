using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AIGAgent.State;

namespace Aevatar.Application.Grains.Agents.Anonymous;

/// <summary>
/// State for Anonymous User GAgent, stores hashed user identifier for session info
/// </summary>
[GenerateSerializer]
public class AnonymousUserState : AIGAgentStateBase
{
    /// <summary>
    /// Hashed user identifier (derived from IP but not storing original IP for privacy)
    /// </summary>
    [Id(0)] 
    public string UserHashId { get; set; } = string.Empty;
    
    /// <summary>
    /// Current active session ID (only one session per user)
    /// </summary>
    [Id(1)] 
    public Guid? CurrentSessionId { get; set; }
    
    /// <summary>
    /// Total chat count used by this user
    /// </summary>
    [Id(2)] 
    public int ChatCount { get; set; }
    
    /// <summary>
    /// Last chat time
    /// </summary>
    [Id(3)] 
    public DateTime LastChatTime { get; set; }
    
    /// <summary>
    /// Creation time of this anonymous user record
    /// </summary>
    [Id(4)] 
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Current guider/role for the session
    /// </summary>
    [Id(5)] 
    public string? CurrentGuider { get; set; }
} 