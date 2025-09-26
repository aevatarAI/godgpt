using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Core.Abstractions;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Agents.Invitation;

public interface IInviteCodeGAgent : IGAgent
{
    /// <summary>
    /// Initialize a new invite code with inviter ID
    /// </summary>
    Task<bool> InitializeAsync(string inviterId, string inviteCode);

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
    
    /// <summary>
    /// Initialize free trial code
    /// </summary>
    Task<bool> InitializeFreeTrialCodeAsync(FreeTrialCodeInitDto initDto);
    
    Task<ValidateCodeResultDto> ValidateAndGetFreeTrialCodeInfoAsync(string userId);
    Task<bool> MarkCodeAsUsedAsync();

    /// <summary>
    /// Get free trial code information
    /// </summary>
    [ReadOnly]
    Task<FreeTrialCodeInfoDto> GetCodeInfoAsync();
} 