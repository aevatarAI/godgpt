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
                    InsightsGeneratedAt = State.InsightsGeneratedAt,
                    UpdatedAt = State.UpdatedAt
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
}

