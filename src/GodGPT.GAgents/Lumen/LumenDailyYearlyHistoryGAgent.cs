using Aevatar.Application.Grains.Common;
using Aevatar.Application.Grains.Lumen.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Lumen;

/// <summary>
/// Interface for Lumen daily yearly history GAgent
/// Manages yearly archive of daily predictions
/// GrainId format: Guid derived from {UserId}-{YYYY}
/// </summary>
public interface ILumenDailyYearlyHistoryGAgent : IGrainWithGuidKey
{
    /// <summary>
    /// Add or update a daily prediction in yearly history
    /// </summary>
    Task AddOrUpdateDailyPredictionAsync(
        string userId,
        Guid predictionId,
        DateOnly date,
        Dictionary<string, Dictionary<string, string>> multilingualResults,
        List<string> availableLanguages);
    
    /// <summary>
    /// Get a specific daily prediction by date
    /// </summary>
    [ReadOnly]
    Task<DailyPredictionRecord?> GetDailyPredictionAsync(DateOnly date);
    
    /// <summary>
    /// Get all daily predictions for this year
    /// </summary>
    [ReadOnly]
    Task<List<DailyPredictionRecord>> GetAllDailyPredictionsAsync();
    
    /// <summary>
    /// Get daily predictions for a date range
    /// </summary>
    [ReadOnly]
    Task<List<DailyPredictionRecord>> GetDailyPredictionsByRangeAsync(DateOnly startDate, DateOnly endDate);
}

[GAgent(nameof(LumenDailyYearlyHistoryGAgent))]
[Reentrant]
public class LumenDailyYearlyHistoryGAgent : 
    GAgentBase<LumenDailyYearlyHistoryState, LumenDailyYearlyHistoryEventLog>,
    ILumenDailyYearlyHistoryGAgent
{
    private readonly ILogger<LumenDailyYearlyHistoryGAgent> _logger;
    
    public LumenDailyYearlyHistoryGAgent(ILogger<LumenDailyYearlyHistoryGAgent> logger)
    {
        _logger = logger;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Lumen daily prediction yearly history management");
    }
    
    /// <summary>
    /// Event-driven state transition handler
    /// </summary>
    protected sealed override void GAgentTransitionState(
        LumenDailyYearlyHistoryState state,
        StateLogEventBase<LumenDailyYearlyHistoryEventLog> @event)
    {
        switch (@event)
        {
            case DailyPredictionAddedEvent addedEvent:
                state.UserId = addedEvent.UserId;
                state.Year = addedEvent.Year;
                state.LastUpdatedAt = addedEvent.AddedAt;
                
                // Add or update prediction
                state.Predictions[addedEvent.Date] = new DailyPredictionRecord
                {
                    PredictionId = addedEvent.PredictionId,
                    Date = addedEvent.Date,
                    CreatedAt = addedEvent.CreatedAt,
                    MultilingualResults = addedEvent.MultilingualResults,
                    AvailableLanguages = addedEvent.AvailableLanguages
                };
                break;
        }
    }
    
    public async Task AddOrUpdateDailyPredictionAsync(
        string userId,
        Guid predictionId,
        DateOnly date,
        Dictionary<string, Dictionary<string, string>> multilingualResults,
        List<string> availableLanguages)
    {
        try
        {
            // Extract year from date
            var year = date.Year;
            
            // Validate date year matches grain year
            if (date.Year != year)
            {
                _logger.LogWarning(
                    "[LumenDailyYearlyHistory] Date year mismatch - Date: {Date}, Grain Year: {Year}",
                    date, year);
                return;
            }
            
            _logger.LogDebug(
                "[LumenDailyYearlyHistory] Adding prediction - UserId: {UserId}, Year: {Year}, Date: {Date}, Languages: {Languages}",
                userId, year, date, string.Join(",", availableLanguages));
            
            // Raise event to add/update prediction
            RaiseEvent(new DailyPredictionAddedEvent
            {
                UserId = userId,
                Year = year,
                PredictionId = predictionId,
                Date = date,
                CreatedAt = DateTime.UtcNow,
                MultilingualResults = multilingualResults,
                AvailableLanguages = availableLanguages,
                AddedAt = DateTime.UtcNow
            });
            
            // Confirm event to persist
            await ConfirmEvents();
            
            _logger.LogInformation(
                "[LumenDailyYearlyHistory] Prediction added successfully - UserId: {UserId}, Date: {Date}, Total: {Total}",
                userId, date, State.Predictions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "[LumenDailyYearlyHistory] Error adding prediction - Date: {Date}", date);
            throw;
        }
    }
    
    public Task<DailyPredictionRecord?> GetDailyPredictionAsync(DateOnly date)
    {
        if (State.Predictions.TryGetValue(date, out var prediction))
        {
            _logger.LogDebug(
                "[LumenDailyYearlyHistory] Found prediction for date: {Date}", date);
            return Task.FromResult<DailyPredictionRecord?>(prediction);
        }
        
        _logger.LogDebug(
            "[LumenDailyYearlyHistory] No prediction found for date: {Date}", date);
        return Task.FromResult<DailyPredictionRecord?>(null);
    }
    
    public Task<List<DailyPredictionRecord>> GetAllDailyPredictionsAsync()
    {
        var predictions = State.Predictions.Values
            .OrderBy(p => p.Date)
            .ToList();
        
        _logger.LogDebug(
            "[LumenDailyYearlyHistory] Retrieved all predictions - Year: {Year}, Count: {Count}",
            State.Year, predictions.Count);
        
        return Task.FromResult(predictions);
    }
    
    public Task<List<DailyPredictionRecord>> GetDailyPredictionsByRangeAsync(DateOnly startDate, DateOnly endDate)
    {
        var predictions = State.Predictions.Values
            .Where(p => p.Date >= startDate && p.Date <= endDate)
            .OrderBy(p => p.Date)
            .ToList();
        
        _logger.LogDebug(
            "[LumenDailyYearlyHistory] Retrieved predictions by range - Start: {Start}, End: {End}, Count: {Count}",
            startDate, endDate, predictions.Count);
        
        return Task.FromResult(predictions);
    }
}

