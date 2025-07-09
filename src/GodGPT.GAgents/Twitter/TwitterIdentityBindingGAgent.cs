using Aevatar.Application.Grains.Twitter.SEvents;
using Aevatar.Application.Grains.Twitter.Dtos;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aevatar.Application.Grains.Twitter;

/// <summary>
/// Interface for managing Twitter identity bindings
/// </summary>
public interface ITwitterIdentityBindingGAgent : IGAgent
{
    /// <summary>
    /// Create or update Twitter account binding
    /// </summary>
    Task<TwitterAuthResultDto> CreateOrUpdateBindingAsync(string twitterId, Guid userId, string username, string profileImageUrl);

    /// <summary>
    /// Get the system user ID for a Twitter user ID
    /// </summary>
    Task<Guid?> GetUserIdAsync();
}

/// <summary>
/// GAgent for managing Twitter identity bindings
/// </summary>
[GAgent]
public class TwitterIdentityBindingGAgent : GAgentBase<TwitterIdentityBindingState, TwitterIdentityBindingLogEvent>, ITwitterIdentityBindingGAgent
{
    private readonly ILogger<TwitterIdentityBindingGAgent> _logger;

    public TwitterIdentityBindingGAgent(ILogger<TwitterIdentityBindingGAgent> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Twitter Identity Binding GAgent for {this.GetPrimaryKeyString()}");
    }
    
    /// <inheritdoc />
    public async Task<TwitterAuthResultDto> CreateOrUpdateBindingAsync(string twitterId, Guid userId, string username, string profileImageUrl)
    {
        try
        {
            // Create or update the binding
            if (State?.TwitterUserId == null)
            {
                RaiseEvent(new TwitterIdentityBindingCreatedLogEvent
                {
                    TwitterUserId = twitterId,
                    UserId = userId,
                    TwitterUsername = username,
                    ProfileImageUrl = profileImageUrl,
                    CreatedAt = DateTime.UtcNow
                });
                await ConfirmEvents();
                _logger.LogInformation("Created new Twitter binding for user {UserId} with Twitter ID {TwitterId}", userId, twitterId);
            }
            else
            {
                RaiseEvent(new TwitterIdentityBindingUpdatedLogEvent
                {
                    TwitterUserId = twitterId,
                    UserId = userId,
                    TwitterUsername = username,
                    ProfileImageUrl = profileImageUrl,
                    UpdatedAt = DateTime.UtcNow
                });
                
                _logger.LogInformation("Updated Twitter binding for user {UserId} with Twitter ID {TwitterId}", userId, twitterId);
            }

            return new TwitterAuthResultDto
            {
                Success = true,
                TwitterId = twitterId,
                Username = username,
                ProfileImageUrl = profileImageUrl
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating/updating Twitter binding");
            return new TwitterAuthResultDto { Success = false, Error = "Internal server error" };
        }
    }

    public Task<Guid?> GetUserIdAsync()
    {
        return Task.FromResult(State?.UserId);
    }

    protected override void GAgentTransitionState(TwitterIdentityBindingState state, StateLogEventBase<TwitterIdentityBindingLogEvent> @event)
    {
        switch (@event)
        {
            case TwitterIdentityBindingCreatedLogEvent created:
                state.TwitterUserId = created.TwitterUserId;
                state.UserId = created.UserId;
                state.TwitterUsername = created.TwitterUsername;
                state.ProfileImageUrl = created.ProfileImageUrl;
                state.CreatedAt = created.CreatedAt;
                state.UpdatedAt = created.CreatedAt;
                break;
            case TwitterIdentityBindingUpdatedLogEvent updated:
                state.UserId = updated.UserId;
                state.TwitterUsername = updated.TwitterUsername;
                state.ProfileImageUrl = updated.ProfileImageUrl;
                state.UpdatedAt = updated.UpdatedAt;
                break;
        }
    }
}