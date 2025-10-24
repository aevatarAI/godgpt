using Aevatar.Application.Grains.Fortune.Dtos;
using Aevatar.Application.Grains.Fortune.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Fortune;

/// <summary>
/// Interface for Fortune Feedback GAgent - manages user feedback on predictions
/// </summary>
public interface IFortuneFeedbackGAgent : IGAgent
{
    Task<SubmitFeedbackResult> SubmitOrUpdateFeedbackAsync(SubmitFeedbackRequest request);
    
    [ReadOnly]
    Task<FeedbackDto?> GetFeedbackAsync();
}

[GAgent(nameof(FortuneFeedbackGAgent))]
[Reentrant]
public class FortuneFeedbackGAgent : GAgentBase<FortuneFeedbackState, FortuneFeedbackEventLog>,
    IFortuneFeedbackGAgent
{
    private readonly ILogger<FortuneFeedbackGAgent> _logger;

    public FortuneFeedbackGAgent(ILogger<FortuneFeedbackGAgent> logger)
    {
        _logger = logger;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Fortune feedback management");
    }

    /// <summary>
    /// Event-driven state transition handler
    /// </summary>
    protected sealed override void GAgentTransitionState(FortuneFeedbackState state,
        StateLogEventBase<FortuneFeedbackEventLog> @event)
    {
        switch (@event)
        {
            case FeedbackSubmittedEvent submittedEvent:
                state.FeedbackId = submittedEvent.FeedbackId;
                state.UserId = submittedEvent.UserId;
                state.PredictionId = submittedEvent.PredictionId;
                state.PredictionMethod = submittedEvent.PredictionMethod;
                state.Rating = submittedEvent.Rating;
                state.FeedbackTypes = submittedEvent.FeedbackTypes;
                state.Comment = submittedEvent.Comment;
                state.Email = submittedEvent.Email;
                state.AgreeToContact = submittedEvent.AgreeToContact;
                state.CreatedAt = submittedEvent.CreatedAt;
                state.UpdatedAt = submittedEvent.CreatedAt;
                break;

            case FeedbackUpdatedEvent updatedEvent:
                state.PredictionMethod = updatedEvent.PredictionMethod;
                state.Rating = updatedEvent.Rating;
                state.FeedbackTypes = updatedEvent.FeedbackTypes;
                state.Comment = updatedEvent.Comment;
                state.Email = updatedEvent.Email;
                state.AgreeToContact = updatedEvent.AgreeToContact;
                state.UpdatedAt = updatedEvent.UpdatedAt;
                break;
        }
    }

    public async Task<SubmitFeedbackResult> SubmitOrUpdateFeedbackAsync(SubmitFeedbackRequest request)
    {
        try
        {
            _logger.LogDebug("[FortuneFeedbackGAgent][SubmitOrUpdateFeedbackAsync] Start - UserId: {UserId}, PredictionId: {PredictionId}",
                request.UserId, request.PredictionId);

            // Validate rating
            if (request.Rating < 0 || request.Rating > 5)
            {
                return new SubmitFeedbackResult
                {
                    Success = false,
                    Message = "Rating must be between 1 and 5"
                };
            }

            // Validate prediction method if specified
            if (!string.IsNullOrEmpty(request.PredictionMethod))
            {
                var validMethods = new[]
                {
                    "forecast", "horoscope", "bazi", "ziwei", "constellation",
                    "numerology", "synastry", "chineseZodiac", "mayanTotem",
                    "humanFigure", "tarot", "zhengYu"
                };

                if (!validMethods.Contains(request.PredictionMethod))
                {
                    return new SubmitFeedbackResult
                    {
                        Success = false,
                        Message = $"Invalid prediction method: {request.PredictionMethod}. Valid methods are: {string.Join(", ", validMethods)}"
                    };
                }
            }

            var now = DateTime.UtcNow;

            // Check if this request is trying to submit detailed feedback
            var requestHasDetails = request.FeedbackTypes.Any() || 
                                   !string.IsNullOrEmpty(request.Comment) || 
                                   !string.IsNullOrEmpty(request.Email) || 
                                   request.AgreeToContact;

            // Check if feedback already exists
            if (!string.IsNullOrEmpty(State.FeedbackId))
            {
                // Check if existing feedback has detailed information
                var existingHasDetails = State.FeedbackTypes.Any() || 
                                        !string.IsNullOrEmpty(State.Comment) || 
                                        !string.IsNullOrEmpty(State.Email) || 
                                        State.AgreeToContact;

                // If request tries to submit detailed info and details already exist, reject
                if (requestHasDetails && existingHasDetails)
                {
                    _logger.LogWarning("[FortuneFeedbackGAgent][SubmitOrUpdateFeedbackAsync] Cannot modify detailed feedback: {FeedbackId}",
                        State.FeedbackId);

                    return new SubmitFeedbackResult
                    {
                        Success = false,
                        Message = "Detailed feedback already submitted and cannot be modified. You can only update the rating."
                    };
                }

                // Allow update:
                // - Rating-only update is always allowed
                // - Adding details for the first time is allowed (upgrade from simple to detailed)
                _logger.LogInformation("[FortuneFeedbackGAgent][SubmitOrUpdateFeedbackAsync] Updating feedback: {FeedbackId}, RatingOnly: {RatingOnly}",
                    State.FeedbackId, !requestHasDetails);

                // Raise update event
                // If rating-only update, preserve existing details; otherwise update all fields
                RaiseEvent(new FeedbackUpdatedEvent
                {
                    PredictionMethod = request.PredictionMethod,
                    Rating = request.Rating,
                    FeedbackTypes = requestHasDetails ? request.FeedbackTypes : State.FeedbackTypes,
                    Comment = requestHasDetails ? request.Comment : State.Comment,
                    Email = requestHasDetails ? request.Email : State.Email,
                    AgreeToContact = requestHasDetails ? request.AgreeToContact : State.AgreeToContact,
                    UpdatedAt = now
                });

                await ConfirmEvents();

                return new SubmitFeedbackResult
                {
                    Success = true,
                    Message = string.Empty,
                    FeedbackId = State.FeedbackId
                };
            }

            // Create new feedback
            var feedbackId = Guid.NewGuid().ToString();

            _logger.LogInformation("[FortuneFeedbackGAgent][SubmitOrUpdateFeedbackAsync] Creating new feedback: {FeedbackId}",
                feedbackId);

            // Raise submitted event
            RaiseEvent(new FeedbackSubmittedEvent
            {
                FeedbackId = feedbackId,
                UserId = request.UserId,
                PredictionId = request.PredictionId,
                PredictionMethod = request.PredictionMethod,
                Rating = request.Rating,
                FeedbackTypes = request.FeedbackTypes,
                Comment = request.Comment,
                Email = request.Email,
                AgreeToContact = request.AgreeToContact,
                CreatedAt = now
            });

            await ConfirmEvents();

            return new SubmitFeedbackResult
            {
                Success = true,
                Message = string.Empty,
                FeedbackId = feedbackId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortuneFeedbackGAgent][SubmitOrUpdateFeedbackAsync] Error submitting feedback");
            return new SubmitFeedbackResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    public Task<FeedbackDto?> GetFeedbackAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(State.FeedbackId))
            {
                return Task.FromResult<FeedbackDto?>(null);
            }

            return Task.FromResult<FeedbackDto?>(new FeedbackDto
            {
                FeedbackId = State.FeedbackId,
                UserId = State.UserId,
                PredictionId = State.PredictionId,
                PredictionMethod = State.PredictionMethod,
                Rating = State.Rating,
                FeedbackTypes = State.FeedbackTypes,
                Comment = State.Comment,
                Email = State.Email,
                AgreeToContact = State.AgreeToContact,
                CreatedAt = State.CreatedAt,
                UpdatedAt = State.UpdatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FortuneFeedbackGAgent][GetFeedbackAsync] Error getting feedback");
            return Task.FromResult<FeedbackDto?>(null);
        }
    }
}

