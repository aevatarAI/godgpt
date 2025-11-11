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
    Task<GetTodayPredictionResult> GetOrGeneratePredictionAsync(FortuneUserDto userInfo, PredictionType type = PredictionType.Daily, string userLanguage = "en");
    
    [ReadOnly]
    Task<PredictionResultDto?> GetPredictionAsync(string userLanguage = "en");
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
                // Initialize language generation status (only initial language is available)
                if (!string.IsNullOrEmpty(generatedEvent.InitialLanguage) && generatedEvent.PredictionTypeGenerated.HasValue)
                {
                    var initialLang = generatedEvent.InitialLanguage;
                    switch (generatedEvent.PredictionTypeGenerated.Value)
                    {
                        case PredictionType.Daily:
                            state.DailyGeneratedLanguages = new List<string> { initialLang };
                            break;
                        case PredictionType.Yearly:
                            state.YearlyGeneratedLanguages = new List<string> { initialLang };
                            break;
                        case PredictionType.Lifetime:
                            state.LifetimeGeneratedLanguages = new List<string> { initialLang };
                break;
        }
    }
                break;
                
            case LanguagesTranslatedEvent translatedEvent:
                // Update multilingual cache with translated languages
                switch (translatedEvent.Type)
                {
                    case PredictionType.Daily:
                        if (state.MultilingualResults == null)
                        {
                            state.MultilingualResults = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
                        }
                        
                        var dateKey = translatedEvent.PredictionDate.ToString("yyyy-MM-dd");
                        if (!state.MultilingualResults.ContainsKey(dateKey))
                        {
                            state.MultilingualResults[dateKey] = new Dictionary<string, Dictionary<string, string>>();
                        }
                        
                        if (translatedEvent.TranslatedLanguages != null)
                        {
                            foreach (var lang in translatedEvent.TranslatedLanguages)
                            {
                                state.MultilingualResults[dateKey][lang.Key] = lang.Value;
                            }
                        }
                        
                        state.DailyGeneratedLanguages = translatedEvent.AllGeneratedLanguages;
                        break;
                        
                    case PredictionType.Yearly:
                        if (state.MultilingualYearly == null)
                        {
                            state.MultilingualYearly = new Dictionary<string, Dictionary<string, string>>();
                        }
                        
                        if (translatedEvent.TranslatedLanguages != null)
                        {
                            foreach (var lang in translatedEvent.TranslatedLanguages)
                            {
                                state.MultilingualYearly[lang.Key] = lang.Value;
                            }
                        }
                        
                        state.YearlyGeneratedLanguages = translatedEvent.AllGeneratedLanguages;
                        break;
                        
                    case PredictionType.Lifetime:
                        if (state.MultilingualLifetime == null)
                        {
                            state.MultilingualLifetime = new Dictionary<string, Dictionary<string, string>>();
                        }
                        
                        if (translatedEvent.TranslatedLanguages != null)
                        {
                            foreach (var lang in translatedEvent.TranslatedLanguages)
                            {
                                state.MultilingualLifetime[lang.Key] = lang.Value;
                            }
                        }
                        
                        state.LifetimeGeneratedLanguages = translatedEvent.AllGeneratedLanguages;
                        break;
                }
                break;
        }
    }

    public async Task<GetTodayPredictionResult> GetOrGeneratePredictionAsync(FortuneUserDto userInfo, PredictionType type = PredictionType.Daily, string userLanguage = "en")
    {
        var totalStopwatch = Stopwatch.StartNew();
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var currentYear = today.Year;
            
            _logger.LogInformation($"[PERF][Fortune] {userInfo.UserId} START - Type: {type}, Date: {today}, Language: {userLanguage}");
            
            // ========== IDEMPOTENCY CHECK: Prevent concurrent generation for this type ==========
            if (State.GenerationLocks.TryGetValue(type, out var lockInfo) && lockInfo.IsGenerating)
            {
                // Check if generation timed out (1 minute - handles service restart scenarios)
                if (lockInfo.StartedAt.HasValue)
            {
                    var elapsed = DateTime.UtcNow - lockInfo.StartedAt.Value;
                    
                    if (elapsed.TotalMinutes < 1)
                    {
                        // Generation is in progress, return waiting status
                        totalStopwatch.Stop();
                        _logger.LogWarning($"[Fortune] {userInfo.UserId} GENERATION_IN_PROGRESS - Type: {type}, StartedAt: {lockInfo.StartedAt}, Elapsed: {elapsed.TotalSeconds:F1}s");
                        
                        return new GetTodayPredictionResult
                        {
                            Success = false,
                            Message = $"{type} prediction is currently being generated. Please wait a moment and try again."
                        };
                    }
                    else
                    {
                        // Generation timed out (service restart or actual timeout), reset lock and retry
                        _logger.LogWarning($"[Fortune] {userInfo.UserId} GENERATION_TIMEOUT - Type: {type}, StartedAt: {lockInfo.StartedAt}, Elapsed: {elapsed.TotalMinutes:F2} minutes, Resetting lock and retrying");
                        lockInfo.IsGenerating = false;
                        lockInfo.StartedAt = null;
                    }
                }
            }
                
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
                            Results = new Dictionary<string, Dictionary<string, string>>(), // Lifetime doesn't have daily results
                            CreatedAt = State.CreatedAt,
                            FromCache = true,
                            LifetimeForecast = lifetimeWithPhase,
                        // Include multilingual cached data
                        MultilingualLifetime = multilingualLifetimeWithPhase,
                        // Language status
                        AvailableLanguages = State.LifetimeGeneratedLanguages ?? new List<string> { "en" },
                        AllLanguagesGenerated = State.LifetimeGeneratedLanguages?.Count == 4
                    };
                    
                    // Extract enum values for frontend (from lifetime forecast)
                    ExtractEnumValues(predictionDto, null, lifetimeWithPhase);
                    
                    // Apply localization: only return requested language version
                    ApplyLocalization(predictionDto, userLanguage);
                    
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
                        MultilingualLifetime = State.MultilingualYearly, // Return yearly multilingual
                        // Language status
                        AvailableLanguages = State.YearlyGeneratedLanguages ?? new List<string> { "en" },
                        AllLanguagesGenerated = State.YearlyGeneratedLanguages?.Count == 4
                    };
                    
                    // Extract enum values for frontend (from yearly forecast)
                    ExtractEnumValues(predictionDto, null, State.YearlyForecast);
                    
                    // Apply localization: only return requested language version
                    ApplyLocalization(predictionDto, userLanguage);

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
                        MultilingualResults = State.MultilingualResults,
                        // Language status
                        AvailableLanguages = State.DailyGeneratedLanguages ?? new List<string> { "en" },
                        AllLanguagesGenerated = State.DailyGeneratedLanguages?.Count == 4
                    };
                    
                    // Extract enum values for frontend (from daily results)
                    ExtractEnumValues(predictionDto, State.Results, null);
                    
                    // Apply localization: only return requested language version
                    ApplyLocalization(predictionDto, userLanguage);
                    
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
            _logger.LogInformation($"[PERF][Fortune] {userInfo.UserId} Cache_Miss - Generating new prediction, Type: {type}, Language: {userLanguage}");

            // Set generation lock
            if (!State.GenerationLocks.ContainsKey(type))
            {
                State.GenerationLocks[type] = new GenerationLockInfo();
            }
            State.GenerationLocks[type].IsGenerating = true;
            State.GenerationLocks[type].StartedAt = DateTime.UtcNow;
            
            try
            {
                var generateStopwatch = Stopwatch.StartNew();
                var predictionResult = await GeneratePredictionAsync(userInfo, today, type, userLanguage);
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
            finally
            {
                // Clear generation lock
                if (State.GenerationLocks.ContainsKey(type))
                {
                    State.GenerationLocks[type].IsGenerating = false;
                    State.GenerationLocks[type].StartedAt = null;
                    _logger.LogInformation($"[Fortune] {userInfo.UserId} GENERATION_LOCK_RELEASED - Type: {type}");
                }
            }
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
    /// Get prediction from state without generating (only returns requested language version)
    /// </summary>
    public Task<PredictionResultDto?> GetPredictionAsync(string userLanguage = "en")
    {
        if (State.PredictionId == Guid.Empty)
        {
            return Task.FromResult<PredictionResultDto?>(null);
        }

        // Determine prediction type based on which fields are populated
        // Each grain stores only one type: daily, yearly, or lifetime
        PredictionResultDto predictionDto;
        
        if (State.YearlyForecast != null && State.YearlyForecast.Count > 0)
        {
            // This is a Yearly prediction grain
            predictionDto = new PredictionResultDto
        {
            PredictionId = State.PredictionId,
            UserId = State.UserId,
            PredictionDate = State.PredictionDate,
                Results = new Dictionary<string, Dictionary<string, string>>(), // Yearly doesn't have daily results
                CreatedAt = State.CreatedAt,
                FromCache = true,
                LifetimeForecast = State.YearlyForecast, // Return yearly in LifetimeForecast field for API compatibility
                MultilingualLifetime = State.MultilingualYearly,
                AvailableLanguages = State.YearlyGeneratedLanguages ?? new List<string> { "en" },
                AllLanguagesGenerated = State.YearlyGeneratedLanguages?.Count == 4
            };
            
            // Extract enum values for frontend (from yearly forecast)
            ExtractEnumValues(predictionDto, null, State.YearlyForecast);
        }
        else if (State.LifetimeForecast != null && State.LifetimeForecast.Count > 0)
        {
            // This is a Lifetime prediction grain
            predictionDto = new PredictionResultDto
            {
                PredictionId = State.PredictionId,
                UserId = State.UserId,
                PredictionDate = State.PredictionDate,
                Results = new Dictionary<string, Dictionary<string, string>>(), // Lifetime doesn't have daily results
            CreatedAt = State.CreatedAt,
            FromCache = true,
            LifetimeForecast = State.LifetimeForecast,
                MultilingualLifetime = State.MultilingualLifetime,
                AvailableLanguages = State.LifetimeGeneratedLanguages ?? new List<string> { "en" },
                AllLanguagesGenerated = State.LifetimeGeneratedLanguages?.Count == 4
            };
            
            // Extract enum values for frontend (from lifetime forecast)
            ExtractEnumValues(predictionDto, null, State.LifetimeForecast);
        }
        else
        {
            // This is a Daily prediction grain
            predictionDto = new PredictionResultDto
            {
                PredictionId = State.PredictionId,
                UserId = State.UserId,
                PredictionDate = State.PredictionDate,
                Results = State.Results,
                CreatedAt = State.CreatedAt,
                FromCache = true,
                LifetimeForecast = new Dictionary<string, string>(), // Daily doesn't have lifetime/yearly
                MultilingualResults = State.MultilingualResults,
                MultilingualLifetime = new Dictionary<string, Dictionary<string, string>>(),
                AvailableLanguages = State.DailyGeneratedLanguages ?? new List<string> { "en" },
                AllLanguagesGenerated = State.DailyGeneratedLanguages?.Count == 4
            };
            
            // Extract enum values for frontend (from daily results)
            ExtractEnumValues(predictionDto, State.Results, null);
        }

        // Apply localization: only return requested language version
        ApplyLocalization(predictionDto, userLanguage);

        return Task.FromResult<PredictionResultDto?>(predictionDto);
    }

    /// <summary>
    /// Generate new prediction using AI
    /// </summary>
    private async Task<GetTodayPredictionResult> GeneratePredictionAsync(FortuneUserDto userInfo, DateOnly predictionDate, PredictionType type, string targetLanguage = "en")
    {
        try
        {
            // Build prompt
            var promptStopwatch = Stopwatch.StartNew();
            var prompt = BuildPredictionPrompt(userInfo, predictionDate, type, targetLanguage);
            promptStopwatch.Stop();
            _logger.LogInformation($"[PERF][Fortune] {userInfo.UserId} Prompt_Build: {promptStopwatch.ElapsedMilliseconds}ms, Length: {prompt.Length} chars");
            
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

            // ========== INJECT BACKEND-CALCULATED FIELDS ==========
            // Pre-calculate values once
            var currentYear = DateTime.UtcNow.Year;
            var birthYear = userInfo.BirthDate.Year;
            
            var sunSign = FortuneCalculator.CalculateZodiacSign(userInfo.BirthDate);
            var birthYearZodiac = FortuneCalculator.GetChineseZodiacWithElement(birthYear);
            var birthYearAnimal = FortuneCalculator.CalculateChineseZodiac(birthYear);
            var currentYearStems = FortuneCalculator.CalculateStemsAndBranches(currentYear);
            var pastCycle = FortuneCalculator.CalculateTenYearCycle(birthYear, -1);
            var currentCycle = FortuneCalculator.CalculateTenYearCycle(birthYear, 0);
            var futureCycle = FortuneCalculator.CalculateTenYearCycle(birthYear, 1);
            
            if (type == PredictionType.Lifetime)
            {
                // Inject into primary language (lifetimeForecast)
                if (lifetimeForecast != null)
                {
                    lifetimeForecast["chineseAstrology_currentYearStems"] = currentYearStems;
                    lifetimeForecast["sunSign_name"] = sunSign;
                    lifetimeForecast["westernOverview_sunSign"] = sunSign;
                    lifetimeForecast["chineseZodiac_animal"] = birthYearAnimal;
                    lifetimeForecast["pastCycle_ageRange"] = pastCycle.AgeRange;
                    lifetimeForecast["pastCycle_period"] = pastCycle.Period;
                    lifetimeForecast["currentCycle_ageRange"] = currentCycle.AgeRange;
                    lifetimeForecast["currentCycle_period"] = currentCycle.Period;
                    lifetimeForecast["futureCycle_ageRange"] = futureCycle.AgeRange;
                    lifetimeForecast["futureCycle_period"] = futureCycle.Period;
                }
                
                // Inject into all multilingual versions
                if (multilingualLifetime != null)
                {
                    foreach (var lang in multilingualLifetime.Keys)
                    {
                        multilingualLifetime[lang]["chineseAstrology_currentYearStems"] = currentYearStems;
                        multilingualLifetime[lang]["sunSign_name"] = sunSign;
                        multilingualLifetime[lang]["westernOverview_sunSign"] = sunSign;
                        multilingualLifetime[lang]["chineseZodiac_animal"] = birthYearAnimal;
                        multilingualLifetime[lang]["pastCycle_ageRange"] = pastCycle.AgeRange;
                        multilingualLifetime[lang]["pastCycle_period"] = pastCycle.Period;
                        multilingualLifetime[lang]["currentCycle_ageRange"] = currentCycle.AgeRange;
                        multilingualLifetime[lang]["currentCycle_period"] = currentCycle.Period;
                        multilingualLifetime[lang]["futureCycle_ageRange"] = futureCycle.AgeRange;
                        multilingualLifetime[lang]["futureCycle_period"] = futureCycle.Period;
                    }
                }
                
                _logger.LogInformation($"[Fortune] {userInfo.UserId} Injected backend-calculated fields into Lifetime prediction");
            }
            else if (type == PredictionType.Yearly)
            {
                var yearlyYear = predictionDate.Year;
                var yearlyYearZodiac = FortuneCalculator.GetChineseZodiacWithElement(yearlyYear);
                var yearlyTaishui = FortuneCalculator.CalculateTaishuiRelationship(birthYear, yearlyYear);
                var zodiacInfluence = $"{birthYearZodiac} native in {yearlyYearZodiac} year → {yearlyTaishui}";
                
                // Inject into primary language (yearlyForecast)
                if (yearlyForecast != null)
                {
                    yearlyForecast["zodiacInfluence"] = zodiacInfluence;
                }
                
                // Inject into all multilingual versions
                if (multilingualYearly != null)
                {
                    foreach (var lang in multilingualYearly.Keys)
                    {
                        multilingualYearly[lang]["zodiacInfluence"] = zodiacInfluence;
                    }
                }
                
                _logger.LogInformation($"[Fortune] {userInfo.UserId} Injected backend-calculated fields into Yearly prediction");
            }
            // Daily type: No backend-calculated fields to inject (all LLM-generated)

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
                MultilingualYearly = multilingualYearly,
                // Language generation tracking
                InitialLanguage = targetLanguage,
                PredictionTypeGenerated = type
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
                MultilingualLifetime = type == PredictionType.Yearly ? multilingualYearly : multilingualLifetime,
                // Language status (only target language is available initially)
                AvailableLanguages = new List<string> { targetLanguage },
                AllLanguagesGenerated = false // Will be true after async generation completes
            };
            
            // Extract enum values for frontend
            ExtractEnumValues(newPredictionDto, parsedResults, 
                type == PredictionType.Yearly ? yearlyForecast : lifetimeForecast);
            
            // Stage 2: Trigger async generation of remaining languages
            Dictionary<string, string>? sourceContent = null;
            switch (type)
            {
                case PredictionType.Daily:
                    if (multilingualResults != null && multilingualResults.Count > 0)
                    {
                        var dateKey = predictionDate.ToString("yyyy-MM-dd");
                        if (multilingualResults.ContainsKey(dateKey) && multilingualResults[dateKey].ContainsKey(targetLanguage))
                        {
                            sourceContent = multilingualResults[dateKey][targetLanguage];
                        }
                    }
                    break;
                case PredictionType.Yearly:
                    if (multilingualYearly != null && multilingualYearly.ContainsKey(targetLanguage))
                    {
                        sourceContent = multilingualYearly[targetLanguage];
                    }
                    break;
                case PredictionType.Lifetime:
                    if (multilingualLifetime != null && multilingualLifetime.ContainsKey(targetLanguage))
                    {
                        sourceContent = multilingualLifetime[targetLanguage];
                    }
                    break;
            }
            
            // Trigger async generation for remaining languages (non-blocking)
            if (sourceContent != null && sourceContent.Count > 0)
            {
                _logger.LogInformation($"[Fortune] {userInfo.UserId} Triggering async generation for remaining languages");
                
                // Fire and forget - run in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await GenerateRemainingLanguagesAsync(userInfo, predictionDate, type, targetLanguage, sourceContent);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"[Fortune] {userInfo.UserId} Background translation task failed");
                    }
                });
            }
            
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
    /// Build prediction prompt for AI (single language generation for first stage)
    /// </summary>
    private string BuildPredictionPrompt(FortuneUserDto userInfo, DateOnly predictionDate, PredictionType type, string targetLanguage = "en")
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
        
        // Calculate display name based on user language (for personalized greetings in predictions)
        // displayName is like fullName - it should NEVER be translated across languages
        var displayName = FortuneCalculator.GetDisplayName($"{userInfo.FirstName} {userInfo.LastName}", targetLanguage);

        string prompt = string.Empty;
        
        // ========== PRE-CALCULATE ACCURATE ASTROLOGICAL VALUES ==========
        var currentYear = DateTime.UtcNow.Year;
        var birthYear = userInfo.BirthDate.Year;
        
        // Western Zodiac
        var sunSign = FortuneCalculator.CalculateZodiacSign(userInfo.BirthDate);
        
        // Chinese Zodiac & Element
        var birthYearZodiac = FortuneCalculator.GetChineseZodiacWithElement(birthYear);
        var birthYearAnimal = FortuneCalculator.CalculateChineseZodiac(birthYear);
        var birthYearElement = FortuneCalculator.CalculateChineseElement(birthYear);
        
        var currentYearZodiac = FortuneCalculator.GetChineseZodiacWithElement(currentYear);
        var currentYearAnimal = FortuneCalculator.CalculateChineseZodiac(currentYear);
        var currentYearElement = FortuneCalculator.CalculateChineseElement(currentYear);
        
        // Heavenly Stems & Earthly Branches
        var currentYearStems = FortuneCalculator.CalculateStemsAndBranches(currentYear);
        var birthYearStems = FortuneCalculator.CalculateStemsAndBranches(birthYear);
        
        // Taishui Relationship
        var taishuiRelationship = FortuneCalculator.CalculateTaishuiRelationship(birthYear, currentYear);
        
        // Age & 10-year Cycles
        var currentAge = FortuneCalculator.CalculateAge(userInfo.BirthDate);
        var pastCycle = FortuneCalculator.CalculateTenYearCycle(birthYear, -1);
        var currentCycle = FortuneCalculator.CalculateTenYearCycle(birthYear, 0);
        var futureCycle = FortuneCalculator.CalculateTenYearCycle(birthYear, 1);
        
        // Language-specific instruction prefix (single language generation for first stage)
        var languageMap = new Dictionary<string, string>
        {
            { "en", "English" },
            { "zh-tw", "Traditional Chinese" },
            { "zh", "Simplified Chinese" },
            { "es", "Spanish" }
        };
        
        var languageName = languageMap.GetValueOrDefault(targetLanguage, "English");
        
        var singleLanguagePrefix = $@"You are a mystical diviner and life guide combining Eastern astrology (Bazi/Chinese Zodiac) and Western astrology (Sun/Moon/Rising). Provide insightful, warm, empowering guidance.

