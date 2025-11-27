using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Lumen.Dtos;
using Aevatar.Application.Grains.Lumen.Options;
using Aevatar.Application.Grains.Lumen.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Lumen;

/// <summary>
/// Interface for Lumen User GAgent - manages user registration and info
/// DEPRECATED: Use ILumenUserProfileGAgent instead. This agent is kept for backward compatibility only.
/// All language management features have been migrated to LumenUserProfileGAgent.
/// </summary>
[Obsolete("This GAgent is deprecated. Use ILumenUserProfileGAgent for all user profile and language management operations. This will be removed in future versions.")]
public interface ILumenUserGAgent : IGAgent
{
    Task<UpdateUserInfoResult> UpdateUserInfoAsync(UpdateUserInfoRequest request);
    
    [ReadOnly]
    Task<GetUserInfoResult> GetUserInfoAsync();
    
    /// <summary>
    /// Clear user data (for testing purposes)
    /// </summary>
    Task<ClearUserResult> ClearUserAsync();
    
    /// <summary>
    /// Save LLM-inferred LatLong from BirthCity (internal use only, not exposed in profile API)
    /// </summary>
    Task SaveInferredLatLongAsync(string latLongInferred, string birthCity);
    
    /// <summary>
    /// Set user's current language (triggers translation for today's predictions)
    /// </summary>
    Task<SetLanguageResult> SetLanguageAsync(string newLanguage, string? userId = null);
    
    /// <summary>
    /// Get user's language information (current language and remaining daily changes)
    /// </summary>
    [ReadOnly]
    Task<GetLanguageInfoResult> GetLanguageInfoAsync();
    
    /// <summary>
    /// Initialize user's language on registration (does not count as a switch)
    /// </summary>
    Task InitializeLanguageAsync(string initialLanguage, string? userId = null);
}

[GAgent(nameof(LumenUserGAgent))]
[Reentrant]
[Obsolete("This GAgent is deprecated. Use LumenUserProfileGAgent for all user profile and language management operations. This will be removed in future versions.")]
public class LumenUserGAgent : GAgentBase<LumenUserState, LumenUserEventLog>, ILumenUserGAgent
{
    private readonly ILogger<LumenUserGAgent> _logger;
    private readonly IGrainFactory _grainFactory;
    private readonly IOptions<LumenUserProfileOptions> _profileOptions;
    
    /// <summary>
    /// Valid lumen prediction actions
    /// </summary>
    private static readonly HashSet<string> ValidActions = new()
    {
        "forecast", "horoscope", "bazi", "ziwei", "constellation", 
        "numerology", "synastry", "chineseZodiac", "mayanTotem", 
        "humanFigure", "tarot", "zhengYu"
    };

    public LumenUserGAgent(
        ILogger<LumenUserGAgent> logger,
        IGrainFactory grainFactory,
        IOptions<LumenUserProfileOptions> profileOptions)
    {
        _logger = logger;
        _grainFactory = grainFactory;
        _profileOptions = profileOptions;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Lumen user management");
    }

