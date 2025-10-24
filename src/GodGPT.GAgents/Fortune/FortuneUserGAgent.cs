using Aevatar.Application.Grains.Fortune.Dtos;
using Aevatar.Application.Grains.Fortune.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Fortune;

/// <summary>
/// Interface for Fortune User GAgent - manages user registration and info
/// </summary>
public interface IFortuneUserGAgent : IGAgent
{
    Task<UpdateUserInfoResult> UpdateUserInfoAsync(UpdateUserInfoRequest request);
    
    [ReadOnly]
    Task<GetUserInfoResult> GetUserInfoAsync();
    
    /// <summary>
    /// Update user selected actions
    /// </summary>
    Task<UpdateUserActionsResult> UpdateUserActionsAsync(UpdateUserActionsRequest request);
    
    /// <summary>
    /// Clear user data (for testing purposes)
    /// </summary>
    Task<ClearUserResult> ClearUserAsync();
}

[GAgent(nameof(FortuneUserGAgent))]
[Reentrant]
public class FortuneUserGAgent : GAgentBase<FortuneUserState, FortuneUserEventLog>, IFortuneUserGAgent
{
    private readonly ILogger<FortuneUserGAgent> _logger;
    
    /// <summary>
    /// Valid fortune prediction actions
    /// </summary>
    private static readonly HashSet<string> ValidActions = new()
    {
        "forecast", "horoscope", "bazi", "ziwei", "constellation", 
        "numerology", "synastry", "chineseZodiac", "mayanTotem", 
        "humanFigure", "tarot", "zhengYu"
    };

    public FortuneUserGAgent(ILogger<FortuneUserGAgent> logger)
    {
        _logger = logger;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Fortune user management");
    }

    /// <summary>
    /// Event-driven state transition handler
    /// </summary>
    protected sealed override void GAgentTransitionState(FortuneUserState state, 
        StateLogEventBase<FortuneUserEventLog> @event)
    {
        switch (@event)
        {
            case UserRegisteredEvent registerEvent:
                state.UserId = registerEvent.UserId;
                state.FirstName = registerEvent.FirstName;
                state.LastName = registerEvent.LastName;
                state.Gender = registerEvent.Gender;
                state.BirthDate = registerEvent.BirthDate;
                state.BirthTime = registerEvent.BirthTime;
                state.BirthCountry = registerEvent.BirthCountry;
                state.BirthCity = registerEvent.BirthCity;
                state.MbtiType = registerEvent.MbtiType;
                state.RelationshipStatus = registerEvent.RelationshipStatus;
                state.Interests = registerEvent.Interests;
                state.CalendarType = registerEvent.CalendarType;
                state.CreatedAt = registerEvent.CreatedAt;
                state.UpdatedAt = registerEvent.CreatedAt;
                state.CurrentResidence = registerEvent.CurrentResidence;
                state.Email = registerEvent.Email;
                break;
            case UserClearedEvent clearEvent:
                // Clear all user data
                state.UserId = string.Empty;
                state.FirstName = string.Empty;
                state.LastName = string.Empty;
                state.Gender = default;
                state.BirthDate = default;
                state.BirthTime = default;
                state.BirthCountry = string.Empty;
                state.BirthCity = string.Empty;
                state.MbtiType = null;
                state.RelationshipStatus = null;
                state.Interests = null;
                state.CalendarType = default;
                state.Actions = new List<string>();
                state.CreatedAt = default;
                state.UpdatedAt = clearEvent.ClearedAt;
                state.CurrentResidence = string.Empty;
                state.Email = string.Empty;
                break;
            case UserActionsUpdatedEvent actionsEvent:
                state.Actions = actionsEvent.Actions;
                state.UpdatedAt = actionsEvent.UpdatedAt;
                break;
        }
    }

