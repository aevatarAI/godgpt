using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Lumen.Dtos;
using Aevatar.Application.Grains.Lumen.Options;
using Aevatar.Application.Grains.Lumen.SEvents;
using Aevatar.Application.Grains.UserInfo;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Lumen;

/// <summary>
/// Interface for Lumen User Profile GAgent (V2) - manages user profile with FullName
/// </summary>
public interface ILumenUserProfileGAgent : IGAgent
{
    Task<UpdateUserProfileResult> UpdateUserProfileAsync(UpdateUserProfileRequest request);
    
    [ReadOnly]
    Task<GetUserProfileResult> GetUserProfileAsync(Guid userId, string userLanguage = "en");
    
    /// <summary>
    /// Get raw state data directly without any migration logic - used for migration checks to prevent circular dependency
    /// </summary>
    [ReadOnly]
    Task<LumenUserProfileDto?> GetRawStateAsync();
    
    /// <summary>
    /// Clear user profile data (for testing purposes)
    /// </summary>
    Task<ClearUserResult> ClearUserAsync();
    
    /// <summary>
    /// Get remaining profile update count for the current week
    /// </summary>
    [ReadOnly]
    Task<GetRemainingUpdatesResult> GetRemainingUpdatesAsync();
    
    /// <summary>
    /// Update user icon (with daily upload limit)
    /// </summary>
    Task<UpdateIconResult> UpdateIconAsync(string iconUrl);
    
    /// <summary>
    /// Set user's current language (triggers translation for today's predictions)
    /// </summary>
    Task<SetLanguageResult> SetLanguageAsync(string newLanguage);
    
    /// <summary>
    /// Get user's language information (current language and remaining daily changes)
    /// </summary>
    [ReadOnly]
    Task<GetLanguageInfoResult> GetLanguageInfoAsync();
    
    /// <summary>
    /// Initialize user's language on registration (does not count as a switch)
    /// </summary>
    Task InitializeLanguageAsync(string initialLanguage);
    
    /// <summary>
    /// Save LLM-inferred LatLong from BirthCity (internal use only, not exposed in profile API)
    /// </summary>
    Task SaveInferredLatLongAsync(string latLongInferred, string birthCity);
    
    /// <summary>
    /// Update user's time zone (does NOT count as profile update, only updates reminder)
    /// </summary>
    Task<UpdateTimeZoneResult> UpdateTimeZoneAsync(UpdateTimeZoneRequest request);
}

[GAgent(nameof(LumenUserProfileGAgent))]
[Reentrant]
public class LumenUserProfileGAgent : GAgentBase<LumenUserProfileState, LumenUserProfileEventLog>, ILumenUserProfileGAgent
{
    private readonly ILogger<LumenUserProfileGAgent> _logger;
    private readonly LumenUserProfileOptions _options;
    
    /// <summary>
    /// Maximum number of profile updates allowed per week (for testing: 100)
    /// </summary>
    [Obsolete("Use _options.MaxProfileUpdatesPerWeek instead. This constant is kept as fallback only.")]
    private const int MaxProfileUpdatesPerWeek = 100;
    
    /// <summary>
    /// Valid lumen prediction actions
    /// </summary>
    private static readonly HashSet<string> ValidActions = new()
    {
        "forecast", "horoscope", "bazi", "ziwei", "constellation", 
        "numerology", "synastry", "chineseZodiac", "mayanTotem", 
        "humanFigure", "tarot", "zhengYu"
    };

    public LumenUserProfileGAgent(
        ILogger<LumenUserProfileGAgent> logger,
        IOptions<LumenUserProfileOptions> options)
    {
        _logger = logger;
        _options = options?.Value ?? new LumenUserProfileOptions(); // Fallback to default if options not configured
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Lumen user profile management (V2)");
    }

    /// <summary>
    /// Event-driven state transition handler
    /// </summary>
    protected sealed override void GAgentTransitionState(LumenUserProfileState state, 
        StateLogEventBase<LumenUserProfileEventLog> @event)
    {
        switch (@event)
        {
            case UserProfileUpdatedEvent updateEvent:
                state.UserId = updateEvent.UserId;
                state.FullName = updateEvent.FullName;
                state.Gender = updateEvent.Gender;
                state.BirthDate = updateEvent.BirthDate;
                state.BirthTime = updateEvent.BirthTime;
                state.BirthCity = updateEvent.BirthCity;
                state.LatLong = updateEvent.LatLong;
                state.MbtiType = updateEvent.MbtiType;
                state.RelationshipStatus = updateEvent.RelationshipStatus;
                state.Interests = updateEvent.Interests;
                state.InterestsList = updateEvent.InterestsList;
                state.CalendarType = updateEvent.CalendarType;
                state.UpdatedAt = updateEvent.UpdatedAt;
                state.CurrentResidence = updateEvent.CurrentResidence;
                state.Email = updateEvent.Email;
                state.Occupation = updateEvent.Occupation;
                state.Icon = updateEvent.Icon;
                state.CurrentTimeZone = updateEvent.CurrentTimeZone; // Set timezone (optional)
                state.IsDeleted = false; // Clear deleted flag on profile update/registration
                
                // Record update timestamp for rate limiting (only for actual updates, not initial registration)
                var isInitialRegistration = state.CreatedAt == default;
                if (!isInitialRegistration)
                {
                state.UpdateHistory.Add(updateEvent.UpdatedAt);
                }
                
                // Set CreatedAt on first registration
                if (isInitialRegistration)
                {
                    state.CreatedAt = updateEvent.UpdatedAt;
                }
                break;
            case UserProfileActionsUpdatedEvent actionsEvent:
                state.Actions = actionsEvent.Actions;
                state.UpdatedAt = actionsEvent.UpdatedAt;
                break;
            case IconUpdatedEvent iconEvent:
                state.Icon = iconEvent.IconUrl;
                state.UpdatedAt = iconEvent.UpdatedAt;
                state.IconUploadHistory.Add(iconEvent.UploadTimestamp);
                break;
            case UserProfileClearedEvent clearEvent:
                // Clear all user profile data
                state.UserId = string.Empty;
                state.FullName = string.Empty;
                state.Gender = default;
                state.BirthDate = default;
                state.BirthTime = null;
                state.BirthCity = null;
                state.LatLong = null;
                state.MbtiType = null;
                state.RelationshipStatus = null;
                state.Interests = null;
                state.InterestsList = null;
                state.CalendarType = null;
                state.CurrentResidence = null;
                state.Email = null;
                state.Occupation = null;
                state.Icon = null;
                state.Actions = new List<string>();
                state.CreatedAt = default;
                state.UpdatedAt = clearEvent.ClearedAt;
                state.UpdateHistory.Clear();
                state.IsDeleted = true; // Mark as deleted to prevent auto-migration
                break;
            case UserProfileLanguageSwitchedEvent languageEvent:
                state.CurrentLanguage = languageEvent.NewLanguage;
                state.LastLanguageSwitchDate = languageEvent.SwitchDate;
                state.TodayLanguageSwitchCount = languageEvent.TodayCount;
                state.UpdatedAt = languageEvent.SwitchedAt;
                break;
            case UserProfileLatLongInferredEvent latLongEvent:
                state.LatLongInferred = latLongEvent.LatLongInferred;
                state.InferredFromCity = latLongEvent.BirthCity;
                break;
            case TimeZoneUpdatedEvent timeZoneEvent:
                state.CurrentTimeZone = timeZoneEvent.TimeZoneId;
                state.UpdatedAt = timeZoneEvent.UpdatedAt;
                // Note: Does NOT add to UpdateHistory (not counted as profile update)
                break;
        }
    }