    /// <summary>
    /// Event-driven state transition handler
    /// </summary>
    protected sealed override void GAgentTransitionState(LumenUserState state, 
        StateLogEventBase<LumenUserEventLog> @event)
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
                state.BirthCity = registerEvent.BirthCity;
                state.LatLong = registerEvent.LatLong;
                state.MbtiType = registerEvent.MbtiType;
                state.RelationshipStatus = registerEvent.RelationshipStatus;
                state.Interests = registerEvent.Interests;
                state.CalendarType = registerEvent.CalendarType;
                state.CreatedAt = registerEvent.CreatedAt;
                state.UpdatedAt = registerEvent.CreatedAt;
                state.CurrentResidence = registerEvent.CurrentResidence;
                state.Email = registerEvent.Email;
                state.CurrentLanguage = registerEvent.InitialLanguage; // Set initial language (does not count as a switch)
                state.LastLanguageSwitchDate = null; // No switch yet
                state.TodayLanguageSwitchCount = 0; // No switches yet
                break;
            case UserClearedEvent clearEvent:
                // Clear all user data
                state.UserId = string.Empty;
                state.FirstName = string.Empty;
                state.LastName = string.Empty;
                state.Gender = default;
                state.BirthDate = default;
                state.BirthTime = default;
                state.BirthCity = null;
                state.LatLong = string.Empty;
                state.MbtiType = null;
                state.RelationshipStatus = null;
                state.Interests = null;
                state.CalendarType = default;
                state.Actions = new List<string>();
                state.CreatedAt = default;
                state.UpdatedAt = clearEvent.ClearedAt;
                state.CurrentResidence = null;
                state.Email = null;
                break;
            case UserActionsUpdatedEvent actionsEvent:
                state.Actions = actionsEvent.Actions;
                state.UpdatedAt = actionsEvent.UpdatedAt;
                break;
            case LatLongInferredEvent latLongEvent:
                state.LatLongInferred = latLongEvent.LatLongInferred;
                state.UpdatedAt = latLongEvent.InferredAt;
                break;
            case LanguageSwitchedEvent languageEvent:
                state.CurrentLanguage = languageEvent.NewLanguage;
                state.LastLanguageSwitchDate = languageEvent.SwitchDate;
                state.TodayLanguageSwitchCount = languageEvent.TodayCount;
                state.UpdatedAt = languageEvent.SwitchedAt;
                break;
        }
    }

    public async Task<UpdateUserInfoResult> UpdateUserInfoAsync(UpdateUserInfoRequest request)
    {
        try
        {
            _logger.LogDebug("[LumenUserGAgent][UpdateUserInfoAsync] Start - UserId: {UserId}", request.UserId);

            // Validate request
            var validationResult = ValidateRegisterRequest(request);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("[LumenUserGAgent][UpdateUserInfoAsync] Validation failed: {Message}", 
                    validationResult.Message);
                return new UpdateUserInfoResult
                {
                    Success = false,
                    Message = validationResult.Message
                };
            }
            
            if (!State.UserId.IsNullOrWhiteSpace() && State.UserId != request.UserId)
            {
                _logger.LogWarning("[LumenUserGAgent][UpdateUserInfoAsync] User ID mismatch: {UserId}", request.UserId);
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
                BirthCity = request.BirthCity,
                LatLong = request.LatLong,
                MbtiType = request.MbtiType,
                RelationshipStatus = request.RelationshipStatus,
                Interests = request.Interests,
                CalendarType = request.CalendarType,
                CreatedAt = now,
                CurrentResidence = request.CurrentResidence,
                Email = request.Email,
                InitialLanguage = request.InitialLanguage // Set initial language (does not count as a switch)
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[LumenUserGAgent][UpdateUserInfoAsync] User registered successfully: {UserId}", 
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
            _logger.LogError(ex, "[LumenUserGAgent][UpdateUserInfoAsync] Error registering user: {UserId}", 
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
            _logger.LogDebug("[LumenUserGAgent][GetUserInfoAsync] Getting user info for: {UserId}", 
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
                UserInfo = new LumenUserDto
                {
                    UserId = State.UserId,
                    FirstName = State.FirstName,
                    LastName = State.LastName,
                    Gender = State.Gender,
                    BirthDate = State.BirthDate,
                    BirthTime = State.BirthTime,
                    BirthCity = State.BirthCity,
                    LatLong = State.LatLong,
                    MbtiType = State.MbtiType,
                    RelationshipStatus = State.RelationshipStatus,
                    Interests = State.Interests,
                    CalendarType = State.CalendarType,
                    Actions = State.Actions,
                    CreatedAt = State.CreatedAt,
                    CurrentResidence = State.CurrentResidence,
                    Email = State.Email,
                    LatLongInferred = State.LatLongInferred,
                    CurrentLanguage = State.CurrentLanguage
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenUserGAgent][GetUserInfoAsync] Error getting user info");
            return Task.FromResult(new GetUserInfoResult
            {
                Success = false,
                Message = "Internal error occurred"
            });
        }
    }

    public async Task<ClearUserResult> ClearUserAsync()
    {
        try
        {
            _logger.LogDebug("[LumenUserGAgent][ClearUserAsync] Clearing user data for: {GrainId}", 
                this.GetPrimaryKey());

            // Check if user exists
            if (string.IsNullOrEmpty(State.UserId))
            {
                _logger.LogWarning("[LumenUserGAgent][ClearUserAsync] User not found");
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

            _logger.LogInformation("[LumenUserGAgent][ClearUserAsync] User data cleared successfully");

            return new ClearUserResult
            {
                Success = true,
                Message = "User data cleared successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenUserGAgent][ClearUserAsync] Error clearing user data");
            return new ClearUserResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    public async Task SaveInferredLatLongAsync(string latLongInferred, string birthCity)
    {
        try
        {
            _logger.LogDebug("[LumenUserGAgent][SaveInferredLatLongAsync] Saving inferred latlong for: {UserId}, City: {BirthCity}", 
                State.UserId, birthCity);

            // Check if user exists
            if (string.IsNullOrEmpty(State.UserId))
            {
                _logger.LogWarning("[LumenUserGAgent][SaveInferredLatLongAsync] User not found");
                return;
            }

            // Only save if not already exists
            if (!string.IsNullOrEmpty(State.LatLongInferred))
            {
                _logger.LogDebug("[LumenUserGAgent][SaveInferredLatLongAsync] LatLongInferred already exists, skipping");
                return;
            }

            var now = DateTime.UtcNow;

            // Raise event to save inferred latlong
            RaiseEvent(new LatLongInferredEvent
            {
                UserId = State.UserId,
                LatLongInferred = latLongInferred,
                BirthCity = birthCity,
                InferredAt = now
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[LumenUserGAgent][SaveInferredLatLongAsync] LatLongInferred saved successfully: {LatLong}", 
                latLongInferred);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenUserGAgent][SaveInferredLatLongAsync] Error saving inferred latlong");
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

    public async Task<SetLanguageResult> SetLanguageAsync(string newLanguage, string? userId = null)
    {
        try
        {
            _logger.LogDebug("[LumenUserGAgent][SetLanguageAsync] Setting language for: {UserId}, Language: {Language}", 
                State.UserId, newLanguage);

            // Auto-initialize if state is empty but userId is provided (for first-time language switch after registration)
            if (string.IsNullOrEmpty(State.UserId) && !string.IsNullOrEmpty(userId))
            {
                _logger.LogInformation("[LumenUserGAgent][SetLanguageAsync] State not initialized, auto-initializing for user: {UserId}", 
                    userId);
                await InitializeLanguageAsync(newLanguage);
                
                var maxChanges = _profileOptions.Value.MaxLanguageSwitchesPerDay;
                return new SetLanguageResult
                {
                    Success = true,
                    Message = "Language initialized successfully",
                    CurrentLanguage = newLanguage,
                    RemainingChanges = maxChanges,
                    MaxChangesPerDay = maxChanges
                };
            }

            // Check if user exists
            if (string.IsNullOrEmpty(State.UserId))
            {
                _logger.LogWarning("[LumenUserGAgent][SetLanguageAsync] User not found and no userId provided for initialization");
                return new SetLanguageResult
                {
                    Success = false,
                    Message = "User not found"
                };
            }

            // Validate language
            if (string.IsNullOrWhiteSpace(newLanguage))
            {
                return new SetLanguageResult
                {
                    Success = false,
                    Message = "Language cannot be empty"
                };
            }

            // Check if language is the same as current
            if (State.CurrentLanguage == newLanguage)
            {
                _logger.LogInformation("[LumenUserGAgent][SetLanguageAsync] Language unchanged: {Language}", newLanguage);
                var maxChanges = _profileOptions.Value.MaxLanguageSwitchesPerDay;
                var remaining = CalculateRemainingChanges(maxChanges);
                return new SetLanguageResult
                {
                    Success = true,
                    Message = "Language is already set to " + newLanguage,
                    CurrentLanguage = State.CurrentLanguage,
                    RemainingChanges = remaining,
                    MaxChangesPerDay = maxChanges
                };
            }

            var now = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(now);
            var maxChangesPerDay = _profileOptions.Value.MaxLanguageSwitchesPerDay;

            // Check if we need to reset today's count
            if (!State.LastLanguageSwitchDate.HasValue || State.LastLanguageSwitchDate.Value != today)
            {
                State.TodayLanguageSwitchCount = 0;
            }

            // Check if today's limit is reached
            if (State.TodayLanguageSwitchCount >= maxChangesPerDay)
            {
                _logger.LogWarning("[LumenUserGAgent][SetLanguageAsync] Daily language switch limit reached: {UserId}", 
                    State.UserId);
                return new SetLanguageResult
                {
                    Success = false,
                    Message = $"Daily language switch limit reached ({maxChangesPerDay} per day)",
                    CurrentLanguage = State.CurrentLanguage,
                    RemainingChanges = 0,
                    MaxChangesPerDay = maxChangesPerDay
                };
            }

            var previousLanguage = State.CurrentLanguage;
            var newCount = State.TodayLanguageSwitchCount + 1;

            // Raise event to update state
            RaiseEvent(new LanguageSwitchedEvent
            {
                UserId = State.UserId,
                PreviousLanguage = previousLanguage,
                NewLanguage = newLanguage,
                SwitchedAt = now,
                SwitchDate = today,
                TodayCount = newCount
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[LumenUserGAgent][SetLanguageAsync] Language switched from {OldLang} to {NewLang}, Count: {Count}", 
                previousLanguage, newLanguage, newCount);

            // Trigger translation for today's predictions (async, fire-and-forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    await TriggerPredictionTranslationsAsync(State.UserId, newLanguage);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[LumenUserGAgent][SetLanguageAsync] Error triggering translations");
                }
            });

            return new SetLanguageResult
            {
                Success = true,
                Message = string.Empty,
                CurrentLanguage = newLanguage,
                RemainingChanges = maxChangesPerDay - newCount,
                MaxChangesPerDay = maxChangesPerDay
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenUserGAgent][SetLanguageAsync] Error setting language");
            return new SetLanguageResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    public Task<GetLanguageInfoResult> GetLanguageInfoAsync()
    {
        try
        {
            _logger.LogDebug("[LumenUserGAgent][GetLanguageInfoAsync] Getting language info for: {UserId}", 
                State.UserId);

            if (string.IsNullOrEmpty(State.UserId))
            {
                return Task.FromResult(new GetLanguageInfoResult
                {
                    Success = false,
                    Message = "User not found"
                });
            }

            var maxChanges = _profileOptions.Value.MaxLanguageSwitchesPerDay;
            var remaining = CalculateRemainingChanges(maxChanges);

            return Task.FromResult(new GetLanguageInfoResult
            {
                Success = true,
                Message = string.Empty,
                CurrentLanguage = State.CurrentLanguage,
                RemainingChanges = remaining,
                MaxChangesPerDay = maxChanges,
                LastSwitchDate = State.LastLanguageSwitchDate.HasValue 
                    ? State.LastLanguageSwitchDate.Value.ToDateTime(TimeOnly.MinValue) 
                    : (DateTime?)null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenUserGAgent][GetLanguageInfoAsync] Error getting language info");
            return Task.FromResult(new GetLanguageInfoResult
            {
                Success = false,
                Message = "Internal error occurred"
            });
        }
    }

    public async Task InitializeLanguageAsync(string initialLanguage, string? userId = null)
    {
        try
        {
            _logger.LogInformation("[LumenUserGAgent][InitializeLanguageAsync] Initializing language for new user: {UserId}, Language: {Language}", 
                State.UserId, initialLanguage);

            // Simple event to set initial language without counting as a switch
            RaiseEvent(new LanguageSwitchedEvent
            {
                UserId = State.UserId,
                PreviousLanguage = string.Empty,
                NewLanguage = initialLanguage,
                SwitchedAt = DateTime.UtcNow,
                SwitchDate = DateOnly.FromDateTime(DateTime.UtcNow),
                TodayCount = 0 // Does not count as a switch
            });

            await ConfirmEvents();

            _logger.LogInformation("[LumenUserGAgent][InitializeLanguageAsync] Language initialized: {Language}", initialLanguage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenUserGAgent][InitializeLanguageAsync] Error initializing language");
        }
    }

    /// <summary>
    /// Calculate remaining language switches for today
    /// </summary>
    private int CalculateRemainingChanges(int maxChangesPerDay)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        
        // If no switches yet, or last switch was not today, return max
        if (!State.LastLanguageSwitchDate.HasValue || State.LastLanguageSwitchDate.Value != today)
        {
            return maxChangesPerDay;
        }
        
        // Return remaining for today
        return Math.Max(0, maxChangesPerDay - State.TodayLanguageSwitchCount);
    }

    /// <summary>
    /// Trigger translation for today's predictions (Daily, Yearly, Lifetime)
    /// </summary>
    private async Task TriggerPredictionTranslationsAsync(string userId, string targetLanguage)
    {
        try
        {
            _logger.LogInformation("[LumenUserGAgent][TriggerPredictionTranslationsAsync] Triggering translations for: {UserId}, Language: {Language}", 
                userId, targetLanguage);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var currentYear = DateTime.UtcNow.Year;

            // Get user info to pass to prediction grains
            var userResult = await GetUserInfoAsync();
            if (!userResult.Success || userResult.UserInfo == null)
            {
                _logger.LogWarning("[LumenUserGAgent][TriggerPredictionTranslationsAsync] Failed to get user info");
                return;
            }

            var userInfo = userResult.UserInfo;

            // Trigger Daily translation
            var dailyGrain = _grainFactory.GetGrain<ILumenPredictionGAgent>(
                CommonHelper.StringToGuid(userId),
                $"{PredictionType.Daily}_{today:yyyy-MM-dd}");
            _ = dailyGrain.TriggerTranslationAsync(userInfo, targetLanguage);

            // Trigger Yearly translation
            var yearlyGrain = _grainFactory.GetGrain<ILumenPredictionGAgent>(
                CommonHelper.StringToGuid(userId),
                $"{PredictionType.Yearly}_{currentYear}");
            _ = yearlyGrain.TriggerTranslationAsync(userInfo, targetLanguage);

            // Trigger Lifetime translation
            var lifetimeGrain = _grainFactory.GetGrain<ILumenPredictionGAgent>(
                CommonHelper.StringToGuid(userId),
                $"{PredictionType.Lifetime}");
            _ = lifetimeGrain.TriggerTranslationAsync(userInfo, targetLanguage);

            _logger.LogInformation("[LumenUserGAgent][TriggerPredictionTranslationsAsync] Translation triggers sent");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenUserGAgent][TriggerPredictionTranslationsAsync] Error triggering translations");
        }
    }
}

