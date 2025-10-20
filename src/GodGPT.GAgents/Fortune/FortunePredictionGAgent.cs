using Aevatar.Application.Grains.Agents.ChatManager.Chat;
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
    Task<GetTodayPredictionResult> GetOrGeneratePredictionAsync(FortuneUserDto userInfo);
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
                state.OverallEnergy = generatedEvent.OverallEnergy;
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
                        OverallEnergy = State.OverallEnergy,
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
            
            var godChat = _clusterClient.GetGrain<IGodChat>(Guid.Parse(userInfo.UserId));
            var chatId = Guid.NewGuid().ToString();

            var settings = new ExecutionPromptSettings
            {
                Temperature = "0.7"
            };

            // Use dedicated "FORTUNE" region for independent LLM configuration
            // This allows Fortune to use cost-optimized models (e.g., GPT-4o-mini)
            // separate from the main chat experience
            var response = await godChat.ChatWithoutHistoryAsync(
                Guid.Parse(userInfo.UserId), 
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
                OverallEnergy = overallEnergy,
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
                    OverallEnergy = overallEnergy,
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
        var mbtiName = userInfo.MbtiType.ToString();
        var relationshipStatus = userInfo.RelationshipStatus?.ToString() ?? "Unknown";
        var birthLocation = $"{userInfo.BirthCity}, {userInfo.BirthCountry}";
        var birthDateTime = $"{userInfo.BirthDate:yyyy-MM-dd} {userInfo.BirthTime:HH:mm}";

        var prompt = $@"Generate daily fortune for {predictionDate:yyyy-MM-dd}.

User: {userInfo.FirstName} {userInfo.LastName}, Birth: {birthDateTime} at {birthLocation}, Gender: {userInfo.Gender}, MBTI: {mbtiName}, Status: {relationshipStatus}, Interests: {userInfo.Interests ?? "None"}

Analyze using 11 methods: zodiac, bazi, ziwei, constellation, numerology, synastry, chineseZodiac, mayan, humanDesign, mbti, tarot.

Return JSON:
{{
  ""overallEnergy"": <0-100>,
  ""overallSummary"": ""<≤10 words>"",
  ""results"": {{
    ""zodiac"": {{""summary"": ""..."", ""description"": ""..."", ""love"": ""★★★☆☆"", ""career"": ""..."", ""health"": ""..."", ""finance"": ""...""}},
    ""bazi"": {{""summary"": ""..."", ""description"": ""..."", ""suitable"": ""..."", ""avoid"": ""..."", ""direction"": ""..."", ""luckyNumber"": ""...""}},
    ""ziwei"": {{""summary"": ""..."", ""description"": ""..."", ""palace"": ""..."", ""element"": ""...""}},
    ""constellation"": {{""summary"": ""..."", ""description"": ""..."", ""mansion"": ""..."", ""influence"": ""...""}},
    ""numerology"": {{""summary"": ""..."", ""description"": ""..."", ""personalDay"": ""..."", ""lifePath"": ""..."", ""luckyNumber"": ""...""}},
    ""synastry"": {{""summary"": ""..."", ""description"": ""..."", ""compatibility"": ""..."", ""suggestion"": ""...""}},
    ""chineseZodiac"": {{""summary"": ""..."", ""description"": ""..."", ""zodiac"": ""..."", ""conflict"": ""..."", ""harmony"": ""...""}},
    ""mayan"": {{""summary"": ""..."", ""description"": ""..."", ""totem"": ""..."", ""tone"": ""..."", ""keyword"": ""...""}},
    ""humanDesign"": {{""summary"": ""..."", ""description"": ""..."", ""type"": ""..."", ""strategy"": ""..."", ""authority"": ""...""}},
    ""mbti"": {{""summary"": ""..."", ""description"": ""..."", ""mood"": ""..."", ""social"": ""..."", ""suggestion"": ""...""}},
    ""tarot"": {{""summary"": ""..."", ""description"": ""..."", ""card1"": ""..."", ""card2"": ""..."", ""card3"": ""..."", ""interpretation"": ""...""}}
  }}
}}

Rules: All summary ≤10 words, description ≤100 words. JSON only, no extra text.";

        return prompt;
    }

    /// <summary>
    /// Parse AI JSON response (new structure with overallEnergy and results)
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

                // Extract overallEnergy
                var overallEnergy = 70; // Default value
                if (fullResponse.ContainsKey("overallEnergy"))
                {
                    if (fullResponse["overallEnergy"] is long energyLong)
                    {
                        overallEnergy = (int)energyLong;
                    }
                    else if (fullResponse["overallEnergy"] is int energyInt)
                    {
                        overallEnergy = energyInt;
                    }
                }

                // Extract results
                if (fullResponse.ContainsKey("results") && fullResponse["results"] != null)
                {
                    var resultsJson = JsonConvert.SerializeObject(fullResponse["results"]);
                    var results = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(resultsJson);
                    return (results, overallEnergy);
                }

                return (null, overallEnergy);
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

