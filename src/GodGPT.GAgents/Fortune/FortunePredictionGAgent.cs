using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Fortune.Dtos;
using Aevatar.Application.Grains.Fortune.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Options;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Fortune;

/// <summary>
/// Interface for Fortune Prediction GAgent - manages fortune prediction generation
/// </summary>
public interface IFortunePredictionGAgent : IGAgent
{
    Task<GetTodayPredictionResult> GetOrGeneratePredictionAsync(FortuneUserDto userInfo, bool lifetime = false);
    
    [ReadOnly]
    Task<PredictionResultDto?> GetPredictionAsync();
}

[GAgent(nameof(FortunePredictionGAgent))]
[Reentrant]
public class FortunePredictionGAgent : GAgentBase<FortunePredictionState, FortunePredictionEventLog>, 
    IFortunePredictionGAgent
{
    private readonly ILogger<FortunePredictionGAgent> _logger;
    private readonly IClusterClient _clusterClient;

    public FortunePredictionGAgent(
        ILogger<FortunePredictionGAgent> logger,
        IClusterClient clusterClient)
    {
        _logger = logger;
        _clusterClient = clusterClient;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Fortune prediction generation and caching");
    }

    /// <summary>
    /// Event-driven state transition handler
    /// </summary>
    protected sealed override void GAgentTransitionState(FortunePredictionState state,
        StateLogEventBase<FortunePredictionEventLog> @event)
    {
        switch (@event)
        {
            case PredictionGeneratedEvent generatedEvent:
                state.PredictionId = generatedEvent.PredictionId;
                state.UserId = generatedEvent.UserId;
                state.PredictionDate = generatedEvent.PredictionDate;
                state.Results = generatedEvent.Results;
                state.Energy = generatedEvent.Energy;
                state.CreatedAt = generatedEvent.CreatedAt;
                state.ProfileUpdatedAt = generatedEvent.ProfileUpdatedAt;
                if (!generatedEvent.LifetimeForecast.IsNullOrEmpty())
                {
                    state.LifetimeForecast = generatedEvent.LifetimeForecast;
                }
                if (!generatedEvent.WeeklyForecast.IsNullOrEmpty())
                {
                    state.WeeklyForecast = generatedEvent.WeeklyForecast;
                    state.WeeklyGeneratedDate = generatedEvent.WeeklyGeneratedDate;
                }
                break;
        }
    }

    public async Task<GetTodayPredictionResult> GetOrGeneratePredictionAsync(FortuneUserDto userInfo, bool lifetime = false)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            _logger.LogDebug("[FortunePredictionGAgent][GetOrGeneratePredictionAsync] Start - UserId: {UserId}, Date: {Date}",
                userInfo.UserId, today);

            // Check if prediction already exists (from cache/state)
            if (lifetime)
            {
                // Check if lifetime exists
                var hasLifetime = !State.LifetimeForecast.IsNullOrEmpty();
                
                // Check if weekly exists and is not expired (< 7 days old)
                var hasValidWeekly = !State.WeeklyForecast.IsNullOrEmpty() && 
                                     State.WeeklyGeneratedDate.HasValue && 
                                     (DateTime.UtcNow - State.WeeklyGeneratedDate.Value).TotalDays < 7;
                
                // Check if profile has been updated since prediction was generated
                var profileNotChanged = !State.ProfileUpdatedAt.HasValue || userInfo.UpdatedAt <= State.ProfileUpdatedAt.Value;
                
                // If both lifetime and valid weekly exist AND profile hasn't changed, return from cache
                if (hasLifetime && hasValidWeekly && profileNotChanged)
                {
                    _logger.LogInformation("[FortunePredictionGAgent][GetOrGeneratePredictionAsync] Returning cached lifetime+weekly prediction for {UserId}",
                        userInfo.UserId);

                    // Calculate and add currentPhase
                    var lifetimeWithPhase = new Dictionary<string, string>(State.LifetimeForecast);
                    var currentPhase = CalculateCurrentPhase(userInfo.BirthDate);
                    lifetimeWithPhase["currentPhase"] = currentPhase.ToString();

                    return new GetTodayPredictionResult
                    {
                        Success = true,
                        Message = string.Empty,
                        Prediction = new PredictionResultDto
                        {
                            PredictionId = State.PredictionId,
                            UserId = State.UserId,
                            PredictionDate = State.PredictionDate,
                            Results = State.Results,
                            CreatedAt = State.CreatedAt,
                            FromCache = true,
                            LifetimeForecast = lifetimeWithPhase,
                            WeeklyForecast = State.WeeklyForecast
                        }
                    };
                }
                
                // Log reason for regeneration
                if (!hasValidWeekly)
                {
                    _logger.LogInformation("[FortunePredictionGAgent][GetOrGeneratePredictionAsync] Weekly expired or missing, regenerating for {UserId}",
                        userInfo.UserId);
                }
                if (!profileNotChanged)
                {
                    _logger.LogInformation("[FortunePredictionGAgent][GetOrGeneratePredictionAsync] Profile updated, regenerating prediction for {UserId}",
                        userInfo.UserId);
                }
            } 
            
            // Check daily prediction cache
            if (!lifetime && State.PredictionId != Guid.Empty && State.PredictionDate == today)
            {
                // Check if profile has been updated since prediction was generated
                var profileNotChanged = !State.ProfileUpdatedAt.HasValue || userInfo.UpdatedAt <= State.ProfileUpdatedAt.Value;
                
                if (profileNotChanged)
                {
                    _logger.LogInformation("[FortunePredictionGAgent][GetOrGeneratePredictionAsync] Returning cached prediction for {UserId}",
                        userInfo.UserId);

                    return new GetTodayPredictionResult
                    {
                        Success = true,
                        Message = string.Empty,
                        Prediction = new PredictionResultDto
                        {
                            PredictionId = State.PredictionId,
                            UserId = State.UserId,
                            PredictionDate = State.PredictionDate,
                            Results = State.Results,
                            CreatedAt = State.CreatedAt,
                            FromCache = true
                        }
                    };
                }
                else
                {
                    _logger.LogInformation("[FortunePredictionGAgent][GetOrGeneratePredictionAsync] Profile updated, regenerating daily prediction for {UserId}",
                        userInfo.UserId);
                }
            }

            // Generate new prediction
            _logger.LogInformation("[FortunePredictionGAgent][GetOrGeneratePredictionAsync] Generating new prediction for {UserId}, {Lifetime}",
                userInfo.UserId, lifetime);

            var predictionResult = await GeneratePredictionAsync(userInfo, today, lifetime);
            if (!predictionResult.Success)
            {
                return predictionResult;
            }

            return predictionResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortunePredictionGAgent][GetOrGeneratePredictionAsync] Error generating prediction");
            return new GetTodayPredictionResult
            {
                Success = false,
                Message = "Failed to generate prediction"
            };
        }
    }

    /// <summary>
    /// Get prediction from state without generating
    /// </summary>
    public Task<PredictionResultDto?> GetPredictionAsync()
    {
        if (State.PredictionId == Guid.Empty)
        {
            return Task.FromResult<PredictionResultDto?>(null);
        }

        return Task.FromResult<PredictionResultDto?>(new PredictionResultDto
        {
            PredictionId = State.PredictionId,
            UserId = State.UserId,
            PredictionDate = State.PredictionDate,
            Results = State.Results,
            CreatedAt = State.CreatedAt,
            FromCache = true,
            LifetimeForecast = State.LifetimeForecast,
            WeeklyForecast = State.WeeklyForecast
        });
    }

    /// <summary>
    /// Generate new prediction using AI
    /// </summary>
    private async Task<GetTodayPredictionResult> GeneratePredictionAsync(FortuneUserDto userInfo, DateOnly predictionDate, bool lifetime)
    {
        try
        {
            // Build prompt
            var prompt = BuildPredictionPrompt(userInfo, predictionDate, lifetime);

            _logger.LogDebug("[FortunePredictionGAgent][GeneratePredictionAsync] Calling AI with prompt for user {UserId}",
                userInfo.UserId);

            // NOTE: Using IGodChat.ChatWithoutHistoryAsync for AI calls
            // This is a temporary solution that reuses existing chat infrastructure
            // Ideally, Fortune should have a dedicated lightweight AI service that:
            // - Reads LLM config from Options
            // - Makes simple HTTP calls to OpenAI/Azure API
            // - No session/history management overhead
            // TODO: Create FortuneAIService for direct API calls when time permits
            
            var userGuid = CommonHelper.StringToGuid(userInfo.UserId);
            var godChat = _clusterClient.GetGrain<IGodChat>(userGuid);
            var chatId = Guid.NewGuid().ToString();

            var settings = new ExecutionPromptSettings
            {
                Temperature = "0.7"
            };

            // Use dedicated "FORTUNE" region for independent LLM configuration
            // This allows Fortune to use cost-optimized models (e.g., GPT-4o-mini)
            // separate from the main chat experience
            var response = await godChat.ChatWithoutHistoryAsync(
                userGuid, 
                string.Empty, 
                prompt, 
                chatId, 
                settings, 
                true, 
                "FORTUNE");

            if (response == null || response.Count() == 0)
            {
                _logger.LogWarning("[FortunePredictionGAgent][GeneratePredictionAsync] No response from AI for user {UserId}",
                    userInfo.UserId);
                return new GetTodayPredictionResult
                {
                    Success = false,
                    Message = "AI service returned no response"
                };
            }

            var aiResponse = response[0].Content;
            _logger.LogDebug("[FortunePredictionGAgent][GeneratePredictionAsync] Received AI response: {Response}",
                aiResponse);

            Dictionary<string, Dictionary<string, string>>? parsedResults = null;
            Dictionary<string, string>? lifetimeForecast = State.LifetimeForecast ?? new Dictionary<string, string>();
            Dictionary<string, string>? weeklyForecast = State.WeeklyForecast ?? new Dictionary<string, string>();

            // Parse AI response based on type
            if (lifetime)
            {
                // Parse Lifetime & Weekly
                var (parsedLifetime, parsedWeekly) = ParseLifetimeWeeklyResponse(aiResponse);
                if (parsedLifetime == null || parsedWeekly == null)
                {
                    _logger.LogError("[FortunePredictionGAgent][GeneratePredictionAsync] Failed to parse Lifetime/Weekly response");
                    return new GetTodayPredictionResult
                    {
                        Success = false,
                        Message = "Failed to parse AI response"
                    };
                }
                
                lifetimeForecast = parsedLifetime;
                weeklyForecast = parsedWeekly;
                parsedResults = new Dictionary<string, Dictionary<string, string>>(); // Empty for lifetime mode
            }
            else
            {
                // Parse Daily (6 dimensions)
                parsedResults = ParseDailyResponse(aiResponse);
                if (parsedResults == null)
                {
                    _logger.LogError("[FortunePredictionGAgent][GeneratePredictionAsync] Failed to parse Daily response");
                    return new GetTodayPredictionResult
                    {
                        Success = false,
                        Message = "Failed to parse AI response"
                    };
                }
            }

            var predictionId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            // Raise event to save prediction
            RaiseEvent(new PredictionGeneratedEvent
            {
                PredictionId = predictionId,
                UserId = userInfo.UserId,
                PredictionDate = predictionDate,
                Results = parsedResults,
                Energy = 0, // Not used, kept for State compatibility
                CreatedAt = now,
                LifetimeForecast = lifetimeForecast,
                WeeklyForecast = weeklyForecast,
                WeeklyGeneratedDate = weeklyForecast.IsNullOrEmpty() ? null : now,
                ProfileUpdatedAt = userInfo.UpdatedAt
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[FortunePredictionGAgent][GeneratePredictionAsync] Prediction generated successfully for user {UserId}",
                userInfo.UserId);

            // Add currentPhase if lifetime was generated
            if (lifetime && lifetimeForecast != null && !lifetimeForecast.IsNullOrEmpty())
            {
                var currentPhase = CalculateCurrentPhase(userInfo.BirthDate);
                lifetimeForecast["currentPhase"] = currentPhase.ToString();
            }

            return new GetTodayPredictionResult
            {
                Success = true,
                Message = string.Empty,
                Prediction = new PredictionResultDto
                {
                    PredictionId = predictionId,
                    UserId = userInfo.UserId,
                    PredictionDate = predictionDate,
                    Results = parsedResults,
                    CreatedAt = now,
                    FromCache = false,
                    LifetimeForecast = lifetimeForecast,
                    WeeklyForecast = weeklyForecast
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortunePredictionGAgent][GeneratePredictionAsync] Error in AI generation");
            return new GetTodayPredictionResult
            {
                Success = false,
                Message = $"AI generation error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Build prediction prompt for AI
    /// </summary>
    private string BuildPredictionPrompt(FortuneUserDto userInfo, DateOnly predictionDate, bool lifetime)
    {
        // Build user info line dynamically based on available fields
        var userInfoParts = new List<string>();
        
        // Required fields
        userInfoParts.Add($"{userInfo.FirstName} {userInfo.LastName}");
        
        // Birth date and time (only include time if provided)
        var birthDateStr = $"Birth: {userInfo.BirthDate:yyyy-MM-dd}";
        if (userInfo.BirthTime.HasValue)
        {
            birthDateStr += $" {userInfo.BirthTime.Value:HH:mm}";
        }
        
        // Calendar type (only include if provided)
        if (userInfo.CalendarType.HasValue)
        {
            var calendarType = userInfo.CalendarType.Value == CalendarTypeEnum.Solar ? "Solar" : "Lunar";
            birthDateStr += $" ({calendarType} calendar)";
        }
        
        userInfoParts.Add(birthDateStr);
        
        // Birth location (optional)
        if (!string.IsNullOrWhiteSpace(userInfo.BirthCity) && !string.IsNullOrWhiteSpace(userInfo.BirthCountry))
        {
            userInfoParts.Add($"at {userInfo.BirthCity}, {userInfo.BirthCountry}");
        }
        else if (!string.IsNullOrWhiteSpace(userInfo.BirthCountry))
        {
            userInfoParts.Add($"in {userInfo.BirthCountry}");
        }
        
        // Gender
        userInfoParts.Add($"Gender: {userInfo.Gender}");
        
        // Relationship status (optional)
        if (userInfo.RelationshipStatus.HasValue)
        {
            userInfoParts.Add($"Status: {userInfo.RelationshipStatus}");
        }
        
        // Interests (optional)
        if (!string.IsNullOrWhiteSpace(userInfo.Interests))
        {
            userInfoParts.Add($"Interests: {userInfo.Interests}");
        }
        
        // MBTI (optional)
        if (userInfo.MbtiType.HasValue)
        {
            userInfoParts.Add($"MBTI: {userInfo.MbtiType}");
        }
        
        var userInfoLine = string.Join(", ", userInfoParts);

        string prompt = string.Empty;
        if (lifetime)
        {
            prompt = $@"Generate lifetime and weekly fortune prediction.
User: {userInfoLine}
Date: {predictionDate:yyyy-MM-dd}

Return JSON with lifetime and weekly predictions:
{{
  ""lifetime"": {{
    ""title"": ""2-4 words"",
    ""description"": ""30-50 words about life transformation"",
    ""traits"": {{
      ""fateRarity"": {{""percentage"": 2.4, ""description"": ""6-10 words""}},
      ""mainElements"": ""Fire (火), Metal (金)"",
      ""lifePath"": ""Leader, Mentor, Innovator""
    }},
    ""phases"": {{
      ""phase1"": {{""description"": ""15-30 words""}},
      ""phase2"": {{""description"": ""15-30 words""}},
      ""phase3"": {{""description"": ""15-30 words""}}
    }}
  }},
  ""weekly"": {{
    ""health"": 4,
    ""money"": 3,
    ""career"": 4,
    ""romance"": 5,
    ""focus"": 3
  }}
}}

CRITICAL RULES:
- title: 2-4 words, poetic
- description: 30-50 words
- fateRarity.percentage: 0.1-10.0 (smaller = more rare)
- fateRarity.description: 6-10 words
- mainElements: String with 1-2 elements in bilingual format, e.g., ""Fire (火), Metal (金)""
- mainElements MUST use bilingual: ""Fire (火)"", ""Metal (金)"", ""Water (水)"", ""Wood (木)"", ""Earth (土)""
- lifePath: String with 2-4 roles separated by commas, e.g., ""Leader, Mentor, Innovator""
- phases: Each description 15-30 words (phase1: 0-20 years, phase2: 21-35 years, phase3: 36+ years)
- weekly: 5 dimensions, each 0-5 integer
- Return valid JSON only, no additional text";
        }
        else
        {
            prompt = $@"Generate daily fortune prediction for {predictionDate:yyyy-MM-dd}.
User: {userInfoLine}

Analyze 6 dimensions: opportunity, bazi, astrology, tarot, lifeTheme1, lifeTheme2

Return JSON:
{{
  ""opportunity"": {{
    ""color"": ""word"",
    ""crystal"": ""word"",
    ""number"": ""word"",
    ""title"": ""3-5 words"",
    ""description"": ""10-25 words""
  }},
  ""bazi"": {{
    ""heavenlyStemEarthlyBranch"": ""10-15 words explanatory content with bilingual elements"",
    ""fiveElements"": ""10-15 words explanatory content, MUST use format like Fire (火), Earth (土)"",
    ""compatibility"": ""10-15 words explanatory content"",
    ""energyFlow"": ""10-15 words explanatory content""
  }},
  ""astrology"": {{
    ""sunSign"": ""word"",
    ""moonSign"": ""word"",
    ""risingSign"": ""word"",
    ""overallFortune"": ""8.2"",
    ""luckyElement"": ""Earth (土)"",
    ""keywordFocus"": ""word"",
    ""moonInfluence"": ""10-15 words""
  }},
  ""tarot"": {{
    ""card"": ""Position | Card Name · Orientation"",
    ""interpretation"": ""25-30 words""
  }},
  ""lifeTheme1"": {{
    ""theme"": ""1 word (AI generates)"",
    ""description"": ""30+ words""
  }},
  ""lifeTheme2"": {{
    ""theme"": ""1 word (AI generates)"",
    ""description"": ""30+ words""
  }}
}}

CRITICAL RULES:
- opportunity.title: 3-5 words
- opportunity.description: 10-25 words
- bazi fields: Each 10-15 words EXPLANATORY content (explain meaning, not just list)
- bazi MUST use bilingual elements: ""Fire (火)"", ""Metal (金)"", ""Water (水)"", ""Wood (木)"", ""Earth (土)""
- astrology.overallFortune: 0-10 with one decimal (e.g., ""8.2"")
- astrology.luckyElement: MUST be bilingual like ""Earth (土)""
- astrology.moonInfluence: 10-15 words
- tarot.card: Format ""Position | CardName · Upright/Reversed""
- tarot.interpretation: 25-30 words
- lifeTheme: AI freely generates theme name and content
- lifeTheme.theme: Single word
- lifeTheme.description: 30+ words (detailed explanation)
- Return valid JSON only, no additional text";            
        }

        return prompt;
    }

    /// <summary>
    /// Calculate current life phase based on birth date
    /// </summary>
    private string CalculateCurrentPhase(DateOnly birthDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var age = today.Year - birthDate.Year;
        
        // Adjust if birthday hasn't occurred this year
        if (today < birthDate.AddYears(age))
        {
            age--;
        }
        
        if (age <= 20) return "phase1";
        if (age <= 35) return "phase2";
        return "phase3";
    }

    /// <summary>
    /// Parse Lifetime & Weekly AI response
    /// </summary>
    private (Dictionary<string, string>?, Dictionary<string, string>?) ParseLifetimeWeeklyResponse(string aiResponse)
    {
        try
        {
            var jsonStart = aiResponse.IndexOf('{');
            var jsonEnd = aiResponse.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonString = aiResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var response = JsonConvert.DeserializeObject<dynamic>(jsonString);
                
                if (response == null)
                {
                    return (null, null);
                }

                // Parse Lifetime
                var lifetimeDict = new Dictionary<string, string>();
                if (response.lifetime != null)
                {
                    lifetimeDict["title"] = response.lifetime.title?.ToString() ?? "";
                    lifetimeDict["description"] = response.lifetime.description?.ToString() ?? "";
                    
                    // Serialize complex objects to JSON strings
                    if (response.lifetime.traits != null)
                    {
                        lifetimeDict["traits"] = JsonConvert.SerializeObject(response.lifetime.traits);
                    }
                    if (response.lifetime.phases != null)
                    {
                        lifetimeDict["phases"] = JsonConvert.SerializeObject(response.lifetime.phases);
                    }
                }

                // Parse Weekly
                var weeklyDict = new Dictionary<string, string>();
                if (response.weekly != null)
                {
                    weeklyDict["health"] = response.weekly.health?.ToString() ?? "0";
                    weeklyDict["money"] = response.weekly.money?.ToString() ?? "0";
                    weeklyDict["career"] = response.weekly.career?.ToString() ?? "0";
                    weeklyDict["romance"] = response.weekly.romance?.ToString() ?? "0";
                    weeklyDict["focus"] = response.weekly.focus?.ToString() ?? "0";
                }

                return (lifetimeDict, weeklyDict);
            }

            return (null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortunePredictionGAgent][ParseLifetimeWeeklyResponse] Failed to parse");
            return (null, null);
        }
    }

    /// <summary>
    /// Parse Daily AI response (6 dimensions)
    /// </summary>
    private Dictionary<string, Dictionary<string, string>>? ParseDailyResponse(string aiResponse)
    {
        try
        {
            var jsonStart = aiResponse.IndexOf('{');
            var jsonEnd = aiResponse.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonString = aiResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                
                // Try to parse as direct results structure
                var results = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(jsonString);
                if (results != null && results.Count > 0)
                {
                    return results;
                }
                
                // Try to parse as wrapped structure with "results" key
                var fullResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                if (fullResponse != null && fullResponse.ContainsKey("results") && fullResponse["results"] != null)
                {
                    var resultsJson = JsonConvert.SerializeObject(fullResponse["results"]);
                    results = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(resultsJson);
                    return results;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortunePredictionGAgent][ParseDailyResponse] Failed to parse");
            return null;
        }
    }

    /// <summary>
    /// Parse AI JSON response (structure with energy at top level and forecast in results)
    /// [DEPRECATED] Kept for backward compatibility
    /// </summary>
    private (Dictionary<string, Dictionary<string, string>>?, int) ParseAIResponse(string aiResponse)
    {
        try
        {
            // Try to extract JSON from response (in case AI adds extra text)
            var jsonStart = aiResponse.IndexOf('{');
            var jsonEnd = aiResponse.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonString = aiResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var fullResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString);
                
                if (fullResponse == null)
                {
                    return (null, 70);
                }

                // Extract energy (supporting both "energy" and "overallEnergy" for backward compatibility)
                var energy = 70; // Default value
                if (fullResponse.ContainsKey("energy"))
                {
                    if (fullResponse["energy"] is long energyLong)
                    {
                        energy = (int)energyLong;
                    }
                    else if (fullResponse["energy"] is int energyInt)
                    {
                        energy = energyInt;
                    }
                }
                else if (fullResponse.ContainsKey("overallEnergy"))
                {
                    if (fullResponse["overallEnergy"] is long energyLong)
                    {
                        energy = (int)energyLong;
                    }
                    else if (fullResponse["overallEnergy"] is int energyInt)
                    {
                        energy = energyInt;
                    }
                }

                // Extract results (should include forecast as first item)
                if (fullResponse.ContainsKey("results") && fullResponse["results"] != null)
                {
                    var resultsJson = JsonConvert.SerializeObject(fullResponse["results"]);
                    var results = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(resultsJson);
                    return (results, energy);
                }

                return (null, energy);
            }

            return (null, 70);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortunePredictionGAgent][ParseAIResponse] Failed to parse JSON");
            return (null, 70);
        }
    }
}