    public async Task<UpdateUserProfileResult> UpdateUserProfileAsync(UpdateUserProfileRequest request)
    {
        try
        {
            _logger.LogDebug("[LumenUserProfileGAgent][UpdateUserProfileAsync] Start - UserId: {UserId}", request.UserId);

            // Validate request
            var validationResult = ValidateProfileRequest(request);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("[LumenUserProfileGAgent][UpdateUserProfileAsync] Validation failed: {Message}", 
                    validationResult.Message);
                return new UpdateUserProfileResult
                {
                    Success = false,
                    Message = validationResult.Message
                };
            }
            
            if (!State.UserId.IsNullOrWhiteSpace() && State.UserId != request.UserId)
            {
                _logger.LogWarning("[LumenUserProfileGAgent][UpdateUserProfileAsync] User ID mismatch: {UserId}", request.UserId);
                return new UpdateUserProfileResult
                {
                    Success = false,
                    Message = "User ID mismatch"
                };
            }

            // Check rate limit: maximum updates per week
            var now = DateTime.UtcNow;
            var oneWeekAgo = now.AddDays(-7);
            
            // Clean up old update history (older than 1 week)
            State.UpdateHistory = State.UpdateHistory
                .Where(timestamp => timestamp > oneWeekAgo)
                .ToList();
            
            // Check if limit exceeded
            var maxProfileUpdatesPerWeek = _options?.MaxProfileUpdatesPerWeek ?? MaxProfileUpdatesPerWeek;
            if (State.UpdateHistory.Count >= maxProfileUpdatesPerWeek)
            {
                var oldestUpdateInWeek = State.UpdateHistory.Min();
                var nextAllowedUpdate = oldestUpdateInWeek.AddDays(7);
                var remainingTime = nextAllowedUpdate - now;
                
                _logger.LogWarning(
                    "[LumenUserProfileGAgent][UpdateUserProfileAsync] Rate limit exceeded for UserId: {UserId}. " +
                    "Updates this week: {Count}/{Max}, Next allowed update: {NextAllowedUpdate}",
                    request.UserId, State.UpdateHistory.Count, maxProfileUpdatesPerWeek, nextAllowedUpdate);
                
                return new UpdateUserProfileResult
                {
                    Success = false,
                    Message = $"Profile update limit exceeded. You have reached the maximum of {maxProfileUpdatesPerWeek} updates per week. " +
                              $"Please try again in {remainingTime.Days} day(s) and {remainingTime.Hours} hour(s)."
                };
            }

            // Raise event to update state
            RaiseEvent(new UserProfileUpdatedEvent
            {
                UserId = request.UserId,
                FullName = request.FullName,
                Gender = request.Gender,
                BirthDate = request.BirthDate,
                BirthTime = request.BirthTime,
                BirthCity = request.BirthCity,
                LatLong = request.LatLong,
                MbtiType = request.MbtiType,
                RelationshipStatus = request.RelationshipStatus,
                Interests = request.Interests,
                CalendarType = request.CalendarType,
                UpdatedAt = now,
                CurrentResidence = request.CurrentResidence,
                Email = request.Email,
                Occupation = request.Occupation,
                Icon = request.Icon,
                CurrentTimeZone = request.CurrentTimeZone,
                InterestsList = request.InterestsList
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[LumenUserProfileGAgent][UpdateUserProfileAsync] User profile updated successfully: {UserId}", 
                request.UserId);

            return new UpdateUserProfileResult
            {
                Success = true,
                Message = string.Empty,
                UserId = request.UserId,
                CreatedAt = State.CreatedAt,
                UpdatedAt = State.UpdatedAt // Return actual UpdatedAt for prediction regeneration check
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenUserProfileGAgent][UpdateUserProfileAsync] Error updating user profile: {UserId}", 
                request.UserId);
            return new UpdateUserProfileResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    public async Task<GetUserProfileResult> GetUserProfileAsync(Guid userId, string userLanguage = "en")
    {
        try
        {
            _logger.LogDebug("[LumenUserProfileGAgent][GetUserProfileAsync] Getting user profile for: {UserId}, Language: {Language}", 
                this.GetPrimaryKey().ToString(), userLanguage);

            // Check if user was deleted
            if (State.IsDeleted)
            {
                _logger.LogWarning("[LumenUserProfileGAgent][GetUserProfileAsync] User profile was deleted {GrainId}", this.GetPrimaryKey().ToString());
                return new GetUserProfileResult
                {
                    Success = false,
                    Message = "User profile not found"
                };
            }

            if (string.IsNullOrEmpty(State.UserId))
            {
                _logger.LogWarning("[LumenUserProfileGAgent][GetUserProfileAsync] Lumen user profile not initialized {GrainId}", this.GetPrimaryKey().ToString());
                return new GetUserProfileResult
                {
                    Success = false,
                    Message = "User profile not found"
                };
            }

            // Calculate WelcomeNote using backend calculations (English base)
            var welcomeNoteBase = GenerateWelcomeNote(State.BirthDate);
            
            // Translate WelcomeNote to requested language
            var welcomeNote = TranslateWelcomeNote(welcomeNoteBase, userLanguage);
            
            // Calculate zodiac sign and Chinese zodiac
            var zodiacSignEn = LumenCalculator.CalculateZodiacSign(State.BirthDate);
            var zodiacSign = TranslateZodiacSign(zodiacSignEn, userLanguage);
            var zodiacSignEnum = LumenCalculator.ParseZodiacSignEnum(zodiacSignEn);
            
            var chineseZodiacWithElementEn = LumenCalculator.GetChineseZodiacWithElement(State.BirthDate.Year);
            var chineseZodiac = TranslateChineseZodiac(chineseZodiacWithElementEn, userLanguage);
            var chineseZodiacAnimal = LumenCalculator.CalculateChineseZodiac(State.BirthDate.Year);
            var chineseZodiacEnum = LumenCalculator.ParseChineseZodiacEnum(chineseZodiacAnimal);

            return new GetUserProfileResult
            {
                Success = true,
                Message = string.Empty,
                UserProfile = new LumenUserProfileDto
                {
                    UserId = State.UserId,
                    FullName = State.FullName,
                    Gender = State.Gender,
                    BirthDate = State.BirthDate,
                    BirthTime = State.BirthTime ?? default,
                    BirthCity = State.BirthCity ?? string.Empty,
                    LatLong = State.LatLong ?? string.Empty,
                    CalendarType = State.CalendarType,
                    CreatedAt = State.CreatedAt,
                    CurrentResidence = State.CurrentResidence,
                    UpdatedAt = State.UpdatedAt,
                    WelcomeNote = welcomeNote,
                    ZodiacSign = zodiacSign,
                    ZodiacSignEnum = zodiacSignEnum,
                    ChineseZodiac = chineseZodiac,
                    ChineseZodiacEnum = chineseZodiacEnum,
                    Occupation = State.Occupation,
                    MbtiType = State.MbtiType,
                    RelationshipStatus = State.RelationshipStatus,
                    Interests = State.Interests,
                    InterestsList = State.InterestsList,
                    Email = State.Email,
                    Icon = State.Icon,
                    CurrentTimeZone = State.CurrentTimeZone,
                    CurrentLanguage = State.CurrentLanguage
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenUserProfileGAgent][GetUserProfileAsync] Error getting user profile");
            return new GetUserProfileResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    /// <summary>
    /// Get raw state data directly without any migration logic - used for migration checks to prevent circular dependency
    /// </summary>
    public Task<LumenUserProfileDto?> GetRawStateAsync()
    {
        try
        {
            _logger.LogDebug("[LumenUserProfileGAgent][GetRawStateAsync] Getting raw state for: {UserId}", 
                this.GetPrimaryKey());

            // If not initialized, return null immediately without any migration logic
            if (string.IsNullOrEmpty(State.UserId))
            {
                return Task.FromResult<LumenUserProfileDto?>(null);
            }

            // Return raw state data without any processing or migration
            var profileDto = new LumenUserProfileDto
            {
                UserId = State.UserId,
                FullName = State.FullName,
                Gender = State.Gender,
                BirthDate = State.BirthDate,
                BirthTime = State.BirthTime ?? default,
                BirthCity = State.BirthCity ?? string.Empty,
                LatLong = State.LatLong ?? string.Empty,
                CalendarType = State.CalendarType,
                CreatedAt = State.CreatedAt,
                CurrentResidence = State.CurrentResidence,
                UpdatedAt = State.UpdatedAt,
                WelcomeNote = new Dictionary<string, string>(), // Empty, no calculation
                Occupation = State.Occupation,
                MbtiType = State.MbtiType,
                RelationshipStatus = State.RelationshipStatus,
                Interests = State.Interests,
                InterestsList = State.InterestsList,
                Email = State.Email,
                Icon = State.Icon,
                CurrentTimeZone = State.CurrentTimeZone,
                CurrentLanguage = State.CurrentLanguage
            };

            return Task.FromResult<LumenUserProfileDto?>(profileDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenUserProfileGAgent][GetRawStateAsync] Error getting raw state");
            return Task.FromResult<LumenUserProfileDto?>(null);
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

    public async Task<ClearUserResult> ClearUserAsync()
    {
        try
        {
            _logger.LogDebug("[LumenUserProfileGAgent][ClearUserAsync] Clearing user profile data for: {GrainId}", 
                this.GetPrimaryKey());

            // Check if user exists
            if (string.IsNullOrEmpty(State.UserId))
            {
                _logger.LogWarning("[LumenUserProfileGAgent][ClearUserAsync] User profile not found");
                return new ClearUserResult
                {
                    Success = false,
                    Message = "User profile not found"
                };
            }

            var now = DateTime.UtcNow;

            // Raise event to clear state
            RaiseEvent(new UserProfileClearedEvent
            {
                ClearedAt = now
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[LumenUserProfileGAgent][ClearUserAsync] User profile cleared successfully");

            return new ClearUserResult
            {
                Success = true,
                Message = "User profile cleared successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenUserProfileGAgent][ClearUserAsync] Error clearing user profile");
            return new ClearUserResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }
    
    public Task<GetRemainingUpdatesResult> GetRemainingUpdatesAsync()
    {
        try
        {
            var now = DateTime.UtcNow;
            var oneWeekAgo = now.AddDays(-7);
            
            // Clean up old update history (older than 1 week) - read-only, just for calculation
            var recentUpdates = State.UpdateHistory
                .Where(timestamp => timestamp > oneWeekAgo)
                .ToList();
            
            var maxProfileUpdatesPerWeek = _options?.MaxProfileUpdatesPerWeek ?? MaxProfileUpdatesPerWeek;
            var usedCount = recentUpdates.Count;
            var remainingCount = Math.Max(0, maxProfileUpdatesPerWeek - usedCount);
            
            // Calculate when next update will be available if limit is reached
            DateTime? nextAvailableAt = null;
            if (remainingCount == 0 && recentUpdates.Count > 0)
            {
                var oldestUpdateInWeek = recentUpdates.Min();
                nextAvailableAt = oldestUpdateInWeek.AddDays(7);
            }
            
            _logger.LogDebug(
                "[LumenUserProfileGAgent][GetRemainingUpdatesAsync] UserId: {UserId}, Used: {Used}/{Max}, Remaining: {Remaining}",
                State.UserId, usedCount, maxProfileUpdatesPerWeek, remainingCount);
            
            return Task.FromResult(new GetRemainingUpdatesResult
            {
                Success = true,
                UsedCount = usedCount,
                MaxCount = maxProfileUpdatesPerWeek,
                RemainingCount = remainingCount,
                NextAvailableAt = nextAvailableAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenUserProfileGAgent][GetRemainingUpdatesAsync] Error getting remaining updates");
            return Task.FromResult(new GetRemainingUpdatesResult
            {
                Success = false,
                UsedCount = 0,
                MaxCount = _options?.MaxProfileUpdatesPerWeek ?? MaxProfileUpdatesPerWeek,
                RemainingCount = 0,
                NextAvailableAt = null
            });
        }
    }
    
    public async Task<UpdateIconResult> UpdateIconAsync(string iconUrl)
    {
        try
        {
            _logger.LogDebug("[LumenUserProfileGAgent][UpdateIconAsync] Start - UserId: {UserId}, IconUrl: {IconUrl}", 
                State.UserId, iconUrl);

            // Check if user profile exists
            if (string.IsNullOrEmpty(State.UserId))
            {
                _logger.LogWarning("[LumenUserProfileGAgent][UpdateIconAsync] User profile not found");
                return new UpdateIconResult
                {
                    Success = false,
                    Message = "User profile not found. Please create your profile first."
                };
            }

            // Check daily upload limit
            var now = DateTime.UtcNow;
            var todayStart = now.Date;
            
            // Clean up old upload history (only keep today's records to prevent infinite growth)
            State.IconUploadHistory = State.IconUploadHistory
                .Where(timestamp => timestamp.Date == todayStart)
                .ToList();
            
            // Check if daily limit exceeded
            var maxIconUploadsPerDay = _options?.MaxIconUploadsPerDay ?? 1;
            if (State.IconUploadHistory.Count >= maxIconUploadsPerDay)
            {
                var remainingTime = todayStart.AddDays(1) - now;
                var resetTime = todayStart.AddDays(1);
                
                _logger.LogWarning(
                    "[LumenUserProfileGAgent][UpdateIconAsync] Daily upload limit exceeded for UserId: {UserId}. " +
                    "Current: {Current}/{Limit}, Reset at: {ResetTime}",
                    State.UserId, State.IconUploadHistory.Count, maxIconUploadsPerDay, resetTime);

                return new UpdateIconResult
                {
                    Success = false,
                    Message = $"Daily icon upload limit ({maxIconUploadsPerDay}) exceeded. Please try again tomorrow.",
                    RemainingUploads = 0
                };
            }

            // Raise event to update icon
            RaiseEvent(new IconUpdatedEvent
            {
                UserId = State.UserId,
                IconUrl = iconUrl,
                UpdatedAt = now,
                UploadTimestamp = now
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            var remainingUploads = Math.Max(0, maxIconUploadsPerDay - State.IconUploadHistory.Count);

            _logger.LogInformation(
                "[LumenUserProfileGAgent][UpdateIconAsync] Icon updated successfully for UserId: {UserId}, " +
                "Remaining uploads today: {Remaining}/{Max}",
                State.UserId, remainingUploads, maxIconUploadsPerDay);

            return new UpdateIconResult
            {
                Success = true,
                Message = "Icon updated successfully",
                IconUrl = iconUrl,
                RemainingUploads = remainingUploads
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenUserProfileGAgent][UpdateIconAsync] Error updating icon");
            return new UpdateIconResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    #region Helper Methods

    /// <summary>
    /// Generate WelcomeNote based on birth date (backend calculations)
    /// </summary>
    private Dictionary<string, string> GenerateWelcomeNote(DateOnly birthDate)
    {
        var birthYear = birthDate.Year;
        
        // Calculate zodiac and chinese zodiac
        var zodiac = LumenCalculator.CalculateZodiacSign(birthDate);
        var chineseZodiac = LumenCalculator.GetChineseZodiacWithElement(birthYear);
        
        // Calculate rhythm (Yin/Yang + Element from birth year stems)
        var birthYearStems = LumenCalculator.CalculateStemsAndBranches(birthYear);
        var rhythm = GetRhythmFromStems(birthYearStems);
        
        // Extract components for essence calculation
        var (yinYang, element) = ParseRhythm(rhythm); // e.g., "Yang Wood" -> ("Yang", "Wood")
        var chineseZodiacAnimal = ExtractChineseZodiacAnimal(chineseZodiac); // e.g., "Fire Dragon" -> "Dragon"
        
        // Calculate essence using deterministic random selection from 4 dimensions
        var essence = GetDeterministicEssence(zodiac, element, yinYang, chineseZodiacAnimal, birthDate);
        
        return new Dictionary<string, string>
        {
            { "zodiac", zodiac },
            { "chineseZodiac", chineseZodiac },
            { "rhythm", rhythm },
            { "essence", essence }
        };
    }

    /// <summary>
    /// Get Yin/Yang + Element from Heavenly Stems
    /// </summary>
    private string GetRhythmFromStems(string stemsInfo)
    {
        // Stems format: "甲 子 Jiǎ Zǐ"
        // Heavenly Stems (天干): 甲乙丙丁戊己庚辛壬癸
        // Mapping: 甲=Yang Wood, 乙=Yin Wood, 丙=Yang Fire, 丁=Yin Fire, 戊=Yang Earth, 己=Yin Earth, 庚=Yang Metal, 辛=Yin Metal, 壬=Yang Water, 癸=Yin Water
        if (stemsInfo.StartsWith("甲")) return "Yang Wood";
        if (stemsInfo.StartsWith("乙")) return "Yin Wood";
        if (stemsInfo.StartsWith("丙")) return "Yang Fire";
        if (stemsInfo.StartsWith("丁")) return "Yin Fire";
        if (stemsInfo.StartsWith("戊")) return "Yang Earth";
        if (stemsInfo.StartsWith("己")) return "Yin Earth";
        if (stemsInfo.StartsWith("庚")) return "Yang Metal";
        if (stemsInfo.StartsWith("辛")) return "Yin Metal";
        if (stemsInfo.StartsWith("壬")) return "Yang Water";
        if (stemsInfo.StartsWith("癸")) return "Yin Water";
        
        return "Yang Wood"; // Default
    }

    /// <summary>
    /// Get essence by deterministically selecting 2 words from the combined trait pool
    /// Uses birth date as seed for consistency
    /// </summary>
    private string GetDeterministicEssence(string zodiac, string element, string yinYang, string chineseZodiacAnimal, DateOnly birthDate)
    {
        // Build word pool from all 4 dimensions
        var wordPool = new List<string>();
        
        // Add Western Zodiac traits (3 words)
        wordPool.AddRange(GetZodiacTraits(zodiac));
        
        // Add Element traits (3 words)
        wordPool.AddRange(GetElementTraits(element));
        
        // Add Yin/Yang traits (3 words)
        wordPool.AddRange(GetPolarityTraits(yinYang));
        
        // Add Chinese Zodiac trait (1 word)
        wordPool.Add(GetChineseZodiacTrait(chineseZodiacAnimal));
        
        // Remove duplicates while preserving order
        var uniqueWords = wordPool.Distinct().ToList();
        
        // Use birth date as seed for deterministic randomness
        var seed = birthDate.Year * 10000 + birthDate.Month * 100 + birthDate.Day;
        var random = new Random(seed);
        
        // Shuffle and pick first 2 words
        var shuffled = uniqueWords.OrderBy(x => random.Next()).ToList();
        var selectedWords = shuffled.Take(2).ToList();
        
        // Capitalize first letter of each word
        return string.Join(", ", selectedWords.Select(w => char.ToUpper(w[0]) + w.Substring(1)));
    }

    /// <summary>
    /// Parse rhythm string into Yin/Yang and Element
    /// </summary>
    private (string yinYang, string element) ParseRhythm(string rhythm)
    {
        // "Yang Wood" -> ("Yang", "Wood")
        var parts = rhythm.Split(' ');
        return (parts[0], parts[1]);
    }

    /// <summary>
    /// Extract animal name from Chinese Zodiac string
    /// </summary>
    private string ExtractChineseZodiacAnimal(string chineseZodiac)
    {
        // "Fire Dragon" -> "Dragon"
        var parts = chineseZodiac.Split(' ');
        return parts.Length > 1 ? parts[1] : parts[0];
    }

    #endregion

    #region Trait Dictionaries

    /// <summary>
    /// Get Western Zodiac traits (3 words per sign)
    /// </summary>
    private List<string> GetZodiacTraits(string zodiac)
    {
        return zodiac switch
        {
            "Aries" => new List<string> { "bold", "passionate", "daring" },
            "Taurus" => new List<string> { "grounded", "loyal", "steady" },
            "Gemini" => new List<string> { "curious", "expressive", "agile" },
            "Cancer" => new List<string> { "nurturing", "sensitive", "intuitive" },
            "Leo" => new List<string> { "radiant", "confident", "magnetic" },
            "Virgo" => new List<string> { "precise", "thoughtful", "reliable" },
            "Libra" => new List<string> { "graceful", "diplomatic", "balanced" },
            "Scorpio" => new List<string> { "intuitive", "intense", "resilient" },
            "Sagittarius" => new List<string> { "adventurous", "fiery", "free-spirited" },
            "Capricorn" => new List<string> { "disciplined", "ambitious", "wise" },
            "Aquarius" => new List<string> { "visionary", "eccentric", "insightful" },
            "Pisces" => new List<string> { "dreamy", "empathic", "fluid" },
            _ => new List<string> { "unique", "dynamic" }
        };
    }

    /// <summary>
    /// Get Element traits (3 words per element)
    /// </summary>
    private List<string> GetElementTraits(string element)
    {
        return element switch
        {
            "Wood" => new List<string> { "adaptable", "growth-focused", "expansive" },
            "Fire" => new List<string> { "dynamic", "passionate", "high-energy" },
            "Earth" => new List<string> { "stable", "practical", "grounded" },
            "Metal" => new List<string> { "refined", "sharp", "resilient" },
            "Water" => new List<string> { "fluid", "deep", "intuitive" },
            _ => new List<string> { "balanced" }
        };
    }

    /// <summary>
    /// Get Polarity (Yin/Yang) traits (3 words per polarity)
    /// </summary>
    private List<string> GetPolarityTraits(string yinYang)
    {
        return yinYang switch
        {
            "Yin" => new List<string> { "introspective", "subtle", "nurturing" },
            "Yang" => new List<string> { "expressive", "active", "assertive" },
            _ => new List<string> { "balanced" }
        };
    }

    /// <summary>
    /// Get Chinese Zodiac trait (1 word per animal)
    /// </summary>
    private string GetChineseZodiacTrait(string animal)
    {
        return animal switch
        {
            "Rat" => "clever",
            "Ox" => "enduring",
            "Tiger" => "daring",
            "Rabbit" => "graceful",
            "Dragon" => "powerful",
            "Snake" => "wise",
            "Horse" => "free-spirited",
            "Goat" => "gentle",
            "Monkey" => "playful",
            "Rooster" => "focused",
            "Dog" => "loyal",
            "Pig" => "generous",
            _ => "unique"
        };
    }
    
    /// <summary>
    /// Parse zodiac sign string to enum
    /// </summary>
    private ZodiacSignEnum ParseZodiacSignEnum(string zodiacSign)
    {
        return zodiacSign switch
        {
            "Aries" => ZodiacSignEnum.Aries,
            "Taurus" => ZodiacSignEnum.Taurus,
            "Gemini" => ZodiacSignEnum.Gemini,
            "Cancer" => ZodiacSignEnum.Cancer,
            "Leo" => ZodiacSignEnum.Leo,
            "Virgo" => ZodiacSignEnum.Virgo,
            "Libra" => ZodiacSignEnum.Libra,
            "Scorpio" => ZodiacSignEnum.Scorpio,
            "Sagittarius" => ZodiacSignEnum.Sagittarius,
            "Capricorn" => ZodiacSignEnum.Capricorn,
            "Aquarius" => ZodiacSignEnum.Aquarius,
            "Pisces" => ZodiacSignEnum.Pisces,
            _ => ZodiacSignEnum.Unknown
        };
    }
    
    /// <summary>
    /// Parse Chinese zodiac animal string to enum
    /// </summary>
    private ChineseZodiacEnum ParseChineseZodiacEnum(string animal)
    {
        return animal switch
        {
            "Rat" => ChineseZodiacEnum.Rat,
            "Ox" => ChineseZodiacEnum.Ox,
            "Tiger" => ChineseZodiacEnum.Tiger,
            "Rabbit" => ChineseZodiacEnum.Rabbit,
            "Dragon" => ChineseZodiacEnum.Dragon,
            "Snake" => ChineseZodiacEnum.Snake,
            "Horse" => ChineseZodiacEnum.Horse,
            "Goat" => ChineseZodiacEnum.Goat,
            "Monkey" => ChineseZodiacEnum.Monkey,
            "Rooster" => ChineseZodiacEnum.Rooster,
            "Dog" => ChineseZodiacEnum.Dog,
            "Pig" => ChineseZodiacEnum.Pig,
            _ => ChineseZodiacEnum.Unknown
        };
    }
    
    /// <summary>
    /// Translate WelcomeNote fields to requested language
    /// </summary>
    private Dictionary<string, string> TranslateWelcomeNote(Dictionary<string, string> baseNote, string language)
    {
        if (language == "en" || string.IsNullOrEmpty(language))
        {
            return baseNote; // Already in English
        }
        
        var translated = new Dictionary<string, string>();
        
        // Translate zodiac
        if (baseNote.TryGetValue("zodiac", out var zodiac))
        {
            translated["zodiac"] = TranslateZodiacSign(zodiac, language);
        }
        
        // Translate chineseZodiac
        if (baseNote.TryGetValue("chineseZodiac", out var chineseZodiac))
        {
            translated["chineseZodiac"] = TranslateChineseZodiac(chineseZodiac, language);
        }
        
        // Translate rhythm (Yin/Yang + Element)
        if (baseNote.TryGetValue("rhythm", out var rhythm))
        {
            translated["rhythm"] = TranslateRhythm(rhythm, language);
        }
        
        // Translate essence (personality traits)
        if (baseNote.TryGetValue("essence", out var essence))
        {
            translated["essence"] = TranslateEssence(essence, language);
        }
        
        return translated;
    }
    
    /// <summary>
    /// Translate Zodiac sign name
    /// </summary>
    private string TranslateZodiacSign(string zodiacSign, string language)
    {
        return language switch
        {
            "zh-tw" => zodiacSign switch
            {
                "Aries" => "白羊座",
                "Taurus" => "金牛座",
                "Gemini" => "雙子座",
                "Cancer" => "巨蟹座",
                "Leo" => "獅子座",
                "Virgo" => "處女座",
                "Libra" => "天秤座",
                "Scorpio" => "天蠍座",
                "Sagittarius" => "射手座",
                "Capricorn" => "摩羯座",
                "Aquarius" => "水瓶座",
                "Pisces" => "雙魚座",
                _ => zodiacSign
            },
            "zh" => zodiacSign switch
            {
                "Aries" => "白羊座",
                "Taurus" => "金牛座",
                "Gemini" => "双子座",
                "Cancer" => "巨蟹座",
                "Leo" => "狮子座",
                "Virgo" => "处女座",
                "Libra" => "天秤座",
                "Scorpio" => "天蝎座",
                "Sagittarius" => "射手座",
                "Capricorn" => "摩羯座",
                "Aquarius" => "水瓶座",
                "Pisces" => "双鱼座",
                _ => zodiacSign
            },
            "es" => zodiacSign switch
            {
                "Aries" => "Aries",
                "Taurus" => "Tauro",
                "Gemini" => "Géminis",
                "Cancer" => "Cáncer",
                "Leo" => "Leo",
                "Virgo" => "Virgo",
                "Libra" => "Libra",
                "Scorpio" => "Escorpio",
                "Sagittarius" => "Sagitario",
                "Capricorn" => "Capricornio",
                "Aquarius" => "Acuario",
                "Pisces" => "Piscis",
                _ => zodiacSign
            },
            _ => zodiacSign // English default
        };
    }
    
    /// <summary>
    /// Translate Chinese Zodiac (with element)
    /// </summary>
    private string TranslateChineseZodiac(string chineseZodiac, string language)
    {
        // Extract element and animal from format "Fire Dragon"
        var parts = chineseZodiac.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return chineseZodiac; // Return as-is if format is unexpected
        }
        
        var element = parts[0];
        var animal = parts[^1];
        
        return language switch
        {
            "zh-tw" or "zh" => $"{TranslateElement(element, language)}{TranslateAnimal(animal, language)}",
            "es" => $"{TranslateAnimal(animal, language)} de {TranslateElement(element, language)}",
            _ => chineseZodiac // English default
        };
    }
    
    private string TranslateElement(string element, string language)
    {
        return (element, language) switch
        {
            ("Wood", "zh-tw") => "木",
            ("Fire", "zh-tw") => "火",
            ("Earth", "zh-tw") => "土",
            ("Metal", "zh-tw") => "金",
            ("Water", "zh-tw") => "水",
            ("Wood", "zh") => "木",
            ("Fire", "zh") => "火",
            ("Earth", "zh") => "土",
            ("Metal", "zh") => "金",
            ("Water", "zh") => "水",
            ("Wood", "es") => "Madera",
            ("Fire", "es") => "Fuego",
            ("Earth", "es") => "Tierra",
            ("Metal", "es") => "Metal",
            ("Water", "es") => "Agua",
            _ => element
        };
    }
    
    private string TranslateAnimal(string animal, string language)
    {
        return (animal, language) switch
        {
            ("Rat", "zh-tw") => "鼠",
            ("Ox", "zh-tw") => "牛",
            ("Tiger", "zh-tw") => "虎",
            ("Rabbit", "zh-tw") => "兔",
            ("Dragon", "zh-tw") => "龍",
            ("Snake", "zh-tw") => "蛇",
            ("Horse", "zh-tw") => "馬",
            ("Goat", "zh-tw") => "羊",
            ("Monkey", "zh-tw") => "猴",
            ("Rooster", "zh-tw") => "雞",
            ("Dog", "zh-tw") => "狗",
            ("Pig", "zh-tw") => "豬",
            ("Rat", "zh") => "鼠",
            ("Ox", "zh") => "牛",
            ("Tiger", "zh") => "虎",
            ("Rabbit", "zh") => "兔",
            ("Dragon", "zh") => "龙",
            ("Snake", "zh") => "蛇",
            ("Horse", "zh") => "马",
            ("Goat", "zh") => "羊",
            ("Monkey", "zh") => "猴",
            ("Rooster", "zh") => "鸡",
            ("Dog", "zh") => "狗",
            ("Pig", "zh") => "猪",
            ("Rat", "es") => "Rata",
            ("Ox", "es") => "Buey",
            ("Tiger", "es") => "Tigre",
            ("Rabbit", "es") => "Conejo",
            ("Dragon", "es") => "Dragón",
            ("Snake", "es") => "Serpiente",
            ("Horse", "es") => "Caballo",
            ("Goat", "es") => "Cabra",
            ("Monkey", "es") => "Mono",
            ("Rooster", "es") => "Gallo",
            ("Dog", "es") => "Perro",
            ("Pig", "es") => "Cerdo",
            _ => animal
        };
    }
    
    private string TranslateRhythm(string rhythm, string language)
    {
        // Format: "Yang Wood", "Yin Fire", etc.
        var parts = rhythm.Split(' ');
        if (parts.Length != 2) return rhythm;
        
        var yinYang = parts[0];
        var element = parts[1];
        
        return language switch
        {
            "zh-tw" => $"{(yinYang == "Yang" ? "陽" : "陰")}{TranslateElement(element, language)}",
            "zh" => $"{(yinYang == "Yang" ? "阳" : "阴")}{TranslateElement(element, language)}",
            "es" => $"{TranslateElement(element, language)} {yinYang}",
            _ => rhythm
        };
    }
    
    private string TranslateEssence(string essence, string language)
    {
        // Format: "Adventurous, Passionate" - comma-separated traits
        if (language == "en" || string.IsNullOrEmpty(language))
        {
            return essence;
        }
        
        var traits = essence.Split(',').Select(t => t.Trim()).ToList();
        var translatedTraits = traits.Select(trait => TranslateTrait(trait, language)).ToList();
        
        return string.Join(", ", translatedTraits);
    }
    
    private string TranslateTrait(string trait, string language)
    {
        // Common personality traits translation (complete version covering all trait pool)
        var lowerTrait = trait.ToLower();
        
        if (language == "zh-tw")
        {
            return lowerTrait switch
            {
                // Original traits
                "adventurous" => "冒險",
                "bold" => "勇敢",
                "passionate" => "熱情",
                "reliable" => "可靠",
                "patient" => "耐心",
                "practical" => "務實",
                "curious" => "好奇",
                "adaptable" => "適應力強",
                "confident" => "自信",
                "generous" => "慷慨",
                "analytical" => "分析",
                "diplomatic" => "圓融",
                "intense" => "強烈",
                "optimistic" => "樂觀",
                "ambitious" => "有抱負",
                "innovative" => "創新",
                "intuitive" => "直覺",
                "compassionate" => "富有同情心",
                // Additional traits from trait pool
                "active" => "活躍",
                "agile" => "敏捷",
                "assertive" => "果斷",
                "balanced" => "平衡",
                "clever" => "聰明",
                "daring" => "大膽",
                "deep" => "深刻",
                "disciplined" => "自律",
                "dreamy" => "夢幻",
                "dynamic" => "充滿活力",
                "eccentric" => "獨特",
                "empathic" => "共情",
                "enduring" => "持久",
                "expansive" => "開放",
                "expressive" => "善於表達",
                "fiery" => "熱情似火",
                "fluid" => "靈活",
                "focused" => "專注",
                "free-spirited" => "自由奔放",
                "gentle" => "溫和",
                "graceful" => "優雅",
                "grounded" => "踏實",
                "growth-focused" => "注重成長",
                "high-energy" => "精力充沛",
                "insightful" => "有洞察力",
                "introspective" => "內省",
                "loyal" => "忠誠",
                "magnetic" => "有魅力",
                "nurturing" => "關懷",
                "playful" => "愛玩",
                "powerful" => "強大",
                "precise" => "精確",
                "radiant" => "光彩照人",
                "refined" => "精緻",
                "resilient" => "堅韌",
                "sensitive" => "敏感",
                "sharp" => "敏銳",
                "stable" => "穩定",
                "steady" => "穩重",
                "subtle" => "細膩",
                "thoughtful" => "體貼",
                "visionary" => "有遠見",
                "wise" => "智慧",
                _ => trait
            };
        }
        
        if (language == "zh")
        {
            return lowerTrait switch
            {
                // Original traits
                "adventurous" => "冒险",
                "bold" => "勇敢",
                "passionate" => "热情",
                "reliable" => "可靠",
                "patient" => "耐心",
                "practical" => "务实",
                "curious" => "好奇",
                "adaptable" => "适应力强",
                "confident" => "自信",
                "generous" => "慷慨",
                "analytical" => "分析",
                "diplomatic" => "圆融",
                "intense" => "强烈",
                "optimistic" => "乐观",
                "ambitious" => "有抱负",
                "innovative" => "创新",
                "intuitive" => "直觉",
                "compassionate" => "富有同情心",
                // Additional traits from trait pool
                "active" => "活跃",
                "agile" => "敏捷",
                "assertive" => "果断",
                "balanced" => "平衡",
                "clever" => "聪明",
                "daring" => "大胆",
                "deep" => "深刻",
                "disciplined" => "自律",
                "dreamy" => "梦幻",
                "dynamic" => "充满活力",
                "eccentric" => "独特",
                "empathic" => "共情",
                "enduring" => "持久",
                "expansive" => "开放",
                "expressive" => "善于表达",
                "fiery" => "热情似火",
                "fluid" => "灵活",
                "focused" => "专注",
                "free-spirited" => "自由奔放",
                "gentle" => "温和",
                "graceful" => "优雅",
                "grounded" => "踏实",
                "growth-focused" => "注重成长",
                "high-energy" => "精力充沛",
                "insightful" => "有洞察力",
                "introspective" => "内省",
                "loyal" => "忠诚",
                "magnetic" => "有魅力",
                "nurturing" => "关怀",
                "playful" => "爱玩",
                "powerful" => "强大",
                "precise" => "精确",
                "radiant" => "光彩照人",
                "refined" => "精致",
                "resilient" => "坚韧",
                "sensitive" => "敏感",
                "sharp" => "敏锐",
                "stable" => "稳定",
                "steady" => "稳重",
                "subtle" => "细腻",
                "thoughtful" => "体贴",
                "visionary" => "有远见",
                "wise" => "智慧",
                _ => trait
            };
        }
        
        if (language == "es")
        {
            return lowerTrait switch
            {
                // Original traits
                "adventurous" => "Aventurero",
                "bold" => "Audaz",
                "passionate" => "Apasionado",
                "reliable" => "Confiable",
                "patient" => "Paciente",
                "practical" => "Práctico",
                "curious" => "Curioso",
                "adaptable" => "Adaptable",
                "confident" => "Seguro",
                "generous" => "Generoso",
                "analytical" => "Analítico",
                "diplomatic" => "Diplomático",
                "intense" => "Intenso",
                "optimistic" => "Optimista",
                "ambitious" => "Ambicioso",
                "innovative" => "Innovador",
                "intuitive" => "Intuitivo",
                "compassionate" => "Compasivo",
                // Additional traits from trait pool
                "active" => "Activo",
                "agile" => "Ágil",
                "assertive" => "Asertivo",
                "balanced" => "Equilibrado",
                "clever" => "Astuto",
                "daring" => "Atrevido",
                "deep" => "Profundo",
                "disciplined" => "Disciplinado",
                "dreamy" => "Soñador",
                "dynamic" => "Dinámico",
                "eccentric" => "Excéntrico",
                "empathic" => "Empático",
                "enduring" => "Duradero",
                "expansive" => "Expansivo",
                "expressive" => "Expresivo",
                "fiery" => "Ardiente",
                "fluid" => "Fluido",
                "focused" => "Concentrado",
                "free-spirited" => "Espíritu libre",
                "gentle" => "Gentil",
                "graceful" => "Elegante",
                "grounded" => "Arraigado",
                "growth-focused" => "Enfocado en crecimiento",
                "high-energy" => "Alta energía",
                "insightful" => "Perspicaz",
                "introspective" => "Introspectivo",
                "loyal" => "Leal",
                "magnetic" => "Magnético",
                "nurturing" => "Protector",
                "playful" => "Juguetón",
                "powerful" => "Poderoso",
                "precise" => "Preciso",
                "radiant" => "Radiante",
                "refined" => "Refinado",
                "resilient" => "Resistente",
                "sensitive" => "Sensible",
                "sharp" => "Agudo",
                "stable" => "Estable",
                "steady" => "Firme",
                "subtle" => "Sutil",
                "thoughtful" => "Considerado",
                "visionary" => "Visionario",
                "wise" => "Sabio",
                _ => trait
            };
        }
        
        return trait;
    }

    #endregion
    
    #region Language Management
    
    public async Task<SetLanguageResult> SetLanguageAsync(string newLanguage)
    {
        try
        {
            _logger.LogDebug("[LumenUserProfileGAgent][SetLanguageAsync] Setting language for: {UserId}, Language: {Language}", 
                State.UserId, newLanguage);

            // Check if user exists
            if (string.IsNullOrEmpty(State.UserId))
            {
                _logger.LogWarning("[LumenUserProfileGAgent][SetLanguageAsync] User not found");
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
                _logger.LogInformation("[LumenUserProfileGAgent][SetLanguageAsync] Language unchanged: {Language}", newLanguage);
                var maxChanges = _options?.MaxLanguageSwitchesPerDay ?? 1;
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
            var maxChangesPerDay = _options?.MaxLanguageSwitchesPerDay ?? 1;

            // Check if we need to reset today's count
            if (!State.LastLanguageSwitchDate.HasValue || State.LastLanguageSwitchDate.Value != today)
            {
                State.TodayLanguageSwitchCount = 0;
            }

            // Check if today's limit is reached
            if (State.TodayLanguageSwitchCount >= maxChangesPerDay)
            {
                _logger.LogWarning("[LumenUserProfileGAgent][SetLanguageAsync] Daily language switch limit reached: {UserId}", 
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
            RaiseEvent(new UserProfileLanguageSwitchedEvent
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

            _logger.LogInformation("[LumenUserProfileGAgent][SetLanguageAsync] Language switched from {OldLang} to {NewLang}, Count: {Count}", 
                previousLanguage, newLanguage, newCount);

            var remainingChanges = Math.Max(0, maxChangesPerDay - newCount);

            return new SetLanguageResult
            {
                Success = true,
                Message = "Language updated successfully",
                CurrentLanguage = newLanguage,
                RemainingChanges = remainingChanges,
                MaxChangesPerDay = maxChangesPerDay
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenUserProfileGAgent][SetLanguageAsync] Error setting language");
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
            _logger.LogDebug("[LumenUserProfileGAgent][GetLanguageInfoAsync] Getting language info for: {UserId}", 
                State.UserId);

            if (string.IsNullOrEmpty(State.UserId))
            {
                return Task.FromResult(new GetLanguageInfoResult
                {
                    Success = false,
                    Message = "User not found"
                });
            }

            var maxChanges = _options?.MaxLanguageSwitchesPerDay ?? 1;
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
            _logger.LogError(ex, "[LumenUserProfileGAgent][GetLanguageInfoAsync] Error getting language info");
            return Task.FromResult(new GetLanguageInfoResult
            {
                Success = false,
                Message = "Internal error occurred"
            });
        }
    }

    public async Task InitializeLanguageAsync(string initialLanguage)
    {
        try
        {
            _logger.LogInformation("[LumenUserProfileGAgent][InitializeLanguageAsync] Initializing language for new user: {UserId}, Language: {Language}", 
                State.UserId, initialLanguage);

            // Simple event to set initial language without counting as a switch
            RaiseEvent(new UserProfileLanguageSwitchedEvent
            {
                UserId = State.UserId,
                PreviousLanguage = string.Empty,
                NewLanguage = initialLanguage,
                SwitchedAt = DateTime.UtcNow,
                SwitchDate = DateOnly.FromDateTime(DateTime.UtcNow),
                TodayCount = 0 // Does not count as a switch
            });

            await ConfirmEvents();

            _logger.LogInformation("[LumenUserProfileGAgent][InitializeLanguageAsync] Language initialized: {Language}", initialLanguage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenUserProfileGAgent][InitializeLanguageAsync] Error initializing language");
        }
    }
    
    public async Task<UpdateTimeZoneResult> UpdateTimeZoneAsync(UpdateTimeZoneRequest request)
    {
        try
        {
            _logger.LogDebug("[LumenUserProfileGAgent][UpdateTimeZoneAsync] Start - UserId: {UserId}, TimeZone: {TimeZoneId}", 
                request.UserId, request.TimeZoneId);

            // Check if user exists
            if (string.IsNullOrEmpty(State.UserId))
            {
                _logger.LogWarning("[LumenUserProfileGAgent][UpdateTimeZoneAsync] User not found: {UserId}", request.UserId);
                return new UpdateTimeZoneResult
                {
                    Success = false,
                    Message = "User not found"
                };
            }

            // Validate user ID matches
            if (State.UserId != request.UserId)
            {
                _logger.LogWarning("[LumenUserProfileGAgent][UpdateTimeZoneAsync] User ID mismatch: {UserId}", request.UserId);
                return new UpdateTimeZoneResult
                {
                    Success = false,
                    Message = "User ID mismatch"
                };
            }

            // Validate timezone ID (IANA format)
            try
            {
                var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId);
                _logger.LogDebug("[LumenUserProfileGAgent][UpdateTimeZoneAsync] Validated timezone: {TimeZoneId}, DisplayName: {DisplayName}", 
                    request.TimeZoneId, timeZoneInfo.DisplayName);
            }
            catch (TimeZoneNotFoundException)
            {
                _logger.LogWarning("[LumenUserProfileGAgent][UpdateTimeZoneAsync] Invalid timezone ID: {TimeZoneId}", request.TimeZoneId);
                return new UpdateTimeZoneResult
                {
                    Success = false,
                    Message = $"Invalid time zone ID: {request.TimeZoneId}. Please use IANA time zone format (e.g., 'America/New_York', 'Asia/Shanghai')"
                };
            }

            var now = DateTime.UtcNow;

            // Raise event to update timezone (does NOT count as profile update)
            RaiseEvent(new TimeZoneUpdatedEvent
            {
                UserId = request.UserId,
                TimeZoneId = request.TimeZoneId,
                UpdatedAt = now
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[LumenUserProfileGAgent][UpdateTimeZoneAsync] Timezone updated successfully: {UserId}, TimeZone: {TimeZoneId}", 
                request.UserId, request.TimeZoneId);

            // Trigger daily prediction grain to re-register reminder with new timezone
            var predictionGrainId = CommonHelper.StringToGuid($"{request.UserId}_daily");
            var predictionGAgent = GrainFactory.GetGrain<ILumenPredictionGAgent>(predictionGrainId);
            _ = predictionGAgent.UpdateTimeZoneReminderAsync(request.TimeZoneId); // Fire and forget

            return new UpdateTimeZoneResult
            {
                Success = true,
                Message = string.Empty,
                TimeZoneId = request.TimeZoneId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenUserProfileGAgent][UpdateTimeZoneAsync] Error updating timezone: {UserId}", request.UserId);
            return new UpdateTimeZoneResult
            {
                Success = false,
                Message = $"Failed to update timezone: {ex.Message}"
            };
        }
    }

    public async Task SaveInferredLatLongAsync(string latLongInferred, string birthCity)
    {
        try
        {
            _logger.LogDebug("[LumenUserProfileGAgent][SaveInferredLatLongAsync] Saving inferred latlong for: {UserId}, City: {BirthCity}", 
                State.UserId, birthCity);

            // Check if user exists
            if (string.IsNullOrEmpty(State.UserId))
            {
                _logger.LogWarning("[LumenUserProfileGAgent][SaveInferredLatLongAsync] User not found");
                return;
            }

            // Only save if not already exists
            if (!string.IsNullOrEmpty(State.LatLongInferred))
            {
                _logger.LogDebug("[LumenUserProfileGAgent][SaveInferredLatLongAsync] LatLongInferred already exists, skipping");
                return;
            }

            var now = DateTime.UtcNow;

            // Raise event to save inferred latlong
            RaiseEvent(new UserProfileLatLongInferredEvent
            {
                UserId = State.UserId,
                LatLongInferred = latLongInferred,
                BirthCity = birthCity,
                InferredAt = now
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[LumenUserProfileGAgent][SaveInferredLatLongAsync] LatLongInferred saved successfully: {LatLong}", 
                latLongInferred);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenUserProfileGAgent][SaveInferredLatLongAsync] Error saving inferred latlong");
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
    
    #endregion
}

