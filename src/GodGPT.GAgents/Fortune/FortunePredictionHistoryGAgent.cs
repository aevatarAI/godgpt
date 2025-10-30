using Aevatar.Application.Grains.Fortune.Dtos;
using Aevatar.Application.Grains.Fortune.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Fortune;

/// <summary>
/// Interface for Fortune Prediction History GAgent - manages prediction history
/// </summary>
public interface IFortunePredictionHistoryGAgent : IGAgent
{
    Task AddPredictionAsync(Guid predictionId, DateOnly predictionDate, int energy, 
        Dictionary<string, Dictionary<string, string>> results);
    
    [ReadOnly]
    Task<PredictionResultDto?> GetPredictionByDateAsync(DateOnly date);
    
    [ReadOnly]
    Task<List<PredictionResultDto>> GetRecentPredictionsAsync(int days = 7);
}

[GAgent(nameof(FortunePredictionHistoryGAgent))]
[Reentrant]
public class FortunePredictionHistoryGAgent : GAgentBase<FortunePredictionHistoryState, FortunePredictionHistoryEventLog>, 
    IFortunePredictionHistoryGAgent
{
    private readonly ILogger<FortunePredictionHistoryGAgent> _logger;
    private const int MaxHistoryDays = 30; // Keep last 30 days

    public FortunePredictionHistoryGAgent(ILogger<FortunePredictionHistoryGAgent> logger)
    {
        _logger = logger;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Fortune prediction history management");
    }

    /// <summary>
    /// Event-driven state transition handler
    /// </summary>
    protected sealed override void GAgentTransitionState(FortunePredictionHistoryState state,
        StateLogEventBase<FortunePredictionHistoryEventLog> @event)
    {
        switch (@event)
        {
            case PredictionAddedToHistoryEvent addedEvent:
                // Remove old prediction for the same date (if exists)
                state.RecentPredictions.RemoveAll(p => p.PredictionDate == addedEvent.PredictionDate);
                
                // Add new prediction
                state.RecentPredictions.Add(new PredictionHistoryRecord
                {
                    PredictionId = addedEvent.PredictionId,
                    PredictionDate = addedEvent.PredictionDate,
                    Energy = addedEvent.Energy,
                    Results = addedEvent.Results,
                    CreatedAt = addedEvent.CreatedAt
                });
                
                // Sort by date descending (newest first)
                state.RecentPredictions = state.RecentPredictions
                    .OrderByDescending(p => p.PredictionDate)
                    .ToList();
                
                // Keep only last MaxHistoryDays
                if (state.RecentPredictions.Count > MaxHistoryDays)
                {
                    state.RecentPredictions = state.RecentPredictions
                        .Take(MaxHistoryDays)
                        .ToList();
                }
                
                state.LastUpdatedAt = DateTime.UtcNow;
                break;
        }
    }

    public async Task AddPredictionAsync(Guid predictionId, DateOnly predictionDate, int energy,
        Dictionary<string, Dictionary<string, string>> results)
    {
        try
        {
            _logger.LogDebug("[FortunePredictionHistoryGAgent][AddPredictionAsync] Adding prediction: {PredictionId}, Date: {Date}",
                predictionId, predictionDate);

            var now = DateTime.UtcNow;

            // Raise event to add prediction to history
            RaiseEvent(new PredictionAddedToHistoryEvent
            {
                PredictionId = predictionId,
                PredictionDate = predictionDate,
                Energy = energy,
                Results = results,
                CreatedAt = now
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[FortunePredictionHistoryGAgent][AddPredictionAsync] Prediction added to history: {PredictionId}",
                predictionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortunePredictionHistoryGAgent][AddPredictionAsync] Error adding prediction to history");
            throw;
        }
    }

    public Task<PredictionResultDto?> GetPredictionByDateAsync(DateOnly date)
    {
        try
        {
            _logger.LogDebug("[FortunePredictionHistoryGAgent][GetPredictionByDateAsync] Getting prediction for date: {Date}", date);

            var prediction = State.RecentPredictions.FirstOrDefault(p => p.PredictionDate == date);
            
            if (prediction == null)
            {
                _logger.LogInformation("[FortunePredictionHistoryGAgent][GetPredictionByDateAsync] No prediction found for date: {Date}", date);
                return Task.FromResult<PredictionResultDto?>(null);
            }

            return Task.FromResult<PredictionResultDto?>(new PredictionResultDto
            {
                PredictionId = prediction.PredictionId,
                UserId = State.UserId,
                PredictionDate = prediction.PredictionDate,
                Results = prediction.Results,
                CreatedAt = prediction.CreatedAt,
                FromCache = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortunePredictionHistoryGAgent][GetPredictionByDateAsync] Error getting prediction by date");
            return Task.FromResult<PredictionResultDto?>(null);
        }
    }

    public Task<List<PredictionResultDto>> GetRecentPredictionsAsync(int days = 7)
    {
        try
        {
            _logger.LogDebug("[FortunePredictionHistoryGAgent][GetRecentPredictionsAsync] Getting recent {Days} days predictions", days);

            if (days < 1 || days > MaxHistoryDays)
            {
                days = Math.Clamp(days, 1, MaxHistoryDays);
            }

            var cutoffDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-days + 1);
            
            var recentPredictions = State.RecentPredictions
                .Where(p => p.PredictionDate >= cutoffDate)
                .OrderByDescending(p => p.PredictionDate)
                .Take(days)
                .Select(p => new PredictionResultDto
                {
                    PredictionId = p.PredictionId,
                    UserId = State.UserId,
                    PredictionDate = p.PredictionDate,
                    Results = p.Results,
                    CreatedAt = p.CreatedAt,
                    FromCache = true
                })
                .ToList();

            _logger.LogInformation("[FortunePredictionHistoryGAgent][GetRecentPredictionsAsync] Found {Count} predictions",
                recentPredictions.Count);

            return Task.FromResult(recentPredictions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortunePredictionHistoryGAgent][GetRecentPredictionsAsync] Error getting recent predictions");
            return Task.FromResult(new List<PredictionResultDto>());
        }
    }
}

