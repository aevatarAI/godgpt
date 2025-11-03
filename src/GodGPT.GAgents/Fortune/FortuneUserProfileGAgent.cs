using Aevatar.Application.Grains.Fortune.Dtos;
using Aevatar.Application.Grains.Fortune.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Fortune;

/// <summary>
/// Interface for Fortune User Profile GAgent (V2) - manages user profile with FullName
/// </summary>
public interface IFortuneUserProfileGAgent : IGAgent
{
    Task<UpdateUserProfileResult> UpdateUserProfileAsync(UpdateUserProfileRequest request);
    
    [ReadOnly]
    Task<GetUserProfileResult> GetUserProfileAsync();
    
    Task<UpdateUserActionsResult> UpdateUserActionsAsync(UpdateUserActionsRequest request);
    
    Task<GenerateProfileInsightsResult> GenerateProfileInsightsAsync(string aiResponse);
}

[GAgent(nameof(FortuneUserProfileGAgent))]
[Reentrant]
public class FortuneUserProfileGAgent : GAgentBase<FortuneUserProfileState, FortuneUserProfileEventLog>, IFortuneUserProfileGAgent
{
    private readonly ILogger<FortuneUserProfileGAgent> _logger;
    
    /// <summary>
    /// Valid fortune prediction actions
    /// </summary>
    private static readonly HashSet<string> ValidActions = new()
    {
        "forecast", "horoscope", "bazi", "ziwei", "constellation", 
        "numerology", "synastry", "chineseZodiac", "mayanTotem", 
        "humanFigure", "tarot", "zhengYu"
    };

    public FortuneUserProfileGAgent(ILogger<FortuneUserProfileGAgent> logger)
    {
        _logger = logger;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Fortune user profile management (V2)");
    }

    /// <summary>
    /// Event-driven state transition handler
    /// </summary>
    protected sealed override void GAgentTransitionState(FortuneUserProfileState state, 
        StateLogEventBase<FortuneUserProfileEventLog> @event)
    {
        switch (@event)
        {
            case UserProfileUpdatedEvent updateEvent:
                state.UserId = updateEvent.UserId;
                state.FullName = updateEvent.FullName;
                state.Gender = updateEvent.Gender;
                state.BirthDate = updateEvent.BirthDate;
                state.BirthTime = updateEvent.BirthTime;
                state.BirthCountry = updateEvent.BirthCountry;
                state.BirthCity = updateEvent.BirthCity;
                state.MbtiType = updateEvent.MbtiType;
                state.RelationshipStatus = updateEvent.RelationshipStatus;
                state.Interests = updateEvent.Interests;
                state.CalendarType = updateEvent.CalendarType;
                state.UpdatedAt = updateEvent.UpdatedAt;
                state.CurrentResidence = updateEvent.CurrentResidence;
                state.Email = updateEvent.Email;
                if (state.CreatedAt == default)
                {
                    state.CreatedAt = updateEvent.UpdatedAt;
                }
                break;
            case UserProfileActionsUpdatedEvent actionsEvent:
                state.Actions = actionsEvent.Actions;
                state.UpdatedAt = actionsEvent.UpdatedAt;
                break;
            case ProfileInsightsGeneratedEvent insightsEvent:
                state.Astrology = insightsEvent.Astrology;
                state.Bazi = insightsEvent.Bazi;
                state.Zodiac = insightsEvent.Zodiac;
                state.InsightsGeneratedAt = insightsEvent.GeneratedAt;
                break;
        }
    }

