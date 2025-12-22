using Aevatar.Application.Grains.Lumen.Dtos;
using Aevatar.Application.Grains.Lumen.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Lumen;

/// <summary>
/// Interface for Lumen Feedback GAgent - manages user feedback on predictions
/// </summary>
public interface ILumenFeedbackGAgent : IGAgent
{
    Task<SubmitFeedbackResult> SubmitOrUpdateFeedbackAsync(SubmitFeedbackRequest request);
    
    /// <summary>
    /// Get feedback - main feedback or specific prediction method feedback
    /// </summary>
    [ReadOnly]
    Task<FeedbackDto?> GetFeedbackAsync(string? predictionMethod = null);
    
    /// <summary>
    /// Update rating for a specific prediction method
    /// </summary>
    Task<UpdateMethodRatingResult> UpdateMethodRatingAsync(UpdateMethodRatingRequest request);
}

[GAgent(nameof(LumenFeedbackGAgent))]
[Reentrant]
public class LumenFeedbackGAgent : GAgentBase<LumenFeedbackState, LumenFeedbackEventLog>,
    ILumenFeedbackGAgent
{
    private readonly ILogger<LumenFeedbackGAgent> _logger;

    public LumenFeedbackGAgent(ILogger<LumenFeedbackGAgent> logger)
    {
        _logger = logger;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Lumen feedback management");
    }

    /// <summary>
    /// Event-driven state transition handler
    /// </summary>
    protected sealed override void GAgentTransitionState(LumenFeedbackState state,
        StateLogEventBase<LumenFeedbackEventLog> @event)
    {
        switch (@event)
        {
            case FeedbackSubmittedEvent submittedEvent:
                state.FeedbackId = submittedEvent.FeedbackId;
                state.UserId = submittedEvent.UserId;
                state.PredictionId = submittedEvent.PredictionId;
                state.MethodFeedbacks[submittedEvent.PredictionMethod] = submittedEvent.FeedbackDetail;
                break;
                
            case MethodRatingUpdatedEvent ratingUpdatedEvent:
                state.FeedbackId = ratingUpdatedEvent.FeedbackId;
                state.UserId = ratingUpdatedEvent.UserId;
                state.PredictionId = ratingUpdatedEvent.PredictionId;
                state.MethodFeedbacks[ratingUpdatedEvent.PredictionMethod] = ratingUpdatedEvent.FeedbackDetail;
                break;
        }
    }

    public async Task<SubmitFeedbackResult> SubmitOrUpdateFeedbackAsync(SubmitFeedbackRequest request)
    {
        try
        {
            _logger.LogDebug("[LumenFeedbackGAgent][SubmitOrUpdateFeedbackAsync] Start - UserId: {UserId}, PredictionId: {PredictionId}",
                request.UserId, request.PredictionId);

            // Validate rating
            if (request.Rating < 0 || request.Rating > 1)
            {
                return new SubmitFeedbackResult
                {
                    Success = false,
                    Message = "Rating must be 0 or 1"
                };
            }

            // Validate prediction method is required
            if (string.IsNullOrWhiteSpace(request.PredictionMethod))
            {
                return new SubmitFeedbackResult
                {
                    Success = false,
                    Message = "PredictionMethod is required"
                };
            }

            var now = DateTime.UtcNow;
            var methodKey = request.PredictionMethod;
            
            // Build complete FeedbackDetail object
            FeedbackDetail newFeedbackDetail;
            if (State.MethodFeedbacks.TryGetValue(methodKey, out var existingFeedback))
            {
                // Update existing feedback with new values
                newFeedbackDetail = new FeedbackDetail
                {
                    PredictionMethod = methodKey,
                    Rating = request.Rating,
                    FeedbackTypes = request.FeedbackTypes,
                    Comment = request.Comment,
                    Email = request.Email,
                    AgreeToContact = request.AgreeToContact,
                    CreatedAt = existingFeedback.CreatedAt,
                    UpdatedAt = now
                };
            }
            else
            {
            // Create new feedback
                newFeedbackDetail = new FeedbackDetail
                {
                    PredictionMethod = methodKey,
                    Rating = request.Rating,
                    FeedbackTypes = request.FeedbackTypes,
                    Comment = request.Comment,
                    Email = request.Email,
                    AgreeToContact = request.AgreeToContact,
                    CreatedAt = now,
                    UpdatedAt = now
                };
            }

            var feedbackId = State.FeedbackId;
            if (feedbackId.IsNullOrEmpty())
            {
                feedbackId = Guid.NewGuid().ToString();
            }

            _logger.LogInformation("[LumenFeedbackGAgent][SubmitOrUpdateFeedbackAsync] Submitting feedback: {FeedbackId}",
                feedbackId);

            // Raise submitted event with complete FeedbackDetail
            RaiseEvent(new FeedbackSubmittedEvent
            {
                FeedbackId = feedbackId,
                UserId = request.UserId,
                PredictionId = request.PredictionId,
                PredictionMethod = methodKey,
                FeedbackDetail = newFeedbackDetail,
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
            _logger.LogError(ex, "[LumenFeedbackGAgent][SubmitOrUpdateFeedbackAsync] Error submitting feedback");
            return new SubmitFeedbackResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    public Task<FeedbackDto?> GetFeedbackAsync(string? predictionMethod = null)
    {
        try
        {
            _logger.LogDebug("[LumenFeedbackGAgent][GetFeedbackAsync] Getting feedback for method: {PredictionMethod}", 
                predictionMethod ?? "main");

            if (string.IsNullOrEmpty(State.FeedbackId))
            {
                return Task.FromResult<FeedbackDto?>(null);
            }

            // If no specific method requested, return main feedback (backward compatibility)
            if (predictionMethod.IsNullOrWhiteSpace())
            {
                return Task.FromResult<FeedbackDto?>(new FeedbackDto
                {
                    FeedbackId = State.FeedbackId,
                    UserId = State.UserId,
                    PredictionId = State.PredictionId,
                    MethodFeedbacks = State.MethodFeedbacks
                });
            }

            // Get specific method feedback
            if (State.MethodFeedbacks.TryGetValue(predictionMethod, out var methodFeedback))
            {
                return Task.FromResult<FeedbackDto?>(new FeedbackDto
                {
                    FeedbackId = State.FeedbackId, // Use main feedback ID
                    UserId = State.UserId,
                    PredictionId = State.PredictionId,
                    MethodFeedbacks = new Dictionary<string, FeedbackDetail>() {{predictionMethod, methodFeedback}}
                });
            }

            // Method not found, return null
            return Task.FromResult<FeedbackDto?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenFeedbackGAgent][GetFeedbackAsync] Error getting feedback");
            return Task.FromResult<FeedbackDto?>(null);
        }
    }

    public async Task<UpdateMethodRatingResult> UpdateMethodRatingAsync(UpdateMethodRatingRequest request)
    {
        try
        {
            _logger.LogDebug("[LumenFeedbackGAgent][UpdateMethodRatingAsync] Start - UserId: {UserId}, Method: {PredictionMethod}, Rating: {Rating}",
                request.UserId, request.PredictionMethod, request.Rating);

            // Validate request
            var validationResult = ValidateUpdateRatingRequest(request);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("[LumenFeedbackGAgent][UpdateMethodRatingAsync] Validation failed: {Message}",
                    validationResult.Message);
                return new UpdateMethodRatingResult
                {
                    Success = false,
                    Message = validationResult.Message
                };
            }

            var methodKey = request.PredictionMethod;
            var now = DateTime.UtcNow;
            
            // Log current state before update
            _logger.LogInformation("[LumenFeedbackGAgent][UpdateMethodRatingAsync] Current State - FeedbackId: {FeedbackId}, UserId: {UserId}, PredictionId: {PredictionId}, MethodFeedbacks Count: {Count}",
                State.FeedbackId, State.UserId, State.PredictionId, State.MethodFeedbacks.Count);
            
            if (State.MethodFeedbacks.TryGetValue(methodKey, out var existingFeedbackCheck))
            {
                _logger.LogInformation("[LumenFeedbackGAgent][UpdateMethodRatingAsync] Existing feedback found for method {Method}: Rating={Rating}",
                    methodKey, existingFeedbackCheck.Rating);
            }
            else
            {
                _logger.LogInformation("[LumenFeedbackGAgent][UpdateMethodRatingAsync] No existing feedback for method {Method}",
                    methodKey);
            }
            
            // Build complete FeedbackDetail object
            FeedbackDetail newFeedbackDetail;
            if (State.MethodFeedbacks.TryGetValue(methodKey, out var existingFeedback))
            {
                // Update existing feedback with new rating
                newFeedbackDetail = new FeedbackDetail
                {
                    PredictionMethod = existingFeedback.PredictionMethod,
                    Rating = request.Rating,
                    FeedbackTypes = existingFeedback.FeedbackTypes,
                    Comment = existingFeedback.Comment,
                    Email = existingFeedback.Email,
                    AgreeToContact = existingFeedback.AgreeToContact,
                    CreatedAt = existingFeedback.CreatedAt,
                    UpdatedAt = now
                };
                _logger.LogInformation("[LumenFeedbackGAgent][UpdateMethodRatingAsync] Updating existing feedback: OldRating={OldRating}, NewRating={NewRating}",
                    existingFeedback.Rating, request.Rating);
            }
            else
            {
                // Create new feedback with only rating
                newFeedbackDetail = new FeedbackDetail
                {
                    PredictionMethod = methodKey,
                    Rating = request.Rating,
                    FeedbackTypes = new List<string>(),
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _logger.LogInformation("[LumenFeedbackGAgent][UpdateMethodRatingAsync] Creating new feedback: Rating={Rating}",
                    request.Rating);
            }

            var feedbackId = State.FeedbackId;
            if (string.IsNullOrEmpty(feedbackId))
            {
                feedbackId = Guid.NewGuid().ToString();
            }
            
            // Raise event with complete FeedbackDetail
            RaiseEvent(new MethodRatingUpdatedEvent
            {
                FeedbackId = feedbackId,
                UserId = request.UserId,
                PredictionId = request.PredictionId,
                PredictionMethod = methodKey,
                FeedbackDetail = newFeedbackDetail,
                UpdatedAt = now
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            // Log final state after update
            _logger.LogInformation("[LumenFeedbackGAgent][UpdateMethodRatingAsync] After ConfirmEvents - State.MethodFeedbacks Count: {Count}",
                State.MethodFeedbacks.Count);
            
            if (State.MethodFeedbacks.TryGetValue(methodKey, out var finalFeedback))
            {
                _logger.LogInformation("[LumenFeedbackGAgent][UpdateMethodRatingAsync] Final feedback state for method {Method}: Rating={Rating}, UpdatedAt={UpdatedAt}",
                    methodKey, finalFeedback.Rating, finalFeedback.UpdatedAt);
            }

            _logger.LogInformation("[LumenFeedbackGAgent][UpdateMethodRatingAsync] Rating updated successfully - Method: {PredictionMethod}, Rating: {Rating}",
                request.PredictionMethod, request.Rating);

            return new UpdateMethodRatingResult
            {
                Success = true,
                Message = string.Empty,
                PredictionMethod = request.PredictionMethod,
                UpdatedRating = request.Rating,
                UpdatedAt = now
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenFeedbackGAgent][UpdateMethodRatingAsync] Error updating method rating");
            return new UpdateMethodRatingResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    /// <summary>
    /// Validate update rating request
    /// </summary>
    private (bool IsValid, string Message) ValidateUpdateRatingRequest(UpdateMethodRatingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return (false, "UserId is required");
        }

        if (request.PredictionId == Guid.Empty)
        {
            return (false, "PredictionId is required");
        }

        if (request.Rating < 0 || request.Rating > 1)
        {
            return (false, "Rating must be 0 or 1");
        }

        // Validate prediction method is required
        if (string.IsNullOrWhiteSpace(request.PredictionMethod))
        {
            return (false, "PredictionMethod is required");
        }

        return (true, string.Empty);
    }
}

