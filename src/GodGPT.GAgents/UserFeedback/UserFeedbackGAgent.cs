using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Service;
using Aevatar.Application.Grains.UserFeedback.Dtos;
using Aevatar.Application.Grains.UserFeedback.Options;
using Aevatar.Application.Grains.UserFeedback.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.UserFeedback;

/// <summary>
/// Interface for User Feedback GAgent - manages user feedback collection and frequency control
/// </summary>
public interface IUserFeedbackGAgent : IGAgent
{
    Task<SubmitFeedbackResult> SubmitFeedbackAsync(SubmitFeedbackRequest request);
    [ReadOnly]
    Task<CheckEligibilityResult> CheckFeedbackEligibilityAsync();
    [ReadOnly]
    Task<GetFeedbackHistoryResult> GetFeedbackHistoryAsync(GetFeedbackHistoryRequest request);
}

[GAgent(nameof(UserFeedbackGAgent))]
[Reentrant]
public class UserFeedbackGAgent : GAgentBase<UserFeedbackState, UserFeedbackEventLog>, 
    IUserFeedbackGAgent
{
    private readonly ILogger<UserFeedbackGAgent> _logger;
    private readonly ILocalizationService _localizationService;
    private readonly IOptionsMonitor<UserFeedbackOptions> _feedbackOptions;

    public UserFeedbackGAgent(
        ILogger<UserFeedbackGAgent> logger,
        ILocalizationService localizationService,
        IOptionsMonitor<UserFeedbackOptions> feedbackOptions)
    {
        _logger = logger;
        _localizationService = localizationService;
        _feedbackOptions = feedbackOptions;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("User feedback management and collection");
    }

    /// <summary>
    /// Event-driven state transition handler
    /// </summary>
    protected sealed override void GAgentTransitionState(UserFeedbackState state,
        StateLogEventBase<UserFeedbackEventLog> @event)
    {
        switch (@event)
        {
            case SubmitFeedbackLogEvent submitEvent:
                // Archive current feedback before adding new one
                if (state.CurrentFeedback != null)
                {
                    var archivedData = JsonConvert.SerializeObject(state.CurrentFeedback);
                    state.ArchivedFeedbacks.Add(archivedData);
                    
                    // Limit archived data count
                    var maxArchived = _feedbackOptions.CurrentValue.MaxArchivedFeedbacks;
                    if (state.ArchivedFeedbacks.Count > maxArchived)
                    {
                        state.ArchivedFeedbacks.RemoveAt(0);
                    }
                }

                state.UserId = submitEvent.UserId;
                state.CurrentFeedback = submitEvent.FeedbackInfo;
                state.LastFeedbackTime = submitEvent.SubmittedAt;
                state.FeedbackCount = submitEvent.FeedbackCount;
                if (state.CreatedAt == default)
                {
                    state.CreatedAt = submitEvent.SubmittedAt;
                }
                state.UpdatedAt = submitEvent.SubmittedAt;
                break;
        }
    }

    public async Task<SubmitFeedbackResult> SubmitFeedbackAsync(SubmitFeedbackRequest request)
    {
        try
        {
            _logger.LogDebug("[UserFeedbackGAgent][SubmitFeedbackAsync] Start - UserId: {UserId}, FeedbackType: {FeedbackType}",
                this.GetPrimaryKey(), request.FeedbackType);
            
            var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
            
            // Validate request
            var validationResult = ValidateSubmitRequest(request, language);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("[UserFeedbackGAgent][SubmitFeedbackAsync] Validation failed for user {UserId}: {ErrorCode} - {Message}",
                    this.GetPrimaryKey(), validationResult.ErrorCode, validationResult.Message);
                    
                return new SubmitFeedbackResult
                {
                    Success = false,
                    Message = validationResult.Message,
                    ErrorCode = validationResult.ErrorCode
                };
            }

            // Check frequency limit
            var eligibilityResult = await CheckFeedbackEligibilityAsync();
            if (!eligibilityResult.Eligible)
            {
                _logger.LogWarning("[UserFeedbackGAgent][SubmitFeedbackAsync] Frequency limit exceeded for user {UserId}. Last feedback: {LastFeedbackTime}",
                    this.GetPrimaryKey(), eligibilityResult.LastFeedbackTime);
                    
                return new SubmitFeedbackResult
                {
                    Success = false,
                    Message = eligibilityResult.Message,
                    ErrorCode = "FREQUENCY_LIMIT_EXCEEDED"
                };
            }

            // Generate English reason texts
            var englishReasonTexts = FeedbackReasonHelper.GetEnglishReasonTexts(request.Reasons, _localizationService);

            // Create feedback info
            var feedbackInfo = new UserFeedbackInfo
            {
                FeedbackId = Guid.NewGuid().ToString(),
                FeedbackType = request.FeedbackType,
                Reasons = request.Reasons,
                Response = request.Response?.Trim() ?? string.Empty,
                ContactRequested = request.ContactRequested,
                Email = request.Email?.Trim() ?? string.Empty,
                SubmittedAt = DateTime.UtcNow,
                ReasonTextsEnglish = englishReasonTexts
            };

            // Raise event to update state
            RaiseEvent(new SubmitFeedbackLogEvent
            {
                UserId = this.GetPrimaryKey(),
                FeedbackInfo = feedbackInfo,
                SubmittedAt = DateTime.UtcNow,
                FeedbackCount = State.FeedbackCount + 1
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation("[UserFeedbackGAgent][SubmitFeedbackAsync] User feedback submitted successfully. UserId: {UserId}, FeedbackId: {FeedbackId}, Type: {Type}",
                this.GetPrimaryKey(), feedbackInfo.FeedbackId, request.FeedbackType);

            return new SubmitFeedbackResult
            {
                Success = true,
                Message = string.Empty,
            };
        }
        catch (Exception ex)
        {
            var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
            _logger.LogError(ex, "[UserFeedbackGAgent][SubmitFeedbackAsync] Error submitting feedback for user: {UserId}", this.GetPrimaryKey());
            
            return new SubmitFeedbackResult
            {
                Success = false,
                Message = _localizationService.GetLocalizedMessage("feedback_submission_failed", language),
                ErrorCode = "INTERNAL_ERROR"
            };
        }
    }

    public Task<CheckEligibilityResult> CheckFeedbackEligibilityAsync()
    {
        _logger.LogDebug("[UserFeedbackGAgent][CheckFeedbackEligibilityAsync] Checking eligibility for user {UserId}",
            this.GetPrimaryKey());
            
        var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
        
        // If no previous feedback, user is eligible
        if (State.LastFeedbackTime == null)
        {
        return Task.FromResult(new CheckEligibilityResult
        {
            Eligible = true,
            Message = string.Empty
        });
        }

        var timeSinceLastFeedback = DateTime.UtcNow - State.LastFeedbackTime.Value;
        var frequencyDays = _feedbackOptions.CurrentValue.FeedbackFrequencyDays;
        var isEligible = timeSinceLastFeedback.TotalDays >= frequencyDays;
        
        if (isEligible)
        {
            return Task.FromResult(new CheckEligibilityResult
            {
                Eligible = true,
                LastFeedbackTime = State.LastFeedbackTime,
                Message = string.Empty
            });
        }

        var nextEligibleTime = State.LastFeedbackTime.Value.AddDays(frequencyDays);
        var daysRemaining = (int)Math.Ceiling((nextEligibleTime - DateTime.UtcNow).TotalDays);
        
        return Task.FromResult(new CheckEligibilityResult
        {
            Eligible = false,
            LastFeedbackTime = State.LastFeedbackTime,
            NextEligibleTime = nextEligibleTime,
            Message = _localizationService.GetLocalizedMessage("feedback_frequency_limit", language, 
                new Dictionary<string, string> { { "days", daysRemaining.ToString() } })
        });
    }

    public Task<GetFeedbackHistoryResult> GetFeedbackHistoryAsync(GetFeedbackHistoryRequest request)
    {
        _logger.LogDebug("[UserFeedbackGAgent][GetFeedbackHistoryAsync] Getting feedback history for user {UserId}. PageSize: {PageSize}, PageIndex: {PageIndex}",
            this.GetPrimaryKey(), request.PageSize, request.PageIndex);
            
        var allFeedbacks = new List<FeedbackHistoryItem>();
        
        // Add current feedback if exists
        if (State.CurrentFeedback != null)
        {
                allFeedbacks.Add(new FeedbackHistoryItem
                {
                    FeedbackId = State.CurrentFeedback.FeedbackId,
                    FeedbackType = State.CurrentFeedback.FeedbackType,
                    Reasons = State.CurrentFeedback.Reasons,
                    Response = State.CurrentFeedback.Response,
                    ContactRequested = State.CurrentFeedback.ContactRequested,
                    Email = State.CurrentFeedback.Email,
                    SubmittedAt = State.CurrentFeedback.SubmittedAt
            });
        }
        
        // Add archived feedbacks
        foreach (var archivedJson in State.ArchivedFeedbacks)
        {
            var archivedFeedback = JsonConvert.DeserializeObject<UserFeedbackInfo>(archivedJson);
            if (archivedFeedback != null)
            {
                allFeedbacks.Add(new FeedbackHistoryItem
                {
                    FeedbackId = archivedFeedback.FeedbackId,
                    FeedbackType = archivedFeedback.FeedbackType,
                    Reasons = archivedFeedback.Reasons,
                    Response = archivedFeedback.Response,
                    ContactRequested = archivedFeedback.ContactRequested,
                    Email = archivedFeedback.Email,
                    SubmittedAt = archivedFeedback.SubmittedAt
                });
            }
        }
        
        // Sort by submission time (newest first)
        allFeedbacks = allFeedbacks.OrderByDescending(f => f.SubmittedAt).ToList();
        
        // Apply pagination
        var totalCount = allFeedbacks.Count;
        var pagedFeedbacks = allFeedbacks
            .Skip(request.PageIndex * request.PageSize)
            .Take(request.PageSize)
            .ToList();
        
        var hasMore = (request.PageIndex + 1) * request.PageSize < totalCount;
        
        _logger.LogDebug("[UserFeedbackGAgent][GetFeedbackHistoryAsync] Retrieved {Count} feedbacks for user {UserId}. Total: {TotalCount}, HasMore: {HasMore}",
            pagedFeedbacks.Count, this.GetPrimaryKey(), totalCount, hasMore);
        
        return Task.FromResult(new GetFeedbackHistoryResult
        {
            Feedbacks = pagedFeedbacks,
            TotalCount = totalCount,
            HasMore = hasMore
        });
    }
    
    /// <summary>
    /// Validate submit feedback request
    /// </summary>
    private (bool IsValid, string Message, string ErrorCode) ValidateSubmitRequest(
        SubmitFeedbackRequest request, GodGPTLanguage language)
    {

        if (!IsValidFeedbackType(request.FeedbackType))
        {
            return (false, _localizationService.GetLocalizedValidationMessage("invalid_feedback_type", language), "INVALID_FEEDBACK_TYPE");
        }

        var maxResponseLength = _feedbackOptions.CurrentValue.MaxResponseLength;
        if (!string.IsNullOrEmpty(request.Response) && request.Response.Length > maxResponseLength)
        {
            return (false, _localizationService.GetLocalizedValidationMessage("response_too_long", language, 
                new Dictionary<string, string> { { "maxLength", maxResponseLength.ToString() } }), "RESPONSE_TOO_LONG");
        }
        
        return (true, string.Empty, string.Empty);
    }

    /// <summary>
    /// Check if feedback type is valid
    /// </summary>
    private static bool IsValidFeedbackType(string feedbackType)
    {
        return feedbackType.Equals(FeedbackTypeConstants.Cancel, StringComparison.OrdinalIgnoreCase) ||
               feedbackType.Equals(FeedbackTypeConstants.Change, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Simple email validation
    /// </summary>
    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
}
