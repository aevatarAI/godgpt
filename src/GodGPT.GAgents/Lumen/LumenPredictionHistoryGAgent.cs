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
    Task AddPredictionAsync(PredictionResultDto prediction);
    
    [ReadOnly]
    Task<PredictionResultDto?> GetPredictionByDateAsync(DateOnly date);
    
    [ReadOnly]
    Task<List<PredictionResultDto>> GetRecentPredictionsAsync(int days = 7);
    
    [ReadOnly]
    Task<List<PredictionResultDto>> GetMonthlyPredictionsAsync(int year, int month);
    
    Task ClearHistoryAsync();
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
                
                // Add new prediction with complete data
                state.RecentPredictions.Add(new PredictionHistoryRecord
                {
                    PredictionId = addedEvent.PredictionId,
                    PredictionDate = addedEvent.PredictionDate,
                    CreatedAt = addedEvent.CreatedAt,
                    Results = addedEvent.Results,
                    Type = addedEvent.Type,
                    AvailableLanguages = addedEvent.AvailableLanguages,
                    RequestedLanguage = addedEvent.RequestedLanguage,
                    ReturnedLanguage = addedEvent.ReturnedLanguage,
                    FromCache = addedEvent.FromCache,
                    AllLanguagesGenerated = addedEvent.AllLanguagesGenerated,
                    IsFallback = addedEvent.IsFallback
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
                
            case PredictionHistoryClearedEvent clearEvent:
                // Clear all history data
                state.UserId = string.Empty;
                state.RecentPredictions.Clear();
                state.LastUpdatedAt = clearEvent.ClearedAt;
                break;
        }
    }

    public async Task AddPredictionAsync(PredictionResultDto prediction)
    {
        try
        {
            _logger.LogDebug("[LumenPredictionHistoryGAgent][AddPredictionAsync] Adding prediction: {PredictionId}, Date: {Date}, Type: {Type}",
                prediction.PredictionId, prediction.PredictionDate, prediction.Type);

            // Set UserId if State is empty (first time)
            if (string.IsNullOrEmpty(State.UserId))
            {
                State.UserId = prediction.UserId;
            }

            // Raise event to add prediction to history with complete data
            RaiseEvent(new PredictionAddedToHistoryEvent
            {
                PredictionId = prediction.PredictionId,
                PredictionDate = prediction.PredictionDate,
                CreatedAt = prediction.CreatedAt,
                Results = prediction.Results,
                Type = prediction.Type,
                UserId = prediction.UserId,
                AvailableLanguages = prediction.AvailableLanguages ?? new List<string>(),
                RequestedLanguage = prediction.RequestedLanguage,
                ReturnedLanguage = prediction.ReturnedLanguage,
                FromCache = prediction.FromCache,
                AllLanguagesGenerated = prediction.AllLanguagesGenerated,
                IsFallback = prediction.IsFallback
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[LumenPredictionHistoryGAgent][AddPredictionAsync] Prediction added to history: {PredictionId}",
                prediction.PredictionId);
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

            // Only return Daily predictions (history is for daily predictions only)
            var prediction = State.RecentPredictions.FirstOrDefault(p => p.PredictionDate == date && p.Type == PredictionType.Daily);
            
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
                Type = prediction.Type,
                FromCache = prediction.FromCache,
                AvailableLanguages = prediction.AvailableLanguages,
                AllLanguagesGenerated = prediction.AllLanguagesGenerated,
                RequestedLanguage = prediction.RequestedLanguage,
                ReturnedLanguage = prediction.ReturnedLanguage,
                IsFallback = prediction.IsFallback
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
            
            // Only return Daily predictions (history is for daily predictions only)
            var recentPredictions = State.RecentPredictions
                .Where(p => p.PredictionDate >= cutoffDate && p.Type == PredictionType.Daily)
                .OrderByDescending(p => p.PredictionDate)
                .Take(days)
                .Select(p => new PredictionResultDto
                {
                    PredictionId = p.PredictionId,
                    UserId = State.UserId,
                    PredictionDate = p.PredictionDate,
                    Results = p.Results,
                    CreatedAt = p.CreatedAt,
                    Type = p.Type,
                    FromCache = p.FromCache,
                    AvailableLanguages = p.AvailableLanguages,
                    AllLanguagesGenerated = p.AllLanguagesGenerated,
                    RequestedLanguage = p.RequestedLanguage,
                    ReturnedLanguage = p.ReturnedLanguage,
                    IsFallback = p.IsFallback
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

            // Only return Daily predictions (history is for daily predictions only)
            var monthlyPredictions = State.RecentPredictions
                .Where(p => p.PredictionDate >= firstDayOfMonth && p.PredictionDate <= lastDayOfMonth && p.Type == PredictionType.Daily)
                .OrderByDescending(p => p.PredictionDate)
                .Select(p => new PredictionResultDto
                {
                    PredictionId = p.PredictionId,
                    UserId = State.UserId,
                    PredictionDate = p.PredictionDate,
                    Results = p.Results,
                    CreatedAt = p.CreatedAt,
                    Type = p.Type,
                    FromCache = p.FromCache,
                    AvailableLanguages = p.AvailableLanguages,
                    AllLanguagesGenerated = p.AllLanguagesGenerated,
                    RequestedLanguage = p.RequestedLanguage,
                    ReturnedLanguage = p.ReturnedLanguage,
                    IsFallback = p.IsFallback
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
    
    public async Task ClearHistoryAsync()
    {
        try
        {
            _logger.LogDebug("[LumenPredictionHistoryGAgent][ClearHistoryAsync] Clearing prediction history");

            // Raise event to clear history
            RaiseEvent(new PredictionHistoryClearedEvent
            {
                ClearedAt = DateTime.UtcNow
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[LumenPredictionHistoryGAgent][ClearHistoryAsync] Prediction history cleared successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenPredictionHistoryGAgent][ClearHistoryAsync] Error clearing prediction history");
            throw;
        }
    }
}

