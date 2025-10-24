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
                // Update main state fields (for backward compatibility)
                state.FeedbackId = submittedEvent.FeedbackId;
                state.UserId = submittedEvent.UserId;
                state.PredictionId = submittedEvent.PredictionId;
                
                // Update MethodFeedbacks dictionary
                var updateMethodKey = submittedEvent.PredictionMethod;
                if (state.MethodFeedbacks.TryGetValue(updateMethodKey, out var existingFeedback))
                {
                    // Update existing feedback
                    existingFeedback.PredictionMethod = submittedEvent.PredictionMethod;
                    existingFeedback.Rating = submittedEvent.Rating;
                    existingFeedback.FeedbackTypes = submittedEvent.FeedbackTypes;
                    existingFeedback.Comment = submittedEvent.Comment;
                    existingFeedback.Email = submittedEvent.Email;
                    existingFeedback.AgreeToContact = submittedEvent.AgreeToContact;
                    existingFeedback.UpdatedAt = submittedEvent.CreatedAt;
                }
                else
                {
                    // Create new feedback if not exists
                    state.MethodFeedbacks[updateMethodKey] = new FeedbackDetail
                    {
                        PredictionMethod = submittedEvent.PredictionMethod,
                        Rating = submittedEvent.Rating,
                        FeedbackTypes = submittedEvent.FeedbackTypes,
                        Comment = submittedEvent.Comment,
                        Email = submittedEvent.Email,
                        AgreeToContact = submittedEvent.AgreeToContact,
                        CreatedAt = submittedEvent.CreatedAt,
                        UpdatedAt = submittedEvent.CreatedAt
                    };
                }
                break;

            case FeedbackUpdatedEvent updatedEvent:
                break;
                
            case MethodRatingUpdatedEvent ratingUpdatedEvent:
                state.FeedbackId = ratingUpdatedEvent.FeedbackId;
                state.UserId = ratingUpdatedEvent.UserId;
                state.PredictionId = ratingUpdatedEvent.PredictionId;
                
                var ratingMethodKey = ratingUpdatedEvent.PredictionMethod;
                if (state.MethodFeedbacks.TryGetValue(ratingMethodKey, out var ratingFeedback))
                {
                    // Update existing feedback
                    ratingFeedback.Rating = ratingUpdatedEvent.Rating;
                    ratingFeedback.UpdatedAt = ratingUpdatedEvent.UpdatedAt;
                }
                else
                {
                    // Create new feedback detail if not exists
                    state.MethodFeedbacks[ratingMethodKey] = new FeedbackDetail
                    {
                        PredictionMethod = ratingUpdatedEvent.PredictionMethod,
                        Rating = ratingUpdatedEvent.Rating,                       
                        CreatedAt = ratingUpdatedEvent.UpdatedAt,
                        UpdatedAt = ratingUpdatedEvent.UpdatedAt
                    };
                }
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

            // // Check if this request is trying to submit detailed feedback
            // var requestHasDetails = request.FeedbackTypes.Any() || 
            //                        !string.IsNullOrEmpty(request.Comment) || 
            //                        !string.IsNullOrEmpty(request.Email) || 
            //                        request.AgreeToContact;
            //
            // // Check if feedback already exists
            // if (!string.IsNullOrEmpty(State.FeedbackId))
            // {
            //     // Check if existing feedback has detailed information
            //     var existingHasDetails = State.FeedbackTypes.Any() || 
            //                             !string.IsNullOrEmpty(State.Comment) || 
            //                             !string.IsNullOrEmpty(State.Email) || 
            //                             State.AgreeToContact;
            //
            //     // If request tries to submit detailed info and details already exist, reject
            //     if (requestHasDetails && existingHasDetails)
            //     {
            //         _logger.LogWarning("[FortuneFeedbackGAgent][SubmitOrUpdateFeedbackAsync] Cannot modify detailed feedback: {FeedbackId}",
            //             State.FeedbackId);
            //
            //         return new SubmitFeedbackResult
            //         {
            //             Success = false,
            //             Message = "Detailed feedback already submitted and cannot be modified. You can only update the rating."
            //         };
            //     }
            //
            //     // Allow update:
            //     // - Rating-only update is always allowed
            //     // - Adding details for the first time is allowed (upgrade from simple to detailed)
            //     _logger.LogInformation("[FortuneFeedbackGAgent][SubmitOrUpdateFeedbackAsync] Updating feedback: {FeedbackId}, RatingOnly: {RatingOnly}",
            //         State.FeedbackId, !requestHasDetails);
            //
            //     // Raise update event
            //     // If rating-only update, preserve existing details; otherwise update all fields
            //     RaiseEvent(new FeedbackUpdatedEvent
            //     {
            //         PredictionMethod = request.PredictionMethod,
            //         Rating = request.Rating,
            //         FeedbackTypes = requestHasDetails ? request.FeedbackTypes : State.FeedbackTypes,
            //         Comment = requestHasDetails ? request.Comment : State.Comment,
            //         Email = requestHasDetails ? request.Email : State.Email,
            //         AgreeToContact = requestHasDetails ? request.AgreeToContact : State.AgreeToContact,
            //         UpdatedAt = now
            //     });
            //
            //     await ConfirmEvents();
            //
            //     return new SubmitFeedbackResult
            //     {
            //         Success = true,
            //         Message = string.Empty,
            //         FeedbackId = State.FeedbackId
            //     };
            // }

            // Create new feedback
            var feedbackId = State.FeedbackId;
            if (feedbackId.IsNullOrEmpty())
            {
                feedbackId = Guid.NewGuid().ToString();
            }

            _logger.LogInformation("[FortuneFeedbackGAgent][SubmitOrUpdateFeedbackAsync] Creating new feedback: {FeedbackId}",
                feedbackId);

            // Raise submitted event
            RaiseEvent(new FeedbackSubmittedEvent
            {
                FeedbackId = feedbackId,
                UserId = request.UserId,
                PredictionId = request.PredictionId,
                PredictionMethod =  request.PredictionMethod ?? "overall",
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

    public Task<FeedbackDto?> GetFeedbackAsync(string? predictionMethod = null)
    {
        try
        {
            _logger.LogDebug("[FortuneFeedbackGAgent][GetFeedbackAsync] Getting feedback for method: {PredictionMethod}", 
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
            _logger.LogError(ex, "[FortuneFeedbackGAgent][GetFeedbackAsync] Error getting feedback");
            return Task.FromResult<FeedbackDto?>(null);
        }
    }

    public async Task<UpdateMethodRatingResult> UpdateMethodRatingAsync(UpdateMethodRatingRequest request)
    {
        try
        {
            _logger.LogDebug("[FortuneFeedbackGAgent][UpdateMethodRatingAsync] Start - UserId: {UserId}, Method: {PredictionMethod}, Rating: {Rating}",
                request.UserId, request.PredictionMethod, request.Rating);

            // Validate request
            var validationResult = ValidateUpdateRatingRequest(request);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("[FortuneFeedbackGAgent][UpdateMethodRatingAsync] Validation failed: {Message}",
                    validationResult.Message);
                return new UpdateMethodRatingResult
                {
                    Success = false,
                    Message = validationResult.Message
                };
            }

            // Check if feedback exists
            var feedbackId = State.FeedbackId;
            if (string.IsNullOrEmpty(State.FeedbackId))
            {
                feedbackId = Guid.NewGuid().ToString();
            }
            var methodKey = string.IsNullOrEmpty(request.PredictionMethod) ? "overall" : request.PredictionMethod;
            var now = DateTime.UtcNow;
            // Raise event to update rating
            RaiseEvent(new MethodRatingUpdatedEvent
            {
                FeedbackId = feedbackId,
                UserId = request.UserId,
                PredictionId = request.PredictionId,
                PredictionMethod = methodKey,
                Rating = request.Rating,
                UpdatedAt = now
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[FortuneFeedbackGAgent][UpdateMethodRatingAsync] Rating updated successfully - Method: {PredictionMethod}, Rating: {Rating}",
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
            _logger.LogError(ex, "[FortuneFeedbackGAgent][UpdateMethodRatingAsync] Error updating method rating");
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

        if (request.Rating < 0 || request.Rating > 5)
        {
            return (false, "Rating must be between 0 and 5");
        }

        return (true, string.Empty);
    }
}

