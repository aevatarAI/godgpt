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
            prompt = multilingualPrefix + $@"Generate lifetime profile for user.
User: {userInfoLine}

FORMAT (flattened, no nesting):
{{
  ""predictions"": {{
    ""en"": {{
      ""welcomeNote_zodiac"": ""[zodiac]"", ""welcomeNote_chineseZodiac"": ""[Element Animal]"", ""welcomeNote_rhythm"": ""[Yin/Yang Element]"", ""welcomeNote_essence"": ""[adj] and [adj]"",
      ""fourPillars_coreIdentity"": ""[30-50w]"", ""fourPillars_coreIdentity_expanded"": ""[60-100w]"",
      ""chineseAstrology_currentYear"": ""Year of the [Element Animal]"", ""chineseAstrology_currentYearStems"": ""[干支 (Pinyin)]"",
      ""chineseAstrology_trait1"": ""[5-15w]"", ""chineseAstrology_trait2"": ""[5-15w]"", ""chineseAstrology_trait3"": ""[5-15w]"", ""chineseAstrology_trait4"": ""[5-15w]"",
      ""zodiacWhisper"": ""[40-60w]"",
      ""sunSign_name"": ""[sign]"", ""sunSign_tagline"": ""You [2-5w]"",
      ""westernOverview_sunSign"": ""[sign]"", ""westernOverview_sunArchetype"": ""The [3-5w]"", ""westernOverview_sunDescription"": ""[20-40w]"",
      ""westernOverview_moonSign"": ""[sign]"", ""westernOverview_moonArchetype"": ""The [3-5w]"", ""westernOverview_moonDescription"": ""[20-40w]"",
      ""westernOverview_risingSign"": ""[sign]"", ""westernOverview_risingArchetype"": ""The [3-5w]"", ""westernOverview_risingDescription"": ""[20-40w]"",
      ""combinedEssence"": ""You think like [Sun], feel like [Moon], move through world like [Rising]."",
      ""strengths_overview"": ""[15-25w]"",
      ""strengths_item1_title"": ""[2-5w]"", ""strengths_item1_description"": ""[20-40w]"",
      ""strengths_item2_title"": ""[2-5w]"", ""strengths_item2_description"": ""[20-40w]"",
      ""strengths_item3_title"": ""[2-5w]"", ""strengths_item3_description"": ""[20-40w]"",
      ""challenges_overview"": ""[20-30w]"",
      ""challenges_item1_title"": ""[2-5w]"", ""challenges_item1_description"": ""[15-30w]"",
      ""challenges_item2_title"": ""[2-5w]"", ""challenges_item2_description"": ""[15-30w]"",
      ""challenges_item3_title"": ""[2-5w]"", ""challenges_item3_description"": ""[15-30w]"",
      ""destiny_overview"": ""[30-40w]"",
      ""destiny_path1_title"": ""[roles]"", ""destiny_path1_description"": ""[5-15w]"",
      ""destiny_path2_title"": ""[roles]"", ""destiny_path2_description"": ""[5-15w]"",
      ""destiny_path3_title"": ""[roles]"", ""destiny_path3_description"": ""[5-15w]"",
      ""chineseZodiac_animal"": ""The [Animal]"", ""chineseZodiac_essence"": ""Essence like [element]"",
      ""zodiacCycle_title"": ""Zodiac Cycle Influence (2024-2043)"", ""zodiacCycle_cycleName"": ""[Cycle Name]"", ""zodiacCycle_cycleNameChinese"": ""[中文]"",
      ""zodiacCycle_overview"": ""[60-80w]"",
      ""zodiacCycle_dayMasterPoint1"": ""[10-20w]"", ""zodiacCycle_dayMasterPoint2"": ""[10-20w]"", ""zodiacCycle_dayMasterPoint3"": ""[10-20w]"", ""zodiacCycle_dayMasterPoint4"": ""[10-20w]"",
      ""tenYearCycles_description"": ""[40-60w]"",
      ""pastCycle_ageRange"": ""Age X-Y (YYYY-YYYY)"", ""pastCycle_period"": ""[干支 (Pinyin)] · [Element Animal]"", ""pastCycle_influenceSummary"": ""[10-15w]"", ""pastCycle_meaning"": ""[60-80w]"",
      ""currentCycle_ageRange"": ""Age X-Y (YYYY-YYYY)"", ""currentCycle_period"": ""[干支 (Pinyin)] · [Element Animal]"", ""currentCycle_influenceSummary"": ""[10-15w]"", ""currentCycle_meaning"": ""[60-80w]"",
      ""futureCycle_ageRange"": ""Age X-Y (YYYY-YYYY)"", ""futureCycle_period"": ""[干支 (Pinyin)] · [Element Animal]"", ""futureCycle_influenceSummary"": ""[10-15w]"", ""futureCycle_meaning"": ""[60-80w]"",
      ""lifePlot_title"": ""You are a [10-20w archetype]."", ""lifePlot_chapter"": ""[30-50w]"",
      ""lifePlot_point1"": ""[5-15w]"", ""lifePlot_point2"": ""[5-15w]"", ""lifePlot_point3"": ""[5-15w]"", ""lifePlot_point4"": ""[5-15w]"",
      ""activationSteps_step1_title"": ""[2-5w]"", ""activationSteps_step1_description"": ""[10-20w]"",
      ""activationSteps_step2_title"": ""[2-5w]"", ""activationSteps_step2_description"": ""[10-20w]"",
      ""activationSteps_step3_title"": ""[2-5w]"", ""activationSteps_step3_description"": ""[10-20w]"",
      ""activationSteps_step4_title"": ""[2-5w]"", ""activationSteps_step4_description"": ""[10-20w]"",
      ""mantra_title"": ""[2-4w]"", ""mantra_point1"": ""[5-15w]"", ""mantra_point2"": ""[5-15w]"", ""mantra_point3"": ""[5-15w]""
    }},
    ""zh-tw"": {{...same}}, ""zh"": {{...same}}, ""es"": {{...same}}
  }}
}}

