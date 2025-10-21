using Aevatar.Application.Grains.Fortune.Dtos;
using Aevatar.Application.Grains.Fortune.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Fortune;

/// <summary>
/// Interface for Fortune User GAgent - manages user registration and info
/// </summary>
public interface IFortuneUserGAgent : IGrainWithStringKey
{
    Task<RegisterUserResult> RegisterAsync(RegisterUserRequest request);
    
    [ReadOnly]
    Task<GetUserInfoResult> GetUserInfoAsync();
}

[GAgent(nameof(FortuneUserGAgent))]
[Reentrant]
public class FortuneUserGAgent : GAgentBase<FortuneUserState, FortuneUserEventLog>, IFortuneUserGAgent
{
    private readonly ILogger<FortuneUserGAgent> _logger;

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
                break;
        }
    }

    public async Task<RegisterUserResult> RegisterAsync(RegisterUserRequest request)
    {
        try
        {
            _logger.LogDebug("[FortuneUserGAgent][RegisterAsync] Start - UserId: {UserId}", request.UserId);

            // Check if user already exists
            if (!string.IsNullOrEmpty(State.UserId))
            {
                _logger.LogWarning("[FortuneUserGAgent][RegisterAsync] User already exists: {UserId}", request.UserId);
                return new RegisterUserResult
                {
                    Success = false,
                    Message = "User already exists"
                };
            }

            // Validate request
            var validationResult = ValidateRegisterRequest(request);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("[FortuneUserGAgent][RegisterAsync] Validation failed: {Message}", 
                    validationResult.Message);
                return new RegisterUserResult
                {
                    Success = false,
                    Message = validationResult.Message
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
                CreatedAt = now
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[FortuneUserGAgent][RegisterAsync] User registered successfully: {UserId}", 
                request.UserId);

            return new RegisterUserResult
            {
                Success = true,
                Message = string.Empty,
                UserId = request.UserId,
                CreatedAt = now
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortuneUserGAgent][RegisterAsync] Error registering user: {UserId}", 
                request.UserId);
            return new RegisterUserResult
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
                    CreatedAt = State.CreatedAt
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

    /// <summary>
    /// Validate register request
    /// </summary>
    private (bool IsValid, string Message) ValidateRegisterRequest(RegisterUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) || request.UserId.Length < 3 || request.UserId.Length > 50)
        {
            return (false, "UserId must be between 3 and 50 characters");
        }

        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            return (false, "First name is required");
        }

        if (string.IsNullOrWhiteSpace(request.LastName))
        {
            return (false, "Last name is required");
        }

        if (request.BirthDate == default || request.BirthDate > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return (false, "Invalid birth date");
        }

        if (string.IsNullOrWhiteSpace(request.BirthCountry))
        {
            return (false, "Birth country is required");
        }

        if (string.IsNullOrWhiteSpace(request.BirthCity))
        {
            return (false, "Birth city is required");
        }

        return (true, string.Empty);
    }
}

