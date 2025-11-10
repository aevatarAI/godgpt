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

                    var predictionDto = new PredictionResultDto
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
                    };
                    
                    // Extract enum values for frontend
                    ExtractEnumValues(predictionDto, State.Results, lifetimeWithPhase);
                    
                    return new GetTodayPredictionResult
                    {
                        Success = true,
                        Message = string.Empty,
                        Prediction = predictionDto
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

                    var predictionDto = new PredictionResultDto
                    {
                        PredictionId = State.PredictionId,
                        UserId = State.UserId,
                        PredictionDate = State.PredictionDate,
                        Results = new Dictionary<string, Dictionary<string, string>>(), // Yearly doesn't have daily results
                        CreatedAt = State.CreatedAt,
                        FromCache = true,
                        LifetimeForecast = State.YearlyForecast, // Return yearly in LifetimeForecast field for API compatibility
                        MultilingualLifetime = State.MultilingualYearly // Return yearly multilingual
                    };
                    
                    // Extract enum values for frontend (from yearly forecast)
                    ExtractEnumValues(predictionDto, null, State.YearlyForecast);
                    
                    return new GetTodayPredictionResult
                    {
                        Success = true,
                        Message = string.Empty,
                        Prediction = predictionDto
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

                    var predictionDto = new PredictionResultDto
                    {
                        PredictionId = State.PredictionId,
                        UserId = State.UserId,
                        PredictionDate = State.PredictionDate,
                        Results = State.Results,
                        CreatedAt = State.CreatedAt,
                        FromCache = true,
                        // Include multilingual cached data
                        MultilingualResults = State.MultilingualResults
                    };
                    
                    // Extract enum values for frontend (from daily results)
                    ExtractEnumValues(predictionDto, State.Results, State.LifetimeForecast);
                    
                    return new GetTodayPredictionResult
                    {
                        Success = true,
                        Message = string.Empty,
                        Prediction = predictionDto
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

            var newPredictionDto = new PredictionResultDto
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
            };
            
            // Extract enum values for frontend
            ExtractEnumValues(newPredictionDto, parsedResults, 
                type == PredictionType.Yearly ? yearlyForecast : lifetimeForecast);
            
            return new GetTodayPredictionResult
            {
                Success = true,
                Message = string.Empty,
                Prediction = newPredictionDto
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
        var multilingualPrefix = @"You are a mystical diviner and life guide combining Eastern astrology (Bazi/Chinese Zodiac) and Western astrology (Sun/Moon/Rising). Provide insightful, warm, empowering guidance.

MULTILINGUAL: Generate in 4 languages with natural translation (not word-by-word): 'en', 'zh-tw', 'zh', 'es'.
EXCEPTION: chineseAstrology_currentYearStems always stays in Chinese+Pinyin (e.g., '乙巳 (Yǐsì)').
Wrap response in 'predictions' object with language codes.

";
        
        if (type == PredictionType.Lifetime)
        {
            prompt = multilingualPrefix + $@"Generate lifetime profile for user.
User: {userInfoLine}

FORMAT (flattened):
{{
  ""predictions"": {{
    ""en"": {{
      ""welcomeNote_zodiac"": ""[zodiac]"", ""welcomeNote_chineseZodiac"": ""[Element Animal]"", ""welcomeNote_rhythm"": ""[Yin/Yang Element]"", ""welcomeNote_essence"": ""[adj] and [adj]"",
      ""fourPillars_coreIdentity"": ""[12-18 words: Address by name, describe chart as fusion of elements]"", 
      ""fourPillars_coreIdentity_expanded"": ""[45-60 words: List Sun/Moon/Rising signs, define archetype, show contrasts using 'both...yet' patterns]"",
      ""chineseAstrology_currentYear"": ""Year of the [Element Animal] - accurate for current lunar year"", 
      ""chineseAstrology_currentYearStems"": ""[干支 (Pinyin)] - accurate Heavenly Stem + Earthly Branch"",
      ""chineseAstrology_trait1"": ""[8-12 words]"", ""chineseAstrology_trait2"": ""[8-12 words]"", ""chineseAstrology_trait3"": ""[8-12 words]"", ""chineseAstrology_trait4"": ""[8-12 words]"",
      ""zodiacWhisper"": ""[40-50 words: How Chinese zodiac enhances Western chart. Start '[Animal] adds...' Use 'You are not only X, but Y']"",
      ""sunSign_name"": ""[sign]"", ""sunSign_tagline"": ""You [2-5 words poetic metaphor]"",
      ""westernOverview_sunSign"": ""[sign]"", ""westernOverview_sunArchetype"": ""The [3-5 words]"", ""westernOverview_sunDescription"": ""[18-25 words: Core traits using 'You']"",
      ""westernOverview_moonSign"": ""[sign]"", ""westernOverview_moonArchetype"": ""The [3-5 words]"", ""westernOverview_moonDescription"": ""[15-20 words: Emotional nature]"",
      ""westernOverview_risingSign"": ""[sign]"", ""westernOverview_risingArchetype"": ""The [3-5 words]"", ""westernOverview_risingDescription"": ""[20-28 words: How they meet world]"",
      ""combinedEssence"": ""[15-20 words: 'You think like [Sun], feel like [Moon], move through world like [Rising]']"",
      ""strengths_overview"": ""[10-15 words: How they evolve/grow]"",
      ""strengths_item1_title"": ""[2-5 words]"", ""strengths_item1_description"": ""[15-25 words: Attribute to sign combinations]"",
      ""strengths_item2_title"": ""[2-5 words]"", ""strengths_item2_description"": ""[12-18 words]"",
      ""strengths_item3_title"": ""[2-5 words]"", ""strengths_item3_description"": ""[12-18 words]"",
      ""challenges_overview"": ""[12-18 words: Start 'Your power grows when...']"",
      ""challenges_item1_title"": ""[2-5 words]"", ""challenges_item1_description"": ""[8-15 words: Frame using sign combinations]"",
      ""challenges_item2_title"": ""[2-5 words]"", ""challenges_item2_description"": ""[10-18 words]"",
      ""challenges_item3_title"": ""[2-5 words]"", ""challenges_item3_description"": ""[10-18 words]"",
      ""destiny_overview"": ""[20-30 words: 'You are here to...' End with dual identity]"",
      ""destiny_path1_title"": ""[3-5 roles separated by /]"", ""destiny_path1_description"": ""[3-6 words]"",
      ""destiny_path2_title"": ""[3-5 roles separated by /]"", ""destiny_path2_description"": ""[5-10 words]"",
      ""destiny_path3_title"": ""[1-3 roles]"", ""destiny_path3_description"": ""[8-15 words]"",
      ""chineseZodiac_animal"": ""The [Animal]"", ""chineseZodiac_essence"": ""Essence like [element]"",
      ""zodiacCycle_title"": ""Zodiac Cycle Influence (YYYY-YYYY) - current 20-year period"", 
      ""zodiacCycle_cycleName"": ""[English name]"", ""zodiacCycle_cycleNameChinese"": ""[Chinese name]"",
      ""zodiacCycle_overview"": ""[50-65 words: State zodiac+element, describe 20-year cycle, how it affects Day Master]"",
      ""zodiacCycle_dayMasterPoint1"": ""[8-12 words]"", ""zodiacCycle_dayMasterPoint2"": ""[6-10 words]"", ""zodiacCycle_dayMasterPoint3"": ""[8-12 words]"", ""zodiacCycle_dayMasterPoint4"": ""[10-15 words]"",
      ""tenYearCycles_description"": ""[40-60 words: Fate Palace sector, element, what it represents, what it aligns them with]"",
      ""pastCycle_ageRange"": ""Age X-Y (YYYY-YYYY)"", ""pastCycle_period"": ""[干支 (Pinyin)] · [Element Animal]"", 
      ""pastCycle_influenceSummary"": ""[8-12 words]"", ""pastCycle_meaning"": ""[60-80 words: Past tense, explain element/energy, reference Ten Gods if relevant]"",
      ""currentCycle_ageRange"": ""Age X-Y (YYYY-YYYY)"", ""currentCycle_period"": ""[干支 (Pinyin)] · [Element Animal]"", 
      ""currentCycle_influenceSummary"": ""[8-12 words]"", ""currentCycle_meaning"": ""[60-80 words: Present tense, what it empowers, reference Ten Gods]"",
      ""futureCycle_ageRange"": ""Age X-Y (YYYY-YYYY)"", ""futureCycle_period"": ""[干支 (Pinyin)] · [Element Animal]"", 
      ""futureCycle_influenceSummary"": ""[8-12 words]"", ""futureCycle_meaning"": ""[60-80 words: Future tense, opportunities/challenges]"",
      ""lifePlot_title"": ""You are a [10-20 words: Poetic archetype]"", 
      ""lifePlot_chapter"": ""[30-50 words: Address by name, describe destiny]"",
      ""lifePlot_point1"": ""[5-15 words]"", ""lifePlot_point2"": ""[5-15 words]"", ""lifePlot_point3"": ""[5-15 words]"", ""lifePlot_point4"": ""[5-15 words: Powerful identity statement]"",
      ""activationSteps_step1_title"": ""[2-5 words]"", ""activationSteps_step1_description"": ""[10-20 words: Actionable]"",
      ""activationSteps_step2_title"": ""[2-5 words]"", ""activationSteps_step2_description"": ""[10-20 words]"",
      ""activationSteps_step3_title"": ""[2-5 words]"", ""activationSteps_step3_description"": ""[10-20 words]"",
      ""activationSteps_step4_title"": ""[2-5 words]"", ""activationSteps_step4_description"": ""[10-20 words: Most powerful]"",
      ""mantra_title"": ""[2-4 words]"", 
      ""mantra_point1"": ""[5-15 words: 'X as if...' pattern]"", ""mantra_point2"": ""[5-15 words]"", ""mantra_point3"": ""[5-15 words: Most powerful]""
    }},
    ""zh-tw"": {{...same}}, ""zh"": {{...same}}, ""es"": {{...same}}
  }}
}}

KEY RULES:
- Calculate accurate Chinese lunar calendar dates/stems for currentYear and currentYearStems
- Use 'both...yet' contrasts for personality, 'You are here to...' for destiny, 'Your power grows when...' for challenges
- Attribute strengths/challenges to specific sign combinations
- Calculate age ranges for 10-year cycles based on birth year
- Use 'You/Your' extensively, warm tone, no special chars/emoji/line breaks";
        }
        else if (type == PredictionType.Yearly)
        {
            prompt = multilingualPrefix + $@"Generate yearly prediction for {predictionDate.Year}.
User: {userInfoLine}

FORMAT (flattened):
{{
  ""predictions"": {{
    ""en"": {{
      ""zodiacInfluence"": ""[Element Animal] native in [Element Animal] year → [Taishui relationship]"",
      ""westernAstroOverlay"": ""[Sun sign] Sun · [2-3 word archetype] — {predictionDate.Year} [Key transits]"",
      ""yearlyTheme_overallTheme"": ""[4-7 words: Theme using 'of' structure]"", 
      ""yearlyTheme_atAGlance"": ""[15-20 words: What systems agree on]"", 
      ""yearlyTheme_expanded"": ""[60-80 words: 3 paragraphs (double space): P1 combination/clash, P2 what it creates, P3 define year (not X but Y)]"",
      ""divineInfluence_career_score"": [1-4], ""divineInfluence_career_tagline"": ""[10-15 words: Start 'Your superpower this year:']"", 
      ""divineInfluence_career_bestMoves"": [""[8-12 words]"", ""[8-15 words]""], ""divineInfluence_career_avoid"": [""[4-8 words]"", ""[4-8 words]""], 
      ""divineInfluence_career_inANutshell"": ""[50-70 words: 3 parts (double space): P1 formula, P2 how it feels, P3 meaning]"",
      ""divineInfluence_love_score"": [1-4], ""divineInfluence_love_tagline"": ""[10-15 words: Philosophical]"", 
      ""divineInfluence_love_bestMoves"": [""[6-10 words]"", ""[6-12 words]""], ""divineInfluence_love_avoid"": [""[6-12 words]"", ""[4-8 words]""], 
      ""divineInfluence_love_inANutshell"": ""[50-70 words: 3 parts (double space): P1 formula, P2 emotional state, P3 what relationships need]"",
      ""divineInfluence_wealth_score"": [1-4], ""divineInfluence_wealth_tagline"": ""[10-15 words]"", 
      ""divineInfluence_wealth_bestMoves"": [""[8-12 words]"", ""[8-15 words]""], ""divineInfluence_wealth_avoid"": [""[4-8 words]"", ""[4-8 words]""], 
      ""divineInfluence_wealth_inANutshell"": ""[50-70 words: 3 parts (double space): P1 formula, P2 climate, P3 what prosperity needs]"",
      ""divineInfluence_health_score"": [1-4], ""divineInfluence_health_tagline"": ""[10-15 words]"", 
      ""divineInfluence_health_bestMoves"": [""[8-12 words]"", ""[8-15 words]""], ""divineInfluence_health_avoid"": [""[4-8 words]"", ""[4-8 words]""], 
      ""divineInfluence_health_inANutshell"": ""[50-70 words: 3 parts (double space): P1 formula, P2 state, P3 what wellness needs]"",
      ""embodimentMantra"": ""[18-25 words: First-person 'My' declarations, 2-3 powerful statements, poetic and rhythmic]""
    }},
    ""zh-tw"": {{...same}}, ""zh"": {{...same}}, ""es"": {{...same}}
  }}
}}

KEY RULES:
- Theme expanded: P1 astrological combo, P2 what it creates, P3 define using contrast (not X but Y)
- Scores: 1=challenging, 2=mixed, 3=favorable, 4=excellent
- inANutshell: Always use formula pattern ('X + Y = Z.'), then describe state, then meaning
- Career tagline starts 'Your superpower this year:', others are philosophical
- Use 'You/Your' (except embodimentMantra uses 'My'), warm tone, no special chars/emoji, use double space not line breaks";
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
      ""todaysReading_tarotCard_name"": ""[card]"", ""todaysReading_tarotCard_represents"": ""[1-2 words]"", ""todaysReading_tarotCard_orientation"": ""[Upright/Reversed]"",
      ""todaysReading_pathTitle"": ""{{firstName}}'s Path Today - A [Adjective] Path"",
      ""todaysReading_pathDescription"": ""[15-25 words: Greet, describe energy]"", ""todaysReading_pathDescriptionExpanded"": ""[30-40 words: Deeper wisdom, actionable]"",
      ""todaysReading_careerAndWork"": ""[10-20 words]"", ""todaysReading_loveAndRelationships"": ""[10-20 words]"", 
      ""todaysReading_wealthAndFinance"": ""[10-20 words]"", ""todaysReading_healthAndWellness"": ""[10-15 words]"",
      ""todaysTakeaway"": ""[15-25 words: Start '{{firstName}}, your...' Use contrast/cause-effect]"",
      ""luckyAlignments_luckyNumber_number"": ""[Seven]"", ""luckyAlignments_luckyNumber_digit"": ""[7]"", 
      ""luckyAlignments_luckyNumber_description"": ""[15-20 words]"",
      ""luckyAlignments_luckyNumber_calculation"": ""How is it calculated? Numerical Energy of the Day ({predictionDate:M-d-yyyy}): [formula reducing to single digit]"",
      ""luckyAlignments_luckyStone"": ""[stone]"", ""luckyAlignments_luckyStone_description"": ""[15-20 words: How to use, what it awakens]"",
      ""luckyAlignments_luckyStone_guidance"": ""[15-20 words: Start 'Meditate:' or 'Practice:', specific ritual]"",
      ""luckyAlignments_luckySpell"": ""[2-4 words poetic name]"", ""luckyAlignments_luckySpell_description"": ""[Quote format first-person affirmation]"",
      ""luckyAlignments_luckySpell_intent"": ""[10-12 words: Start 'To [verb]...']"",
      ""twistOfFate_favorable"": [""[4-8 words]"", ""[4-8 words]""], ""twistOfFate_avoid"": [""[4-8 words]"", ""[4-8 words]""], 
      ""twistOfFate_todaysRecommendation"": ""[10-15 words: Start 'Today's turning point lies in...']""
    }},
    ""zh-tw"": {{...same}}, ""zh"": {{...same}}, ""es"": {{...same}}
  }}
}}