RULES:
- Use You/Your in all descriptions
- No special chars like ✨★※ or emoji in JSON values
- No line breaks in string values (use space instead)
- Warm, conversational tone
- For challenges: frame positively";
        }
        else if (type == PredictionType.Yearly)
        {
            prompt = multilingualPrefix + $@"Generate yearly prediction for {predictionDate.Year}.
User: {userInfoLine}

FORMAT (flattened):
{{
  ""predictions"": {{
    ""en"": {{
      ""zodiacInfluence"": ""[User zodiac] in [Year zodiac] year → [Taishui]"",
      ""westernAstroOverlay"": ""[Sun sign] Sun · [Role] — {predictionDate.Year} [Transits]"",
      ""yearlyTheme_overallTheme"": ""[3-5w]"", ""yearlyTheme_atAGlance"": ""[20-30w]"", ""yearlyTheme_expanded"": ""[60-100w, use space for paragraphs]"",
      ""divineInfluence_career_score"": [1-4], ""divineInfluence_career_tagline"": ""[10-15w]"", ""divineInfluence_career_bestMoves"": [""[15-25w]"", ""[15-25w]""], ""divineInfluence_career_avoid"": [""[10-20w]"", ""[10-20w]""], ""divineInfluence_career_inANutshell"": ""[40-60w]"",
      ""divineInfluence_love_score"": [1-4], ""divineInfluence_love_tagline"": ""[10-15w]"", ""divineInfluence_love_bestMoves"": [""[15-25w]"", ""[15-25w]""], ""divineInfluence_love_avoid"": [""[10-20w]"", ""[10-20w]""], ""divineInfluence_love_inANutshell"": ""[40-60w]"",
      ""divineInfluence_wealth_score"": [1-4], ""divineInfluence_wealth_tagline"": ""[10-15w]"", ""divineInfluence_wealth_bestMoves"": [""[15-25w]"", ""[15-25w]""], ""divineInfluence_wealth_avoid"": [""[10-20w]"", ""[10-20w]""], ""divineInfluence_wealth_inANutshell"": ""[40-60w]"",
      ""divineInfluence_health_score"": [1-4], ""divineInfluence_health_tagline"": ""[10-15w]"", ""divineInfluence_health_bestMoves"": [""[15-25w]"", ""[15-25w]""], ""divineInfluence_health_avoid"": [""[10-20w]"", ""[10-20w]""], ""divineInfluence_health_inANutshell"": ""[40-60w]"",
      ""embodimentMantra"": ""[20-40w]""
    }},
    ""zh-tw"": {{...same}}, ""zh"": {{...same}}, ""es"": {{...same}}
  }}
}}

RULES:
- Use You/Your extensively
- No special chars or emoji in values
- No line breaks in strings (use space)
- Score 1-4 realistically
- Warm, actionable tone";
        }
        else // PredictionType.Daily
        {
            prompt = multilingualPrefix + $@"Generate daily prediction for {predictionDate:yyyy-MM-dd}.
User: {userInfoLine}

FORMAT (flattened):
{{
  ""predictions"": {{
    ""en"": {{
      ""dayTitle"": ""The Day of [w1] and [w2]"",
      ""todaysReading_tarotCard_name"": ""[card]"", ""todaysReading_tarotCard_represents"": ""[1-3w]"", ""todaysReading_pathTitle"": ""{{firstName}}'s Path Today - A [Adj] Path"",
      ""todaysReading_pathDescription"": ""[30-50w MIN 30]"", ""todaysReading_pathDescriptionExpanded"": ""[30-50w MIN 30]"",
      ""todaysReading_careerAndWork"": ""[20-30w MIN 20]"", ""todaysReading_loveAndRelationships"": ""[20-30w MIN 20]"", ""todaysReading_wealthAndFinance"": ""[20-30w MIN 20]"", ""todaysReading_healthAndWellness"": ""[20-30w MIN 20]"",
      ""todaysTakeaway"": ""[One sentence with {{firstName}}]"",
      ""luckyAlignments_luckyNumber_number"": ""[Seven]"", ""luckyAlignments_luckyNumber_digit"": ""[7]"", ""luckyAlignments_luckyNumber_description"": ""[20-30w]"",
      ""luckyAlignments_luckyNumber_calculation"": ""How is it calculated? Numerical Energy of the Day ({predictionDate:M-d-yyyy}): [formula]"",
      ""luckyAlignments_luckyStone"": ""[stone]"", ""luckyAlignments_luckyStone_description"": ""[20-30w]"", ""luckyAlignments_luckyStone_guidance"": ""Crystal Guidance [20-30w]"",
      ""luckyAlignments_luckySpell"": ""[2-4w]"", ""luckyAlignments_luckySpell_description"": ""[10-15w]"", ""luckyAlignments_luckySpell_intent"": ""Spell Intent [15-25w]"",
      ""twistOfFate_favorable"": [""[10-15w MIN 10]"", ""[10-15w MIN 10]""], ""twistOfFate_avoid"": [""[10-15w MIN 10]"", ""[10-15w MIN 10]""], ""twistOfFate_todaysRecommendation"": ""[One sentence]""
    }},
    ""zh-tw"": {{...same}}, ""zh"": {{...same}}, ""es"": {{...same}}
  }}
}}

RULES:
- Use You/Your extensively
- No special chars or emoji in values
- No line breaks in strings (use space instead of \n)
- Warm, conversational tone
- Meet minimum word counts";            
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