    public async Task<UpdateUserProfileResult> UpdateUserProfileAsync(UpdateUserProfileRequest request)
    {
        try
        {
            _logger.LogDebug("[FortuneUserProfileGAgent][UpdateUserProfileAsync] Start - UserId: {UserId}", request.UserId);

            // Validate request
            var validationResult = ValidateProfileRequest(request);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("[FortuneUserProfileGAgent][UpdateUserProfileAsync] Validation failed: {Message}", 
                    validationResult.Message);
                return new UpdateUserProfileResult
                {
                    Success = false,
                    Message = validationResult.Message
                };
            }
            
            if (!State.UserId.IsNullOrWhiteSpace() && State.UserId != request.UserId)
            {
                _logger.LogWarning("[FortuneUserProfileGAgent][UpdateUserProfileAsync] User ID mismatch: {UserId}", request.UserId);
                return new UpdateUserProfileResult
                {
                    Success = false,
                    Message = "User ID mismatch"
                };
            }

            var now = DateTime.UtcNow;

            // Raise event to update state
            RaiseEvent(new UserProfileUpdatedEvent
            {
                UserId = request.UserId,
                FullName = request.FullName,
                Gender = request.Gender,
                BirthDate = request.BirthDate,
                BirthTime = request.BirthTime,
                BirthCountry = request.BirthCountry,
                BirthCity = request.BirthCity,
                MbtiType = request.MbtiType,
                RelationshipStatus = request.RelationshipStatus,
                Interests = request.Interests,
                CalendarType = request.CalendarType,
                UpdatedAt = now,
                CurrentResidence = request.CurrentResidence,
                Email = request.Email
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[FortuneUserProfileGAgent][UpdateUserProfileAsync] User profile updated successfully: {UserId}", 
                request.UserId);

            return new UpdateUserProfileResult
            {
                Success = true,
                Message = string.Empty,
                UserId = request.UserId,
                CreatedAt = State.CreatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortuneUserProfileGAgent][UpdateUserProfileAsync] Error updating user profile: {UserId}", 
                request.UserId);
            return new UpdateUserProfileResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    public Task<GetUserProfileResult> GetUserProfileAsync()
    {
        try
        {
            _logger.LogDebug("[FortuneUserProfileGAgent][GetUserProfileAsync] Getting user profile for: {UserId}", 
                this.GetPrimaryKey());

            if (string.IsNullOrEmpty(State.UserId))
            {
                return Task.FromResult(new GetUserProfileResult
                {
                    Success = false,
                    Message = "User profile not found"
                });
            }

            return Task.FromResult(new GetUserProfileResult
            {
                Success = true,
                Message = string.Empty,
                UserProfile = new FortuneUserProfileDto
                {
                    UserId = State.UserId,
                    FullName = State.FullName,
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
                    Email = State.Email,
                    Astrology = State.Astrology,
                    Bazi = State.Bazi,
                    Zodiac = State.Zodiac,
                    InsightsGeneratedAt = State.InsightsGeneratedAt
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortuneUserProfileGAgent][GetUserProfileAsync] Error getting user profile");
            return Task.FromResult(new GetUserProfileResult
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
            _logger.LogDebug("[FortuneUserProfileGAgent][UpdateUserActionsAsync] Start - UserId: {UserId}", request.UserId);

            // Check if user exists
            if (string.IsNullOrEmpty(State.UserId))
            {
                _logger.LogWarning("[FortuneUserProfileGAgent][UpdateUserActionsAsync] User not found: {UserId}", request.UserId);
                return new UpdateUserActionsResult
                {
                    Success = false,
                    Message = "User not found"
                };
            }

            // Validate that the user ID matches
            if (State.UserId != request.UserId)
            {
                _logger.LogWarning("[FortuneUserProfileGAgent][UpdateUserActionsAsync] User ID mismatch. State: {StateUserId}, Request: {RequestUserId}", 
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
                _logger.LogWarning("[FortuneUserProfileGAgent][UpdateUserActionsAsync] Validation failed: {Message}", 
                    validationResult.Message);
                return new UpdateUserActionsResult
                {
                    Success = false,
                    Message = validationResult.Message
                };
            }

            var now = DateTime.UtcNow;

            // Raise event to update actions
            RaiseEvent(new UserProfileActionsUpdatedEvent
            {
                UserId = request.UserId,
                Actions = request.Actions,
                UpdatedAt = now
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[FortuneUserProfileGAgent][UpdateUserActionsAsync] User actions updated successfully: {UserId}, Actions: {Actions}", 
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
            _logger.LogError(ex, "[FortuneUserProfileGAgent][UpdateUserActionsAsync] Error updating user actions: {UserId}", 
                request.UserId);
            return new UpdateUserActionsResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    /// <summary>
    /// Validate profile request
    /// </summary>
    private (bool IsValid, string Message) ValidateProfileRequest(UpdateUserProfileRequest profileRequest)
    {
        if (string.IsNullOrWhiteSpace(profileRequest.UserId) || profileRequest.UserId.Length < 3 || profileRequest.UserId.Length > 50)
        {
            return (false, "UserId must be between 3 and 50 characters");
        }

        if (string.IsNullOrWhiteSpace(profileRequest.FullName))
        {
            return (false, "Full name is required");
        }

        if (profileRequest.BirthDate == default || profileRequest.BirthDate > DateOnly.FromDateTime(DateTime.UtcNow))
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

        // Check maximum limit
        if (actions.Count > ValidActions.Count)
        {
            return (false, $"Too many actions. Maximum allowed: {ValidActions.Count}");
        }

        return (true, string.Empty);
    }

    public async Task<GenerateProfileInsightsResult> GenerateProfileInsightsAsync(string aiResponse)
    {
        try
        {
            _logger.LogDebug("[FortuneUserProfileGAgent][GenerateProfileInsightsAsync] Parsing AI response for user: {UserId}", 
                State.UserId);

            // Parse AI response to extract three parts: astrology, bazi, zodiac
            var (astrology, bazi, zodiac) = ParseProfileInsightsResponse(aiResponse);

            if (astrology == null || bazi == null || zodiac == null)
            {
                _logger.LogError("[FortuneUserProfileGAgent][GenerateProfileInsightsAsync] Failed to parse insights");
                return new GenerateProfileInsightsResult
                {
                    Success = false,
                    Message = "Failed to parse AI response"
                };
            }

            var now = DateTime.UtcNow;

            // Raise event to save insights
            RaiseEvent(new ProfileInsightsGeneratedEvent
            {
                UserId = State.UserId,
                Astrology = astrology,
                Bazi = bazi,
                Zodiac = zodiac,
                GeneratedAt = now
            });

            await ConfirmEvents();

            _logger.LogInformation("[FortuneUserProfileGAgent][GenerateProfileInsightsAsync] Insights generated successfully for user: {UserId}", 
                State.UserId);

            return new GenerateProfileInsightsResult
            {
                Success = true,
                Message = string.Empty,
                Astrology = astrology,
                Bazi = bazi,
                Zodiac = zodiac,
                GeneratedAt = now
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortuneUserProfileGAgent][GenerateProfileInsightsAsync] Error generating insights");
            return new GenerateProfileInsightsResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    /// <summary>
    /// Parse AI response to extract profile insights (astrology, bazi, zodiac)
    /// </summary>
    private (Dictionary<string, string>?, Dictionary<string, string>?, Dictionary<string, string>?) ParseProfileInsightsResponse(string aiResponse)
    {
        try
        {
            var jsonStart = aiResponse.IndexOf('{');
            var jsonEnd = aiResponse.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonString = aiResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var response = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                
                if (response == null)
                {
                    return (null, null, null);
                }

                // Extract and serialize each section
                var astrology = new Dictionary<string, string>();
                var bazi = new Dictionary<string, string>();
                var zodiac = new Dictionary<string, string>();

                // Parse Astrology
                if (response.ContainsKey("astrology") && response["astrology"] != null)
                {
                    var astrologyJson = Newtonsoft.Json.JsonConvert.SerializeObject(response["astrology"]);
                    var astrologyObj = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(astrologyJson);
                    
                    if (astrologyObj != null)
                    {
                        foreach (var kvp in astrologyObj)
                        {
                            astrology[kvp.Key] = kvp.Value is string str ? str : Newtonsoft.Json.JsonConvert.SerializeObject(kvp.Value);
                        }
                    }
                }

                // Parse Bazi
                if (response.ContainsKey("bazi") && response["bazi"] != null)
                {
                    var baziJson = Newtonsoft.Json.JsonConvert.SerializeObject(response["bazi"]);
                    var baziObj = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(baziJson);
                    
                    if (baziObj != null)
                    {
                        foreach (var kvp in baziObj)
                        {
                            bazi[kvp.Key] = kvp.Value is string str ? str : Newtonsoft.Json.JsonConvert.SerializeObject(kvp.Value);
                        }
                    }
                }

                // Parse Zodiac
                if (response.ContainsKey("zodiac") && response["zodiac"] != null)
                {
                    var zodiacJson = Newtonsoft.Json.JsonConvert.SerializeObject(response["zodiac"]);
                    var zodiacObj = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(zodiacJson);
                    
                    if (zodiacObj != null)
                    {
                        foreach (var kvp in zodiacObj)
                        {
                            zodiac[kvp.Key] = kvp.Value is string str ? str : Newtonsoft.Json.JsonConvert.SerializeObject(kvp.Value);
                        }
                    }
                }

                return (astrology, bazi, zodiac);
            }

            return (null, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortuneUserProfileGAgent][ParseProfileInsightsResponse] Failed to parse");
            return (null, null, null);
        }
    }
}

