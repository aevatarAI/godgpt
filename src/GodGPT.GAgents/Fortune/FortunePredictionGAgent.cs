using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Fortune.Dtos;
using Aevatar.Application.Grains.Fortune.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Options;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Orleans;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Fortune;

/// <summary>
/// Interface for Fortune Prediction GAgent - manages fortune prediction generation
/// </summary>
public interface IFortunePredictionGAgent : IGAgent
{
    Task<GetTodayPredictionResult> GetOrGeneratePredictionAsync(FortuneUserDto userInfo);
    
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
                break;
        }
    }

    public async Task<GetTodayPredictionResult> GetOrGeneratePredictionAsync(FortuneUserDto userInfo)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            _logger.LogDebug("[FortunePredictionGAgent][GetOrGeneratePredictionAsync] Start - UserId: {UserId}, Date: {Date}",
                userInfo.UserId, today);

            // Check if prediction already exists (from cache/state)
            if (State.PredictionId != Guid.Empty && State.PredictionDate == today)
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
                        Energy = State.Energy,
                        Results = State.Results,
                        CreatedAt = State.CreatedAt,
                        FromCache = true
                    }
                };
            }

            // Generate new prediction
            _logger.LogInformation("[FortunePredictionGAgent][GetOrGeneratePredictionAsync] Generating new prediction for {UserId}",
                userInfo.UserId);

            var predictionResult = await GeneratePredictionAsync(userInfo, today);
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
            Energy = State.Energy,
            Results = State.Results,
            CreatedAt = State.CreatedAt,
            FromCache = true
        });
    }

    /// <summary>
    /// Generate new prediction using AI
    /// </summary>
    private async Task<GetTodayPredictionResult> GeneratePredictionAsync(FortuneUserDto userInfo, DateOnly predictionDate)
    {
        try
        {
            // Build prompt
            var prompt = BuildPredictionPrompt(userInfo, predictionDate);

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

            // Parse AI response
            var (parsedResults, overallEnergy) = ParseAIResponse(aiResponse);
            if (parsedResults == null)
            {
                _logger.LogError("[FortunePredictionGAgent][GeneratePredictionAsync] Failed to parse AI response");
                return new GetTodayPredictionResult
                {
                    Success = false,
                    Message = "Failed to parse AI response"
                };
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
                Energy = overallEnergy,
                CreatedAt = now
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[FortunePredictionGAgent][GeneratePredictionAsync] Prediction generated successfully for user {UserId}",
                userInfo.UserId);

            return new GetTodayPredictionResult
            {
                Success = true,
                Message = string.Empty,
                Prediction = new PredictionResultDto
                {
                    PredictionId = predictionId,
                    UserId = userInfo.UserId,
                    PredictionDate = predictionDate,
                    Energy = overallEnergy,
                    Results = parsedResults,
                    CreatedAt = now,
                    FromCache = false
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
    private string BuildPredictionPrompt(FortuneUserDto userInfo, DateOnly predictionDate)
    {
        var relationshipStatus = userInfo.RelationshipStatus?.ToString() ?? "Unknown";
        var birthLocation = $"{userInfo.BirthCity}, {userInfo.BirthCountry}";
        var birthDateTime = $"{userInfo.BirthDate:yyyy-MM-dd} {userInfo.BirthTime:HH:mm}";
        var calendarType = userInfo.CalendarType == CalendarTypeEnum.Solar ? "Solar" : "Lunar";

        var prompt = $@"Generate daily fortune for {predictionDate:yyyy-MM-dd}.
User: {userInfo.FirstName} {userInfo.LastName}, Birth: {birthDateTime} ({calendarType} calendar) at {birthLocation}, Gender: {userInfo.Gender}, Status: {relationshipStatus}, Interests: {userInfo.Interests ?? "None"}

Analyze using 11 methods: horoscope, bazi, ziwei, constellation, numerology, synastry, chineseZodiac, mayanTotem, humanFigure, tarot, zhengYu.
Data Sources: Lunar calendar uses Purple Mountain Observatory (Chinese Academy of Sciences) astronomical calendar. Constellation sun/moon positions use NASA data.

Return JSON (each method has summary/description/detail + specific fields):
{{""energy"":<0-100>,""results"":{{""forecast"":{{""summary"":""..."",""description"":""..."",""detail"":""..."",""love"":""★★★☆☆"",""career"":""★★★★☆"",""health"":""★★★☆☆"",""finance"":""★★★★★""}},""horoscope"":{{""summary"":""..."",""description"":""..."",""detail"":""..."",""yourSign"":""..."",""risingSign"":""...""}},""bazi"":{{""summary"":""..."",""description"":""..."",""detail"":""..."",""dayMaster"":""..."",""suitable"":""..."",""avoid"":""..."",""direction"":""..."",""luckyNumber"":""...""}},""ziwei"":{{""summary"":""..."",""description"":""..."",""detail"":""..."",""palace"":""..."",""element"":""...""}},""constellation"":{{""summary"":""..."",""description"":""..."",""detail"":""..."",""mansion"":""..."",""influence"":""...""}},""numerology"":{{""summary"":""..."",""description"":""..."",""detail"":""..."",""personalDay"":""..."",""lifePath"":""..."",""luckyNumber"":""...""}},""synastry"":{{""summary"":""..."",""description"":""..."",""detail"":""..."",""compatibility"":""..."",""suggestion"":""...""}},""chineseZodiac"":{{""summary"":""..."",""description"":""..."",""detail"":""..."",""zodiac"":""..."",""conflict"":""..."",""harmony"":""...""}},""mayanTotem"":{{""summary"":""..."",""description"":""..."",""detail"":""..."",""totem"":""..."",""tone"":""..."",""keyword"":""...""}},""humanFigure"":{{""summary"":""..."",""description"":""..."",""detail"":""..."",""type"":""..."",""strategy"":""..."",""authority"":""...""}},""tarot"":{{""summary"":""..."",""description"":""..."",""detail"":""..."",""top"":""..."",""left"":""..."",""right"":""..."",""interpretation"":""...""}},""zhengYu"":{{""summary"":""..."",""description"":""..."",""detail"":""..."",""element"":""..."",""balance"":""..."",""guidance"":""...""}}}}}}

CRITICAL RULES:
- summary: max 10 words
- description: MUST be 30-100 words (minimum 30 words required, do NOT write less than 30 words)
- detail: MUST be 100-300 words in TWO paragraphs separated by \n\n (minimum 100 words total, do NOT write as one long paragraph)
- forecast: comprehensive overall prediction
- star ratings: use ★★★☆☆ format (1-5 stars)
- chineseZodiac: include Five Elements information naturally
- Return valid JSON only, no additional text";

        return prompt;
    }

    /// <summary>
    /// Parse AI JSON response (structure with energy at top level and forecast in results)
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

