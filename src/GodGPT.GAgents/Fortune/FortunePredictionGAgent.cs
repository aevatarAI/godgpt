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
using System.Diagnostics;

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
                state.Energy = generatedEvent.Energy; // Deprecated field
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
        var totalStopwatch = Stopwatch.StartNew();
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var currentYear = today.Year;
            
            _logger.LogInformation($"[PERF][Fortune] {userInfo.UserId} START - Type: {type}, Date: {today}");
                
                // Check if profile has been updated since prediction was generated
                var profileNotChanged = !State.ProfileUpdatedAt.HasValue || userInfo.UpdatedAt <= State.ProfileUpdatedAt.Value;
                
            // Check if prediction already exists (from cache/state) based on type
            if (type == PredictionType.Lifetime)
            {
                // Lifetime: never expires unless profile changes
                var hasLifetime = !State.LifetimeForecast.IsNullOrEmpty();
                
                if (hasLifetime && profileNotChanged)
                {
                    totalStopwatch.Stop();
                    _logger.LogInformation($"[PERF][Fortune] {userInfo.UserId} Cache_Hit: {totalStopwatch.ElapsedMilliseconds}ms - Type: Lifetime");

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
                    totalStopwatch.Stop();
                    _logger.LogInformation($"[PERF][Fortune] {userInfo.UserId} Cache_Hit: {totalStopwatch.ElapsedMilliseconds}ms - Type: Yearly");

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
                    totalStopwatch.Stop();
                    _logger.LogInformation($"[PERF][Fortune] {userInfo.UserId} Cache_Hit: {totalStopwatch.ElapsedMilliseconds}ms - Type: Daily");

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
            _logger.LogInformation($"[PERF][Fortune] {userInfo.UserId} Cache_Miss - Generating new prediction, Type: {type}");

            var generateStopwatch = Stopwatch.StartNew();
            var predictionResult = await GeneratePredictionAsync(userInfo, today, type);
            generateStopwatch.Stop();
            
            totalStopwatch.Stop();
            
            if (!predictionResult.Success)
            {
                _logger.LogWarning($"[PERF][Fortune] {userInfo.UserId} Generation_Failed: {generateStopwatch.ElapsedMilliseconds}ms, TOTAL: {totalStopwatch.ElapsedMilliseconds}ms");
                return predictionResult;
            }

            _logger.LogInformation($"[PERF][Fortune] {userInfo.UserId} Generation_Success: {generateStopwatch.ElapsedMilliseconds}ms, TOTAL: {totalStopwatch.ElapsedMilliseconds}ms - Type: {type}");
            return predictionResult;
        }
        catch (Exception ex)
        {
            totalStopwatch.Stop();
            _logger.LogError(ex, $"[PERF][Fortune] {userInfo.UserId} Error: {totalStopwatch.ElapsedMilliseconds}ms - Exception in GetOrGeneratePredictionAsync");
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
            var promptStopwatch = Stopwatch.StartNew();
            var prompt = BuildPredictionPrompt(userInfo, predictionDate, type);
            promptStopwatch.Stop();
            _logger.LogInformation($"[PERF][Fortune] {userInfo.UserId} Prompt_Build: {promptStopwatch.ElapsedMilliseconds}ms, Length: {prompt.Length} chars");

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
            var llmStopwatch = Stopwatch.StartNew();
            var response = await godChat.ChatWithoutHistoryAsync(
                userGuid, 
                string.Empty, 
                prompt, 
                chatId, 
                settings, 
                true, 
                "FORTUNE");
            llmStopwatch.Stop();
            _logger.LogInformation($"[PERF][Fortune] {userInfo.UserId} LLM_Call: {llmStopwatch.ElapsedMilliseconds}ms - Type: {type}");

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
            _logger.LogInformation($"[PERF][Fortune] {userInfo.UserId} LLM_Response: {aiResponse.Length} chars");

            Dictionary<string, Dictionary<string, string>>? parsedResults = null;
            Dictionary<string, string>? lifetimeForecast = new Dictionary<string, string>();
            Dictionary<string, string>? yearlyForecast = new Dictionary<string, string>();
            
            // Multilingual caches
            Dictionary<string, Dictionary<string, Dictionary<string, string>>>? multilingualResults = null;
            Dictionary<string, Dictionary<string, string>>? multilingualLifetime = null;
            Dictionary<string, Dictionary<string, string>>? multilingualYearly = null;

            // Parse AI response based on type
            var parseStopwatch = Stopwatch.StartNew();
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
            parseStopwatch.Stop();
            _logger.LogInformation($"[PERF][Fortune] {userInfo.UserId} Parse_Response: {parseStopwatch.ElapsedMilliseconds}ms - Type: {type}");

            var predictionId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            // Raise event to save prediction (with multilingual support)
            RaiseEvent(new PredictionGeneratedEvent
            {
                PredictionId = predictionId,
                UserId = userInfo.UserId,
                PredictionDate = predictionDate,
                Results = parsedResults,
                Energy = 0, // Deprecated field, kept for backward compatibility
                CreatedAt = now,
                LifetimeForecast = lifetimeForecast,
                WeeklyForecast = new Dictionary<string, string>(), // Deprecated field
                WeeklyGeneratedDate = null, // Deprecated field
                ProfileUpdatedAt = userInfo.UpdatedAt,
                // Multilingual data
                MultilingualResults = multilingualResults,
                MultilingualLifetime = multilingualLifetime,
                MultilingualWeekly = null, // Deprecated field
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
EXCEPTION: chineseAstrology_currentYearStems must remain in Chinese with Pinyin format across ALL languages (e.g., '乙巳 (Yǐsì)').
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
      ""fourPillars_coreIdentity"": ""[12-18 words: Address user by name, describe chart as fusion of elements/qualities. E.g., 'Sean, your chart presents a fascinating fusion of air intellect, earth depth, and fire instinct.']"", 
      ""fourPillars_coreIdentity_expanded"": ""[45-60 words: State Sun sign + birth year animal + Rising + Moon, define archetype in one phrase, then list 3-4 contrasting traits using 'both...yet' or 'and' patterns]"",
      ""chineseAstrology_currentYear"": ""Year of the [Element Animal] - MUST be accurate for current year based on Chinese lunar calendar"", 
      ""chineseAstrology_currentYearStems"": ""[干支词组 (Pinyin)] - MUST be accurate Heavenly Stem + Earthly Branch for current year. Example: '己巳 (Jǐsì)' for 1989, '乙巳 (Yǐsì)' for 2025'"",
      ""chineseAstrology_trait1"": ""[8-12 words: One key personality trait from Chinese zodiac]"", 
      ""chineseAstrology_trait2"": ""[8-12 words: Another trait]"", 
      ""chineseAstrology_trait3"": ""[8-12 words: Another trait]"", 
      ""chineseAstrology_trait4"": ""[8-12 words: Optional fourth trait]"",
      ""zodiacWhisper"": ""[40-50 words: Explain how Chinese zodiac element/animal adds specific qualities to the Western chart. Start with '[Animal] adds...' Use 'You are not only X, but Y.' Address directly with 'You']"",
      ""sunSign_name"": ""[sign name]"", ""sunSign_tagline"": ""You [2-5 words, poetic metaphor like 'flow like water' or 'burn like fire']"",
      ""westernOverview_sunSign"": ""[sign]"", 
      ""westernOverview_sunArchetype"": ""The [3-5 words archetype title]"", 
      ""westernOverview_sunDescription"": ""[18-25 words: Core Sun sign traits, use 'You' extensively. E.g., 'Sean, you are empathic, creative, deeply intuitive. You dream in symbols and speak in soul codes.']"",
      ""westernOverview_moonSign"": ""[sign]"", 
      ""westernOverview_moonArchetype"": ""The [3-5 words archetype title]"", 
      ""westernOverview_moonDescription"": ""[15-20 words: Emotional nature, describe Moon qualities. E.g., 'Emotional depth, protective instincts, and strong intuitive memory. Words carry nurturing frequency.']"",
      ""westernOverview_risingSign"": ""[sign]"", 
      ""westernOverview_risingArchetype"": ""The [3-5 words archetype title]"", 
      ""westernOverview_risingDescription"": ""[20-28 words: How they appear/move through world. E.g., 'You meet the world with radiant confidence. Your purpose is to shine through self-expressive mastery — when paired with your Bazi fire-hour, communication becomes royal.']"",
      ""combinedEssence"": ""[15-20 words: Pattern 'You think like [article] [Sun], feel like [article] [Moon], and move through the world like [article] [Rising].']"",
      ""strengths_overview"": ""[10-15 words: Philosophical statement about how they evolve/grow. E.g., 'You evolve in layers. You're always quietly editing your identity.']"",
      ""strengths_item1_title"": ""[2-5 words: Strength name]"", 
      ""strengths_item1_description"": ""[15-25 words: Describe strength using specific sign combinations. E.g., 'Gemini gives you broadcast power, Scorpio Moon gives you x-ray insight. You can read between lines and speak beneath surface tensions.']"",
      ""strengths_item2_title"": ""[2-5 words: Strength name]"", 
      ""strengths_item2_description"": ""[12-18 words: Another strength with sign attribution]"",
      ""strengths_item3_title"": ""[2-5 words: Strength name]"", 
      ""strengths_item3_description"": ""[12-18 words: Another strength with sign attribution]"",
      ""challenges_overview"": ""[12-18 words: State when/how their power grows. Start with 'Your power grows when...' E.g., 'Your power grows when you balance intuition and logic, and when you allow safe emotional openings.']"",
      ""challenges_item1_title"": ""[2-5 words: Challenge name]"", 
      ""challenges_item1_description"": ""[8-15 words: Frame challenge using sign combinations. E.g., 'Virgo + Gemini can trap you in mental loops.']"",
      ""challenges_item2_title"": ""[2-5 words: Challenge name]"", 
      ""challenges_item2_description"": ""[10-18 words: Another challenge with signs]"",
      ""challenges_item3_title"": ""[2-5 words: Challenge name]"", 
      ""challenges_item3_description"": ""[10-18 words: Another challenge with signs]"",
      ""destiny_overview"": ""[20-30 words: State life purpose/calling using 'You are here to...' Start with purpose, end with identity. E.g., 'You are here to grasp patterns beneath the surface, and express them with clarity and usefulness. You are both mirror and messenger.']"",
      ""destiny_path1_title"": ""[3-5 roles separated by / (slash). E.g., 'Therapist / Coach / Strategist']"", 
      ""destiny_path1_description"": ""[3-6 words: Core essence of this path. E.g., 'Blending logic and depth.']"",
      ""destiny_path2_title"": ""[3-5 roles separated by /. E.g., 'Writer / Researcher / Otherworldly Communicator']"", 
      ""destiny_path2_description"": ""[5-10 words: E.g., 'Someone who processes and transmits hidden wisdom.']"",
      ""destiny_path3_title"": ""[1-3 roles or unique title. E.g., 'Spiritual Scientist']"", 
      ""destiny_path3_description"": ""[8-15 words: E.g., 'Interpreting emotional data, creating inner systems, helping others decode themselves.']"",
      ""chineseZodiac_animal"": ""The [Animal - user's birth year zodiac animal]"", 
      ""chineseZodiac_essence"": ""Essence like [element - the element associated with user's birth year, e.g., 'fire', 'water', 'earth', 'metal', 'wood']"",
      ""zodiacCycle_title"": ""Zodiac Cycle Influence (2024-2043) - MUST use actual current 20-year period"", 
      ""zodiacCycle_cycleName"": ""[Cycle Name in English, e.g., 'Li Fire Luck Cycle']"", 
      ""zodiacCycle_cycleNameChinese"": ""[Chinese name, e.g., '九紫离火运']"",
      ""zodiacCycle_overview"": ""[50-65 words: State user's zodiac + element alignment. Start with 'Your Chinese Zodiac is [Animal] ([Element]), a [element]-aligned sign.' Describe the current 20-year cycle period, what element/energy dominates, and how it affects the user's Day Master. E.g., 'From 2024 to 2043, we enter the Li Fire Luck Cycle — an era dominated by expressive, spiritual fire energy. For you, this enhances your metal Day Master.']"",
      ""zodiacCycle_dayMasterPoint1"": ""[8-12 words: How this cycle affects user. E.g., 'Fire tempers your essence into useable form']"", 
      ""zodiacCycle_dayMasterPoint2"": ""[6-10 words: Another effect]"", 
      ""zodiacCycle_dayMasterPoint3"": ""[8-12 words: Another effect]"", 
      ""zodiacCycle_dayMasterPoint4"": ""[10-15 words: Final effect or timing advice]"",
      ""tenYearCycles_description"": ""[40-60 words: Describe user's Fate Palace (命宫). State which sector it lies in (e.g., Gen 艮, Kun 坤), its element, and what it represents. Then explain what this aligns the user with. Start with 'Your Fate Palace (命宫) lies in the [Sector] ([Chinese]) sector — [element] element, representing [qualities]. This aligns you with [activities/strengths].']"",
      ""pastCycle_ageRange"": ""Age X-Y (YYYY-YYYY) - MUST calculate based on user's birth year"", 
      ""pastCycle_period"": ""[干支 (Pinyin)] · [Element Animal] - e.g., '甲午 (Jia-Wu) · Wood Horse']"", 
      ""pastCycle_influenceSummary"": ""[8-12 words: Brief summary of past cycle's theme]"", 
      ""pastCycle_meaning"": ""[60-80 words: Describe what this past cycle meant for the user. Use past tense. Explain the dominant element/energy, what it supported or challenged, and how it shaped them. Reference the Ten Gods (e.g., Shi Shen, Zheng Guan) if relevant.]"",
      ""currentCycle_ageRange"": ""Age X-Y (YYYY-YYYY) - MUST calculate based on user's birth year and current year"", 
      ""currentCycle_period"": ""[干支 (Pinyin)] · [Element Animal]"", 
      ""currentCycle_influenceSummary"": ""[8-12 words: Brief summary of current cycle's theme, e.g., 'Current cycle: peak creativity & expression']"", 
      ""currentCycle_meaning"": ""[60-80 words: Describe what this current cycle means for the user. Use present tense. Start with 'This is your [Cycle Name] —' Explain the dominant element/energy, what it's empowering or fueling, and what activities/paths it supports. Reference the Ten Gods if relevant. E.g., 'This is your Expression Cycle — fire is empowering your Geng Metal, fueling charisma, speech, creativity, and public influence. As a Shi Shen cycle, it supports teaching, writing, performing, and crafting new systems of reality through structured content.']"",
      ""futureCycle_ageRange"": ""Age X-Y (YYYY-YYYY) - MUST calculate based on user's birth year"", 
      ""futureCycle_period"": ""[干支 (Pinyin)] · [Element Animal]"", 
      ""futureCycle_influenceSummary"": ""[8-12 words: Brief summary of future cycle's theme]"", 
      ""futureCycle_meaning"": ""[60-80 words: Describe what this future cycle will bring for the user. Use future tense. Explain the dominant element/energy, what it will support or challenge, and what opportunities or lessons it brings. Reference the Ten Gods if relevant.]"",
      ""lifePlot_title"": ""You are a [10-20 words: Create poetic archetype that captures user's essence. E.g., 'You are a linguistic architect of reality.']"", 
      ""lifePlot_chapter"": ""[30-50 words: Address user by name, describe their destiny/calling in inspiring terms. E.g., 'Sean, your destiny isn't just to express — it's to build entire frameworks, crafts, and worlds through language.']"",
      ""lifePlot_point1"": ""[5-15 words: How one element/chart aspect shapes them. E.g., 'Fire refines your soul']"", 
      ""lifePlot_point2"": ""[5-15 words: Another element/chart aspect]"", 
      ""lifePlot_point3"": ""[5-15 words: Another element/chart aspect]"", 
      ""lifePlot_point4"": ""[5-15 words: Final powerful statement about their identity. E.g., 'You are not here to find your voice — you are the voice']"",
      ""activationSteps_step1_title"": ""[2-5 words: Action-oriented title. E.g., 'Morning Practice']"", 
      ""activationSteps_step1_description"": ""[10-20 words: Specific actionable advice. E.g., 'Journaling, scripting, or sigil sentences to code your day']"",
      ""activationSteps_step2_title"": ""[2-5 words: E.g., 'Teach or Share']"", 
      ""activationSteps_step2_description"": ""[10-20 words: E.g., 'Host micro-teachings, even just 1:1 sharing']"",
      ""activationSteps_step3_title"": ""[2-5 words: E.g., 'Framework Building']"", 
      ""activationSteps_step3_description"": ""[10-20 words: E.g., 'Develop a system of language, symbols, or logic unique to your essence']"",
      ""activationSteps_step4_title"": ""[2-5 words: E.g., 'Speak With Intention']"", 
      ""activationSteps_step4_description"": ""[10-20 words: Powerful closing statement. E.g., 'Every word you say or write is a ripple collapsing future states']"",
      ""mantra_title"": ""[2-4 words: Section title. E.g., 'Be Intentional']"", 
      ""mantra_point1"": ""[5-15 words: Inspiring directive. E.g., 'Write as if you are sculpting spirit']"", 
      ""mantra_point2"": ""[5-15 words: Another directive. E.g., 'Speak as if you are broadcasting codes']"", 
      ""mantra_point3"": ""[5-15 words: Final powerful statement. E.g., 'Recognize: your voice is a divine tool for reality programming']""
    }},
    ""zh-tw"": {{...same}}, ""zh"": {{...same}}, ""es"": {{...same}}
  }}
}}

CONTENT GUIDELINES:
- fourPillars coreIdentity: Address user by name, describe as ''fusion of'' elements
- fourPillars expanded: List zodiac signs, define archetype, show contrasts using ''both...yet'' patterns
- chineseAstrology currentYear: MUST calculate accurate year animal+element based on Chinese lunar calendar (e.g., 2025 = Wood Snake year)
- chineseAstrology currentYearStems: CRITICAL - Calculate accurate Heavenly Stem + Earthly Branch for the year (e.g., 2025 = 乙巳 Yǐsì). Must be ONE compound word. MUST use Chinese characters + Pinyin in ALL language versions (do NOT translate)
- chineseAstrology traits: 3-4 concrete personality traits from user's birth year animal
- zodiacWhisper: How Chinese zodiac adds to/enhances Western astrology. Use ''You are not only X, but Y'' pattern
- sunSign tagline: Create poetic metaphor using nature/elements (e.g., ''flow like water'', ''burn like fire'', ''shine like diamond'')
- westernOverview archetypes: Give each sign a title (e.g., ''The Dream Weaver'', ''The Intuitive Nurturer'', ''The Radiant Leader'')
- westernOverview descriptions: Sun is core identity, Moon is emotional nature, Rising is how they meet the world
- combinedEssence: Use exact pattern ''You think like [a/an] [Sun], feel like [a/an] [Moon], and move through the world like [a/an] [Rising].''
- strengths overview: Philosophical statement on evolution/growth pattern
- strengths items: Attribute each strength to specific sign combinations (e.g., ''Gemini gives you X, Scorpio Moon gives you Y'')
- challenges overview: Start with ''Your power grows when...'' to frame positively
- challenges items: Use sign combinations to explain challenges (e.g., ''Virgo + Gemini can trap you in mental loops'')
- destiny overview: State life purpose with 'You are here to...' pattern, end with dual identity (e.g., ''You are both X and Y'')
- destiny paths: List 3 potential career/calling directions using slash-separated roles, each path more specific than last
- zodiacCycle title: MUST calculate accurate current 20-year period based on actual year
- zodiacCycle names: Provide both English cycle name and Chinese name (e.g., 'Li Fire Luck Cycle' / '九紫离火运')
- zodiacCycle overview: Start by stating user's zodiac+element, then describe the 20-year cycle and how it affects their Day Master
- zodiacCycle dayMaster points: 4 specific ways this cycle influences the user, can include timing advice
- tenYearCycles: Describe user's Fate Palace (命宫), its sector (with Chinese), element, and what it aligns them with
- pastCycle/currentCycle/futureCycle: MUST calculate accurate age ranges based on user's birth year. Each 10-year period includes:
  * ageRange: Age span and year span (e.g., ""Age 27-37 (2016-2026)"")
  * period: Heavenly Stem + Earthly Branch in Chinese with Pinyin, plus Element Animal (e.g., ""甲午 (Jia-Wu) · Wood Horse"")
  * influenceSummary: Brief theme summary (8-12 words)
  * meaning: Detailed explanation (60-80 words) using appropriate tense (past/present/future), referencing Ten Gods system if relevant
- lifePlot title: Create poetic archetype starting with ""You are a..."" (10-20 words total)
- lifePlot chapter: Address user by name, describe their destiny in inspiring terms (30-50 words)
- lifePlot points: 4 bullet points - first 3 describe how elements/chart aspects shape them (5-15 words each), final point is powerful identity statement
- activationSteps: 4 practical action steps to activate their destiny. Each has a title (2-5 words) and description (10-20 words). Be specific and actionable. Final step should be most powerful.
- mantra: Section titled with 2-4 words. 3 inspiring directives (5-15 words each) using command language like ""Write as if..."", ""Speak as if..."", ""Recognize..."". Final point should be most powerful.

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
      ""zodiacInfluence"": ""[Element Animal] native in a [Element Animal] year → [Taishui relationship like Self-Punishment/Harmony etc (刑太岁/冲太岁/etc)]"",
      ""westernAstroOverlay"": ""[Sun sign] Sun · [2-3 word archetype/role] — {predictionDate.Year} [Key planetary transits]"",
      ""yearlyTheme_overallTheme"": ""[4-7 words: Powerful theme title using 'of' structure, like 'Recalibration of Self, Speech & System']"", 
      ""yearlyTheme_atAGlance"": ""[15-20 words: State what Eastern and Western systems agree on for this year]"", 
      ""yearlyTheme_expanded"": ""[60-80 words total, 3 paragraphs separated by double space: P1 describe astrological combination and zodiac clash, P2 what this creates/tests, P3 define what kind of year this is (not X but Y)]"",
      ""divineInfluence_career_score"": [1-4], ""divineInfluence_career_tagline"": ""[10-15 words: Start with 'Your superpower this year:']"", 
      ""divineInfluence_career_bestMoves"": [""[8-12 words: Specific actionable advice]"", ""[8-15 words: Another specific action]""], 
      ""divineInfluence_career_avoid"": [""[4-8 words: Specific behavior to avoid]"", ""[4-8 words: Another thing to avoid]""], 
      ""divineInfluence_career_inANutshell"": ""[50-70 words, 3 parts separated by double space: Part 1 planetary formula (e.g., 'Saturn + 天岁 Clash = Structural Audit.'), Part 2 describe how it feels, Part 3 what delays/challenges mean]"",
      ""divineInfluence_love_score"": [1-4], ""divineInfluence_love_tagline"": ""[10-15 words: Philosophical statement about love this year]"", 
      ""divineInfluence_love_bestMoves"": [""[6-10 words: Single advice OR Committed advice with label]"", ""[6-12 words: Another advice (can be for different relationship status)]""], 
      ""divineInfluence_love_avoid"": [""[6-12 words: What to avoid in relationships]"", ""[4-8 words: Another pitfall to avoid]""], 
      ""divineInfluence_love_inANutshell"": ""[50-70 words, 3 parts separated by double space: Part 1 planetary formula (e.g., 'Neptune + 天岁 Clash = Emotional Sensitivity.'), Part 2 emotional state this year, Part 3 what relationships require]"",
      ""divineInfluence_wealth_score"": [1-4], ""divineInfluence_wealth_tagline"": ""[10-15 words: Financial philosophy for the year]"", 
      ""divineInfluence_wealth_bestMoves"": [""[8-12 words: Specific financial strategy]"", ""[8-15 words: Another wealth action]""], 
      ""divineInfluence_wealth_avoid"": [""[4-8 words: Financial behavior to avoid]"", ""[4-8 words: Another wealth pitfall]""], 
      ""divineInfluence_wealth_inANutshell"": ""[50-70 words, 3 parts separated by double space: Part 1 planetary formula for wealth, Part 2 financial climate this year, Part 3 what prosperity requires]"",
      ""divineInfluence_health_score"": [1-4], ""divineInfluence_health_tagline"": ""[10-15 words: Health philosophy for the year]"", 
      ""divineInfluence_health_bestMoves"": [""[8-12 words: Specific health practice]"", ""[8-15 words: Another health action]""], 
      ""divineInfluence_health_avoid"": [""[4-8 words: Health behavior to avoid]"", ""[4-8 words: Another health pitfall]""], 
      ""divineInfluence_health_inANutshell"": ""[50-70 words, 3 parts separated by double space: Part 1 planetary formula for health, Part 2 body-mind state this year, Part 3 what wellness requires]"",
      ""embodimentMantra"": ""[18-25 words: First-person declarations using 'My'. Create 2-3 powerful statements about how user will embody this year's energy. Poetic, rhythmic, empowering]""
    }},
    ""zh-tw"": {{...same}}, ""zh"": {{...same}}, ""es"": {{...same}}
  }}
}}

CONTENT GUIDELINES:
- zodiacInfluence: Show user's Chinese zodiac element+animal, year's element+animal, and Taishui relationship
- westernAstroOverlay: Mention Sun sign, give them an archetype/role, list key 2025 transits
- yearlyTheme overallTheme: Use 'of' structure to create gravitas (e.g., 'Recalibration of...', 'Year of...')
- yearlyTheme atAGlance: Start with 'Both Eastern and Western systems agree:' or similar consensus statement
- yearlyTheme expanded: 
  * P1: Address user directly, describe the rare astrological combination (Chinese zodiac clash + Western transits)
  * P2: Explain what this combination creates, tests, or demands
  * P3: Define the year's nature using contrast ('This is not X — it's Y' or 'Not for X, but for Y')
