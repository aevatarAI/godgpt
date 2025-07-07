using Aevatar.Core.Abstractions;
using Orleans;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Agents.Anonymous;

/// <summary>
/// Interface for Anonymous User GAgent - manages IP-based guest chat sessions
/// </summary>
public interface IAnonymousUserGAgent : IGAgent
{
    /// <summary>
    /// Get current chat count for this IP
    /// </summary>
    [ReadOnly]
    Task<int> GetChatCountAsync();
    
    /// <summary>
    /// Check if this IP can still chat (within limits)
    /// </summary>
    [ReadOnly]
    Task<bool> CanChatAsync();
    
    /// <summary>
    /// Get remaining chat count for this IP
    /// </summary>
    [ReadOnly]
    Task<int> GetRemainingChatsAsync();
    
    /// <summary>
    /// Get maximum chat count from configuration
    /// </summary>
    [ReadOnly]
    Task<int> GetMaxChatCountAsync();
    
    /// <summary>
    /// Create or reset guest session for this IP
    /// </summary>
    Task<Guid> CreateGuestSessionAsync(string? guider = null);
    
    /// <summary>
    /// Execute guest chat with session validation and count increment
    /// </summary>
    Task GuestChatAsync(string content, string chatId);
    
    /// <summary>
    /// Get current session info
    /// </summary>
    [ReadOnly]
    Task<GuestSessionInfo?> GetCurrentSessionAsync();
} 