KEY RULES:
- Tarot: Include orientation (Upright/Reversed) affecting tone
- pathDescription starts 'Hi {{firstName}}', pathDescriptionExpanded offers deeper wisdom with metaphors
- todaysTakeaway uses contrast patterns ('not X but Y', 'the more X, the Y')
- Lucky Number: Calculate from date digits. Stone: Ritual steps. Spell: First-person affirmation with intent
- Use 'You/Your' extensively, warm tone, no special chars/emoji/line breaks";            
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
    
    /// <summary>
    /// Extract enum values from prediction results
    /// </summary>
    private void ExtractEnumValues(PredictionResultDto prediction, Dictionary<string, Dictionary<string, string>>? results, Dictionary<string, string>? lifetime)
    {
        // Extract Tarot Card from daily prediction (from en language version)
        if (results != null && results.TryGetValue("en", out var enResults))
        {
            if (enResults.TryGetValue("todaysReading_tarotCard_name", out var tarotName))
            {
                prediction.TodaysTarotCard = ParseTarotCard(tarotName);
            }
            
            if (enResults.TryGetValue("luckyAlignments_luckyStone", out var stoneName))
            {
                prediction.LuckyStone = ParseCrystalStone(stoneName);
            }
        }
        
        // Extract Zodiac signs from lifetime prediction
        if (lifetime != null)
        {
            if (lifetime.TryGetValue("westernOverview_sunSign", out var sunSign))
            {
                prediction.SunSign = ParseZodiacSign(sunSign);
            }
            
            if (lifetime.TryGetValue("westernOverview_moonSign", out var moonSign))
            {
                prediction.MoonSign = ParseZodiacSign(moonSign);
            }
            
            if (lifetime.TryGetValue("westernOverview_risingSign", out var risingSign))
            {
                prediction.RisingSign = ParseZodiacSign(risingSign);
            }
            
            if (lifetime.TryGetValue("chineseZodiac_animal", out var chineseZodiac))
            {
                prediction.ChineseZodiac = ParseChineseZodiac(chineseZodiac);
            }
        }
    }
    
    /// <summary>
    /// Parse tarot card name to enum
    /// </summary>
    private TarotCardEnum ParseTarotCard(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName)) return TarotCardEnum.Unknown;
        
        // Remove "The " prefix and spaces for matching
        var normalized = cardName.Replace("The ", "").Replace(" ", "").Replace("of", "Of").Trim();
        
        if (Enum.TryParse<TarotCardEnum>(normalized, true, out var result))
        {
            return result;
        }
        
        _logger.LogWarning($"[FortunePredictionGAgent][ParseTarotCard] Unknown tarot card: {cardName}");
        return TarotCardEnum.Unknown;
    }
    
    /// <summary>
    /// Parse zodiac sign name to enum
    /// </summary>
    private ZodiacSignEnum ParseZodiacSign(string signName)
    {
        if (string.IsNullOrWhiteSpace(signName)) return ZodiacSignEnum.Unknown;
        
        var normalized = signName.Trim();
        
        if (Enum.TryParse<ZodiacSignEnum>(normalized, true, out var result))
        {
            return result;
        }
        
        _logger.LogWarning($"[FortunePredictionGAgent][ParseZodiacSign] Unknown zodiac sign: {signName}");
        return ZodiacSignEnum.Unknown;
    }
    
    /// <summary>
    /// Parse chinese zodiac animal to enum
    /// </summary>
    private ChineseZodiacEnum ParseChineseZodiac(string animalName)
    {
        if (string.IsNullOrWhiteSpace(animalName)) return ChineseZodiacEnum.Unknown;
        
        // Remove "The " prefix for matching
        var normalized = animalName.Replace("The ", "").Trim();
        
        if (Enum.TryParse<ChineseZodiacEnum>(normalized, true, out var result))
        {
            return result;
        }
        
        _logger.LogWarning($"[FortunePredictionGAgent][ParseChineseZodiac] Unknown chinese zodiac: {animalName}");
        return ChineseZodiacEnum.Unknown;
    }
    
    /// <summary>
    /// Parse crystal stone name to enum
    /// </summary>
    private CrystalStoneEnum ParseCrystalStone(string stoneName)
    {
        if (string.IsNullOrWhiteSpace(stoneName)) return CrystalStoneEnum.Unknown;
        
        // Remove spaces and special chars for matching
        var normalized = stoneName.Replace(" ", "").Replace("'", "").Replace("-", "").Trim();
        
        // Handle special cases
        var specialCases = new Dictionary<string, CrystalStoneEnum>(StringComparer.OrdinalIgnoreCase)
        {
            { "RoseQuartz", CrystalStoneEnum.RoseQuartz },
            { "ClearQuartz", CrystalStoneEnum.ClearQuartz },
            { "BlackTourmaline", CrystalStoneEnum.BlackTourmaline },
            { "TigersEye", CrystalStoneEnum.TigersEye },
            { "Tiger'sEye", CrystalStoneEnum.TigersEye },
            { "TigerEye", CrystalStoneEnum.TigersEye },
            { "LapisLazuli", CrystalStoneEnum.Lapis },
            { "Lapis", CrystalStoneEnum.Lapis }
        };
        
        if (specialCases.TryGetValue(normalized, out var specialResult))
        {
            return specialResult;
        }
        
        if (Enum.TryParse<CrystalStoneEnum>(normalized, true, out var result))
        {
            return result;
        }
        
        _logger.LogWarning($"[FortunePredictionGAgent][ParseCrystalStone] Unknown crystal stone: {stoneName}");
        return CrystalStoneEnum.Unknown;
    }
}

