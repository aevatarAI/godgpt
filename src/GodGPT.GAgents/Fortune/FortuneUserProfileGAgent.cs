using Aevatar.Application.Grains.Fortune.Dtos;
using Aevatar.Application.Grains.Fortune.SEvents;
using Aevatar.Application.Grains.UserInfo;
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
    Task<GetUserProfileResult> GetUserProfileAsync(Guid userId);
    
    /// <summary>
    /// Get raw state data directly without any migration logic - used for migration checks to prevent circular dependency
    /// </summary>
    [ReadOnly]
    Task<FortuneUserProfileDto?> GetRawStateAsync();
    
    Task<UpdateUserActionsResult> UpdateUserActionsAsync(UpdateUserActionsRequest request);
    
    Task<GenerateProfileInsightsResult> GenerateProfileInsightsAsync(string aiResponse);
    
    /// <summary>
    /// Clear user profile data (for testing purposes)
    /// </summary>
    Task<ClearUserResult> ClearUserAsync();
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
            case UserProfileClearedEvent clearEvent:
                // Clear all user profile data
                state.UserId = string.Empty;
                state.FullName = string.Empty;
                state.Gender = default;
                state.BirthDate = default;
                state.BirthTime = null;
                state.BirthCountry = null;
                state.BirthCity = null;
                state.MbtiType = null;
                state.RelationshipStatus = null;
                state.Interests = null;
                state.CalendarType = null;
                state.CurrentResidence = null;
                state.Email = null;
                state.Actions = new List<string>();
                state.Astrology = new Dictionary<string, string>();
                state.Bazi = new Dictionary<string, string>();
                state.Zodiac = new Dictionary<string, string>();
                state.CreatedAt = default;
                state.UpdatedAt = clearEvent.ClearedAt;
                state.InsightsGeneratedAt = null;
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

    public async Task<GetUserProfileResult> GetUserProfileAsync(Guid userId)
    {
        try
        {
            _logger.LogDebug("[FortuneUserProfileGAgent][GetUserProfileAsync] Getting user profile for: {UserId}", 
                this.GetPrimaryKey().ToString());

            if (string.IsNullOrEmpty(State.UserId))
            {
                // Try to migrate data from UserInfoCollectionGAgent
                await TryToMigrateDataFromUserInfoCollectionAsync(userId);
            }

            if (string.IsNullOrEmpty(State.UserId))
            {
                _logger.LogWarning("[FortuneUserProfileGAgent][GetUserProfileAsync] Fortune user profile not initialized {GrainId}", this.GetPrimaryKey().ToString());
                return new GetUserProfileResult
                {
                    Success = false,
                    Message = "User profile not found"
                };
            }

            // Calculate WelcomeNote using backend calculations
            var welcomeNote = GenerateWelcomeNote(State.BirthDate);

            return new GetUserProfileResult
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
                    InsightsGeneratedAt = State.InsightsGeneratedAt,
                    UpdatedAt = State.UpdatedAt,
                    WelcomeNote = welcomeNote
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortuneUserProfileGAgent][GetUserProfileAsync] Error getting user profile");
            return new GetUserProfileResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    private async Task TryToMigrateDataFromUserInfoCollectionAsync(Guid userId)
    {
        _logger.LogWarning(
            "[FortuneUserProfileGAgent][GetUserProfileAsync] Fortune user profile not initialized. {GrainId}",
            this.GetPrimaryKey().ToString());

        // Use GetRawStateAsync to prevent circular dependency
        var userInfoCollectionGAgent = GrainFactory.GetGrain<IUserInfoCollectionGAgent>(userId);
        var userInfoResult = await userInfoCollectionGAgent.GetRawStateAsync();

        if (userInfoResult != null && userInfoResult.IsInitialized && userInfoResult.NameInfo != null)
        {
            _logger.LogInformation(
                "[FortuneUserProfileGAgent][GetUserProfileAsync] Migrating data from UserInfoCollection to FortuneUserProfile, userId {UserId}",
                userId.ToString());

            var now = DateTime.UtcNow;

            // Prepare migration data - bypass validation for data migration scenario
            // Concatenate FirstName + LastName → FullName
            var firstName = userInfoResult.NameInfo.FirstName?.Trim() ?? "";
            var lastName = userInfoResult.NameInfo.LastName?.Trim() ?? "";
            var fullName = $"{firstName} {lastName}".Trim();

            // If both are empty, use firstName (which is already empty string)
            if (string.IsNullOrWhiteSpace(fullName))
            {
                fullName = firstName;
            }

            // Map Gender: 1→Male(0), 2→Female(1)
            GenderEnum gender = GenderEnum.Other;
            if (userInfoResult.NameInfo.Gender == 1)
            {
                gender = GenderEnum.Male;
            }
            else if (userInfoResult.NameInfo.Gender == 2)
            {
                gender = GenderEnum.Female;
            }

            // Construct BirthDate from Day/Month/Year (if all present)
            DateOnly? birthDate = null;
            if (userInfoResult.BirthDateInfo != null &&
                userInfoResult.BirthDateInfo.Day.HasValue &&
                userInfoResult.BirthDateInfo.Month.HasValue &&
                userInfoResult.BirthDateInfo.Year.HasValue)
            {
                try
                {
                    birthDate = new DateOnly(
                        userInfoResult.BirthDateInfo.Year.Value,
                        userInfoResult.BirthDateInfo.Month.Value,
                        userInfoResult.BirthDateInfo.Day.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[FortuneUserProfileGAgent][GetUserProfileAsync] Invalid birth date: {Year}-{Month}-{Day}",
                        userInfoResult.BirthDateInfo.Year, userInfoResult.BirthDateInfo.Month,
                        userInfoResult.BirthDateInfo.Day);
                }
            }

            // Construct BirthTime from Hour/Minute (if present)
            TimeOnly? birthTime = null;
            if (userInfoResult.BirthTimeInfo != null &&
                userInfoResult.BirthTimeInfo.Hour.HasValue &&
                userInfoResult.BirthTimeInfo.Minute.HasValue)
            {
                birthTime = new TimeOnly(userInfoResult.BirthTimeInfo.Hour.Value,
                    userInfoResult.BirthTimeInfo.Minute.Value);
            }

            // Map location
            string birthCountry = userInfoResult.LocationInfo?.Country;
            string birthCity = userInfoResult.LocationInfo?.City;

            // Check if we have minimum required data for migration
            bool hasDataToMigrate = !string.IsNullOrWhiteSpace(fullName) && birthDate.HasValue;

            if (hasDataToMigrate)
            {
                _logger.LogInformation(
                    "[FortuneUserProfileGAgent][GetUserProfileAsync] Saving migrated data for userId {UserId}, " +
                    "fullName: {FullName}, gender: {Gender}, birthDate: {BirthDate}, country: {Country}, city: {City}",
                    userId.ToString(), fullName, gender, birthDate, birthCountry, birthCity);

                // Directly raise event to bypass validation - migration is internal data sync
                RaiseEvent(new UserProfileUpdatedEvent
                {
                    UserId = userId.ToString(),
                    FullName = fullName,
                    Gender = gender,
                    BirthDate = birthDate.Value,
                    BirthTime = birthTime,
                    BirthCountry = birthCountry,
                    BirthCity = birthCity,
                    MbtiType = null,
                    RelationshipStatus = null,
                    Interests = null,
                    CalendarType = null,
                    UpdatedAt = now,
                    CurrentResidence = null,
                    Email = null
                });

                await ConfirmEvents();

                _logger.LogInformation(
                    "[FortuneUserProfileGAgent][GetUserProfileAsync] Successfully migrated data from UserInfoCollection");
            }
            else
            {
                _logger.LogDebug(
                    "[FortuneUserProfileGAgent][GetUserProfileAsync] UserInfoCollection exists but insufficient data for migration (missing FullName or BirthDate)");
            }
        }
    }

    /// <summary>
    /// Get raw state data directly without any migration logic - used for migration checks to prevent circular dependency
    /// </summary>
    public Task<FortuneUserProfileDto?> GetRawStateAsync()
    {
        try
        {
            _logger.LogDebug("[FortuneUserProfileGAgent][GetRawStateAsync] Getting raw state for: {UserId}", 
                this.GetPrimaryKey());

            // If not initialized, return null immediately without any migration logic
            if (string.IsNullOrEmpty(State.UserId))
            {
                return Task.FromResult<FortuneUserProfileDto?>(null);
            }

            // Return raw state data without any processing or migration
            var profileDto = new FortuneUserProfileDto
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
                InsightsGeneratedAt = State.InsightsGeneratedAt,
                UpdatedAt = State.UpdatedAt,
                WelcomeNote = new Dictionary<string, string>() // Empty, no calculation
            };

            return Task.FromResult<FortuneUserProfileDto?>(profileDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortuneUserProfileGAgent][GetRawStateAsync] Error getting raw state");
            return Task.FromResult<FortuneUserProfileDto?>(null);
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
                
                // Log the raw AI response for debugging
                _logger.LogDebug("[FortuneUserProfileGAgent][ParseProfileInsightsResponse] Raw JSON: {Json}", jsonString.Length > 500 ? jsonString.Substring(0, 500) + "..." : jsonString);
                
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

    /// <summary>
    /// Build AI prompt for profile insights generation
    /// </summary>
    public static string BuildProfileInsightsPrompt(FortuneUserProfileDto profile)
    {
        // Build user info
        var userInfoParts = new List<string>();
        userInfoParts.Add($"Name: {profile.FullName}");
        userInfoParts.Add($"Birth Date: {profile.BirthDate:yyyy-MM-dd}");
        
        if (profile.BirthTime.HasValue)
        {
            userInfoParts.Add($"Birth Time: {profile.BirthTime.Value:HH:mm}");
        }
        
        if (!string.IsNullOrWhiteSpace(profile.BirthCity) && !string.IsNullOrWhiteSpace(profile.BirthCountry))
        {
            userInfoParts.Add($"Birth Place: {profile.BirthCity}, {profile.BirthCountry}");
        }
        else if (!string.IsNullOrWhiteSpace(profile.BirthCountry))
        {
            userInfoParts.Add($"Birth Country: {profile.BirthCountry}");
        }
        
        userInfoParts.Add($"Gender: {profile.Gender}");
        
        if (profile.CalendarType.HasValue)
        {
            var calendarType = profile.CalendarType.Value == CalendarTypeEnum.Solar ? "Solar" : "Lunar";
            userInfoParts.Add($"Calendar Type: {calendarType}");
        }
        
        var userInfo = string.Join(", ", userInfoParts);

        // Calculate Chinese Zodiac year
        var birthYear = profile.BirthDate.Year;
        var zodiacAnimals = new[] { "Rat", "Ox", "Tiger", "Rabbit", "Dragon", "Snake", "Horse", "Goat", "Monkey", "Rooster", "Dog", "Pig" };
        var zodiacAnimal = zodiacAnimals[(birthYear - 4) % 12];

        var prompt = $@"Generate comprehensive profile insights based on birth information.
User Info: {userInfo}
Birth Year Zodiac: {zodiacAnimal}

You MUST return valid JSON with three sections: astrology, bazi, and zodiac.

REQUIRED FORMAT:
{{
  ""astrology"": {{
    ""signPlacements"": ""{{\\""sunSign\\"": \\""[calculate]\\"", \\""moonSign\\"": \\""[calculate]\\"", \\""risingSign\\"": \\""[calculate]\\""}}"",
    ""significance"": ""{{\\""title\\"": \\""[Planet] in [Sign]\\"", \\""description\\"": \\""[30-50 words]\\""}}""
  }},
  ""bazi"": {{
    ""structure"": ""[Weak/Strong Self, Prosperous/Weak Wealth (身弱/身强财旺/财弱)]"",
    ""fourPillarsChart"": ""{{\\""hourPillar\\"":{{\\""heavenlyStem\\"":{{...5 fields...}}, \\""earthlyBranch\\"":{{...5 fields...}}}}, \\""dayPillar\\"":{{same}}, \\""monthPillar\\"":{{same}}, \\""yearPillar\\"":{{same}}}}"",
    ""energyFlow"": ""{{\\""dayMaster\\"": \\""[element]\\"", \\""usefulGods\\"": \\""[elements]\\"", \\""structure\\"": \\""[2-3 words]\\""}}""
    ""bodyStrength"": ""{{\\""result\\"": \\""[身强/身弱]\\"", \\""summary\\"": \\""[10-30 words]\\"", \\""overcontrollingElements\\"": \\""[10-30 words]\\"", \\""advice\\"": \\""[10-30 words]\\""}}""
    ""fiveElements"": ""{{\\""metal\\"": [1-10], \\""wood\\"": [1-10], \\""water\\"": [1-10], \\""fire\\"": [1-10], \\""earth\\"": [1-10], \\""overview\\"": \\""[10-30 words]\\""}}""
    ""tenTransformations"": ""{{\\""thePeer\\"": [1-3], \\""theChallenger\\"": [1-3], \\""thePerformer\\"": [1-3], \\""theInnovator\\"": [1-3], \\""theInvestor\\"": [1-3], \\""theWorker\\"": [1-3], \\""thePioneer\\"": [1-3], \\""theAdministrator\\"": [1-3], \\""theScholar\\"": [1-3], \\""theGuardian\\"": [1-3]}}""
    ""dayMasterDescription"": ""[10-30 words]""
  }},
  ""zodiac"": {{
    ""yourSign"": ""[combining element and animal]"",
    ""animalSpirit"": ""[50-100 words combining symbolism and characteristics]"",
    ""element"": ""[50-100 words combining element and animal]"",
    ""quickTraits"": ""{{\\""trait\\"": \\""[10-20 words]\\"", \\""personality\\"": \\""[10-20 words]\\"", \\""caution\\"": \\""[10-20 words]\\""}}""
    ""luckySet"": ""{{\\""colours\\"": [\\""[Color 色]\\"", \\""[Color 色]\\""], \\""numbers\\"": [[number], [number]], \\""days\\"": [\\""[Animal 支]\\"", \\""[Animal 支]\\""]}}""
  }}
}}

RULES:
- CRITICAL: Return ONLY valid JSON. NO special characters (+, &, #, etc.) outside of quoted strings. Ensure all nested JSON strings are properly escaped.
- Astrology: Calculate Sun/Moon/Rising signs. significance.title: ""[Planet] in [Sign]"", description: 10-30 words
- Bazi: 
  * bodyStrength.result: ""身弱 (Weak Self)"" OR ""身强 (Strong Self)""
  * structure: ""[Weak/Strong] Self, [Prosperous/Weak] Wealth (身弱/身强财旺/财弱)"" - MUST match bodyStrength.result
  * Four Pillars - EACH cell needs 5 fields (heavenlyStem or earthlyBranch):
    - heavenlyStem: yinYang (陽/陰 in traditional Chinese), element (木/火/土/金/水), character (甲乙丙丁戊己庚辛壬癸), pinyin (Jia/Yi/Bing/Ding/Wu/Ji/Geng/Xin/Ren/Gui - no brackets), direction (East 1/East 3/South 1/South 3/Centre/West 1/West 3/North 1/North 3)
    - earthlyBranch: yinYang (陽/陰), element (木/火/土/金/水), character (子丑寅卯辰巳午未申酉戌亥), pinyin (Zi/Chou/Yin/Mao/Chen/Si/Wu/Wei/Shen/You/Xu/Hai - no brackets), zodiac (Rat/Ox/Tiger/Rabbit/Dragon/Snake/Horse/Goat/Monkey/Rooster/Dog/Pig)
  * WARNING - FIXED MAPPINGS (DO NOT MIX): Each character MUST use its corresponding yinYang+element+pinyin+direction/zodiac from tables below
  * Heavenly Stems - FIXED mapping: 甲-陽木-Jia-East 1, 乙-陰木-Yi-East 3, 丙-陽火-Bing-South 1, 丁-陰火-Ding-South 3, 戊-陽土-Wu-Centre, 己-陰土-Ji-Centre, 庚-陽金-Geng-West 1, 辛-陰金-Xin-West 3, 壬-陽水-Ren-North 1, 癸-陰水-Gui-North 3
  * Earthly Branches - FIXED mapping: 子-陽水-Zi-Rat, 丑-陰土-Chou-Ox, 寅-陽木-Yin-Tiger, 卯-陰木-Mao-Rabbit, 辰-陽土-Chen-Dragon, 巳-陰火-Si-Snake, 午-陽火-Wu-Horse, 未-陰土-Wei-Goat, 申-陽金-Shen-Monkey, 酉-陰金-You-Rooster, 戌-陽土-Xu-Dog, 亥-陰水-Hai-Pig
  * fiveElements: 1-10, tenTransformations: 1-3, text: 10-30 words
- Zodiac: Animal {zodiacAnimal}, Element by year digit (0-1:Metal, 2-3:Water, 4-5:Wood, 6-7:Fire, 8-9:Earth). animalSpirit/element: 50-100 words each, quickTraits: 10-20 words, luckySet: 2-3 items

EXAMPLE (format reference):
{{
  ""astrology"": {{
    ""signPlacements"": ""{{\\""sunSign\\"": \\""Leo\\"", \\""moonSign\\"": \\""Cancer\\"", \\""risingSign\\"": \\""Sagittarius\\""}}"",
    ""significance"": ""{{\\""title\\"": \\""Sun in Leo\\"", \\""description\\"": \\""[10-30 words]\\""}}""
  }},
  ""bazi"": {{
    ""structure"": ""Strong Self, Prosperous Wealth (身强财旺)"",
    ""fourPillarsChart"": ""{{\\""hourPillar\\"":{{\\""heavenlyStem\\"":{{...}}, \\""earthlyBranch\\"":{{...}}}}, \\""dayPillar\\"":{{...}}, \\""monthPillar\\"":{{...}}, \\""yearPillar\\"":{{...}}}}"",
    ""energyFlow"": ""{{\\""dayMaster\\"": \\""Ren Water (壬水)\\"", \\""usefulGods\\"": \\""Wood & Fire (木火)\\"", \\""structure\\"": \\""Strong Self\\""}}""
    ""bodyStrength"": ""{{\\""result\\"": \\""身强 (Strong Self)\\"", \\""summary\\"": \\""[10-30 words]\\"", \\""overcontrollingElements\\"": \\""[10-30 words]\\"", \\""advice\\"": \\""[10-30 words]\\""}}""
    ""fiveElements"": ""{{\\""metal\\"": 6, \\""wood\\"": 4, \\""water\\"": 8, \\""fire\\"": 3, \\""earth\\"": 2, \\""overview\\"": \\""[10-30 words]\\""}}""
    ""tenTransformations"": ""{{\\""thePeer\\"": 3, \\""theChallenger\\"": 1, \\""thePerformer\\"": 2, \\""theInnovator\\"": 2, \\""theInvestor\\"": 1, \\""theWorker\\"": 1, \\""thePioneer\\"": 2, \\""theAdministrator\\"": 1, \\""theScholar\\"": 2, \\""theGuardian\\"": 2}}""
    ""dayMasterDescription"": ""[10-30 words]""
  }},
  ""zodiac"": {{
    ""yourSign"": ""Water Horse (水马)"",
    ""animalSpirit"": ""Horse (马) - [50-100 words]"",
    ""element"": ""Water (水) - [50-100 words]"",
    ""quickTraits"": ""{{\\""trait\\"": \\""[10-20 words]\\"", \\""personality\\"": \\""[10-20 words]\\"", \\""caution\\"": \\""[10-20 words]\\""}}""
    ""luckySet"": ""{{\\""colours\\"": [\\""Blue 蓝\\"", \\""Green 绿\\""], \\""numbers\\"": [2, 3], \\""days\\"": [\\""Tiger 虎\\"", \\""Dog 戌\\""]}}""
  }}
}}";

        return prompt;
    }

    public async Task<ClearUserResult> ClearUserAsync()
    {
        try
        {
            _logger.LogDebug("[FortuneUserProfileGAgent][ClearUserAsync] Clearing user profile data for: {GrainId}", 
                this.GetPrimaryKey());

            // Check if user exists
            if (string.IsNullOrEmpty(State.UserId))
            {
                _logger.LogWarning("[FortuneUserProfileGAgent][ClearUserAsync] User profile not found");
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

            _logger.LogInformation("[FortuneUserProfileGAgent][ClearUserAsync] User profile cleared successfully");

            return new ClearUserResult
            {
                Success = true,
                Message = "User profile cleared successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortuneUserProfileGAgent][ClearUserAsync] Error clearing user profile");
            return new ClearUserResult
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
        var zodiac = FortuneCalculator.CalculateZodiacSign(birthDate);
        var chineseZodiac = FortuneCalculator.GetChineseZodiacWithElement(birthYear);
        
        // Calculate rhythm (Yin/Yang + Element from birth year stems)
        var birthYearStems = FortuneCalculator.CalculateStemsAndBranches(birthYear);
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

    #endregion
}

