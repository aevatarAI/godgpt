using Aevatar.Application.Grains.Fortune.Dtos;
using Aevatar.Application.Grains.Fortune.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
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
                state.Score = submittedEvent.Score;
                state.CreatedAt = submittedEvent.CreatedAt;
                state.UpdatedAt = submittedEvent.CreatedAt;
                break;

            case FeedbackUpdatedEvent updatedEvent:
                state.Score = updatedEvent.Score;
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

            // Validate score
            if (request.Score < 1 || request.Score > 10)
            {
                return new SubmitFeedbackResult
                {
                    Success = false,
                    Message = "Score must be between 1 and 10"
                };
            }

            var now = DateTime.UtcNow;

            // Check if feedback already exists (update scenario)
            if (!string.IsNullOrEmpty(State.FeedbackId))
            {
                _logger.LogInformation("[FortuneFeedbackGAgent][SubmitOrUpdateFeedbackAsync] Updating existing feedback: {FeedbackId}",
                    State.FeedbackId);

                // Raise update event
                RaiseEvent(new FeedbackUpdatedEvent
                {
                    Score = request.Score,
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
                Score = request.Score,
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
                Score = State.Score,
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

