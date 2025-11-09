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
/// Prediction type enumeration
/// </summary>
public enum PredictionType
{
    Daily = 0,      // Daily prediction - updates every day
    Yearly = 1,     // Yearly prediction - updates every year
    Lifetime = 2    // Lifetime prediction - never updates (unless profile changes)
}

/// <summary>
/// Interface for Fortune Prediction GAgent - manages fortune prediction generation
/// </summary>
public interface IFortunePredictionGAgent : IGAgent
{
    Task<GetTodayPredictionResult> GetOrGeneratePredictionAsync(FortuneUserDto userInfo, PredictionType type = PredictionType.Daily);
    
    [ReadOnly]
    Task<PredictionResultDto?> GetPredictionAsync();
}

[GAgent(nameof(FortunePredictionGAgent))]
[Reentrant]
public class FortunePredictionGAgent : GAgentBase<LumenPredictionState, LumenPredictionEventLog>, 
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
    protected sealed override void GAgentTransitionState(LumenPredictionState state,
        StateLogEventBase<LumenPredictionEventLog> @event)
    {
        switch (@event)
        {
            case LumenPredictionGeneratedEvent generatedEvent:
                state.PredictionId = generatedEvent.PredictionId;
                state.UserId = generatedEvent.UserId;
                state.PredictionDate = generatedEvent.PredictionDate;
                state.Results = generatedEvent.Results;
                state.CreatedAt = generatedEvent.CreatedAt;
                state.ProfileUpdatedAt = generatedEvent.ProfileUpdatedAt;
                if (!generatedEvent.LifetimeForecast.IsNullOrEmpty())
                {
                    state.LifetimeForecast = generatedEvent.LifetimeForecast;
                }
                if (!generatedEvent.YearlyForecast.IsNullOrEmpty())
                {
                    state.YearlyForecast = generatedEvent.YearlyForecast;
                    state.YearlyGeneratedDate = generatedEvent.YearlyGeneratedDate;
                }
                // Update multilingual caches
                if (generatedEvent.MultilingualResults != null)
                {
                    state.MultilingualResults = generatedEvent.MultilingualResults;
                }
                if (generatedEvent.MultilingualLifetime != null)
                {
                    state.MultilingualLifetime = generatedEvent.MultilingualLifetime;
                }
                if (generatedEvent.MultilingualYearly != null)
                {
                    state.MultilingualYearly = generatedEvent.MultilingualYearly;
                }
                break;
        }
    }

    public async Task<GetTodayPredictionResult> GetOrGeneratePredictionAsync(FortuneUserDto userInfo, PredictionType type = PredictionType.Daily)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var currentYear = today.Year;
            
            _logger.LogDebug("[FortunePredictionGAgent][GetOrGeneratePredictionAsync] Start - UserId: {UserId}, Type: {Type}, Date: {Date}",
                userInfo.UserId, type, today);
                
                // Check if profile has been updated since prediction was generated
                var profileNotChanged = !State.ProfileUpdatedAt.HasValue || userInfo.UpdatedAt <= State.ProfileUpdatedAt.Value;
                
            // Check if prediction already exists (from cache/state) based on type
            if (type == PredictionType.Lifetime)
            {
                // Lifetime: never expires unless profile changes
                var hasLifetime = !State.LifetimeForecast.IsNullOrEmpty();
                
                if (hasLifetime && profileNotChanged)
                {
                    _logger.LogInformation("[FortunePredictionGAgent][GetOrGeneratePredictionAsync] Returning cached lifetime+weekly prediction (multilingual) for {UserId}",
                        userInfo.UserId);

                    // Calculate and add currentPhase to all language versions
                    var lifetimeWithPhase = new Dictionary<string, string>(State.LifetimeForecast);
                    var currentPhase = CalculateCurrentPhase(userInfo.BirthDate);
                    lifetimeWithPhase["currentPhase"] = currentPhase.ToString();
                    
                    // Add currentPhase to multilingual versions too
                    Dictionary<string, Dictionary<string, string>>? multilingualLifetimeWithPhase = null;
                    if (State.MultilingualLifetime != null)
                    {
                        multilingualLifetimeWithPhase = new Dictionary<string, Dictionary<string, string>>();
                        foreach (var kvp in State.MultilingualLifetime)
                        {
                            var lifetimeCopy = new Dictionary<string, string>(kvp.Value);
                            lifetimeCopy["currentPhase"] = currentPhase.ToString();
                            multilingualLifetimeWithPhase[kvp.Key] = lifetimeCopy;
                        }
                    }

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
                            // Include multilingual cached data
                            MultilingualLifetime = multilingualLifetimeWithPhase
                        }
                    };
                }
                
                // Log reason for regeneration
                if (!profileNotChanged)
                {
                    _logger.LogInformation("[FortunePredictionGAgent][GetOrGeneratePredictionAsync] Profile updated, regenerating lifetime prediction for {UserId}",
                        userInfo.UserId);
                }
            }
            else if (type == PredictionType.Yearly)
            {
                // Yearly: expires after 1 year OR if profile changes
                var hasYearly = !State.YearlyForecast.IsNullOrEmpty();
                var yearlyNotExpired = State.YearlyGeneratedDate.HasValue && 
                                      State.YearlyGeneratedDate.Value.Year == currentYear;
                
                if (hasYearly && yearlyNotExpired && profileNotChanged)
                {
                    _logger.LogInformation("[FortunePredictionGAgent][GetOrGeneratePredictionAsync] Returning cached yearly prediction (multilingual) for {UserId}",
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
                            Results = new Dictionary<string, Dictionary<string, string>>(), // Yearly doesn't have daily results
                            CreatedAt = State.CreatedAt,
                            FromCache = true,
                            LifetimeForecast = State.YearlyForecast, // Return yearly in LifetimeForecast field for API compatibility
                            MultilingualLifetime = State.MultilingualYearly // Return yearly multilingual
                        }
                    };
                }
                
                // Log reason for regeneration
                if (!yearlyNotExpired)
                {
                    _logger.LogInformation("[FortunePredictionGAgent][GetOrGeneratePredictionAsync] Yearly expired, regenerating for {UserId}",
                        userInfo.UserId);
                }
                if (!profileNotChanged)
                {
                    _logger.LogInformation("[FortunePredictionGAgent][GetOrGeneratePredictionAsync] Profile updated, regenerating yearly prediction for {UserId}",
                        userInfo.UserId);
                }
            }
            else // PredictionType.Daily
            {
                // Daily: expires every day OR if profile changes
                if (State.PredictionId != Guid.Empty && State.PredictionDate == today && profileNotChanged)
                {
                    _logger.LogInformation("[FortunePredictionGAgent][GetOrGeneratePredictionAsync] Returning cached prediction (with multilingual) for {UserId}",
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
                            FromCache = true,
                            // Include multilingual cached data
                            MultilingualResults = State.MultilingualResults
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
            _logger.LogInformation("[FortunePredictionGAgent][GetOrGeneratePredictionAsync] Generating new prediction for {UserId}, Type: {Type}",
                userInfo.UserId, type);

            var predictionResult = await GeneratePredictionAsync(userInfo, today, type);
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
            // Include multilingual cached data
            MultilingualResults = State.MultilingualResults,
            MultilingualLifetime = State.MultilingualLifetime
        });
    }

    /// <summary>
    /// Generate new prediction using AI
    /// </summary>
    private async Task<GetTodayPredictionResult> GeneratePredictionAsync(FortuneUserDto userInfo, DateOnly predictionDate, PredictionType type)
    {
        try
        {
            // Build prompt
            var prompt = BuildPredictionPrompt(userInfo, predictionDate, type);

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
            Dictionary<string, string>? lifetimeForecast = new Dictionary<string, string>();
            Dictionary<string, string>? yearlyForecast = new Dictionary<string, string>();
            
            // Multilingual caches
            Dictionary<string, Dictionary<string, Dictionary<string, string>>>? multilingualResults = null;
            Dictionary<string, Dictionary<string, string>>? multilingualLifetime = null;
            Dictionary<string, Dictionary<string, string>>? multilingualYearly = null;

            // Parse AI response based on type
            if (type == PredictionType.Lifetime)
            {
                // Parse Lifetime (multilingual)
                var (parsedLifetime, mlLifetime) = ParseMultilingualLifetimeResponse(aiResponse);
                if (parsedLifetime == null)
                {
                    _logger.LogError("[FortunePredictionGAgent][GeneratePredictionAsync] Failed to parse Lifetime response");
                    return new GetTodayPredictionResult
                    {
                        Success = false,
                        Message = "Failed to parse AI response"
                    };
                }
                
                lifetimeForecast = parsedLifetime;
                multilingualLifetime = mlLifetime;
                parsedResults = new Dictionary<string, Dictionary<string, string>>(); // Empty for lifetime mode
            }
            else if (type == PredictionType.Yearly)
            {
                // Parse Yearly (multilingual) - reuse lifetime parser as structure is similar
                var (parsedYearly, mlYearly) = ParseMultilingualLifetimeResponse(aiResponse);
                if (parsedYearly == null)
                {
                    _logger.LogError("[FortunePredictionGAgent][GeneratePredictionAsync] Failed to parse Yearly response");
                    return new GetTodayPredictionResult
                    {
                        Success = false,
                        Message = "Failed to parse AI response"
                    };
                }
                
                yearlyForecast = parsedYearly;
                multilingualYearly = mlYearly;
                parsedResults = new Dictionary<string, Dictionary<string, string>>(); // Empty for yearly mode
            }
            else // PredictionType.Daily
            {
                // Parse Daily (6 dimensions, multilingual)
                (parsedResults, multilingualResults) = ParseMultilingualDailyResponse(aiResponse);
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

            // Raise event to save prediction (with multilingual support)
            RaiseEvent(new LumenPredictionGeneratedEvent
            {
                PredictionId = predictionId,
                UserId = userInfo.UserId,
                PredictionDate = predictionDate,
                Results = parsedResults,
                CreatedAt = now,
                LifetimeForecast = lifetimeForecast,
                ProfileUpdatedAt = userInfo.UpdatedAt,
                // Multilingual data
                MultilingualResults = multilingualResults,
                MultilingualLifetime = multilingualLifetime,
                // Yearly data
                YearlyForecast = yearlyForecast,
                YearlyGeneratedDate = type == PredictionType.Yearly ? now : State.YearlyGeneratedDate,
                MultilingualYearly = multilingualYearly
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[FortunePredictionGAgent][GeneratePredictionAsync] Prediction generated successfully (multilingual) for user {UserId}",
                userInfo.UserId);

            // Add currentPhase to all language versions if lifetime was generated
            if (type == PredictionType.Lifetime && multilingualLifetime != null)
            {
                var currentPhase = CalculateCurrentPhase(userInfo.BirthDate);
                foreach (var lang in multilingualLifetime.Keys)
                {
                    multilingualLifetime[lang]["currentPhase"] = currentPhase.ToString();
                }
                // Also add to default version
                if (lifetimeForecast != null && !lifetimeForecast.IsNullOrEmpty())
                {
                lifetimeForecast["currentPhase"] = currentPhase.ToString();
                }
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
                    // For yearly, return in LifetimeForecast field for API compatibility
                    LifetimeForecast = type == PredictionType.Yearly ? yearlyForecast : lifetimeForecast,
                    // Multilingual data
                    MultilingualResults = multilingualResults,
                    MultilingualLifetime = type == PredictionType.Yearly ? multilingualYearly : multilingualLifetime
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
    private string BuildPredictionPrompt(FortuneUserDto userInfo, DateOnly predictionDate, PredictionType type)
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
        
        // Multilingual instruction prefix
        var multilingualPrefix = @"IMPORTANT: Generate prediction in 4 languages (English, Traditional Chinese, Simplified Chinese, Spanish).
All text fields must be naturally translated (not word-by-word), keeping the same meaning and tone.
Wrap your response in a 'predictions' object with language codes: 'en', 'zh-tw', 'zh', 'es'.

";
        
        if (type == PredictionType.Lifetime)
        {
            prompt = multilingualPrefix + $@"Generate lifetime fortune prediction based on user's birth info.
User: {userInfoLine}
Date: {predictionDate:yyyy-MM-dd}

REQUIRED FORMAT:
{{
  ""predictions"": {{
    ""en"": {{
  ""lifetime"": {{
        ""title"": ""You are [archetype]"",
        ""description"": ""[30-50 words about life purpose and goals]"",
    ""traits"": {{
      ""fateRarity"": {{""percentage"": [0.1-10.0], ""description"": ""[6-10 words]""}},
      ""mainElements"": ""[1-2 bilingual elements]"",
      ""lifePath"": ""[2-4 roles]""
    }},
    ""phases"": {{
      ""phase1"": {{""description"": ""[15-30 words for 0-20yrs]""}},
      ""phase2"": {{""description"": ""[15-30 words for 21-35yrs]""}},
      ""phase3"": {{""description"": ""[15-30 words for 36+yrs]""}}
        }}
    }}
  }},
    ""zh-tw"": {{
      ""lifetime"": {{ ...Traditional Chinese version... }}
    }},
    ""zh"": {{
      ""lifetime"": {{ ...Simplified Chinese version... }}
    }},
    ""es"": {{
      ""lifetime"": {{ ...Spanish version... }}
    }}
  }}
}}

RULES:
- title: MUST follow format ""You are [Archetype]"" where Archetype is a broad life role (e.g. Leader, Explorer, Creator, Healer, Builder, Visionary, Guardian, Catalyst)
- description: 30-50 words focusing on LIFE PURPOSE and GOALS - describe what this person is meant to achieve in life, their mission, and the impact they're destined to create
- fateRarity.percentage: 0.1-10.0 (smaller=rarer), description: 6-10 words
- mainElements: 1-2 bilingual elements from ""Fire (火)"", ""Metal (金)"", ""Water (水)"", ""Wood (木)"", ""Earth (土)""
- lifePath: 2-4 roles comma-separated
- phases: 15-30 words each (phase1: 0-20yrs, phase2: 21-35yrs, phase3: 36+yrs)

EXAMPLE (format reference):
{{
  ""lifetime"": {{
    ""title"": ""You are a Visionary"",
    ""description"": ""Your life purpose is to inspire innovation and lead transformative change. Your goals center on creating lasting impact through bold ideas, empowering others to see new possibilities, and building bridges between tradition and progress."",
    ""traits"": {{
      ""fateRarity"": {{""percentage"": 2.3, ""description"": ""[6-10 words]""}},
      ""mainElements"": ""Fire (火), Metal (金)"",
      ""lifePath"": ""Innovator, Mentor, Visionary""
    }},
    ""phases"": {{
      ""phase1"": {{""description"": ""[15-30 words for 0-20yrs]""}},
      ""phase2"": {{""description"": ""[15-30 words for 21-35yrs]""}},
      ""phase3"": {{""description"": ""[15-30 words for 36+yrs]""}}
    }}
  }}
}}";
        }
        else if (type == PredictionType.Yearly)
        {
            prompt = multilingualPrefix + $@"Generate yearly fortune prediction for {predictionDate.Year} based on user's birth info.
User: {userInfoLine}
Year: {predictionDate.Year}

REQUIRED FORMAT (FLATTENED - use underscore for nested keys):
{{
  ""predictions"": {{
    ""en"": {{
      ""zodiacInfluence"": ""[User's zodiac] native in a [Year's zodiac] year → [Taishui relationship]"",
      ""westernAstroOverlay"": ""[Sun sign] Sun · [Year role/archetype] — {predictionDate.Year}\n[Key planetary positions]"",
      
      ""yearlyTheme_overallTheme"": ""[3-5 words theme title]"",
      ""yearlyTheme_atAGlance"": ""[20-30 words: one sentence summary]"",
      ""yearlyTheme_expanded"": ""[60-100 words: 3 paragraphs separated by \n\n explaining astrological combination, energy summary, and year characterization]"",
      
      ""divineInfluence_career_score"": [1-4],
      ""divineInfluence_career_tagline"": ""[10-15 words: warm, relatable tagline for career this year]"",
      ""divineInfluence_career_bestMoves"": [""[15-25 words]"", ""[15-25 words]""],
      ""divineInfluence_career_avoid"": [""[10-20 words]"", ""[10-20 words]""],
      ""divineInfluence_career_inANutshell"": ""[40-60 words: 3 paragraphs separated by \n\n with astrological formula, impact description, and practical insight]"",
      
      ""divineInfluence_love_score"": [1-4],
      ""divineInfluence_love_tagline"": ""[10-15 words]"",
      ""divineInfluence_love_bestMoves"": [""[15-25 words]"", ""[15-25 words]""],
      ""divineInfluence_love_avoid"": [""[10-20 words]"", ""[10-20 words]""],
      ""divineInfluence_love_inANutshell"": ""[40-60 words: 3 paragraphs separated by \n\n]"",
      
      ""divineInfluence_wealth_score"": [1-4],
      ""divineInfluence_wealth_tagline"": ""[10-15 words]"",
      ""divineInfluence_wealth_bestMoves"": [""[15-25 words]"", ""[15-25 words]""],
      ""divineInfluence_wealth_avoid"": [""[10-20 words]"", ""[10-20 words]""],
      ""divineInfluence_wealth_inANutshell"": ""[40-60 words: 3 paragraphs separated by \n\n]"",
      
      ""divineInfluence_health_score"": [1-4],
      ""divineInfluence_health_tagline"": ""[10-15 words]"",
      ""divineInfluence_health_bestMoves"": [""[15-25 words]"", ""[15-25 words]""],
      ""divineInfluence_health_avoid"": [""[10-20 words]"", ""[10-20 words]""],
      ""divineInfluence_health_inANutshell"": ""[40-60 words: 3 paragraphs separated by \n\n]"",
      
      ""embodimentMantra"": ""[20-40 words: poetic, powerful mantra for the year]""
    }},
    ""zh-tw"": {{...same flattened structure, Traditional Chinese}},
    ""zh"": {{...same flattened structure, Simplified Chinese}},
    ""es"": {{...same flattened structure, Spanish}}
  }}
}}

STRUCTURE RULES:
1. zodiacInfluence: Combine Chinese zodiac (user's birth year animal + current year animal) + Taishui relationship (刑太岁/冲太岁/合太岁/害太岁 etc.)
2. westernAstroOverlay: User's sun sign + archetypal role for the year + major planetary transits relevant to {predictionDate.Year}
3. yearlyTheme.overallTheme: A thematic title (3-5 words) capturing the year's essence
4. yearlyTheme.atAGlance: One clear sentence summarizing what this year brings
5. divineInfluence: Score each dimension 1-4 (1=lowest, 4=highest energy/opportunity)
   - bestMoves: 2 specific, actionable strategies
   - avoid: 2 clear warnings about what to steer clear of
6. embodimentMantra: A poetic, memorable statement to carry through the year

TRANSLATION PATTERNS:
- zodiacInfluence: Keep zodiac animals in English or use local names (蛇年/Año de la Serpiente)
- Taishui relationships: Use Chinese terms with translations where appropriate
- embodimentMantra: Translate poetically, maintaining rhythm and power

TONE & STYLE:
- Use second person (You/Your) extensively to create direct, personal connection
- Write as if speaking directly to the user, making them feel seen and understood
- Maintain warm, conversational tone—like a trusted guide, not a distant oracle
- Balance mystical wisdom with approachable, actionable language
- Scores should reflect realistic assessment, not overly optimistic

EXAMPLE (for reference):
{{
  ""predictions"": {{
    ""en"": {{
      ""zodiacInfluence"": ""Earth Snake native in a Wood Snake year → Self-Punishment Taishui (刑太岁)"",
      ""westernAstroOverlay"": ""Pisces Sun · Intuitive Creator — 2025\nSaturn & Neptune in Pisces"",
      ""yearlyTheme"": {{
        ""overallTheme"": ""Recalibration of Self, Speech & System"",
        ""atAGlance"": ""Both Eastern and Western systems agree: 2025 is a year of internal pressure, clarity, and purification.""
      }},
      ""divineInfluence"": {{
        ""career"": {{
          ""score"": 3,
          ""bestMoves"": [
            ""Rebrand or restructure your personal/professional platform—you're ready for a new chapter that reflects your evolved values."",
            ""Start building passive or IP-based income: course, book, tool, system—your expertise is worth packaging.""
          ],
          ""avoid"": [
            ""Taking shortcuts to 'force results'—sustainable growth takes patient strategy this year."",
            ""Team dramas or unclear partnerships—clarify roles and boundaries early.""
          ]
        }},
        ""love"": {{
          ""score"": 2,
          ""bestMoves"": [
            ""Communicate your needs with radical honesty—vulnerability deepens real connection this year."",
            ""Invest in quality time over grand gestures—presence matters more than performance.""
          ],
          ""avoid"": [
            ""Avoiding difficult conversations—unspoken tension will only grow."",
            ""Forcing commitment or clarity before it's naturally ready—trust timing.""
          ]
        }},
        ""wealth"": {{
          ""score"": 3,
          ""bestMoves"": [
            ""Focus on long-term wealth building: retirement, property, or passive income streams aligned with your purpose."",
            ""Audit your financial systems—automate, consolidate, and optimize what you already have.""
          ],
          ""avoid"": [
            ""High-risk speculation or 'get rich quick' schemes—2025 rewards slow, steady growth."",
            ""Overspending to compensate for emotional voids—money can't buy inner peace this year.""
          ]
        }},
        ""health"": {{
          ""score"": 2,
          ""bestMoves"": [
            ""Prioritize nervous system regulation: meditation, breathwork, or somatic therapy—your body holds stress you don't realize."",
            ""Build consistent, gentle routines: sleep hygiene, hydration, movement—small habits compound powerfully this year.""
          ],
          ""avoid"": [
            ""Ignoring emotional red flags or pushing through burnout—rest is productive this year."",
            ""Over-reliance on stimulants (caffeine, sugar) to mask exhaustion—address the root cause.""
          ]
        }}
      }},
      ""embodimentMantra"": ""My words build worlds. My silence tunes my frequency. My path is not rushed—it is authored.""
    }}
  }}
}}";
        }
        else // PredictionType.Daily
        {
            prompt = multilingualPrefix + $@"⚠️ CRITICAL: All word count requirements are MINIMUMS. ""10-15 words"" means AT LEAST 10 words.

Generate daily fortune prediction for {predictionDate:yyyy-MM-dd} based on user's profile.
User: {userInfoLine}

OUTPUT FORMAT (FLATTENED - use underscore for nested keys):
{{
  ""predictions"": {{
    ""en"": {{
      ""dayTitle"": ""The Day of [word1] and [word2]"",
      
      ""todaysReading_tarotCard_name"": ""[Tarot card name]"",
      ""todaysReading_tarotCard_represents"": ""[1-3 words: what card represents]"",
      ""todaysReading_pathTitle"": ""{{firstName}}'s Path Today - A [Adjective] Path"",
      ""todaysReading_pathDescription"": ""[30-50 words: warm greeting + day's theme - MINIMUM 30 WORDS]"",
      ""todaysReading_pathDescriptionExpanded"": ""[30-50 words: deeper insight continuing from pathDescription - MINIMUM 30 WORDS]"",
      ""todaysReading_careerAndWork"": ""[20-30 words: specific actionable advice - MINIMUM 20 WORDS]"",
      ""todaysReading_loveAndRelationships"": ""[20-30 words: specific actionable advice - MINIMUM 20 WORDS]"",
      ""todaysReading_wealthAndFinance"": ""[20-30 words: specific actionable advice - MINIMUM 20 WORDS]"",
      ""todaysReading_healthAndWellness"": ""[20-30 words: specific actionable advice - MINIMUM 20 WORDS]"",
      
      ""todaysTakeaway"": ""[One powerful sentence with {{firstName}}]"",
      
      ""luckyAlignments_luckyNumber_number"": ""[Word form like Seven]"",
      ""luckyAlignments_luckyNumber_digit"": ""[Digit form like 7]"",
      ""luckyAlignments_luckyNumber_description"": ""[20-30 words: Today carries Number X's energy and its qualities]"",
      ""luckyAlignments_luckyNumber_calculation"": ""How is it calculated?\n\nNumerical Energy of the Day ({predictionDate:M-d-yyyy}): [Show formula like 2 + 0 + 2 + 5 + 1 + 1 + 0 + 5 = 16 → 1 + 6 = 7]"",
      
      ""luckyAlignments_luckyStone"": ""[Single word stone name]"",
      ""luckyAlignments_luckyStone_description"": ""[20-30 words: how to use the stone and its effect]"",
      ""luckyAlignments_luckyStone_guidance"": ""Crystal Guidance\n\n[20-30 words: specific ritual or meditation instructions]"",
      
      ""luckyAlignments_luckySpell"": ""[2-4 words: the spell phrase itself]"",
      ""luckyAlignments_luckySpell_description"": ""[10-15 words: Tell yourself + the spell in quotes]"",
      ""luckyAlignments_luckySpell_intent"": ""Spell Intent\n\n[15-25 words: purpose and effect of the spell]"",
      
      ""twistOfFate_favorable"": [""[10-15 words - MUST be at least 10 words]"", ""[10-15 words - MUST be at least 10 words]""],
      ""twistOfFate_avoid"": [""[10-15 words - MUST be at least 10 words]"", ""[10-15 words - MUST be at least 10 words]""],
      ""twistOfFate_todaysRecommendation"": ""[One sentence: day's turning point]""
    }},
    ""zh-tw"": {{...same flattened structure, Traditional Chinese}},
    ""zh"": {{...same flattened structure, Simplified Chinese}},
    ""es"": {{...same flattened structure, Spanish}}
  }}
}}

TRANSLATION PATTERNS:
- dayTitle: ""The Day of X and Y"" → ""[X]與[Y]之日"" (zh-tw) / ""[X]与[Y]之日"" (zh) / ""El Día de X y Y"" (es)
- pathTitle: ""{{firstName}}'s Path Today - A X Path"" → ""{{firstName}}今日之路 - [X]之路"" (zh-tw/zh) / ""El Camino de {{firstName}} Hoy - Un Camino X"" (es)
- calculation: Keep formula in numbers, but translate text labels
- guidance: Translate ritual instructions naturally

TONE & STYLE:
- Use second person (You/Your) extensively to create direct, personal connection
- Write as if speaking directly to the user, making them feel seen and understood
- Maintain warm, conversational tone—like a trusted guide, not a distant oracle
- Balance mystical wisdom with approachable language

EXAMPLE (Collapsed view - frontend shows these by default):
{{
  ""predictions"": {{
    ""en"": {{
      ""dayTitle"": ""The Day of Reflection and Strength"",
      ""todaysReading_tarotCard_name"": ""The Empress"",
      ""todaysReading_tarotCard_represents"": ""Abundance"",
      ""todaysReading_pathTitle"": ""Sean's Path Today - A Difficult Path"",
      ""todaysReading_pathDescription"": ""Hi Sean, today brings you challenges but also growth. The road ahead may feel blocked to you, but remember—obstacles forge your character and reveal your inner strength."",
      ""todaysReading_pathDescriptionExpanded"": ""Yet obstacles are not the end—they're invitations for you to refine your approach, deepen your resolve, and trust your process. What feels hard now is shaping your future mastery."",
      ""todaysReading_careerAndWork"": ""You'll find strategic planning serves you better than pushing hard today. Patient preparation is your ally, not aggressive action. Focus on building your foundation."",
      ""todaysReading_loveAndRelationships"": ""Misunderstandings may arise around you—stay calm and communicate gently. Your ability to listen deeply will clear what force cannot. Patience is your superpower today."",
      ""todaysReading_wealthAndFinance"": ""Exercise caution with your investments today. Prioritize stability in your finances over expansion. Review your financial structure and consolidate what you have."",
      ""todaysReading_healthAndWellness"": ""Your strength today lies in stillness, not intensity. Gentle movement, rest, and turning your focus inward will restore your energy more than pushing your limits."",
      ""todaysTakeaway"": ""Sean, your power today is not in force, but in your stillness—ground yourself and clarity will follow."",
      ""luckyAlignments_luckyNumber_number"": ""Seven"",
      ""luckyAlignments_luckyNumber_digit"": ""7"",
      ""luckyAlignments_luckyNumber_description"": ""Today carries the energy of Number 7, introspection, wisdom, mystery, and spiritual elevation."",
      ""luckyAlignments_luckyNumber_calculation"": ""How is it calculated?\n\nNumerical Energy of the Day (5-11-2025): 2 + 0 + 2 + 5 + 1 + 1 + 0 + 5 = 16 → 1 + 6 = 7"",
      ""luckyAlignments_luckyStone"": ""Amethyst"",
      ""luckyAlignments_luckyStone_description"": ""Hold or wear amethyst as you meditate, it awakens your inner clarity and aligns you with wisdom's frequency."",
      ""luckyAlignments_luckyStone_guidance"": ""Crystal Guidance\n\nMeditate: Light a stick of lavender incense, hold the crystal, and take three deep breaths."",
      ""luckyAlignments_luckySpell"": ""The Still"",
      ""luckyAlignments_luckySpell_description"": ""Tell yourself 'When thoughts drift like clouds, I return to stillness.'"",
      ""luckyAlignments_luckySpell_intent"": ""Spell Intent\n\nTo clear the mind, restore focus, and awaken inner guidance."",
      ""twistOfFate_favorable"": [
        ""Engage deeply with those you trust and value their counsel—they see what you need."",
        ""Listen to your capable advisors who can see what you might miss in your own path.""
      ],
      ""twistOfFate_avoid"": [
        ""Acting alone or being overly headstrong without seeking consultation—you need perspective today."",
        ""Forcing your progress or confronting others aggressively—patience will serve you better.""
      ],
      ""twistOfFate_todaysRecommendation"": ""Your turning point today lies in the connections you make, the collaboration you embrace, and the wisdom you exchange with your trusted allies.""
    }}
  }}
}}";            
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
    
    /// <summary>
    /// Parse multilingual daily response from AI
    /// Returns (default English results, multilingual dictionary)
    /// </summary>
    private (Dictionary<string, Dictionary<string, string>>?, Dictionary<string, Dictionary<string, Dictionary<string, string>>>?) ParseMultilingualDailyResponse(string aiResponse)
    {
        try
        {
            // Extract JSON - try multiple strategies
            string jsonContent = aiResponse;
            
            // Strategy 1: Extract from markdown code blocks
            var codeBlockMatch = System.Text.RegularExpressions.Regex.Match(aiResponse, @"```(?:json)?\s*([\s\S]*?)\s*```");
            if (codeBlockMatch.Success)
            {
                jsonContent = codeBlockMatch.Groups[1].Value.Trim();
            }
            
            // Strategy 2: Find complete JSON object (from first { to last })
            var firstBrace = jsonContent.IndexOf('{');
            var lastBrace = jsonContent.LastIndexOf('}');
            
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                jsonContent = jsonContent.Substring(firstBrace, lastBrace - firstBrace + 1);
            }
            
            // Clean up any trailing characters
            jsonContent = jsonContent.Trim();
            
            _logger.LogDebug("[FortunePredictionGAgent][ParseMultilingualDailyResponse] Extracted JSON length: {Length}", jsonContent.Length);

            var fullResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
            if (fullResponse == null)
            {
                _logger.LogWarning("[FortunePredictionGAgent][ParseMultilingualDailyResponse] Failed to deserialize response");
                return (null, null);
            }

            // Check if response has predictions wrapper
            if (!fullResponse.ContainsKey("predictions"))
            {
                // Fallback to old format (single language)
                _logger.LogWarning("[FortunePredictionGAgent][ParseMultilingualDailyResponse] No predictions wrapper found, using old format");
                var singleLangResults = ParseDailyResponse(aiResponse);
                return (singleLangResults, null);
            }

            var predictionsJson = JsonConvert.SerializeObject(fullResponse["predictions"]);
            var predictions = JsonConvert.DeserializeObject<Dictionary<string, object>>(predictionsJson);
            
            if (predictions == null || predictions.Count == 0)
            {
                _logger.LogWarning("[FortunePredictionGAgent][ParseMultilingualDailyResponse] No predictions found");
                return (null, null);
            }

            // Convert each language's nested structure to flattened Dictionary<string, string>
            var multilingualResults = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
            
            foreach (var langKvp in predictions)
            {
                var lang = langKvp.Key;
                var langDataJson = JsonConvert.SerializeObject(langKvp.Value);
                var langDataFlat = FlattenNestedJson(langDataJson);
                
                if (langDataFlat != null && langDataFlat.Count > 0)
                {
                    multilingualResults[lang] = langDataFlat;
                }
            }

            // Extract English version as default
            Dictionary<string, Dictionary<string, string>>? defaultResults = null;
            if (multilingualResults.ContainsKey("en"))
            {
                defaultResults = multilingualResults["en"];
            }
            else if (multilingualResults.Count > 0)
            {
                // Use first available language as fallback
                defaultResults = multilingualResults.Values.FirstOrDefault();
            }

            return (defaultResults, multilingualResults);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortunePredictionGAgent][ParseMultilingualDailyResponse] Failed to parse multilingual response");
            // Fallback to old format
            return (ParseDailyResponse(aiResponse), null);
        }
    }
    
    /// <summary>
    /// Parse multilingual lifetime response from AI
    /// Returns (default lifetime, multilingual lifetime)
    /// </summary>
    private (Dictionary<string, string>?, Dictionary<string, Dictionary<string, string>>?) ParseMultilingualLifetimeResponse(string aiResponse)
    {
        try
        {
            // Extract JSON - try multiple strategies
            string jsonContent = aiResponse;
            
            // Strategy 1: Extract from markdown code blocks
            var codeBlockMatch = System.Text.RegularExpressions.Regex.Match(aiResponse, @"```(?:json)?\s*([\s\S]*?)\s*```");
            if (codeBlockMatch.Success)
            {
                jsonContent = codeBlockMatch.Groups[1].Value.Trim();
            }
            
            // Strategy 2: Find complete JSON object (from first { to last })
            var firstBrace = jsonContent.IndexOf('{');
            var lastBrace = jsonContent.LastIndexOf('}');
            
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                jsonContent = jsonContent.Substring(firstBrace, lastBrace - firstBrace + 1);
            }
            
            // Clean up any trailing characters
            jsonContent = jsonContent.Trim();
            
            _logger.LogDebug("[FortunePredictionGAgent][ParseMultilingualLifetimeResponse] Extracted JSON length: {Length}", jsonContent.Length);

            var fullResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
            if (fullResponse == null)
            {
                _logger.LogWarning("[FortunePredictionGAgent][ParseMultilingualLifetimeResponse] Failed to deserialize response");
                return (null, null);
            }

            // Check if response has predictions wrapper
            if (!fullResponse.ContainsKey("predictions"))
            {
                // Fallback to old format (single language)
                _logger.LogWarning("[FortunePredictionGAgent][ParseMultilingualLifetimeResponse] No predictions wrapper found, using old format");
                var (lifetime, _) = ParseLifetimeWeeklyResponse(aiResponse);
                return (lifetime, null);
            }

            var predictionsJson = JsonConvert.SerializeObject(fullResponse["predictions"]);
            var predictions = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, object>>>(predictionsJson);
            
            if (predictions == null || predictions.Count == 0)
            {
                _logger.LogWarning("[FortunePredictionGAgent][ParseMultilingualLifetimeResponse] No predictions found");
                return (null, null);
            }

            // Extract multilingual lifetime (for lifetime predictions) or yearly (for yearly predictions)
            var multilingualLifetime = new Dictionary<string, Dictionary<string, string>>();
            
            foreach (var langKvp in predictions)
            {
                var lang = langKvp.Key;
                var langData = langKvp.Value;
                
                // Check for lifetime or yearly data (yearly uses the whole structure, lifetime uses nested "lifetime" key)
                object targetData = null;
                if (langData.ContainsKey("lifetime"))
                {
                    targetData = langData["lifetime"];
                }
                else
                {
                    // For yearly, the whole langData is the prediction structure
                    targetData = langData;
                }
                
                if (targetData != null)
                {
                    var dataJson = JsonConvert.SerializeObject(targetData);
                    
                    // Use the same flattening logic as daily predictions
                    var flattened = new Dictionary<string, string>();
                    var parsedData = JsonConvert.DeserializeObject<Dictionary<string, object>>(dataJson);
                    
                    if (parsedData != null)
                    {
                        foreach (var kvp in parsedData)
                        {
                            FlattenObject(kvp.Value, kvp.Key, flattened);
                        }
                        
                        multilingualLifetime[lang] = flattened;
                    }
                }
            }

            // Extract English version as default
            Dictionary<string, string>? defaultLifetime = null;
            
            if (multilingualLifetime.ContainsKey("en"))
            {
                defaultLifetime = multilingualLifetime["en"];
            }
            else if (multilingualLifetime.Count > 0)
            {
                defaultLifetime = multilingualLifetime.Values.FirstOrDefault();
            }

            return (defaultLifetime, multilingualLifetime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortunePredictionGAgent][ParseMultilingualLifetimeResponse] Failed to parse multilingual response");
            // Fallback to old format
            var (lifetime, _) = ParseLifetimeWeeklyResponse(aiResponse);
            return (lifetime, null);
        }
    }
    
    /// <summary>
    /// Helper method to flatten nested dictionary structure
    /// </summary>
    private Dictionary<string, string> FlattenDictionary(Dictionary<string, string> source)
    {
        // This is a simplified version - you may need to enhance based on actual structure
        // For now, it just returns as-is since the actual parsing would convert nested objects to JSON strings
        return source;
    }
    
    /// <summary>
    /// Flatten nested JSON into Dictionary<section, Dictionary<field, value>>
    /// Uses underscore to join nested keys (e.g., "tarotCard.name" → "tarotCard_name")
    /// This allows frontend to access nested fields without JSON.parse()
    /// </summary>
    private Dictionary<string, Dictionary<string, string>>? FlattenNestedJson(string json)
    {
        try
        {
            var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            if (data == null) return null;
            
            var result = new Dictionary<string, Dictionary<string, string>>();
            
            foreach (var kvp in data)
            {
                var sectionName = kvp.Key;
                var sectionData = new Dictionary<string, string>();
                
                // Recursively flatten the section data
                FlattenObject(kvp.Value, "", sectionData);
                
                result[sectionName] = sectionData;
            }
            
            _logger.LogDebug("[FortunePredictionGAgent][FlattenNestedJson] Flattened {Count} sections", result.Count);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortunePredictionGAgent][FlattenNestedJson] Failed to flatten JSON");
            return null;
        }
    }
    
    /// <summary>
    /// Recursively flatten an object into a flat dictionary with underscore-separated keys
    /// </summary>
    private void FlattenObject(object obj, string prefix, Dictionary<string, string> result)
    {
        if (obj == null)
        {
            if (!string.IsNullOrEmpty(prefix))
            {
                result[prefix] = "";
            }
            return;
        }
        
        // Handle different value types
        switch (obj)
        {
            case string strValue:
                // Simple string - store directly
                result[prefix] = strValue;
                break;
                
            case Newtonsoft.Json.Linq.JObject jObject:
                // Nested object - recurse into it
                foreach (var property in jObject.Properties())
                {
                    var newKey = string.IsNullOrEmpty(prefix) 
                        ? property.Name 
                        : $"{prefix}_{property.Name}";
                    FlattenObject(property.Value, newKey, result);
                }
                break;
                
            case Newtonsoft.Json.Linq.JArray jArray:
                // Array - store as JSON string for now (could expand to array_0, array_1, etc.)
                result[prefix] = jArray.ToString(Newtonsoft.Json.Formatting.None);
                break;
                
            case Newtonsoft.Json.Linq.JValue jValue:
                // Primitive value (number, boolean, etc.)
                result[prefix] = jValue.ToString();
                break;
                
            default:
                // For other types, try to serialize as JSON and recurse
                try
                {
                    var json = JsonConvert.SerializeObject(obj);
                    var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    
                    if (parsed != null)
                    {
                        foreach (var kvp in parsed)
                        {
                            var newKey = string.IsNullOrEmpty(prefix) 
                                ? kvp.Key 
                                : $"{prefix}_{kvp.Key}";
                            FlattenObject(kvp.Value, newKey, result);
                        }
                    }
                    else
                    {
                        // Can't parse - store as JSON string
                        result[prefix] = json;
                    }
                }
                catch
                {
                    // Last resort - convert to string
                    result[prefix] = obj.ToString() ?? "";
                }
                break;
        }
    }
}

