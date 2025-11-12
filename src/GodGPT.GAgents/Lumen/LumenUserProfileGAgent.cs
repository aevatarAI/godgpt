using Aevatar.Application.Grains.Lumen.Dtos;
using Aevatar.Application.Grains.Lumen.SEvents;
using Aevatar.Application.Grains.UserInfo;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
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
}

[GAgent(nameof(LumenUserProfileGAgent))]
[Reentrant]
public class LumenUserProfileGAgent : GAgentBase<LumenUserProfileState, LumenUserProfileEventLog>, ILumenUserProfileGAgent
{
    private readonly ILogger<LumenUserProfileGAgent> _logger;
    
    /// <summary>
    /// Valid lumen prediction actions
    /// </summary>
    private static readonly HashSet<string> ValidActions = new()
    {
        "forecast", "horoscope", "bazi", "ziwei", "constellation", 
        "numerology", "synastry", "chineseZodiac", "mayanTotem", 
        "humanFigure", "tarot", "zhengYu"
    };

    public LumenUserProfileGAgent(ILogger<LumenUserProfileGAgent> logger)
    {
        _logger = logger;
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
                state.CreatedAt = default;
                state.UpdatedAt = clearEvent.ClearedAt;
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

            _logger.LogInformation("[LumenUserProfileGAgent][UpdateUserProfileAsync] User profile updated successfully: {UserId}", 
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

            if (string.IsNullOrEmpty(State.UserId))
            {
                // Try to migrate data from UserInfoCollectionGAgent
                await TryToMigrateDataFromUserInfoCollectionAsync(userId);
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
                    BirthTime = State.BirthTime,
                    BirthCountry = State.BirthCountry,
                    BirthCity = State.BirthCity,
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
                    Email = State.Email
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

    private async Task TryToMigrateDataFromUserInfoCollectionAsync(Guid userId)
    {
        _logger.LogWarning(
            "[LumenUserProfileGAgent][GetUserProfileAsync] Lumen user profile not initialized. {GrainId}",
            this.GetPrimaryKey().ToString());

        // Use GetRawStateAsync to prevent circular dependency
        var userInfoCollectionGAgent = GrainFactory.GetGrain<IUserInfoCollectionGAgent>(userId);
        var userInfoResult = await userInfoCollectionGAgent.GetRawStateAsync();

        if (userInfoResult != null && userInfoResult.IsInitialized && userInfoResult.NameInfo != null)
        {
            _logger.LogInformation(
                "[LumenUserProfileGAgent][GetUserProfileAsync] Migrating data from UserInfoCollection to LumenUserProfile, userId {UserId}",
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
                        "[LumenUserProfileGAgent][GetUserProfileAsync] Invalid birth date: {Year}-{Month}-{Day}",
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
                    "[LumenUserProfileGAgent][GetUserProfileAsync] Saving migrated data for userId {UserId}, " +
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
                    "[LumenUserProfileGAgent][GetUserProfileAsync] Successfully migrated data from UserInfoCollection");
            }
            else
            {
                _logger.LogDebug(
                    "[LumenUserProfileGAgent][GetUserProfileAsync] UserInfoCollection exists but insufficient data for migration (missing FullName or BirthDate)");
            }
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
                BirthTime = State.BirthTime,
                BirthCountry = State.BirthCountry,
                BirthCity = State.BirthCity,
                CalendarType = State.CalendarType,
                CreatedAt = State.CreatedAt,
                CurrentResidence = State.CurrentResidence,
                UpdatedAt = State.UpdatedAt,
                WelcomeNote = new Dictionary<string, string>(), // Empty, no calculation
                Occupation = State.Occupation,
                MbtiType = State.MbtiType,
                RelationshipStatus = State.RelationshipStatus,
                Interests = State.Interests,
                Email = State.Email
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
            "zh-tw" or "zh" => zodiacSign switch
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
            ("Wood", "zh-tw" or "zh") => "木",
            ("Fire", "zh-tw" or "zh") => "火",
            ("Earth", "zh-tw" or "zh") => "土",
            ("Metal", "zh-tw" or "zh") => "金",
            ("Water", "zh-tw" or "zh") => "水",
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
            ("Rat", "zh-tw" or "zh") => "鼠",
            ("Ox", "zh-tw" or "zh") => "牛",
            ("Tiger", "zh-tw" or "zh") => "虎",
            ("Rabbit", "zh-tw" or "zh") => "兔",
            ("Dragon", "zh-tw" or "zh") => "龍",
            ("Snake", "zh-tw" or "zh") => "蛇",
            ("Horse", "zh-tw" or "zh") => "馬",
            ("Goat", "zh-tw" or "zh") => "羊",
            ("Monkey", "zh-tw" or "zh") => "猴",
            ("Rooster", "zh-tw" or "zh") => "雞",
            ("Dog", "zh-tw" or "zh") => "狗",
            ("Pig", "zh-tw" or "zh") => "豬",
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
            "zh-tw" or "zh" => $"{(yinYang == "Yang" ? "陽" : "陰")}{TranslateElement(element, language)}",
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
        // Common personality traits translation (simplified version)
        var lowerTrait = trait.ToLower();
        
        if (language == "zh-tw" || language == "zh")
        {
            return lowerTrait switch
            {
                "adventurous" => "冒險的",
                "bold" => "勇敢的",
                "passionate" => "熱情的",
                "reliable" => "可靠的",
                "patient" => "耐心的",
                "practical" => "務實的",
                "curious" => "好奇的",
                "adaptable" => "適應力強的",
                "confident" => "自信的",
                "generous" => "慷慨的",
                "analytical" => "分析的",
                "diplomatic" => "圓融的",
                "intense" => "強烈的",
                "optimistic" => "樂觀的",
                "ambitious" => "有抱負的",
                "innovative" => "創新的",
                "intuitive" => "直覺的",
                "compassionate" => "富有同情心的",
                _ => trait
            };
        }
        
        if (language == "es")
        {
            return lowerTrait switch
            {
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
                _ => trait
            };
        }
        
        return trait;
    }

    #endregion
}

