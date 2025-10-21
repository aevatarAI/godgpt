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
public interface IFortuneFeedbackGAgent : IGAgent, IGrainWithStringKey
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
            if (request.Rating < 1 || request.Rating > 5)
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

            // Check if feedback already exists - do not allow duplicate submission
            if (!string.IsNullOrEmpty(State.FeedbackId))
            {
                _logger.LogWarning("[FortuneFeedbackGAgent][SubmitOrUpdateFeedbackAsync] Feedback already exists: {FeedbackId}, PredictionId: {PredictionId}, Method: {Method}",
                    State.FeedbackId, request.PredictionId, request.PredictionMethod ?? "overall");

                return new SubmitFeedbackResult
                {
                    Success = false,
                    Message = $"Feedback already submitted for this prediction{(string.IsNullOrEmpty(request.PredictionMethod) ? "" : $" ({request.PredictionMethod})")}"
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

