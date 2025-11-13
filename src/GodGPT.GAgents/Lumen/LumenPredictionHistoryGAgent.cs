using Aevatar.Application.Grains.Lumen.Dtos;
using Aevatar.Application.Grains.Lumen.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Lumen;

/// <summary>
/// Interface for Lumen Prediction History GAgent - manages prediction history
/// </summary>
public interface ILumenPredictionHistoryGAgent : IGAgent
{
    Task AddPredictionAsync(Guid predictionId, DateOnly predictionDate, 
        Dictionary<string, string> results, PredictionType type);
    
    [ReadOnly]
    Task<PredictionResultDto?> GetPredictionByDateAsync(DateOnly date);
    
    [ReadOnly]
    Task<List<PredictionResultDto>> GetRecentPredictionsAsync(int days = 7);
    
    [ReadOnly]
    Task<List<PredictionResultDto>> GetMonthlyPredictionsAsync(int year, int month);
}

[GAgent(nameof(LumenPredictionHistoryGAgent))]
[Reentrant]
public class LumenPredictionHistoryGAgent : GAgentBase<LumenPredictionHistoryState, LumenPredictionHistoryEventLog>, 
    ILumenPredictionHistoryGAgent
{
    private readonly ILogger<LumenPredictionHistoryGAgent> _logger;
    private const int MaxHistoryDays = 30; // Keep last 30 days

    public LumenPredictionHistoryGAgent(ILogger<LumenPredictionHistoryGAgent> logger)
    {
        _logger = logger;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Lumen prediction history management");
    }

    /// <summary>
    /// Event-driven state transition handler
    /// </summary>
    protected sealed override void GAgentTransitionState(LumenPredictionHistoryState state,
        StateLogEventBase<LumenPredictionHistoryEventLog> @event)
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
                    CreatedAt = addedEvent.CreatedAt,
                    Results = addedEvent.Results,
                    Type = addedEvent.Type
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

    public async Task AddPredictionAsync(Guid predictionId, DateOnly predictionDate, 
        Dictionary<string, string> results, PredictionType type)
    {
        try
        {
            _logger.LogDebug("[LumenPredictionHistoryGAgent][AddPredictionAsync] Adding prediction: {PredictionId}, Date: {Date}, Type: {Type}",
                predictionId, predictionDate, type);

            var now = DateTime.UtcNow;

            // Raise event to add prediction to history
            RaiseEvent(new PredictionAddedToHistoryEvent
            {
                PredictionId = predictionId,
                PredictionDate = predictionDate,
                CreatedAt = now,
                Results = results,
                Type = type
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[LumenPredictionHistoryGAgent][AddPredictionAsync] Prediction added to history: {PredictionId}",
                predictionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenPredictionHistoryGAgent][AddPredictionAsync] Error adding prediction to history");
            throw;
        }
    }

    public Task<PredictionResultDto?> GetPredictionByDateAsync(DateOnly date)
    {
        try
        {
            _logger.LogDebug("[LumenPredictionHistoryGAgent][GetPredictionByDateAsync] Getting prediction for date: {Date}", date);

            var prediction = State.RecentPredictions.FirstOrDefault(p => p.PredictionDate == date);
            
            if (prediction == null)
            {
                _logger.LogInformation("[LumenPredictionHistoryGAgent][GetPredictionByDateAsync] No prediction found for date: {Date}", date);
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
            _logger.LogError(ex, "[LumenPredictionHistoryGAgent][GetPredictionByDateAsync] Error getting prediction by date");
            return Task.FromResult<PredictionResultDto?>(null);
        }
    }

    public Task<List<PredictionResultDto>> GetRecentPredictionsAsync(int days = 7)
    {
        try
        {
            _logger.LogDebug("[LumenPredictionHistoryGAgent][GetRecentPredictionsAsync] Getting recent {Days} days predictions", days);

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

            _logger.LogInformation("[LumenPredictionHistoryGAgent][GetRecentPredictionsAsync] Found {Count} predictions", 
                recentPredictions.Count);

            return Task.FromResult(recentPredictions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenPredictionHistoryGAgent][GetRecentPredictionsAsync] Error getting recent predictions");
            return Task.FromResult(new List<PredictionResultDto>());
        }
    }

    public Task<List<PredictionResultDto>> GetMonthlyPredictionsAsync(int year, int month)
    {
        try
        {
            _logger.LogDebug("[LumenPredictionHistoryGAgent][GetMonthlyPredictionsAsync] Getting predictions for {Year}-{Month}", 
                year, month);

            // Get first and last day of the month
            var firstDayOfMonth = new DateOnly(year, month, 1);
            var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

            var monthlyPredictions = State.RecentPredictions
                .Where(p => p.PredictionDate >= firstDayOfMonth && p.PredictionDate <= lastDayOfMonth)
                .OrderByDescending(p => p.PredictionDate)
                .Select(p => new PredictionResultDto
                {
                    PredictionId = p.PredictionId,
                    UserId = State.UserId,
                    PredictionDate = p.PredictionDate,
                    Results = p.Results,
                    CreatedAt = p.CreatedAt,
                    FromCache = true,
                    Type = p.Type
                })
                .ToList();

            _logger.LogInformation("[LumenPredictionHistoryGAgent][GetMonthlyPredictionsAsync] Found {Count} predictions for {Year}-{Month}", 
                monthlyPredictions.Count, year, month);

            return Task.FromResult(monthlyPredictions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenPredictionHistoryGAgent][GetMonthlyPredictionsAsync] Error getting monthly predictions");
            return Task.FromResult(new List<PredictionResultDto>());
        }
    }
}