- divineInfluence scores: Be realistic (1=challenging, 2=mixed, 3=favorable, 4=excellent)
- divineInfluence taglines: Career 'Your superpower this year:', Love/Wealth/Health are philosophical
- divineInfluence bestMoves: Concrete actions. Love can specify 'If You Are Single' / 'If You Are Committed'
- divineInfluence inANutshell: Always use formula pattern (e.g., 'Saturn + 天岁 Clash = X.'), describe state, explain meaning
- embodimentMantra: First-person 'My' statements. Example: 'My words build worlds. My silence tunes my frequency. My path is not rushed — it is authored.'

RULES:
- Use You/Your extensively (except embodimentMantra uses 'My')
- No special chars or emoji
- No line breaks in strings (use double space to separate parts)
- Warm, empowering tone";
        }
        else // PredictionType.Daily
        {
            prompt = multilingualPrefix + $@"Generate daily prediction for {predictionDate:yyyy-MM-dd}.
User: {userInfoLine}

FORMAT (flattened):
{{
  ""predictions"": {{
    ""en"": {{
      ""dayTitle"": ""The Day of [word1] and [word2]"",
      ""todaysReading_tarotCard_name"": ""[card name]"", ""todaysReading_tarotCard_represents"": ""[1-2 words essence]"", 
      ""todaysReading_pathTitle"": ""{{firstName}}'s Path Today - A [Adjective] Path"",
      ""todaysReading_pathDescription"": ""[15-25 words: Greet user, describe today's energy/theme, how it may feel]"", 
      ""todaysReading_pathDescriptionExpanded"": ""[30-40 words: Deeper insight on navigating today, actionable wisdom]"",
      ""todaysReading_careerAndWork"": ""[10-20 words: Specific career/work guidance, actionable and direct]"", 
      ""todaysReading_loveAndRelationships"": ""[10-20 words: Relationship guidance, communication tips, emotional advice]"", 
      ""todaysReading_wealthAndFinance"": ""[10-20 words: Financial decisions, money mindset, investment guidance]"", 
      ""todaysReading_healthAndWellness"": ""[10-15 words: Physical/mental health focus, self-care advice]"",
      ""todaysTakeaway"": ""[15-25 words: Start with '{{firstName}}, your...', deliver core insight/truth. Use contrast or cause-effect. Powerful and memorable]"",
      ""luckyAlignments_luckyNumber_number"": ""[Seven]"", ""luckyAlignments_luckyNumber_digit"": ""[7]"", 
      ""luckyAlignments_luckyNumber_description"": ""[15-20 words: Describe the number's energy/qualities relevant to today]"",
      ""luckyAlignments_luckyNumber_calculation"": ""How is it calculated? Numerical Energy of the Day ({predictionDate:M-d-yyyy}): [show full formula step by step, reduce to single digit]"",
      ""luckyAlignments_luckyStone"": ""[stone name]"", 
      ""luckyAlignments_luckyStone_description"": ""[15-20 words: How to use the stone and what it awakens/aligns]"",
      ""luckyAlignments_luckyStone_guidance"": ""[15-20 words: Start with 'Meditate:' or 'Practice:', give specific ritual/action steps]"",
      ""luckyAlignments_luckySpell"": ""[2-4 words: The spell name, can be poetic]"", 
      ""luckyAlignments_luckySpell_description"": ""[Quote format: 'When thoughts drift like clouds, I return to stillness.']"",
      ""luckyAlignments_luckySpell_intent"": ""[10-12 words: Start with 'To [verb]...', describe the spell's purpose clearly]"",
      ""twistOfFate_favorable"": [""[4-8 words: Specific favorable action or approach]"", ""[4-8 words: Another favorable action]""], 
      ""twistOfFate_avoid"": [""[4-8 words: Specific behavior or approach to avoid]"", ""[4-8 words: Another thing to avoid]""], 
      ""twistOfFate_todaysRecommendation"": ""[10-15 words: Start with 'Today's turning point lies in...' or similar. Synthesize the key insight]""
    }},
    ""zh-tw"": {{...same}}, ""zh"": {{...same}}, ""es"": {{...same}}
  }}
}}

CONTENT GUIDELINES:
- pathDescription: Start with 'Hi {{firstName}}', describe the day's overall energy and how it may feel
- pathDescriptionExpanded: Offer deeper wisdom on navigating today. Use metaphors if fitting. Focus on transformation and understanding, not just doing
- todaysTakeaway: Powerful closing message. Use patterns like 'Your power is not in X, but in Y' or 'The more you X, the Y'. Make it memorable
- Career/Love/Wealth/Health: Be specific and actionable
- Lucky Number: Calculate using date digits, describe its spiritual meaning
- Lucky Stone: Explain how to use it (hold/wear/meditate), give practical ritual steps
- Lucky Spell: Create a poetic name, write as first-person affirmation/mantra, state clear intent
- Twist of Fate: Provide 2 specific favorable actions and 2 things to avoid. Be concrete and actionable
- Today's Recommendation: Synthesize the turning point in one clear sentence

RULES:
- Use You/Your extensively
- No special chars or emoji
- No line breaks in strings
- Warm, direct tone";            
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