Generate prediction in {languageName} only.
EXCEPTIONS:
- chineseAstrology_currentYearStems always stays in Chinese+Pinyin with space separation (e.g., '乙 巳 Yi Si'), regardless of target language.
- For Chinese (zh-tw/zh): Properly adapt English grammar structures - convert possessives (""Sean's"" → ""Sean的""), remove/adapt articles (""The Star"" → ""星星""), use natural Chinese sentence order.
Wrap response in JSON format.

";
        
        if (type == PredictionType.Lifetime)
        {
            prompt = singleLanguagePrefix + $@"Generate lifetime profile for user.
User: {userInfoLine}
Current Year: {currentYear}

========== PRE-CALCULATED VALUES (Use these EXACT values, do NOT recalculate) ==========
Sun Sign: {sunSign}
Birth Year Zodiac: {birthYearZodiac}
Birth Year Element: {birthYearElement}
Current Year ({currentYear}): {currentYearZodiac}
Current Year Stems: {currentYearStems}
Past Cycle: {pastCycle.AgeRange} · {pastCycle.Period}
Current Cycle: {currentCycle.AgeRange} · {currentCycle.Period}
Future Cycle: {futureCycle.AgeRange} · {futureCycle.Period}

FORMAT (flattened - Backend will inject: zodiac, chineseZodiac, currentYearStems, cycle ages/periods):
{{
  ""predictions"": {{
    ""{targetLanguage}"": {{
      ""fourPillars_coreIdentity"": ""[12-18 words: Address by name, describe chart as fusion of elements]"", 
      ""fourPillars_coreIdentity_expanded"": ""[45-60 words: Use {sunSign} as Sun sign, define archetype, show contrasts using 'both...yet' patterns]"",
      ""chineseAstrology_currentYear"": ""Year of the {currentYearZodiac}"", 
      ""chineseAstrology_trait1"": ""[VARIED: 8-12 words interpretation]"", ""chineseAstrology_trait2"": ""[VARIED: 8-12 words]"", ""chineseAstrology_trait3"": ""[VARIED: 8-12 words]"", ""chineseAstrology_trait4"": ""[VARIED: 8-12 words]"",
      ""zodiacWhisper"": ""[VARIED: 40-50 words perspective on how {birthYearAnimal} enhances Western chart. Start '{birthYearAnimal} adds...' Use 'You are not only X, but Y']"",
      ""sunSign_tagline"": ""[VARIED: You [2-5 words poetic metaphor]]"",
      ""westernOverview_sunArchetype"": ""[Sun in {sunSign} - The [3-5 words archetype title]]"", ""westernOverview_sunDescription"": ""[18-25 words: Core traits using 'You']"",
      ""westernOverview_moonSign"": ""[Infer from context or use {sunSign}]"", ""westernOverview_moonArchetype"": ""[Moon in [sign] - The [3-5 words archetype title]]"", ""westernOverview_moonDescription"": ""[15-20 words: Emotional nature]"",
      ""westernOverview_risingSign"": ""[Use {sunSign} if no birth time]"", ""westernOverview_risingArchetype"": ""[Rising in [sign] - The [3-5 words archetype title]]"", ""westernOverview_risingDescription"": ""[20-28 words: How they meet world]"",
      ""combinedEssence"": ""[15-20 words: 'You think like [Sun], feel like [Moon], move through world like [Rising]']"",
      ""strengths_overview"": ""[VARIED: 10-15 words on growth path]"",
      ""strengths_item1_title"": ""[VARIED: 2-5 words]"", ""strengths_item1_description"": ""[VARIED: 15-25 words, attribute to specific sign combinations]"",
      ""strengths_item2_title"": ""[VARIED: 2-5 words]"", ""strengths_item2_description"": ""[VARIED: 12-18 words]"",
      ""strengths_item3_title"": ""[VARIED: 2-5 words]"", ""strengths_item3_description"": ""[VARIED: 12-18 words]"",
      ""challenges_overview"": ""[VARIED: 12-18 words starting 'Your power grows when...']"",
      ""challenges_item1_title"": ""[VARIED: 2-5 words]"", ""challenges_item1_description"": ""[VARIED: 8-15 words, frame using sign combinations]"",
      ""challenges_item2_title"": ""[VARIED: 2-5 words]"", ""challenges_item2_description"": ""[VARIED: 10-18 words]"",
      ""challenges_item3_title"": ""[VARIED: 2-5 words]"", ""challenges_item3_description"": ""[VARIED: 10-18 words]"",
      ""destiny_overview"": ""[VARIED: 20-30 words starting 'You are here to...', end with dual identity]"",
      ""destiny_path1_title"": ""[VARIED: 3-5 roles separated by /]"", ""destiny_path1_description"": ""[VARIED: 3-6 words]"",
      ""destiny_path2_title"": ""[VARIED: 3-5 roles separated by /]"", ""destiny_path2_description"": ""[VARIED: 5-10 words]"",
      ""destiny_path3_title"": ""[VARIED: 1-3 roles]"", ""destiny_path3_description"": ""[VARIED: 8-15 words]"",
      ""chineseZodiac_essence"": ""[Essence like {birthYearElement}]"",
      ""zodiacCycle_title"": ""[Zodiac Cycle Influence (YYYY-YYYY), calculate 20-year period from birth year]"", 
      ""zodiacCycle_cycleName"": ""[English name]"", ""zodiacCycle_cycleNameChinese"": ""[Chinese name]"",
      ""zodiacCycle_overview"": ""[50-65 words: State zodiac+element, describe 20-year cycle, how it affects Day Master]"",
      ""zodiacCycle_dayMasterPoint1"": ""[8-12 words]"", ""zodiacCycle_dayMasterPoint2"": ""[6-10 words]"", ""zodiacCycle_dayMasterPoint3"": ""[8-12 words]"", ""zodiacCycle_dayMasterPoint4"": ""[10-15 words]"",
      ""tenYearCycles_description"": ""[40-60 words: Fate Palace sector, element, what it represents, what it aligns them with]"",
      ""pastCycle_influenceSummary"": ""[8-12 words]"", ""pastCycle_meaning"": ""[60-80 words: Past tense, explain element/energy, reference Ten Gods if relevant]"",
      ""currentCycle_influenceSummary"": ""[8-12 words]"", ""currentCycle_meaning"": ""[60-80 words: Present tense, what it empowers, reference Ten Gods]"",
      ""futureCycle_influenceSummary"": ""[8-12 words]"", ""futureCycle_meaning"": ""[60-80 words: Future tense, opportunities/challenges]"",
      ""lifePlot_title"": ""[VARIED: You are a [10-20 words poetic archetype]]"", 
      ""lifePlot_chapter"": ""[VARIED: 30-50 words addressing by name, describe destiny uniquely]"",
      ""lifePlot_point1"": ""[VARIED: 5-15 words]"", ""lifePlot_point2"": ""[VARIED: 5-15 words]"", ""lifePlot_point3"": ""[VARIED: 5-15 words]"", ""lifePlot_point4"": ""[VARIED: 5-15 words, powerful identity statement]"",
      ""activationSteps_step1_title"": ""[VARIED: 2-5 words]"", ""activationSteps_step1_description"": ""[VARIED: 10-20 words actionable advice]"",
      ""activationSteps_step2_title"": ""[VARIED: 2-5 words]"", ""activationSteps_step2_description"": ""[VARIED: 10-20 words]"",
      ""activationSteps_step3_title"": ""[VARIED: 2-5 words]"", ""activationSteps_step3_description"": ""[VARIED: 10-20 words]"",
      ""activationSteps_step4_title"": ""[VARIED: 2-5 words]"", ""activationSteps_step4_description"": ""[VARIED: 10-20 words, most powerful]"",
      ""mantra_title"": ""[VARIED: 2-4 words]"", 
      ""mantra_point1"": ""[VARIED: 5-15 words using 'X as if...' pattern]"", ""mantra_point2"": ""[VARIED: 5-15 words]"", ""mantra_point3"": ""[VARIED: 5-15 words, most powerful]""
    }}
  }}
}}

RULES:
- All fields marked [VARIED] must generate FRESH content each time (descriptions, taglines, advice, archetypes, metaphors)
- Use 'both...yet' contrasts, 'You are here to...', 'Your power grows when...' patterns
- Use 'You/Your' extensively, warm tone, no special chars/emoji/line breaks";
        }
        else if (type == PredictionType.Yearly)
        {
            var yearlyYear = predictionDate.Year;
            var yearlyYearZodiac = FortuneCalculator.GetChineseZodiacWithElement(yearlyYear);
            var yearlyTaishui = FortuneCalculator.CalculateTaishuiRelationship(birthYear, yearlyYear);
            
            prompt = singleLanguagePrefix + $@"Generate yearly prediction for {yearlyYear}.
User: {userInfoLine}

========== PRE-CALCULATED VALUES (Use these EXACT values, do NOT recalculate) ==========
Sun Sign: {sunSign}
Birth Year Zodiac: {birthYearZodiac}
Yearly Year ({yearlyYear}): {yearlyYearZodiac}
Taishui Relationship: {yearlyTaishui}

FORMAT (flattened - Backend will inject: zodiacInfluence):
{{
  ""predictions"": {{
    ""{targetLanguage}"": {{
      ""westernAstroOverlay"": ""{sunSign} Sun · [2-3 word archetype] — {yearlyYear} [Key planetary transits based on {sunSign}]"",
      ""yearlyTheme_overallTheme"": ""[VARIED: 4-7 words using 'of' structure]"", 
      ""yearlyTheme_atAGlance"": ""[VARIED: 15-20 words on what both systems agree]"", 
      ""yearlyTheme_expanded"": ""[VARIED: 60-80 words in 3 paragraphs (double space): P1 combination/clash, P2 what it creates, P3 define year using 'not X but Y']"",
      ""divineInfluence_career_score"": [VARIED: 1-4 based on astrological analysis], ""divineInfluence_career_tagline"": ""[VARIED: 10-15 words starting 'Your superpower this year:']"", 
      ""divineInfluence_career_bestMoves"": [""[VARIED: 8-12 words actionable advice]"", ""[VARIED: 8-15 words]""], ""divineInfluence_career_avoid"": [""[VARIED: 3-6 specific activities, comma-separated. Examples: Job Hopping, Micromanaging, Overcommitting]"", ""[VARIED: 3-6 activities]""], 
      ""divineInfluence_career_inANutshell"": ""[VARIED: 50-70 words in 3 parts (double space): P1 formula, P2 how it feels, P3 meaning]"",
      ""divineInfluence_love_score"": [VARIED: 1-4], ""divineInfluence_love_tagline"": ""[VARIED: 10-15 words philosophical]"", 
      ""divineInfluence_love_bestMoves"": [""[VARIED: 6-10 words]"", ""[VARIED: 6-12 words]""], ""divineInfluence_love_avoid"": [""[VARIED: 3-6 behaviors, comma-separated. Examples: Jealousy, Past Baggage, Unrealistic Expectations]"", ""[VARIED: 3-6 behaviors]""], 
      ""divineInfluence_love_inANutshell"": ""[VARIED: 50-70 words in 3 parts (double space): P1 formula, P2 emotional state, P3 relationship needs]"",
      ""divineInfluence_wealth_score"": [VARIED: 1-4], ""divineInfluence_wealth_tagline"": ""[VARIED: 10-15 words]"", 
      ""divineInfluence_wealth_bestMoves"": [""[VARIED: 8-12 words]"", ""[VARIED: 8-15 words]""], ""divineInfluence_wealth_avoid"": [""[VARIED: 3-6 actions, comma-separated. Examples: Gambling, Impulse Purchases, High-Risk Loans]"", ""[VARIED: 3-6 actions]""], 
      ""divineInfluence_wealth_inANutshell"": ""[VARIED: 50-70 words in 3 parts (double space): P1 formula, P2 climate, P3 prosperity needs]"",
      ""divineInfluence_health_score"": [VARIED: 1-4], ""divineInfluence_health_tagline"": ""[VARIED: 10-15 words]"", 
      ""divineInfluence_health_bestMoves"": [""[VARIED: 8-12 words]"", ""[VARIED: 8-15 words]""], ""divineInfluence_health_avoid"": [""[VARIED: 3-6 habits, comma-separated. Examples: Late Nights, Junk Food, Ignoring Symptoms]"", ""[VARIED: 3-6 habits]""], 
      ""divineInfluence_health_inANutshell"": ""[VARIED: 50-70 words in 3 parts (double space): P1 formula, P2 state, P3 wellness needs]"",
      ""embodimentMantra"": ""[VARIED: 18-25 words using first-person 'My' declarations, 2-3 powerful statements, poetic and rhythmic]""
    }}
  }}
}}

RULES:
- All fields marked [VARIED] must generate FRESH content (themes, scores, taglines, advice, mantras)
- Scores: 1=challenging, 2=mixed, 3=favorable, 4=excellent (VARY based on astrological analysis)
- inANutshell: Use formula pattern ('X + Y = Z.'), then state, then meaning
- Career tagline starts 'Your superpower this year:', others philosophical
- Avoid fields: 3-6 specific, actionable nouns (not sentences)
- Use 'You/Your' (except embodimentMantra uses 'My'), warm tone, no special chars/emoji, use double space not line breaks";
        }
        else // PredictionType.Daily
        {
            // Determine user's zodiac element for personalized recommendations
            var zodiacElement = sunSign switch
            {
                "Aries" or "Leo" or "Sagittarius" => "Fire",
                "Taurus" or "Virgo" or "Capricorn" => "Earth",
                "Gemini" or "Libra" or "Aquarius" => "Air",
                "Cancer" or "Scorpio" or "Pisces" => "Water",
                _ => "Fire"
            };
            
            prompt = singleLanguagePrefix + $@"Generate daily prediction for {predictionDate:yyyy-MM-dd}.
User: {userInfoLine}

========== PRE-CALCULATED VALUES (Use for personalization) ==========
Display Name: {displayName} (Use this in greetings and personalized messages. NEVER translate this name.)
Sun Sign: {sunSign}
Zodiac Element: {zodiacElement}
Birth Year Zodiac: {birthYearZodiac}
Chinese Element: {birthYearElement}

FORMAT (flattened):
{{
  ""predictions"": {{
    ""{targetLanguage}"": {{
      ""dayTitle"": ""[VARIED: The Day of [word1] and [word2] - choose words reflecting today's unique energy]"",
      ""todaysReading_tarotCard_name"": ""[VARIED: Select DIFFERENT card for THIS user. Consider their Sun sign ({sunSign}), element ({zodiacElement}), and today's energy. Choose from full 78-card deck - Major/Minor Arcana. DO NOT use same card for all users]"", ""todaysReading_tarotCard_represents"": ""[1-2 words essence]"", ""todaysReading_tarotCard_orientation"": ""[VARIED: Upright/Reversed reflecting THIS user's individual life phase. Consider their {sunSign} nature]"",
      ""todaysReading_pathTitle"": ""{displayName}'s Path Today - A [VARIED Adjective] Path"",
      ""todaysReading_pathDescription"": ""[VARIED: 15-25 words greeting, describe UNIQUE energy for this user. Start 'Hi {displayName}']"", ""todaysReading_pathDescriptionExpanded"": ""[VARIED: 30-40 words offering FRESH wisdom and actionable guidance]"",
      ""todaysReading_careerAndWork"": ""[VARIED: 10-20 words]"", ""todaysReading_loveAndRelationships"": ""[VARIED: 10-20 words]"", 
      ""todaysReading_wealthAndFinance"": ""[VARIED: 10-20 words]"", ""todaysReading_healthAndWellness"": ""[VARIED: 10-15 words]"",
      ""todaysTakeaway"": ""[VARIED: 15-25 words starting '{displayName}, your...' with contrast/cause-effect pattern]"",
      ""luckyAlignments_luckyNumber_number"": ""[VARIED: Generate different number for each user, 1-9. Word (digit) format, e.g., Seven (7)]"", ""luckyAlignments_luckyNumber_digit"": ""[VARIED: 1-9, ensure variety across users]"", 
      ""luckyAlignments_luckyNumber_description"": ""[VARIED: 15-20 words on what THIS number means for THIS user today]"",
      ""luckyAlignments_luckyNumber_calculation"": ""[VARIED: 12-18 words formula example combining today's date with birth numerology, make it look authentic]"",
      ""luckyAlignments_luckyStone"": ""[VARIED: Select DIFFERENT stone for THIS user's element ({zodiacElement}). MUST vary by element: Fire→Carnelian/Ruby/Garnet, Earth→Jade/Emerald/Moss Agate, Air→Citrine/Aquamarine/Clear Quartz, Water→Moonstone/Pearl/Lapis Lazuli. Choose specific stone based on {sunSign} + today's energy needs. DO NOT use same stone for all {zodiacElement} users]"", ""luckyAlignments_luckyStone_description"": ""[VARIED: 15-20 words on how THIS {zodiacElement}-element stone helps THIS user today]"",
      ""luckyAlignments_luckyStone_guidance"": ""[VARIED: 15-20 words starting 'Meditate:' or 'Practice:', SPECIFIC ritual for this user]"",
      ""luckyAlignments_luckySpell"": ""[VARIED: 2 words poetic name]"", ""luckyAlignments_luckySpell_description"": ""[VARIED: 20-30 words in quote format, first-person affirmation]"",
      ""luckyAlignments_luckySpell_intent"": ""[VARIED: 10-12 words starting 'To [verb]...']"",
      ""twistOfFate_favorable"": [""[VARIED: EXACTLY 5 activities, 2-3 words each, concrete actions suited to this user today. Examples: Take walk, Meditate quietly, Organize workspace, Text friend, Drink water]"", ""[VARIED: EXACTLY 5 different activities. Examples: Read books, Cook meal, Early sleep, Journal thoughts, Call family]""], 
      ""twistOfFate_avoid"": [""[VARIED: EXACTLY 5 activities to avoid, 2-3 words each, specific actions. Examples: Buy stocks, Argue unnecessarily, Overshare secrets, Start plans, Skip meals]"", ""[VARIED: EXACTLY 5 different activities. Examples: Impulse shopping, Late nights, Heavy drinking, Risky decisions, Lend money]""], 
      ""twistOfFate_todaysRecommendation"": ""[VARIED: 10-15 words starting 'Today's turning point lies in...']""
    }}
  }}
}}

KEY RULES - PERSONALIZATION AND VARIETY:
[MUST PERSONALIZE - Based on User Profile]:
- Tarot Card: Select DIFFERENT card for each user based on:
  * Their Sun sign ({sunSign}) and element ({zodiacElement})
  * Today's energy and their current life phase
  * Choose from full 78-card deck (Major + Minor Arcana)
  * Orientation (Upright/Reversed) varies by individual
  
- Lucky Stone: Select stone matching user's element ({zodiacElement}):
  * Fire signs → Choose from Carnelian, Ruby, Garnet, Red Jasper
  * Earth signs → Choose from Jade, Emerald, Moss Agate, Tiger's Eye
  * Air signs → Choose from Citrine, Aquamarine, Clear Quartz, Amethyst
  * Water signs → Choose from Moonstone, Pearl, Lapis Lazuli, Blue Lace Agate
  * IMPORTANT: Pick DIFFERENT stones for different users, even within same element
  
- Lucky Number: Generate VARIED numbers (1-9) for different users. Don't default to same number.

[MUST VARY - All Creative Content]:
- All descriptions, advice, recommendations: Generate NEW perspectives each time
- dayTitle, pathDescription, takeaway, spell, favorable/avoid lists: Must be unique and fresh
- Each user should feel content is specifically tailored to THEM

- pathDescription starts 'Hi {displayName}', pathDescriptionExpanded offers deeper wisdom
- todaysTakeaway uses contrast patterns ('not X but Y', 'the more X, the Y')
- Twist of Fate: EXACTLY 5 activities per list, 2-3 words each, concrete actionable behaviors (not sentences)
- Use 'You/Your' extensively, warm tone, no special chars/emoji/line breaks";            
        }

        return prompt;
    }

    /// <summary>
    /// Build translation prompt for remaining languages (second stage)
    /// </summary>
    private string BuildTranslationPrompt(Dictionary<string, string> sourceContent, string sourceLanguage, List<string> targetLanguages, PredictionType type)
    {
        var languageMap = new Dictionary<string, string>
        {
            { "en", "English" },
            { "zh-tw", "Traditional Chinese" },
            { "zh", "Simplified Chinese" },
            { "es", "Spanish" }
        };
        
        var sourceLangName = languageMap.GetValueOrDefault(sourceLanguage, "English");
        var targetLangNames = string.Join(", ", targetLanguages.Select(lang => languageMap.GetValueOrDefault(lang, lang)));
        
        // Serialize source content to JSON
        var sourceJson = JsonConvert.SerializeObject(sourceContent, Formatting.Indented);
        
        var translationPrompt = $@"You are a professional translator specializing in astrology and divination content.

TASK: Translate the following {type} prediction from {sourceLangName} into {targetLangNames}.

CRITICAL RULES:
1. TRANSLATE - do NOT regenerate or reinterpret. Keep the exact same meaning and content structure.
2. NEVER TRANSLATE user names - keep them exactly as they appear (e.g., ""Sean"" stays ""Sean"" in all languages)
   - In possessives: ""Sean's Path"" → ""Sean的道路"" (keep name, only translate structure)
3. PRESERVE these fields in Chinese+Pinyin regardless of target language:
   - chineseAstrology_currentYearStems (e.g., '乙 巳 Yi Si')
   - pastCycle_period, currentCycle_period, futureCycle_period (e.g., '甲子 (Jiǎzǐ)')
4. Maintain natural, fluent expression in each target language (not word-for-word).
5. Keep all field names unchanged.
6. Preserve all numbers, dates, and proper nouns.
7. For Chinese translations (zh-tw, zh): Properly adapt English grammar:
   - Articles: Remove or adapt ""The/A"" naturally (e.g., ""The Star"" → ""星星"")
   - Sentence structure: Adjust to natural Chinese word order
8. Output format: {{""predictions"": {{""zh-tw"": {{...}}, ""zh"": {{...}}, ""es"": {{...}}}}}}

SOURCE CONTENT ({sourceLangName}):
{sourceJson}

Generate translations for: {targetLangNames}
Output in JSON format with 'predictions' object containing each target language.
";

        return translationPrompt;
    }

    /// <summary>
    /// Generate remaining languages asynchronously (second stage)
    /// </summary>
    private async Task GenerateRemainingLanguagesAsync(FortuneUserDto userInfo, DateOnly predictionDate, PredictionType type, string sourceLanguage, Dictionary<string, string> sourceContent)
    {
        try
        {
            var allLanguages = new List<string> { "en", "zh-tw", "zh", "es" };
            var remainingLanguages = allLanguages.Where(lang => lang != sourceLanguage).ToList();
            
            if (remainingLanguages.Count == 0)
            {
                _logger.LogInformation($"[Fortune][AsyncTranslation] {userInfo.UserId} No remaining languages to generate for {type}");
                return;
            }
            
            _logger.LogInformation($"[Fortune][AsyncTranslation] {userInfo.UserId} START - Type: {type}, Source: {sourceLanguage}, Targets: {string.Join(", ", remainingLanguages)}");
            
            // Build translation prompt
            var translationPrompt = BuildTranslationPrompt(sourceContent, sourceLanguage, remainingLanguages, type);
            
            // Call LLM for translation
            var llmStopwatch = Stopwatch.StartNew();
            var userGuid = CommonHelper.StringToGuid(userInfo.UserId);
            var godChat = _clusterClient.GetGrain<IGodChat>(userGuid);
            var chatId = Guid.NewGuid().ToString();
            
            var response = await godChat.ChatWithoutHistoryAsync(
                userGuid,
                chatId,
                translationPrompt,
                "FORTUNE");
            llmStopwatch.Stop();
            _logger.LogInformation($"[Fortune][AsyncTranslation] {userInfo.UserId} LLM_Call: {llmStopwatch.ElapsedMilliseconds}ms");
            
            if (response == null || response.Count() == 0)
            {
                _logger.LogWarning($"[Fortune][AsyncTranslation] {userInfo.UserId} No response from LLM");
                return;
            }
            
            var aiResponse = response[0].Content;
            _logger.LogDebug($"[Fortune][AsyncTranslation] {userInfo.UserId} Raw LLM Response Length: {aiResponse?.Length ?? 0} chars");
            
            // Parse response
            var parseStopwatch = Stopwatch.StartNew();
            
            // Extract JSON from response
            string jsonContent = aiResponse;
            var codeBlockMatch = System.Text.RegularExpressions.Regex.Match(aiResponse, @"```(?:json)?\s*([\s\S]*?)\s*```");
            if (codeBlockMatch.Success)
            {
                jsonContent = codeBlockMatch.Groups[1].Value.Trim();
                _logger.LogDebug($"[Fortune][AsyncTranslation] {userInfo.UserId} Extracted from code block");
            }
            var firstBrace = jsonContent.IndexOf('{');
            var lastBrace = jsonContent.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                jsonContent = jsonContent.Substring(firstBrace, lastBrace - firstBrace + 1);
            }
            jsonContent = jsonContent.Trim();
            
            // Validate jsonContent before parsing
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                _logger.LogError($"[Fortune][AsyncTranslation] {userInfo.UserId} Empty JSON content after extraction. Raw response: {aiResponse}");
                return;
            }
            
            if (!jsonContent.StartsWith("{") || !jsonContent.EndsWith("}"))
            {
                _logger.LogError($"[Fortune][AsyncTranslation] {userInfo.UserId} Invalid JSON format. Content starts with: {jsonContent.Substring(0, Math.Min(100, jsonContent.Length))}");
                return;
            }
            
            _logger.LogDebug($"[Fortune][AsyncTranslation] {userInfo.UserId} Attempting to parse JSON of length: {jsonContent.Length}");
            
            Dictionary<string, object>? parsedResponse = null;
            try
            {
                parsedResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, $"[Fortune][AsyncTranslation] {userInfo.UserId} JSON parsing failed. First 500 chars: {jsonContent.Substring(0, Math.Min(500, jsonContent.Length))}");
                return;
            }
            
            if (parsedResponse == null || !parsedResponse.ContainsKey("predictions"))
            {
                var keys = parsedResponse != null ? string.Join(", ", parsedResponse.Keys) : "null";
                _logger.LogWarning($"[Fortune][AsyncTranslation] {userInfo.UserId} Invalid response format - missing 'predictions' key. Keys: {keys}");
                return;
            }
            
            var predictionsObj = parsedResponse["predictions"];
            var translatedLanguages = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(
                JsonConvert.SerializeObject(predictionsObj));
            
            parseStopwatch.Stop();
            _logger.LogInformation($"[Fortune][AsyncTranslation] {userInfo.UserId} Parse_Response: {parseStopwatch.ElapsedMilliseconds}ms");
            
            // Raise event to update state with translated languages
            RaiseEvent(new LanguagesTranslatedEvent
            {
                Type = type,
                PredictionDate = predictionDate,
                TranslatedLanguages = translatedLanguages,
                AllGeneratedLanguages = allLanguages
            });
            
            // Confirm events to persist state changes
            await ConfirmEvents();
            
            _logger.LogInformation($"[Fortune][AsyncTranslation] {userInfo.UserId} SUCCESS - Generated {translatedLanguages.Count} languages for {type}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[Fortune][AsyncTranslation] {userInfo.UserId} Error generating remaining languages for {type}");
        }
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

            Dictionary<string, object>? fullResponse = null;
            try
            {
                fullResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "[FortunePredictionGAgent][ParseMultilingualDailyResponse] JSON parse error. First 500 chars: {JsonPreview}", 
                    jsonContent.Length > 500 ? jsonContent.Substring(0, 500) : jsonContent);
                _logger.LogError("[FortunePredictionGAgent][ParseMultilingualDailyResponse] Last 200 chars: {JsonEnd}", 
                    jsonContent.Length > 200 ? jsonContent.Substring(jsonContent.Length - 200) : jsonContent);
                return (null, null);
            }
            
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

            Dictionary<string, object>? fullResponse = null;
            try
            {
                fullResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "[FortunePredictionGAgent][ParseMultilingualLifetimeResponse] JSON parse error. First 500 chars: {JsonPreview}", 
                    jsonContent.Length > 500 ? jsonContent.Substring(0, 500) : jsonContent);
                _logger.LogError("[FortunePredictionGAgent][ParseMultilingualLifetimeResponse] Last 200 chars: {JsonEnd}", 
                    jsonContent.Length > 200 ? jsonContent.Substring(jsonContent.Length - 200) : jsonContent);
                return (null, null);
            }
            
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
    
    /// <summary>
    /// Apply localization: only keep requested language version, remove other languages
    /// </summary>
    private void ApplyLocalization(PredictionResultDto prediction, string userLanguage)
    {
        // Apply localization to daily results
        if (prediction.MultilingualResults != null && prediction.MultilingualResults.Count > 0)
        {
            if (prediction.MultilingualResults.TryGetValue(userLanguage, out var localizedResults))
            {
                prediction.Results = localizedResults;
            }
            else if (prediction.MultilingualResults.TryGetValue("en", out var englishResults))
            {
                // Fallback to English if requested language not available
                prediction.Results = englishResults;
                _logger.LogWarning("[FortunePredictionGAgent][ApplyLocalization] Language {UserLanguage} not found, using English fallback",
                    userLanguage);
            }
            
            // Clear multilingual field to avoid returning all languages
            prediction.MultilingualResults = null;
        }
        
        // Apply localization to lifetime/yearly forecast
        if (prediction.MultilingualLifetime != null && prediction.MultilingualLifetime.Count > 0)
        {
            if (prediction.MultilingualLifetime.TryGetValue(userLanguage, out var localizedLifetime))
            {
                prediction.LifetimeForecast = localizedLifetime;
            }
            else if (prediction.MultilingualLifetime.TryGetValue("en", out var englishLifetime))
            {
                // Fallback to English
                prediction.LifetimeForecast = englishLifetime;
                _logger.LogWarning("[FortunePredictionGAgent][ApplyLocalization] Lifetime/Yearly {UserLanguage} not found, using English fallback",
                    userLanguage);
            }
            
            // Clear multilingual field to avoid returning all languages
            prediction.MultilingualLifetime = null;
        }
    }
}

