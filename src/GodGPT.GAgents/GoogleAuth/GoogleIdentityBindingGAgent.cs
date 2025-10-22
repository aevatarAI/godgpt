using Aevatar.Application.Grains.GoogleAuth.Dtos;
using Aevatar.Application.Grains.GoogleAuth.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Json.Schema.Generation;
using Microsoft.Extensions.Logging;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.GoogleAuth;

/// <summary>
/// Google identity binding GAgent interface
/// </summary>
public interface IGoogleIdentityBindingGAgent : IGAgent
{
    /// <summary>
    /// Create or update Google identity binding
    /// </summary>
    Task<GoogleIdentityBindingResultDto> CreateOrUpdateBindingAsync(string googleUserId, Guid userId, string email, string displayName);
    
    /// <summary>
    /// Get binding info
    /// </summary>
    Task<GoogleIdentityBindingDto> GetBindingAsync();

    /// <summary>
    /// Remove binding
    /// </summary>
    Task<bool> RemoveBindingAsync();
}

/// <summary>
/// Google identity binding GAgent
/// </summary>
[Description("Google identity binding agent")]
[GAgent]
[Reentrant]
public class GoogleIdentityBindingGAgent : GAgentBase<GoogleIdentityBindingState, GoogleIdentityBindingLogEvent, EventBase, ConfigurationBase>, IGoogleIdentityBindingGAgent
{
    private readonly ILogger<GoogleIdentityBindingGAgent> _logger;

    public GoogleIdentityBindingGAgent(ILogger<GoogleIdentityBindingGAgent> logger)
    {
        _logger = logger;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Google Identity Binding GAgent");
    }

    /// <summary>
    /// Create or update Google identity binding
    /// </summary>
    public async Task<GoogleIdentityBindingResultDto> CreateOrUpdateBindingAsync(string googleUserId, Guid userId, string email, string displayName)
    {
        try
        {
            _logger.LogInformation("Creating or updating Google identity binding for GoogleUserId: {GoogleUserId}, UserId: {UserId}",
                googleUserId, userId);

            if (State.UserId == null)
            {
                // Create new binding
                RaiseEvent(new GoogleIdentityBindingCreatedLogEvent
                {
                    GoogleUserId = googleUserId,
                    UserId = userId,
                    Email = email,
                    DisplayName = displayName,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                // Update existing binding
                RaiseEvent(new GoogleIdentityBindingUpdatedLogEvent
                {
                    Email = email,
                    DisplayName = displayName,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            await ConfirmEvents();

            return new GoogleIdentityBindingResultDto
            {
                Success = true,
                GoogleUserId = googleUserId,
                Email = email,
                DisplayName = displayName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating or updating Google identity binding");
            return new GoogleIdentityBindingResultDto
            {
                Success = false,
                Error = "Internal server error"
            };
        }
    }

    /// <summary>
    /// Get binding by system user ID
    /// </summary>
    public Task<GoogleIdentityBindingDto> GetBindingAsync()
    {
        return Task.FromResult(new GoogleIdentityBindingDto
        {
            GoogleUserId = State.GoogleUserId,
            UserId = State.UserId,
            Email = State.Email,
            DisplayName = State.DisplayName,
            CreatedAt = State.CreatedAt,
            UpdatedAt = State.UpdatedAt
        });
    }

    /// <summary>
    /// Remove binding
    /// </summary>
    public async Task<bool> RemoveBindingAsync()
    {
        try
        {
            _logger.LogInformation("Removing Google identity binding for GoogleUserId: {GoogleUserId}",
                State.GoogleUserId);

            // Clear all state
            RaiseEvent(new GoogleIdentityBindingUpdatedLogEvent
            {
                Email = string.Empty,
                DisplayName = string.Empty,
                UpdatedAt = DateTime.UtcNow
            });

            await ConfirmEvents();

            _logger.LogInformation("Successfully removed Google identity binding");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing Google identity binding");
            return false;
        }
    }

    protected sealed override void GAgentTransitionState(GoogleIdentityBindingState state, StateLogEventBase<GoogleIdentityBindingLogEvent> @event)
    {
        switch (@event)
        {
            case GoogleIdentityBindingCreatedLogEvent created:
                state.GoogleUserId = created.GoogleUserId;
                state.UserId = created.UserId;
                state.Email = created.Email;
                state.DisplayName = created.DisplayName;
                state.CreatedAt = created.CreatedAt;
                break;
            case GoogleIdentityBindingUpdatedLogEvent updated:
                state.Email = updated.Email;
                state.DisplayName = updated.DisplayName;
                state.UpdatedAt = updated.UpdatedAt;
                break;
        }
    }
}
