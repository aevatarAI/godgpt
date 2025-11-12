using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Lumen.Dtos;
using Aevatar.Application.Grains.Lumen.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Options;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Orleans.Concurrency;
using System.Diagnostics;

namespace Aevatar.Application.Grains.Lumen;

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
/// Interface for Lumen Prediction GAgent - manages lumen prediction generation
/// </summary>
public interface ILumenPredictionGAgent : IGAgent
{
    Task<GetTodayPredictionResult> GetOrGeneratePredictionAsync(LumenUserDto userInfo, PredictionType type = PredictionType.Daily, string userLanguage = "en");
    
    [ReadOnly]
    Task<PredictionResultDto?> GetPredictionAsync(string userLanguage = "en");
    
    [ReadOnly]
    Task<PredictionStatusDto?> GetPredictionStatusAsync(DateTime? profileUpdatedAt = null);
}

[GAgent(nameof(LumenPredictionGAgent))]
[Reentrant]
public class LumenPredictionGAgent : GAgentBase<LumenPredictionState, LumenPredictionEventLog>, 
    ILumenPredictionGAgent
{
    private readonly ILogger<LumenPredictionGAgent> _logger;
    private readonly IClusterClient _clusterClient;

    public LumenPredictionGAgent(
        ILogger<LumenPredictionGAgent> logger,
        IClusterClient clusterClient)
    {
        _logger = logger;
        _clusterClient = clusterClient;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Lumen prediction generation and caching");
    }

    /// <summary>
    /// Event-driven state transition handler
    /// </summary>
    protected sealed override void GAgentTransitionState(LumenPredictionState state,
        StateLogEventBase<LumenPredictionEventLog> @event)
    {
        switch (@event)
        {
            case PredictionGeneratedEvent generatedEvent:
                state.PredictionId = generatedEvent.PredictionId;
                state.UserId = generatedEvent.UserId;
                state.PredictionDate = generatedEvent.PredictionDate;
                state.CreatedAt = generatedEvent.CreatedAt;
                state.ProfileUpdatedAt = generatedEvent.ProfileUpdatedAt;
                state.Type = generatedEvent.Type;
                
                // Store flattened results
                state.Results = generatedEvent.Results;
                
                // Store multilingual results
                if (generatedEvent.MultilingualResults != null)
                {
                    state.MultilingualResults = generatedEvent.MultilingualResults;
                }
                
                // Initialize language generation status
                if (!string.IsNullOrEmpty(generatedEvent.InitialLanguage))
                {
                    state.GeneratedLanguages = new List<string> { generatedEvent.InitialLanguage };
                }
                break;
                
            case LanguagesTranslatedEvent translatedEvent:
                // Update multilingual cache with translated languages
                if (translatedEvent.TranslatedLanguages != null)
                {
                    foreach (var lang in translatedEvent.TranslatedLanguages)
                    {
                        state.MultilingualResults[lang.Key] = lang.Value;
                    }
                }
                
                // Update generated languages list
                state.GeneratedLanguages = translatedEvent.AllGeneratedLanguages;
                break;
        }
    }

    public async Task<GetTodayPredictionResult> GetOrGeneratePredictionAsync(LumenUserDto userInfo, PredictionType type = PredictionType.Daily, string userLanguage = "en")
    {
        var totalStopwatch = Stopwatch.StartNew();
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var currentYear = today.Year;
            
            _logger.LogInformation($"[PERF][Lumen] {userInfo.UserId} START - Type: {type}, Date: {today}, Language: {userLanguage}");
            
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
                        _logger.LogWarning($"[Lumen] {userInfo.UserId} GENERATION_IN_PROGRESS - Type: {type}, StartedAt: {lockInfo.StartedAt}, Elapsed: {elapsed.TotalSeconds:F1}s");
                        
                        return new GetTodayPredictionResult
                        {
                            Success = false,
                            Message = $"{type} prediction is currently being generated. Please wait a moment and try again."
                        };
                    }
                    else
                    {
                        // Generation timed out (service restart or actual timeout), reset lock and retry
                        _logger.LogWarning($"[Lumen] {userInfo.UserId} GENERATION_TIMEOUT - Type: {type}, StartedAt: {lockInfo.StartedAt}, Elapsed: {elapsed.TotalMinutes:F2} minutes, Resetting lock and retrying");
                        lockInfo.IsGenerating = false;
                        lockInfo.StartedAt = null;
                    }
                }
            }
                
                // Check if profile has been updated since prediction was generated
                var profileNotChanged = !State.ProfileUpdatedAt.HasValue || userInfo.UpdatedAt <= State.ProfileUpdatedAt.Value;
                
            // Check if prediction already exists (from cache/state)
            var hasCachedPrediction = State.PredictionId != Guid.Empty && 
                                     !State.Results.IsNullOrEmpty() && 
                                     State.Type == type;
            
            // Check expiration based on type
            bool notExpired = type switch
            {
                PredictionType.Lifetime => true, // Lifetime never expires
                PredictionType.Yearly => State.PredictionDate.Year == currentYear, // Yearly expires after 1 year
                PredictionType.Daily => State.PredictionDate == today, // Daily expires every day
                _ => false
            };
            
            if (hasCachedPrediction && notExpired && profileNotChanged)
            {
                // Return cached prediction
                totalStopwatch.Stop();
                _logger.LogInformation($"[PERF][Lumen] {userInfo.UserId} Cache_Hit: {totalStopwatch.ElapsedMilliseconds}ms - Type: {type}");
                
                // Get localized results
                Dictionary<string, string> localizedResults;
                string returnedLanguage;
                bool isFallback;
                
                if (State.MultilingualResults.ContainsKey(userLanguage))
                {
                    localizedResults = State.MultilingualResults[userLanguage];
                    returnedLanguage = userLanguage;
                    isFallback = false;
                }
                else if (State.MultilingualResults.ContainsKey("en"))
                {
                    localizedResults = State.MultilingualResults["en"];
                    returnedLanguage = "en";
                    isFallback = true;
                }
                else
                {
                    localizedResults = State.Results;
                    returnedLanguage = "en";
                    isFallback = userLanguage != "en";
                }
                
                // Add currentPhase for Lifetime predictions
                if (type == PredictionType.Lifetime)
                {
                    var currentPhase = CalculateCurrentPhase(userInfo.BirthDate);
                    localizedResults = new Dictionary<string, string>(localizedResults);
                    localizedResults["currentPhase"] = currentPhase.ToString();
                }
                
                var cachedDto = new PredictionResultDto
                {
                    PredictionId = State.PredictionId,
                    UserId = State.UserId,
                    PredictionDate = State.PredictionDate,
                    CreatedAt = State.CreatedAt,
                    FromCache = true,
                    Type = State.Type,
                    Results = localizedResults,
                    AvailableLanguages = State.GeneratedLanguages ?? new List<string> { "en" },
                    AllLanguagesGenerated = State.GeneratedLanguages?.Count == 4,
                    RequestedLanguage = userLanguage,
                    ReturnedLanguage = returnedLanguage,
                    IsFallback = isFallback,
                    Feedbacks = null
                };
                
                return new GetTodayPredictionResult
                {
                    Success = true,
                    Message = string.Empty,
                    Prediction = cachedDto
                };
            }
            
            // Log reason for regeneration
            if (hasCachedPrediction && !notExpired)
            {
                _logger.LogInformation("[LumenPredictionGAgent][GetOrGeneratePredictionAsync] {Type} expired, regenerating for {UserId}", 
                    type, userInfo.UserId);
            }
            if (hasCachedPrediction && !profileNotChanged)
            {
                _logger.LogInformation("[LumenPredictionGAgent][GetOrGeneratePredictionAsync] Profile updated, regenerating {Type} prediction for {UserId}",
                    type, userInfo.UserId);
            }

            // Generate new prediction
            _logger.LogInformation($"[PERF][Lumen] {userInfo.UserId} Cache_Miss - Generating new prediction, Type: {type}, Language: {userLanguage}");

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
                    _logger.LogWarning($"[PERF][Lumen] {userInfo.UserId} Generation_Failed: {generateStopwatch.ElapsedMilliseconds}ms, TOTAL: {totalStopwatch.ElapsedMilliseconds}ms");
                return predictionResult;
            }

                _logger.LogInformation($"[PERF][Lumen] {userInfo.UserId} Generation_Success: {generateStopwatch.ElapsedMilliseconds}ms, TOTAL: {totalStopwatch.ElapsedMilliseconds}ms - Type: {type}");
            return predictionResult;
            }
            finally
            {
                // Clear generation lock
                if (State.GenerationLocks.ContainsKey(type))
                {
                    State.GenerationLocks[type].IsGenerating = false;
                    State.GenerationLocks[type].StartedAt = null;
                    _logger.LogInformation($"[Lumen] {userInfo.UserId} GENERATION_LOCK_RELEASED - Type: {type}");
                }
            }
        }
        catch (Exception ex)
        {
            totalStopwatch.Stop();
            _logger.LogError(ex, $"[PERF][Lumen] {userInfo.UserId} Error: {totalStopwatch.ElapsedMilliseconds}ms - Exception in GetOrGeneratePredictionAsync");
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

        // Get localized results based on user language
        Dictionary<string, string> localizedResults;
        string returnedLanguage;
        bool isFallback;
        
        if (State.MultilingualResults.ContainsKey(userLanguage))
        {
            // Requested language is available
            localizedResults = State.MultilingualResults[userLanguage];
            returnedLanguage = userLanguage;
            isFallback = false;
        }
        else if (State.MultilingualResults.ContainsKey("en"))
        {
            // Fallback to English
            localizedResults = State.MultilingualResults["en"];
            returnedLanguage = "en";
            isFallback = true;
            _logger.LogWarning("[LumenPredictionGAgent][GetPredictionAsync] Language {UserLanguage} not found, using English fallback", userLanguage);
        }
        else
        {
            // Use default results if multilingual not available
            localizedResults = State.Results;
            returnedLanguage = "en"; // Assume default is English
            isFallback = userLanguage != "en";
        }

        var predictionDto = new PredictionResultDto
        {
            PredictionId = State.PredictionId,
            UserId = State.UserId,
            PredictionDate = State.PredictionDate,
            CreatedAt = State.CreatedAt,
            FromCache = true,
            Type = State.Type,
            Results = localizedResults,
            AvailableLanguages = State.GeneratedLanguages ?? new List<string> { "en" },
            AllLanguagesGenerated = State.GeneratedLanguages?.Count == 4,
            RequestedLanguage = userLanguage,
            ReturnedLanguage = returnedLanguage,
            IsFallback = isFallback,
            Feedbacks = null
        };

        return Task.FromResult<PredictionResultDto?>(predictionDto);
    }

    /// <summary>
    /// Get prediction generation status
    /// </summary>
    public Task<PredictionStatusDto?> GetPredictionStatusAsync(DateTime? profileUpdatedAt = null)
    {
        // If no prediction has been generated yet, return a status indicating "never generated"
        if (State.PredictionId == Guid.Empty)
        {
            // Check if currently generating for the first time
            var isGenerating = false;
            DateTime? generationStartedAt = null;
            if (State.GenerationLocks.TryGetValue(State.Type, out var lockInfo))
            {
                isGenerating = lockInfo.IsGenerating;
                generationStartedAt = lockInfo.StartedAt;
                
                // Check for stale lock (>1 minute) and reset
                if (isGenerating && lockInfo.StartedAt.HasValue && 
                    (DateTime.UtcNow - lockInfo.StartedAt.Value).TotalMinutes > 1)
                {
                    isGenerating = false;
                    generationStartedAt = null;
                }
            }
            
            return Task.FromResult<PredictionStatusDto?>(new PredictionStatusDto
            {
                Type = State.Type,
                IsGenerated = false,
                IsGenerating = isGenerating,
                GeneratedAt = null,
                GenerationStartedAt = generationStartedAt,
                PredictionDate = null,
                AvailableLanguages = new List<string>(),
                NeedsRegeneration = true, // Always needs generation if never generated
                TranslationStatus = null
            });
        }

        // Check if currently generating
        var isGenerating2 = false;
        DateTime? generationStartedAt2 = null;
        if (State.GenerationLocks.TryGetValue(State.Type, out var lockInfo2))
        {
            isGenerating2 = lockInfo2.IsGenerating;
            generationStartedAt2 = lockInfo2.StartedAt;
            
            // Check for stale lock (>1 minute) and reset
            if (isGenerating2 && lockInfo2.StartedAt.HasValue && 
                (DateTime.UtcNow - lockInfo2.StartedAt.Value).TotalMinutes > 1)
            {
                isGenerating2 = false;
                generationStartedAt2 = null;
            }
        }

        // Check if needs regeneration (profile was updated after prediction was generated)
        var needsRegeneration = false;
        if (profileUpdatedAt.HasValue && State.ProfileUpdatedAt.HasValue)
        {
            needsRegeneration = profileUpdatedAt.Value > State.ProfileUpdatedAt.Value;
        }

        // For Daily predictions, also check if prediction is for today
        if (State.Type == PredictionType.Daily)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (State.PredictionDate != today)
            {
                needsRegeneration = true;
            }
        }

        // Build translation status if translating
        TranslationStatusInfo? translationStatus = null;
        if (isGenerating2 && generationStartedAt2.HasValue)
        {
            var allLanguages = new List<string> { "en", "zh-tw", "zh", "es" };
            var availableLanguages = State.GeneratedLanguages ?? new List<string>();
            var targetLanguages = allLanguages.Where(lang => !availableLanguages.Contains(lang)).ToList();
            
            translationStatus = new TranslationStatusInfo
            {
                IsTranslating = true,
                StartedAt = generationStartedAt2.Value,
                TargetLanguages = targetLanguages,
                EstimatedCompletion = generationStartedAt2.Value.AddSeconds(30) // Estimate 30 seconds for translation
            };
        }

        var statusDto = new PredictionStatusDto
        {
            Type = State.Type,
            IsGenerated = true,
            IsGenerating = isGenerating2,
            GeneratedAt = State.CreatedAt,
            GenerationStartedAt = generationStartedAt2,
            PredictionDate = State.PredictionDate,
            AvailableLanguages = State.GeneratedLanguages ?? new List<string>(),
            NeedsRegeneration = needsRegeneration,
            TranslationStatus = translationStatus
        };

        return Task.FromResult<PredictionStatusDto?>(statusDto);
    }

    /// <summary>
    /// Generate new prediction using AI
    /// </summary>
    private async Task<GetTodayPredictionResult> GeneratePredictionAsync(LumenUserDto userInfo, DateOnly predictionDate, PredictionType type, string targetLanguage = "en")
    {
        try
        {
            // Build prompt
            var promptStopwatch = Stopwatch.StartNew();
            var prompt = BuildPredictionPrompt(userInfo, predictionDate, type, targetLanguage);
            promptStopwatch.Stop();
            _logger.LogInformation($"[PERF][Lumen] {userInfo.UserId} Prompt_Build: {promptStopwatch.ElapsedMilliseconds}ms, Length: {prompt.Length} chars");
            
            var userGuid = CommonHelper.StringToGuid(userInfo.UserId);
            
            // Use deterministic grain key based on userId + predictionType
            // This enables concurrent LLM calls for different prediction types (daily/yearly/lifetime)
            // while keeping grain count minimal (3 grains per user)
            // Format: userId_daily, userId_yearly, userId_lifetime
            var predictionGrainKey = CommonHelper.StringToGuid($"{userInfo.UserId}_{type.ToString().ToLower()}");
            var godChat = _clusterClient.GetGrain<IGodChat>(predictionGrainKey);
            var chatId = Guid.NewGuid().ToString();

            var settings = new ExecutionPromptSettings
            {
                Temperature = "0.7"
            };

            // Use dedicated "LUMEN" region for independent LLM configuration
            // This allows Lumen to use cost-optimized models (e.g., GPT-4o-mini)
            // separate from the main chat experience
            var llmStopwatch = Stopwatch.StartNew();
            var response = await godChat.ChatWithoutHistoryAsync(
                userGuid, 
                string.Empty, 
                prompt, 
                chatId, 
                settings, 
                true, 
                "LUMEN");
            llmStopwatch.Stop();
            _logger.LogInformation($"[PERF][Lumen] {userInfo.UserId} LLM_Call: {llmStopwatch.ElapsedMilliseconds}ms - Type: {type}");

            if (response == null || response.Count() == 0)
            {
                _logger.LogWarning("[LumenPredictionGAgent][GeneratePredictionAsync] No response from AI for user {UserId}",
                    userInfo.UserId);
                return new GetTodayPredictionResult
                {
                    Success = false,
                    Message = "AI service returned no response"
                };
            }

            var aiResponse = response[0].Content;
            _logger.LogInformation($"[PERF][Lumen] {userInfo.UserId} LLM_Response: {aiResponse.Length} chars");

            // Unified flat results structure (all types use same format now)
            Dictionary<string, string>? parsedResults = null;
            Dictionary<string, Dictionary<string, string>>? multilingualResults = null;

            // Parse AI response based on type (returns flattened structure)
            var parseStopwatch = Stopwatch.StartNew();
            (parsedResults, multilingualResults) = type switch
            {
                PredictionType.Lifetime => ParseMultilingualLifetimeResponse(aiResponse),
                PredictionType.Yearly => ParseMultilingualLifetimeResponse(aiResponse), // Yearly uses same parser
                PredictionType.Daily => ParseMultilingualDailyResponse(aiResponse),
                _ => throw new ArgumentException($"Unsupported prediction type: {type}")
            };
            
            if (parsedResults == null)
            {
                _logger.LogError("[LumenPredictionGAgent][GeneratePredictionAsync] Failed to parse {Type} response", type);
                return new GetTodayPredictionResult
                {
                    Success = false,
                    Message = "Failed to parse AI response"
                };
            }
            
            parseStopwatch.Stop();
            _logger.LogInformation($"[PERF][Lumen] {userInfo.UserId} Parse_Response: {parseStopwatch.ElapsedMilliseconds}ms - Type: {type}");

            // ========== INJECT BACKEND-CALCULATED FIELDS ==========
            // Pre-calculate values once
            var currentYear = DateTime.UtcNow.Year;
            var birthYear = userInfo.BirthDate.Year;
            
            var sunSign = LumenCalculator.CalculateZodiacSign(userInfo.BirthDate);
            var birthYearZodiac = LumenCalculator.GetChineseZodiacWithElement(birthYear);
            var birthYearAnimal = LumenCalculator.CalculateChineseZodiac(birthYear);
            var currentYearStems = LumenCalculator.CalculateStemsAndBranches(currentYear);
            var pastCycle = LumenCalculator.CalculateTenYearCycle(birthYear, -1);
            var currentCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 0);
            var futureCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 1);
            
            if (type == PredictionType.Lifetime)
            {
                // Calculate Four Pillars (Ba Zi)
                var fourPillars = LumenCalculator.CalculateFourPillars(userInfo.BirthDate, userInfo.BirthTime);
                
                // Inject into primary language results
                parsedResults["chineseAstrology_currentYearStems"] = currentYearStems;
                parsedResults["sunSign_name"] = sunSign;
                parsedResults["sunSign_enum"] = ((int)LumenCalculator.ParseZodiacSignEnum(sunSign)).ToString();
                parsedResults["westernOverview_sunSign"] = sunSign;
                parsedResults["chineseZodiac_animal"] = birthYearAnimal;
                parsedResults["chineseZodiac_enum"] = ((int)LumenCalculator.ParseChineseZodiacEnum(birthYearAnimal)).ToString();
                parsedResults["chineseZodiac_title"] = $"The {birthYearAnimal.Split(' ').Last()}";
                parsedResults["pastCycle_ageRange"] = pastCycle.AgeRange;
                parsedResults["pastCycle_period"] = pastCycle.Period;
                parsedResults["currentCycle_ageRange"] = currentCycle.AgeRange;
                parsedResults["currentCycle_period"] = currentCycle.Period;
                parsedResults["futureCycle_ageRange"] = futureCycle.AgeRange;
                parsedResults["futureCycle_period"] = futureCycle.Period;
                
                // Inject Four Pillars data
                InjectFourPillarsData(parsedResults, fourPillars, targetLanguage);
                
                // Inject into all multilingual versions
                if (multilingualResults != null)
                {
                    foreach (var lang in multilingualResults.Keys)
                    {
                        multilingualResults[lang]["chineseAstrology_currentYearStems"] = currentYearStems;
                        multilingualResults[lang]["sunSign_name"] = sunSign;
                        multilingualResults[lang]["sunSign_enum"] = ((int)LumenCalculator.ParseZodiacSignEnum(sunSign)).ToString();
                        multilingualResults[lang]["westernOverview_sunSign"] = sunSign;
                        multilingualResults[lang]["chineseZodiac_animal"] = birthYearAnimal;
                        multilingualResults[lang]["chineseZodiac_enum"] = ((int)LumenCalculator.ParseChineseZodiacEnum(birthYearAnimal)).ToString();
                        multilingualResults[lang]["chineseZodiac_title"] = TranslateZodiacTitle(birthYearAnimal, lang);
                        multilingualResults[lang]["pastCycle_ageRange"] = pastCycle.AgeRange;
                        multilingualResults[lang]["pastCycle_period"] = pastCycle.Period;
                        multilingualResults[lang]["currentCycle_ageRange"] = currentCycle.AgeRange;
                        multilingualResults[lang]["currentCycle_period"] = currentCycle.Period;
                        multilingualResults[lang]["futureCycle_ageRange"] = futureCycle.AgeRange;
                        multilingualResults[lang]["futureCycle_period"] = futureCycle.Period;
                        
                        // Inject Four Pillars data with language-specific formatting
                        InjectFourPillarsData(multilingualResults[lang], fourPillars, lang);
                    }
                }
                
                _logger.LogInformation($"[Lumen] {userInfo.UserId} Injected backend-calculated fields into Lifetime prediction");
            }
            else if (type == PredictionType.Yearly)
            {
                var yearlyYear = predictionDate.Year;
                var yearlyYearZodiac = LumenCalculator.GetChineseZodiacWithElement(yearlyYear);
                var yearlyTaishui = LumenCalculator.CalculateTaishuiRelationship(birthYear, yearlyYear);
                
                // Inject into primary language results
                parsedResults["sunSign_name"] = sunSign;
                parsedResults["sunSign_enum"] = ((int)LumenCalculator.ParseZodiacSignEnum(sunSign)).ToString();
                parsedResults["chineseZodiac_animal"] = birthYearAnimal;
                parsedResults["chineseZodiac_enum"] = ((int)LumenCalculator.ParseChineseZodiacEnum(birthYearAnimal)).ToString();
                parsedResults["chineseAstrology_currentYearStems"] = currentYearStems;
                parsedResults["chineseAstrology_taishuiRelationship"] = yearlyTaishui;
                parsedResults["zodiacInfluence"] = $"{birthYearZodiac} native in {yearlyYearZodiac} year → {yearlyTaishui}";
                
                // Inject into all multilingual versions
                if (multilingualResults != null)
                {
                    foreach (var lang in multilingualResults.Keys)
                    {
                        multilingualResults[lang]["sunSign_name"] = sunSign;
                        multilingualResults[lang]["sunSign_enum"] = ((int)LumenCalculator.ParseZodiacSignEnum(sunSign)).ToString();
                        multilingualResults[lang]["chineseZodiac_animal"] = birthYearAnimal;
                        multilingualResults[lang]["chineseZodiac_enum"] = ((int)LumenCalculator.ParseChineseZodiacEnum(birthYearAnimal)).ToString();
                        multilingualResults[lang]["chineseAstrology_currentYearStems"] = currentYearStems;
                        multilingualResults[lang]["chineseAstrology_taishuiRelationship"] = yearlyTaishui;
                        multilingualResults[lang]["zodiacInfluence"] = $"{birthYearZodiac} native in {yearlyYearZodiac} year → {yearlyTaishui}";
                    }
                }
                
                _logger.LogInformation($"[Lumen] {userInfo.UserId} Injected backend-calculated fields into Yearly prediction");
            }
            // Daily type: Enum fields are already extracted by parser during flattening

            var predictionId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            // Add currentPhase for lifetime predictions
            if (type == PredictionType.Lifetime)
            {
                var currentPhase = CalculateCurrentPhase(userInfo.BirthDate);
                parsedResults["currentPhase"] = currentPhase.ToString();
                
                if (multilingualResults != null)
                {
                    foreach (var lang in multilingualResults.Keys)
                    {
                        multilingualResults[lang]["currentPhase"] = currentPhase.ToString();
                    }
                }
            }

            // Raise event to save prediction (unified structure)
            RaiseEvent(new PredictionGeneratedEvent
            {
                PredictionId = predictionId,
                UserId = userInfo.UserId,
                PredictionDate = predictionDate,
                CreatedAt = now,
                ProfileUpdatedAt = userInfo.UpdatedAt,
                Type = type,
                Results = parsedResults,
                MultilingualResults = multilingualResults,
                InitialLanguage = targetLanguage
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[LumenPredictionGAgent][GeneratePredictionAsync] {Type} prediction generated successfully for user {UserId}",
                type, userInfo.UserId);

            // Build return DTO
            var newPredictionDto = new PredictionResultDto
            {
                PredictionId = predictionId,
                UserId = userInfo.UserId,
                PredictionDate = predictionDate,
                CreatedAt = now,
                FromCache = false,
                Type = type,
                Results = parsedResults,
                AvailableLanguages = new List<string> { targetLanguage },
                AllLanguagesGenerated = false, // Will be true after async generation completes
                RequestedLanguage = targetLanguage,
                ReturnedLanguage = targetLanguage,
                IsFallback = false, // First generation always returns the requested language
                Feedbacks = null
            };
            
            // Stage 2: Trigger async generation of remaining languages
            if (multilingualResults != null && multilingualResults.ContainsKey(targetLanguage))
            {
                var sourceContent = multilingualResults[targetLanguage];
                
                _logger.LogInformation($"[Lumen] {userInfo.UserId} Triggering async generation for remaining languages");
                
                // Fire and forget - run in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await GenerateRemainingLanguagesAsync(userInfo, predictionDate, type, targetLanguage, sourceContent);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"[Lumen] {userInfo.UserId} Background translation task failed");
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
            _logger.LogError(ex, "[LumenPredictionGAgent][GeneratePredictionAsync] Error in AI generation");
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
    private string BuildPredictionPrompt(LumenUserDto userInfo, DateOnly predictionDate, PredictionType type, string targetLanguage = "en")
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
        
        // Current residence (optional)
        if (!string.IsNullOrWhiteSpace(userInfo.CurrentResidence))
        {
            userInfoParts.Add($"Current Residence: {userInfo.CurrentResidence}");
        }
        
        // Occupation (optional)
        if (!string.IsNullOrWhiteSpace(userInfo.Occupation))
        {
            userInfoParts.Add($"Occupation: {userInfo.Occupation}");
        }
        
        var userInfoLine = string.Join(", ", userInfoParts);
        
        // Calculate display name based on user language (for personalized greetings in predictions)
        // displayName is like fullName - it should NEVER be translated across languages
        var displayName = LumenCalculator.GetDisplayName($"{userInfo.FirstName} {userInfo.LastName}", targetLanguage);

        string prompt = string.Empty;
        
        // ========== PRE-CALCULATE ACCURATE ASTROLOGICAL VALUES ==========
        var currentYear = DateTime.UtcNow.Year;
        var birthYear = userInfo.BirthDate.Year;
        
        // Western Zodiac
        var sunSign = LumenCalculator.CalculateZodiacSign(userInfo.BirthDate);
        
        // Chinese Zodiac & Element
        var birthYearZodiac = LumenCalculator.GetChineseZodiacWithElement(birthYear);
        var birthYearAnimal = LumenCalculator.CalculateChineseZodiac(birthYear);
        var birthYearElement = LumenCalculator.CalculateChineseElement(birthYear);
        
        var currentYearZodiac = LumenCalculator.GetChineseZodiacWithElement(currentYear);
        var currentYearAnimal = LumenCalculator.CalculateChineseZodiac(currentYear);
        var currentYearElement = LumenCalculator.CalculateChineseElement(currentYear);
        
        // Heavenly Stems & Earthly Branches
        var currentYearStems = LumenCalculator.CalculateStemsAndBranches(currentYear);
        var birthYearStems = LumenCalculator.CalculateStemsAndBranches(birthYear);
        
        // Taishui Relationship
        var taishuiRelationship = LumenCalculator.CalculateTaishuiRelationship(birthYear, currentYear);
        
        // Age & 10-year Cycles
        var currentAge = LumenCalculator.CalculateAge(userInfo.BirthDate);
        var pastCycle = LumenCalculator.CalculateTenYearCycle(birthYear, -1);
        var currentCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 0);
        var futureCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 1);
        
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
Birth Year Zodiac (USER'S ZODIAC): {birthYearZodiac}
Birth Year Animal (USER'S ANIMAL): {birthYearAnimal}
Birth Year Element: {birthYearElement}
Current Year ({currentYear}): {currentYearZodiac}
Current Year Stems: {currentYearStems}
Past Cycle: {pastCycle.AgeRange} · {pastCycle.Period}
Current Cycle: {currentCycle.AgeRange} · {currentCycle.Period}
Future Cycle: {futureCycle.AgeRange} · {futureCycle.Period}

IMPORTANT: All Chinese Zodiac content must be based on USER'S Birth Year Zodiac ({birthYearZodiac}), NOT the current year zodiac ({currentYearZodiac}).

FORMAT (flattened - Backend will inject: chineseZodiac_title, chineseZodiac_animal, sunSign_name, currentYearStems, cycle ages/periods, fourPillars details):
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
      ""zodiacCycle_overview"": ""[50-65 words: Start with 'Your Chinese Zodiac is {birthYearAnimal}...' Explain the current 20-year cycle (e.g., 2024-2043) and how this period's energy influences the user's life. Reference both the user's zodiac and the cycle's characteristics.]"",
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
            var yearlyYearZodiac = LumenCalculator.GetChineseZodiacWithElement(yearlyYear);
            var yearlyTaishui = LumenCalculator.CalculateTaishuiRelationship(birthYear, yearlyYear);
            
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
      ""divineInfluence_career_score"": [VARIED: 1-5 based on astrological analysis], ""divineInfluence_career_tagline"": ""[VARIED: 10-15 words starting 'Your superpower this year:']"", 
      ""divineInfluence_career_bestMoves"": [""[VARIED: 8-12 words actionable advice]"", ""[VARIED: 8-15 words]""], ""divineInfluence_career_avoid"": [""[VARIED: 3-6 specific activities, comma-separated. Examples: Job Hopping, Micromanaging, Overcommitting]"", ""[VARIED: 3-6 activities]""], 
      ""divineInfluence_career_inANutshell"": ""[VARIED: 50-70 words in 3 parts (double space): P1 formula, P2 how it feels, P3 meaning]"",
      ""divineInfluence_love_score"": [VARIED: 1-5], ""divineInfluence_love_tagline"": ""[VARIED: 10-15 words philosophical]"", 
      ""divineInfluence_love_bestMoves"": [""[VARIED: 6-10 words]"", ""[VARIED: 6-12 words]""], ""divineInfluence_love_avoid"": [""[VARIED: 3-6 behaviors, comma-separated. Examples: Jealousy, Past Baggage, Unrealistic Expectations]"", ""[VARIED: 3-6 behaviors]""], 
      ""divineInfluence_love_inANutshell"": ""[VARIED: 50-70 words in 3 parts (double space): P1 formula, P2 emotional state, P3 relationship needs]"",
      ""divineInfluence_wealth_score"": [VARIED: 1-5], ""divineInfluence_wealth_tagline"": ""[VARIED: 10-15 words]"", 
      ""divineInfluence_wealth_bestMoves"": [""[VARIED: 8-12 words]"", ""[VARIED: 8-15 words]""], ""divineInfluence_wealth_avoid"": [""[VARIED: 3-6 actions, comma-separated. Examples: Gambling, Impulse Purchases, High-Risk Loans]"", ""[VARIED: 3-6 actions]""], 
      ""divineInfluence_wealth_inANutshell"": ""[VARIED: 50-70 words in 3 parts (double space): P1 formula, P2 climate, P3 prosperity needs]"",
      ""divineInfluence_health_score"": [VARIED: 1-5], ""divineInfluence_health_tagline"": ""[VARIED: 10-15 words]"", 
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
      ""luckyAlignments_luckyNumber_number"": ""[VARIED: Generate different number for each user, 1-9. MUST use 'Word (digit)' format: English→'Seven (7)', Spanish→'Siete (7)', Chinese→'七（7）' with Chinese parentheses（）]"", ""luckyAlignments_luckyNumber_digit"": ""[VARIED: 1-9, ensure variety across users]"", 
      ""luckyAlignments_luckyNumber_description"": ""[VARIED: 15-20 words on what THIS number means for THIS user today]"",
      ""luckyAlignments_luckyNumber_calculation"": ""[VARIED: 12-18 words formula example combining today's date with birth numerology, make it look authentic]"",
      ""luckyAlignments_luckyStone"": ""[VARIED: Select DIFFERENT stone for THIS user's element ({zodiacElement}). MUST vary by element: Fire→Carnelian/Ruby/Garnet, Earth→Jade/Emerald/Moss Agate, Air→Citrine/Aquamarine/Clear Quartz, Water→Moonstone/Pearl/Lapis Lazuli. Choose specific stone based on {sunSign} + today's energy needs. DO NOT use same stone for all {zodiacElement} users]"", ""luckyAlignments_luckyStone_description"": ""[VARIED: 15-20 words on how THIS {zodiacElement}-element stone helps THIS user today]"",
      ""luckyAlignments_luckyStone_guidance"": ""[VARIED: 15-20 words starting 'Meditate:' or 'Practice:', SPECIFIC ritual for this user]"",
      ""luckyAlignments_luckySpell"": ""[VARIED: 2 words poetic name]"", ""luckyAlignments_luckySpell_description"": ""[VARIED: 20-30 words in quote format, first-person affirmation]"",
      ""luckyAlignments_luckySpell_intent"": ""[VARIED: 10-12 words starting 'To [verb]...']"",
      ""twistOfFate_favorable"": [""[VARIED: EXACTLY 5 activities, 2-3 words each, concrete actions suited to this user today. Examples: Take walk, Meditate quietly, Organize workspace, Text friend, Drink water]"", ""[VARIED: EXACTLY 5 different activities. Examples: Read books, Cook meal, Early sleep, Journal thoughts, Call family]""], 
      ""twistOfFate_avoid"": [""[VARIED: EXACTLY 5 activities to avoid, 2-3 words each, specific actions. Examples: Buy stocks, Argue unnecessarily, Overshare secrets, Start plans, Skip meals]"", ""[VARIED: EXACTLY 5 different activities. Examples: Impulse shopping, Late nights, Heavy drinking, Risky decisions, Lend money]""], 
      ""twistOfFate_todaysRecommendation"": ""[VARIED: 10-15 words, direct statement about today's focus. NO prefixes like 'Today's turning point' - just the core message. Examples: 'Connections, collaboration, and the exchange of ideas', 'Patience, reflection, and inner peace']""
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
4. TRANSLATE luckyNumber format correctly:
   - English: ""Seven (7)"" - word + space + English parentheses ()
   - Spanish: ""Siete (7)"" - word + space + English parentheses ()
   - Chinese: ""七（7）"" - word + NO space + Chinese full-width parentheses （）
5. Maintain natural, fluent expression in each target language (not word-for-word).
6. Keep all field names unchanged.
7. Preserve all numbers, dates, and proper nouns.
8. For Chinese translations (zh-tw, zh): Properly adapt English grammar:
   - Articles: Remove or adapt ""The/A"" naturally (e.g., ""The Star"" → ""星星"")
   - Sentence structure: Adjust to natural Chinese word order

OUTPUT FORMAT REQUIREMENTS:
- ALL field values MUST be strings, NEVER arrays or objects
- If a field contains multiple items, join them with commas into a SINGLE string
  Example: CORRECT: ""patience, courage, wisdom""
           WRONG: [""patience"", ""courage"", ""wisdom""]
- Structure: {{""predictions"": {{""zh-tw"": {{...}}, ""zh"": {{...}}, ""es"": {{...}}}}}}
- Every field value must be a simple string type

SOURCE CONTENT ({sourceLangName}):
{sourceJson}

Generate translations for: {targetLangNames}
Output ONLY valid JSON with all values as strings. No arrays, no nested objects in field values.
";

        return translationPrompt;
    }

    /// <summary>
    /// Generate remaining languages asynchronously (second stage) - CONCURRENT translation for better performance
    /// </summary>
    private async Task GenerateRemainingLanguagesAsync(LumenUserDto userInfo, DateOnly predictionDate, PredictionType type, string sourceLanguage, Dictionary<string, string> sourceContent)
    {
        try
        {
            var allLanguages = new List<string> { "en", "zh-tw", "zh", "es" };
            var remainingLanguages = allLanguages.Where(lang => lang != sourceLanguage).ToList();
            
            if (remainingLanguages.Count == 0)
            {
                _logger.LogInformation($"[Lumen][AsyncTranslation] {userInfo.UserId} No remaining languages to generate for {type}");
                return;
            }
            
            _logger.LogInformation($"[Lumen][AsyncTranslation] {userInfo.UserId} START - Type: {type}, Source: {sourceLanguage}, Targets: {string.Join(", ", remainingLanguages)}");
            
            // ========== OPTIMIZATION: Translate each language separately ==========
            // Each translation uses the same grain (userId + type), so they will be serialized
            // This keeps grain count minimal (3 per user) while reducing prompt size per call
            // Old: 1 LLM call for 3 languages → 42+ seconds (massive output)
            // New: 3 separate LLM calls → serialized by grain (still faster due to smaller output per call)
            var translationTasks = remainingLanguages.Select(targetLang => 
                TranslateSingleLanguageAsync(userInfo, predictionDate, type, sourceLanguage, sourceContent, targetLang)
            ).ToList();
            
            await Task.WhenAll(translationTasks);
            
            _logger.LogInformation($"[Lumen][AsyncTranslation] {userInfo.UserId} SUCCESS - All {remainingLanguages.Count} languages translated for {type}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[Lumen][AsyncTranslation] {userInfo.UserId} Error generating remaining languages for {type}");
        }
    }

    /// <summary>
    /// Translate to a single language (used for concurrent translation)
    /// </summary>
    private async Task TranslateSingleLanguageAsync(LumenUserDto userInfo, DateOnly predictionDate, PredictionType type, string sourceLanguage, Dictionary<string, string> sourceContent, string targetLanguage)
    {
        try
        {
            _logger.LogInformation($"[Lumen][AsyncTranslation] {userInfo.UserId} Translating {sourceLanguage} → {targetLanguage} for {type}");
            
            // Build single-language translation prompt
            var translationPrompt = BuildSingleLanguageTranslationPrompt(sourceContent, sourceLanguage, targetLanguage, type);
            
            // Call LLM for translation
            var llmStopwatch = Stopwatch.StartNew();
            var userGuid = CommonHelper.StringToGuid(userInfo.UserId);
            
            // Use the same grain key as prediction generation (userId + type)
            // Translation for different languages will be serialized within the same grain
            // This keeps grain count minimal (3 grains per user)
            var translationGrainKey = CommonHelper.StringToGuid($"{userInfo.UserId}_{type.ToString().ToLower()}");
            var godChat = _clusterClient.GetGrain<IGodChat>(translationGrainKey);
            var chatId = Guid.NewGuid().ToString();
            
            var response = await godChat.ChatWithoutHistoryAsync(
                userGuid,
                chatId,
                translationPrompt,
                "LUMEN");
            llmStopwatch.Stop();
            _logger.LogInformation($"[Lumen][AsyncTranslation] {userInfo.UserId} {targetLanguage} LLM_Call: {llmStopwatch.ElapsedMilliseconds}ms");
            
            if (response == null || response.Count() == 0)
            {
                _logger.LogWarning($"[Lumen][AsyncTranslation] {userInfo.UserId} {targetLanguage} No response from LLM");
                return;
            }
            
            var aiResponse = response[0].Content;
            
            // Parse response
            var parseStopwatch = Stopwatch.StartNew();
            
            // Extract JSON from response
            string jsonContent = aiResponse;
            var codeBlockMatch = System.Text.RegularExpressions.Regex.Match(aiResponse, @"```(?:json)?\s*([\s\S]*?)\s*```");
            if (codeBlockMatch.Success)
            {
                jsonContent = codeBlockMatch.Groups[1].Value.Trim();
            }
            var firstBrace = jsonContent.IndexOf('{');
            var lastBrace = jsonContent.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                jsonContent = jsonContent.Substring(firstBrace, lastBrace - firstBrace + 1);
            }
            jsonContent = jsonContent.Trim();
            
            // Validate jsonContent
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                _logger.LogError($"[Lumen][AsyncTranslation] {userInfo.UserId} {targetLanguage} Empty JSON content");
                return;
            }
            
            if (!jsonContent.StartsWith("{") || !jsonContent.EndsWith("}"))
            {
                _logger.LogError($"[Lumen][AsyncTranslation] {userInfo.UserId} {targetLanguage} Invalid JSON format");
                return;
            }
            
            // Parse with fault tolerance for array values
            var contentDict = new Dictionary<string, string>();
            var contentFields = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
            
            if (contentFields != null)
            {
                foreach (var fieldKvp in contentFields)
                {
                    var fieldName = fieldKvp.Key;
                    var fieldValue = fieldKvp.Value;
                    
                    // Handle different value types
                    if (fieldValue is Newtonsoft.Json.Linq.JArray arrayValue)
                    {
                        // Convert array to comma-separated string
                        var items = arrayValue.Select(item => item.ToString()).ToArray();
                        contentDict[fieldName] = string.Join(", ", items);
                        _logger.LogWarning($"[Lumen][AsyncTranslation] {userInfo.UserId} {targetLanguage}.{fieldName} was array, converted to string");
                    }
                    else if (fieldValue != null)
                    {
                        contentDict[fieldName] = fieldValue.ToString();
                    }
                    else
                    {
                        contentDict[fieldName] = string.Empty;
                    }
                }
            }
            
            parseStopwatch.Stop();
            _logger.LogInformation($"[Lumen][AsyncTranslation] {userInfo.UserId} {targetLanguage} Parse: {parseStopwatch.ElapsedMilliseconds}ms, Fields: {contentDict.Count}");
            
            // Raise event to update state with this language
            var translatedLanguages = new Dictionary<string, Dictionary<string, string>>
            {
                { targetLanguage, contentDict }
            };
            
            var allLanguages = new List<string> { "en", "zh-tw", "zh", "es" };
            var updatedLanguages = (State.GeneratedLanguages ?? new List<string>()).Union(new[] { targetLanguage }).ToList();
            
            RaiseEvent(new LanguagesTranslatedEvent
            {
                Type = type,
                PredictionDate = predictionDate,
                TranslatedLanguages = translatedLanguages,
                AllGeneratedLanguages = updatedLanguages
            });
            
            // Confirm events to persist state changes
            await ConfirmEvents();
            
            _logger.LogInformation($"[Lumen][AsyncTranslation] {userInfo.UserId} {targetLanguage} COMPLETED - Total: {llmStopwatch.ElapsedMilliseconds + parseStopwatch.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[Lumen][AsyncTranslation] {userInfo.UserId} Error translating to {targetLanguage} for {type}");
        }
    }

    /// <summary>
    /// Build single-language translation prompt (for concurrent translation)
    /// </summary>
    private string BuildSingleLanguageTranslationPrompt(Dictionary<string, string> sourceContent, string sourceLanguage, string targetLanguage, PredictionType type)
    {
        var languageMap = new Dictionary<string, string>
        {
            { "en", "English" },
            { "zh-tw", "Traditional Chinese" },
            { "zh", "Simplified Chinese" },
            { "es", "Spanish" }
        };
        
        var sourceLangName = languageMap.GetValueOrDefault(sourceLanguage, "English");
        var targetLangName = languageMap.GetValueOrDefault(targetLanguage, targetLanguage);
        
        // Serialize source content to JSON
        var sourceJson = JsonConvert.SerializeObject(sourceContent, Formatting.Indented);
        
        var translationPrompt = $@"You are a professional translator specializing in astrology and divination content.

TASK: Translate the following {type} prediction from {sourceLangName} into {targetLangName}.

CRITICAL RULES:
1. TRANSLATE - do NOT regenerate or reinterpret. Keep the exact same meaning and content structure.
2. NEVER TRANSLATE user names - keep them exactly as they appear (e.g., ""Sean"" stays ""Sean"")
   - In possessives: ""Sean's Path"" → ""Sean的道路"" (keep name, only translate structure)
3. PRESERVE these fields in Chinese+Pinyin regardless of target language:
   - chineseAstrology_currentYearStems (e.g., '乙 巳 Yi Si')
   - pastCycle_period, currentCycle_period, futureCycle_period (e.g., '甲子 (Jiǎzǐ)')
4. TRANSLATE luckyNumber format correctly:
   - English: ""Seven (7)"" - word + space + English parentheses ()
   - Spanish: ""Siete (7)"" - word + space + English parentheses ()
   - Chinese: ""七（7）"" - word + NO space + Chinese full-width parentheses （）
5. Maintain natural, fluent expression in {targetLangName} (not word-for-word).
6. Keep all field names unchanged.
7. Preserve all numbers, dates, and proper nouns.
8. For Chinese translations (zh-tw, zh): Properly adapt English grammar:
   - Articles: Remove or adapt ""The/A"" naturally (e.g., ""The Star"" → ""星星"")
   - Sentence structure: Adjust to natural Chinese word order

OUTPUT FORMAT REQUIREMENTS:
- ALL field values MUST be strings, NEVER arrays or objects
- If a field contains multiple items, join them with commas into a SINGLE string
  Example: CORRECT: ""patience, courage, wisdom""
           WRONG: [""patience"", ""courage"", ""wisdom""]
- Output a flat JSON object with all translated fields
- Every field value must be a simple string type

SOURCE CONTENT ({sourceLangName}):
{sourceJson}

Output ONLY valid JSON with all values as strings. No arrays, no nested objects in field values.
";

        return translationPrompt;
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
            _logger.LogError(ex, "[LumenPredictionGAgent][ParseLifetimeWeeklyResponse] Failed to parse");
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
            _logger.LogError(ex, "[LumenPredictionGAgent][ParseDailyResponse] Failed to parse");
            return null;
        }
    }
    /// <summary>
    /// Parse multilingual daily response from AI
    /// Returns (default English results, multilingual dictionary)
    /// </summary>
    private (Dictionary<string, string>?, Dictionary<string, Dictionary<string, string>>?) ParseMultilingualDailyResponse(string aiResponse)
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
            
            _logger.LogDebug("[LumenPredictionGAgent][ParseMultilingualDailyResponse] Extracted JSON length: {Length}", jsonContent.Length);

            Dictionary<string, object>? fullResponse = null;
            try
            {
                fullResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "[LumenPredictionGAgent][ParseMultilingualDailyResponse] JSON parse error. First 500 chars: {JsonPreview}", 
                    jsonContent.Length > 500 ? jsonContent.Substring(0, 500) : jsonContent);
                _logger.LogError("[LumenPredictionGAgent][ParseMultilingualDailyResponse] Last 200 chars: {JsonEnd}", 
                    jsonContent.Length > 200 ? jsonContent.Substring(jsonContent.Length - 200) : jsonContent);
                return (null, null);
            }
            
            if (fullResponse == null)
            {
                _logger.LogWarning("[LumenPredictionGAgent][ParseMultilingualDailyResponse] Failed to deserialize response");
                return (null, null);
            }

            // Check if response has predictions wrapper
            if (!fullResponse.ContainsKey("predictions"))
            {
                // Fallback to old format (single language)
                _logger.LogWarning("[LumenPredictionGAgent][ParseMultilingualDailyResponse] No predictions wrapper found, using old format");
                var singleLangResults = ParseDailyResponse(aiResponse);
                if (singleLangResults != null && singleLangResults.Count > 0)
                {
                    // Flatten nested structure
                    var firstResult = singleLangResults.Values.FirstOrDefault();
                    return (firstResult, null);
                }
                return (null, null);
            }

            var predictionsJson = JsonConvert.SerializeObject(fullResponse["predictions"]);
            var predictions = JsonConvert.DeserializeObject<Dictionary<string, object>>(predictionsJson);
            
            if (predictions == null || predictions.Count == 0)
            {
                _logger.LogWarning("[LumenPredictionGAgent][ParseMultilingualDailyResponse] No predictions found");
                return (null, null);
            }

            // Convert each language's nested structure to flattened Dictionary<string, string>
            var multilingualResults = new Dictionary<string, Dictionary<string, string>>();
            
            foreach (var langKvp in predictions)
            {
                var lang = langKvp.Key;
                var langDataJson = JsonConvert.SerializeObject(langKvp.Value);
                var langDataFlat = FlattenNestedJsonToFlat(langDataJson);
                
                if (langDataFlat != null && langDataFlat.Count > 0)
                {
                    multilingualResults[lang] = langDataFlat;
                }
            }

            // Extract English version as default (flat structure)
            Dictionary<string, string>? defaultResults = null;
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
            _logger.LogError(ex, "[LumenPredictionGAgent][ParseMultilingualDailyResponse] Failed to parse multilingual response");
            // Fallback to old format
            var fallbackResults = ParseDailyResponse(aiResponse);
            if (fallbackResults != null && fallbackResults.Count > 0)
            {
                var firstResult = fallbackResults.Values.FirstOrDefault();
                return (firstResult, null);
            }
            return (null, null);
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
            
            _logger.LogDebug("[LumenPredictionGAgent][ParseMultilingualLifetimeResponse] Extracted JSON length: {Length}", jsonContent.Length);

            Dictionary<string, object>? fullResponse = null;
            try
            {
                fullResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "[LumenPredictionGAgent][ParseMultilingualLifetimeResponse] JSON parse error. First 500 chars: {JsonPreview}", 
                    jsonContent.Length > 500 ? jsonContent.Substring(0, 500) : jsonContent);
                _logger.LogError("[LumenPredictionGAgent][ParseMultilingualLifetimeResponse] Last 200 chars: {JsonEnd}", 
                    jsonContent.Length > 200 ? jsonContent.Substring(jsonContent.Length - 200) : jsonContent);
                return (null, null);
            }
            
            if (fullResponse == null)
            {
                _logger.LogWarning("[LumenPredictionGAgent][ParseMultilingualLifetimeResponse] Failed to deserialize response");
                return (null, null);
            }

            // Check if response has predictions wrapper
            if (!fullResponse.ContainsKey("predictions"))
            {
                // Fallback to old format (single language)
                _logger.LogWarning("[LumenPredictionGAgent][ParseMultilingualLifetimeResponse] No predictions wrapper found, using old format");
                var (lifetime, _) = ParseLifetimeWeeklyResponse(aiResponse);
                return (lifetime, null);
            }

            var predictionsJson = JsonConvert.SerializeObject(fullResponse["predictions"]);
            var predictions = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, object>>>(predictionsJson);
            
            if (predictions == null || predictions.Count == 0)
            {
                _logger.LogWarning("[LumenPredictionGAgent][ParseMultilingualLifetimeResponse] No predictions found");
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
            _logger.LogError(ex, "[LumenPredictionGAgent][ParseMultilingualLifetimeResponse] Failed to parse multilingual response");
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
    /// Flatten nested JSON into Dictionary<field, value>
    /// Uses underscore to join nested keys (e.g., "tarotCard_name")
    /// Returns a completely flat dictionary
    /// </summary>
    private Dictionary<string, string>? FlattenNestedJsonToFlat(string json)
    {
        try
        {
            var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            if (data == null) return null;
            
            var flatResult = new Dictionary<string, string>();
            
            // Recursively flatten the entire object into a single-level dictionary
            FlattenObject(data, "", flatResult);
            
            _logger.LogDebug("[LumenPredictionGAgent][FlattenNestedJsonToFlat] Flattened {Count} fields", flatResult.Count);
            
            return flatResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenPredictionGAgent][FlattenNestedJsonToFlat] Failed to flatten JSON");
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
                if (!string.IsNullOrEmpty(prefix))
                {
                    result[prefix] = strValue;
                }
                break;
                
            case Dictionary<string, object> dict:
                // Dictionary - recurse into each key-value pair
                foreach (var kvp in dict)
                {
                    var newKey = string.IsNullOrEmpty(prefix) 
                        ? kvp.Key 
                        : $"{prefix}_{kvp.Key}";
                    FlattenObject(kvp.Value, newKey, result);
                }
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
                if (!string.IsNullOrEmpty(prefix))
                {
                    result[prefix] = jArray.ToString(Newtonsoft.Json.Formatting.None);
                }
                break;
                
            case Newtonsoft.Json.Linq.JValue jValue:
                // Primitive value (number, boolean, etc.)
                if (!string.IsNullOrEmpty(prefix))
                {
                    result[prefix] = jValue.ToString();
                }
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
        
        _logger.LogWarning($"[LumenPredictionGAgent][ParseTarotCard] Unknown tarot card: {cardName}");
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
        
        _logger.LogWarning($"[LumenPredictionGAgent][ParseZodiacSign] Unknown zodiac sign: {signName}");
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
        
        _logger.LogWarning($"[LumenPredictionGAgent][ParseChineseZodiac] Unknown chinese zodiac: {animalName}");
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
        
        _logger.LogWarning($"[LumenPredictionGAgent][ParseCrystalStone] Unknown crystal stone: {stoneName}");
        return CrystalStoneEnum.Unknown;
    }
    
    /// <summary>
    /// Inject Four Pillars (Ba Zi) data into prediction dictionary with language-specific formatting
    /// </summary>
    private void InjectFourPillarsData(Dictionary<string, string> prediction, FourPillarsInfo fourPillars, string language)
    {
        // Year Pillar
        prediction["fourPillars_yearPillar"] = fourPillars.YearPillar.GetFormattedString(language);
        prediction["fourPillars_yearPillar_stem"] = fourPillars.YearPillar.StemChinese;
        prediction["fourPillars_yearPillar_branch"] = fourPillars.YearPillar.BranchChinese;
        prediction["fourPillars_yearPillar_stemPinyin"] = fourPillars.YearPillar.StemPinyin;
        prediction["fourPillars_yearPillar_branchPinyin"] = fourPillars.YearPillar.BranchPinyin;
        prediction["fourPillars_yearPillar_yinYang"] = TranslateYinYang(fourPillars.YearPillar.YinYang, language);
        prediction["fourPillars_yearPillar_element"] = TranslateElement(fourPillars.YearPillar.Element, language);
        prediction["fourPillars_yearPillar_direction"] = TranslateDirection(fourPillars.YearPillar.Direction, language);
        
        // Month Pillar
        prediction["fourPillars_monthPillar"] = fourPillars.MonthPillar.GetFormattedString(language);
        prediction["fourPillars_monthPillar_stem"] = fourPillars.MonthPillar.StemChinese;
        prediction["fourPillars_monthPillar_branch"] = fourPillars.MonthPillar.BranchChinese;
        prediction["fourPillars_monthPillar_stemPinyin"] = fourPillars.MonthPillar.StemPinyin;
        prediction["fourPillars_monthPillar_branchPinyin"] = fourPillars.MonthPillar.BranchPinyin;
        prediction["fourPillars_monthPillar_yinYang"] = TranslateYinYang(fourPillars.MonthPillar.YinYang, language);
        prediction["fourPillars_monthPillar_element"] = TranslateElement(fourPillars.MonthPillar.Element, language);
        prediction["fourPillars_monthPillar_direction"] = TranslateDirection(fourPillars.MonthPillar.Direction, language);
        
        // Day Pillar
        prediction["fourPillars_dayPillar"] = fourPillars.DayPillar.GetFormattedString(language);
        prediction["fourPillars_dayPillar_stem"] = fourPillars.DayPillar.StemChinese;
        prediction["fourPillars_dayPillar_branch"] = fourPillars.DayPillar.BranchChinese;
        prediction["fourPillars_dayPillar_stemPinyin"] = fourPillars.DayPillar.StemPinyin;
        prediction["fourPillars_dayPillar_branchPinyin"] = fourPillars.DayPillar.BranchPinyin;
        prediction["fourPillars_dayPillar_yinYang"] = TranslateYinYang(fourPillars.DayPillar.YinYang, language);
        prediction["fourPillars_dayPillar_element"] = TranslateElement(fourPillars.DayPillar.Element, language);
        prediction["fourPillars_dayPillar_direction"] = TranslateDirection(fourPillars.DayPillar.Direction, language);
        
        // Hour Pillar (optional)
        if (fourPillars.HourPillar != null)
        {
            prediction["fourPillars_hourPillar"] = fourPillars.HourPillar.GetFormattedString(language);
            prediction["fourPillars_hourPillar_stem"] = fourPillars.HourPillar.StemChinese;
            prediction["fourPillars_hourPillar_branch"] = fourPillars.HourPillar.BranchChinese;
            prediction["fourPillars_hourPillar_stemPinyin"] = fourPillars.HourPillar.StemPinyin;
            prediction["fourPillars_hourPillar_branchPinyin"] = fourPillars.HourPillar.BranchPinyin;
            prediction["fourPillars_hourPillar_yinYang"] = TranslateYinYang(fourPillars.HourPillar.YinYang, language);
            prediction["fourPillars_hourPillar_element"] = TranslateElement(fourPillars.HourPillar.Element, language);
            prediction["fourPillars_hourPillar_direction"] = TranslateDirection(fourPillars.HourPillar.Direction, language);
        }
    }
    
    private string TranslateYinYang(string yinYang, string language) => language switch
    {
        "zh-tw" or "zh" => yinYang == "Yang" ? "陽" : "陰",
        "es" => yinYang == "Yang" ? "Yang" : "Yin",
        _ => yinYang  // English default
    };
    
    private string TranslateElement(string element, string language) => (element, language) switch
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
        _ => element  // English default
    };
    
    private string TranslateDirection(string direction, string language) => (direction, language) switch
    {
        ("East 1", "zh-tw" or "zh") => "東一",
        ("East 2", "zh-tw" or "zh") => "東二",
        ("South 1", "zh-tw" or "zh") => "南一",
        ("South 2", "zh-tw" or "zh") => "南二",
        ("West 1", "zh-tw" or "zh") => "西一",
        ("West 2", "zh-tw" or "zh") => "西二",
        ("North 1", "zh-tw" or "zh") => "北一",
        ("North 2", "zh-tw" or "zh") => "北二",
        ("Centre", "zh-tw" or "zh") => "中",
        ("East 1", "es") => "Este 1",
        ("East 2", "es") => "Este 2",
        ("South 1", "es") => "Sur 1",
        ("South 2", "es") => "Sur 2",
        ("West 1", "es") => "Oeste 1",
        ("West 2", "es") => "Oeste 2",
        ("North 1", "es") => "Norte 1",
        ("North 2", "es") => "Norte 2",
        ("Centre", "es") => "Centro",
        _ => direction  // English default
    };
    
    /// <summary>
    /// Translate Chinese Zodiac title (e.g., "The Pig") to different languages
    /// </summary>
    private string TranslateZodiacTitle(string birthYearAnimal, string language)
    {
        // Extract animal name (e.g., "Wood Pig" -> "Pig")
        var animalName = birthYearAnimal.Split(' ').Last();
        
        return language switch
        {
            "zh-tw" or "zh" => animalName switch
            {
                "Rat" => "鼠",
                "Ox" => "牛",
                "Tiger" => "虎",
                "Rabbit" => "兔",
                "Dragon" => "龍",
                "Snake" => "蛇",
                "Horse" => "馬",
                "Goat" => "羊",
                "Monkey" => "猴",
                "Rooster" => "雞",
                "Dog" => "狗",
                "Pig" => "豬",
                _ => animalName
            },
            "es" => animalName switch
            {
                "Rat" => "La Rata",
                "Ox" => "El Buey",
                "Tiger" => "El Tigre",
                "Rabbit" => "El Conejo",
                "Dragon" => "El Dragón",
                "Snake" => "La Serpiente",
                "Horse" => "El Caballo",
                "Goat" => "La Cabra",
                "Monkey" => "El Mono",
                "Rooster" => "El Gallo",
                "Dog" => "El Perro",
                "Pig" => "El Cerdo",
                _ => $"El {animalName}"
            },
            _ => $"The {animalName}"  // English default
        };
    }
}

