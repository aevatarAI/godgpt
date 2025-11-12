using Aevatar.Application.Grains.Lumen.Dtos;
using Aevatar.Application.Grains.Lumen.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Lumen;

/// <summary>
/// Interface for Fortune Favourite GAgent - manages user's favourite predictions
/// </summary>
public interface IFortuneFavouriteGAgent : IGAgent
{
    Task<ToggleFavouriteResult> ToggleFavouriteAsync(ToggleFavouriteRequest request);
    
    [ReadOnly]
    Task<GetFavouritesResult> GetFavouritesAsync();
    
    [ReadOnly]
    Task<bool> IsFavouriteAsync(Guid predictionId);
}

[GAgent(nameof(FortuneFavouriteGAgent))]
[Reentrant]
public class FortuneFavouriteGAgent : GAgentBase<FortuneFavouriteState, FortuneFavouriteEventLog>,
    IFortuneFavouriteGAgent
{
    private readonly ILogger<FortuneFavouriteGAgent> _logger;
    private const int MaxFavourites = 100; // Maximum 100 favourites per user

    public FortuneFavouriteGAgent(ILogger<FortuneFavouriteGAgent> logger)
    {
        _logger = logger;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Fortune favourite management");
    }

    /// <summary>
    /// Event-driven state transition handler
    /// </summary>
    protected sealed override void GAgentTransitionState(FortuneFavouriteState state,
        StateLogEventBase<FortuneFavouriteEventLog> @event)
    {
        switch (@event)
        {
            case PredictionFavouritedEvent favouritedEvent:
                state.UserId = favouritedEvent.UserId;
                state.Favourites[favouritedEvent.PredictionId] = favouritedEvent.FavouriteDetail;
                state.LastUpdatedAt = favouritedEvent.FavouritedAt;
                break;
                
            case PredictionUnfavouritedEvent unfavouritedEvent:
                state.UserId = unfavouritedEvent.UserId;
                state.Favourites.Remove(unfavouritedEvent.PredictionId);
                state.LastUpdatedAt = unfavouritedEvent.UnfavouritedAt;
                break;
        }
    }

    public async Task<ToggleFavouriteResult> ToggleFavouriteAsync(ToggleFavouriteRequest request)
    {
        try
        {
            _logger.LogDebug("[FortuneFavouriteGAgent][ToggleFavouriteAsync] Start - UserId: {UserId}, PredictionId: {PredictionId}, Date: {Date}, IsFavourite: {IsFavourite}",
                request.UserId, request.PredictionId, request.Date, request.IsFavourite);

            // Validate request
            var validationResult = ValidateToggleRequest(request);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("[FortuneFavouriteGAgent][ToggleFavouriteAsync] Validation failed: {Message}",
                    validationResult.Message);
                return new ToggleFavouriteResult
                {
                    Success = false,
                    Message = validationResult.Message
                };
            }

            var now = DateTime.UtcNow;
            var isCurrentlyFavourite = State.Favourites.ContainsKey(request.PredictionId);

            // Check if action is needed
            if (request.IsFavourite == isCurrentlyFavourite)
            {
                _logger.LogInformation("[FortuneFavouriteGAgent][ToggleFavouriteAsync] No change needed - PredictionId: {PredictionId}, IsFavourite: {IsFavourite}",
                    request.PredictionId, request.IsFavourite);
                return new ToggleFavouriteResult
                {
                    Success = true,
                    Message = string.Empty,
                    IsFavourite = isCurrentlyFavourite,
                    UpdatedAt = State.LastUpdatedAt
                };
            }

            if (request.IsFavourite)
            {
                // Add to favourites
                // Check max limit
                if (State.Favourites.Count >= MaxFavourites)
                {
                    _logger.LogWarning("[FortuneFavouriteGAgent][ToggleFavouriteAsync] Max favourites limit reached: {UserId}",
                        request.UserId);
                    return new ToggleFavouriteResult
                    {
                        Success = false,
                        Message = $"Maximum favourites limit ({MaxFavourites}) reached. Please remove some favourites first."
                    };
                }

                var favouriteDetail = new FavouriteDetail
                {
                    Date = request.Date,
                    PredictionId = request.PredictionId,
                    FavouritedAt = now
                };

                RaiseEvent(new PredictionFavouritedEvent
                {
                    UserId = request.UserId,
                    Date = request.Date,
                    PredictionId = request.PredictionId,
                    FavouriteDetail = favouriteDetail,
                    FavouritedAt = now
                });
            }
            else
            {
                // Remove from favourites
                RaiseEvent(new PredictionUnfavouritedEvent
                {
                    UserId = request.UserId,
                    PredictionId = request.PredictionId,
                    UnfavouritedAt = now
                });
            }

            await ConfirmEvents();

            _logger.LogInformation("[FortuneFavouriteGAgent][ToggleFavouriteAsync] Success - UserId: {UserId}, PredictionId: {PredictionId}, IsFavourite: {IsFavourite}",
                request.UserId, request.PredictionId, request.IsFavourite);

            return new ToggleFavouriteResult
            {
                Success = true,
                Message = string.Empty,
                IsFavourite = request.IsFavourite,
                UpdatedAt = now
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortuneFavouriteGAgent][ToggleFavouriteAsync] Error toggling favourite: {UserId}",
                request.UserId);
            return new ToggleFavouriteResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    public Task<GetFavouritesResult> GetFavouritesAsync()
    {
        try
        {
            _logger.LogDebug("[FortuneFavouriteGAgent][GetFavouritesAsync] Getting favourites for user: {UserId}",
                State.UserId);

            var favourites = State.Favourites.Values
                .OrderByDescending(f => f.Date)
                .Select(f => new FavouriteItemDto
                {
                    Date = f.Date,
                    PredictionId = f.PredictionId,
                    FavouritedAt = f.FavouritedAt
                })
                .ToList();

            _logger.LogInformation("[FortuneFavouriteGAgent][GetFavouritesAsync] Found {Count} favourites",
                favourites.Count);

            return Task.FromResult(new GetFavouritesResult
            {
                Success = true,
                Message = string.Empty,
                Favourites = favourites
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortuneFavouriteGAgent][GetFavouritesAsync] Error getting favourites");
            return Task.FromResult(new GetFavouritesResult
            {
                Success = false,
                Message = "Internal error occurred",
                Favourites = new List<FavouriteItemDto>()
            });
        }
    }

    public Task<bool> IsFavouriteAsync(Guid predictionId)
    {
        try
        {
            _logger.LogDebug("[FortuneFavouriteGAgent][IsFavouriteAsync] Checking if prediction is favourite: {PredictionId}", predictionId);
            return Task.FromResult(State.Favourites.ContainsKey(predictionId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortuneFavouriteGAgent][IsFavouriteAsync] Error checking favourite");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Validate toggle favourite request
    /// </summary>
    private (bool IsValid, string Message) ValidateToggleRequest(ToggleFavouriteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return (false, "UserId is required");
        }

        if (request.PredictionId == Guid.Empty)
        {
            return (false, "PredictionId is required");
        }

        if (request.Date == default)
        {
            return (false, "Date is required");
        }

        // Validate date is not in the future
        if (request.Date > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return (false, "Cannot favourite future predictions");
        }

        return (true, string.Empty);
    }
}

