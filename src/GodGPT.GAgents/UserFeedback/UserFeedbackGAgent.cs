using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Service;
using Aevatar.Application.Grains.UserFeedback.Dtos;
using Aevatar.Application.Grains.UserFeedback.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
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
    
    private const int FeedbackFrequencyDays = 14;
    private const int MaxArchivedFeedbacks = 50;
    private const int MaxResponseLength = 2000;

    public UserFeedbackGAgent(
        ILogger<UserFeedbackGAgent> logger,
        ILocalizationService localizationService)
    {
        _logger = logger;
        _localizationService = localizationService;
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
                    if (state.ArchivedFeedbacks.Count > MaxArchivedFeedbacks)
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
            var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
            
            // Validate request
            var validationResult = ValidateSubmitRequest(request, language);
            if (!validationResult.IsValid)
            {
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

            _logger.LogInformation("User feedback submitted successfully. UserId: {UserId}, FeedbackId: {FeedbackId}, Type: {Type}",
                this.GetPrimaryKey().ToString(), feedbackInfo.FeedbackId, request.FeedbackType);

            return new SubmitFeedbackResult
            {
                Success = true,
                Message = string.Empty,
            };
        }
        catch (Exception ex)
        {
            var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
            _logger.LogError(ex, "Error submitting feedback for user: {UserId}", this.GetPrimaryKey().ToString());
            
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
        var isEligible = timeSinceLastFeedback.TotalDays >= FeedbackFrequencyDays;
        
        if (isEligible)
        {
            return Task.FromResult(new CheckEligibilityResult
            {
                Eligible = true,
                LastFeedbackTime = State.LastFeedbackTime,
                Message = string.Empty
            });
        }

        var nextEligibleTime = State.LastFeedbackTime.Value.AddDays(FeedbackFrequencyDays);
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

        if (!string.IsNullOrEmpty(request.Response) && request.Response.Length > MaxResponseLength)
        {
            return (false, _localizationService.GetLocalizedValidationMessage("response_too_long", language, 
                new Dictionary<string, string> { { "maxLength", MaxResponseLength.ToString() } }), "RESPONSE_TOO_LONG");
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
