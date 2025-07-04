using Aevatar.Core.Abstractions;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Agents.Invitation;

public interface IInviteCodeGAgent : IGAgent
{
    /// <summary>
    /// Initialize a new invite code with inviter ID
    /// </summary>
    Task<bool> InitializeAsync(string inviterId);

    /// <summary>
    /// Validate invite code and return inviter ID if valid
    /// </summary>
    [ReadOnly]
    Task<(bool isValid, string inviterId)> ValidateAndGetInviterAsync();

    /// <summary>
    /// Checks if the invite code has been initialized with an inviter.
    /// </summary>
    [ReadOnly]
    Task<bool> IsInitialized();
    
    /// <summary>
    /// Deactivate the invite code
    /// </summary>
    Task DeactivateCodeAsync();
} 