    public async Task<UpdateUserInfoResult> UpdateUserInfoAsync(UpdateUserInfoRequest request)
    {
        try
        {
            _logger.LogDebug("[FortuneUserGAgent][UpdateUserInfoAsync] Start - UserId: {UserId}", request.UserId);

            // Validate request
            var validationResult = ValidateRegisterRequest(request);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("[FortuneUserGAgent][UpdateUserInfoAsync] Validation failed: {Message}", 
                    validationResult.Message);
                return new UpdateUserInfoResult
                {
                    Success = false,
                    Message = validationResult.Message
                };
            }
            
            if (!State.UserId.IsNullOrWhiteSpace() && State.UserId != request.UserId)
            {
                _logger.LogWarning("[FortuneUserGAgent][UpdateUserInfoAsync] User ID mismatch: {UserId}", request.UserId);
                return new UpdateUserInfoResult
                {
                    Success = false,
                    Message = "User ID mismatch"
                };
            }

            var now = DateTime.UtcNow;

            // Raise event to update state
            RaiseEvent(new UserRegisteredEvent
            {
                UserId = request.UserId,
                FirstName = request.FirstName,
                LastName = request.LastName,
                Gender = request.Gender,
                BirthDate = request.BirthDate,
                BirthTime = request.BirthTime,
                BirthCountry = request.BirthCountry,
                BirthCity = request.BirthCity,
                MbtiType = request.MbtiType,
                RelationshipStatus = request.RelationshipStatus,
                Interests = request.Interests,
                CalendarType = request.CalendarType,
                CreatedAt = now,
                CurrentResidence = request.CurrentResidence,
                Email = request.Email
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[FortuneUserGAgent][UpdateUserInfoAsync] User registered successfully: {UserId}", 
                request.UserId);

            return new UpdateUserInfoResult
            {
                Success = true,
                Message = string.Empty,
                UserId = request.UserId,
                CreatedAt = now
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortuneUserGAgent][UpdateUserInfoAsync] Error registering user: {UserId}", 
                request.UserId);
            return new UpdateUserInfoResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    public Task<GetUserInfoResult> GetUserInfoAsync()
    {
        try
        {
            _logger.LogDebug("[FortuneUserGAgent][GetUserInfoAsync] Getting user info for: {UserId}", 
                this.GetPrimaryKey());

            if (string.IsNullOrEmpty(State.UserId))
            {
                return Task.FromResult(new GetUserInfoResult
                {
                    Success = false,
                    Message = "User not found"
                });
            }

            return Task.FromResult(new GetUserInfoResult
            {
                Success = true,
                Message = string.Empty,
                UserInfo = new FortuneUserDto
                {
                    UserId = State.UserId,
                    FirstName = State.FirstName,
                    LastName = State.LastName,
                    Gender = State.Gender,
                    BirthDate = State.BirthDate,
                    BirthTime = State.BirthTime,
                    BirthCountry = State.BirthCountry,
                    BirthCity = State.BirthCity,
                    MbtiType = State.MbtiType,
                    RelationshipStatus = State.RelationshipStatus,
                    Interests = State.Interests,
                    CalendarType = State.CalendarType,
                    Actions = State.Actions,
                    CreatedAt = State.CreatedAt,
                    CurrentResidence = State.CurrentResidence,
                    Email = State.Email
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortuneUserGAgent][GetUserInfoAsync] Error getting user info");
            return Task.FromResult(new GetUserInfoResult
            {
                Success = false,
                Message = "Internal error occurred"
            });
        }
    }

    public async Task<UpdateUserActionsResult> UpdateUserActionsAsync(UpdateUserActionsRequest request)
    {
        try
        {
            _logger.LogDebug("[FortuneUserGAgent][UpdateUserActionsAsync] Start - UserId: {UserId}", request.UserId);

            // Check if user exists
            if (string.IsNullOrEmpty(State.UserId))
            {
                _logger.LogWarning("[FortuneUserGAgent][UpdateUserActionsAsync] User not found: {UserId}", request.UserId);
                return new UpdateUserActionsResult
                {
                    Success = false,
                    Message = "User not found"
                };
            }

            // Validate that the user ID matches
            if (State.UserId != request.UserId)
            {
                _logger.LogWarning("[FortuneUserGAgent][UpdateUserActionsAsync] User ID mismatch. State: {StateUserId}, Request: {RequestUserId}", 
                    State.UserId, request.UserId);
                return new UpdateUserActionsResult
                {
                    Success = false,
                    Message = "User ID mismatch"
                };
            }

            // Validate actions
            var validationResult = ValidateActions(request.Actions);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("[FortuneUserGAgent][UpdateUserActionsAsync] Validation failed: {Message}", 
                    validationResult.Message);
                return new UpdateUserActionsResult
                {
                    Success = false,
                    Message = validationResult.Message
                };
            }

            var now = DateTime.UtcNow;

            // Raise event to update actions
            RaiseEvent(new UserActionsUpdatedEvent
            {
                UserId = request.UserId,
                Actions = request.Actions,
                UpdatedAt = now
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[FortuneUserGAgent][UpdateUserActionsAsync] User actions updated successfully: {UserId}, Actions: {Actions}", 
                request.UserId, string.Join(", ", request.Actions));

            return new UpdateUserActionsResult
            {
                Success = true,
                Message = string.Empty,
                UpdatedActions = request.Actions,
                UpdatedAt = now
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortuneUserGAgent][UpdateUserActionsAsync] Error updating user actions: {UserId}", 
                request.UserId);
            return new UpdateUserActionsResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    public async Task<ClearUserResult> ClearUserAsync()
    {
        try
        {
            _logger.LogDebug("[FortuneUserGAgent][ClearUserAsync] Clearing user data for: {GrainId}", 
                this.GetPrimaryKey());

            // Check if user exists
            if (string.IsNullOrEmpty(State.UserId))
            {
                _logger.LogWarning("[FortuneUserGAgent][ClearUserAsync] User not found");
                return new ClearUserResult
                {
                    Success = false,
                    Message = "User not found"
                };
            }

            var now = DateTime.UtcNow;

            // Raise event to clear state
            RaiseEvent(new UserClearedEvent
            {
                ClearedAt = now
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[FortuneUserGAgent][ClearUserAsync] User data cleared successfully");

            return new ClearUserResult
            {
                Success = true,
                Message = "User data cleared successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortuneUserGAgent][ClearUserAsync] Error clearing user data");
            return new ClearUserResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    /// <summary>
    /// Validate register request
    /// </summary>
    private (bool IsValid, string Message) ValidateRegisterRequest(UpdateUserInfoRequest infoRequest)
    {
        if (string.IsNullOrWhiteSpace(infoRequest.UserId) || infoRequest.UserId.Length < 3 || infoRequest.UserId.Length > 50)
        {
            return (false, "UserId must be between 3 and 50 characters");
        }

        if (string.IsNullOrWhiteSpace(infoRequest.FirstName))
        {
            return (false, "First name is required");
        }

        if (string.IsNullOrWhiteSpace(infoRequest.LastName))
        {
            return (false, "Last name is required");
        }

        if (infoRequest.BirthDate == default || infoRequest.BirthDate > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return (false, "Invalid birth date");
        }

        return (true, string.Empty);
    }

    /// <summary>
    /// Validate actions list
    /// </summary>
    private (bool IsValid, string Message) ValidateActions(List<string> actions)
    {
        if (actions == null)
        {
            return (false, "Actions list cannot be null");
        }

        // Check for invalid actions
        var invalidActions = actions.Where(action => !ValidActions.Contains(action)).ToList();
        if (invalidActions.Any())
        {
            return (false, $"Invalid actions: {string.Join(", ", invalidActions)}");
        }

        // Check for duplicates
        if (actions.Count != actions.Distinct().Count())
        {
            return (false, "Duplicate actions are not allowed");
        }

        // Check maximum limit (optional - you can adjust this)
        if (actions.Count > ValidActions.Count)
        {
            return (false, $"Too many actions. Maximum allowed: {ValidActions.Count}");
        }

        return (true, string.Empty);
    }
}

