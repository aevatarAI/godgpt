using Aevatar.Application.Grains.Common.Constants;

namespace Aevatar.Application.Grains.UserFeedback.Dtos;

/// <summary>
/// Submit feedback request
/// </summary>
[GenerateSerializer]
public class SubmitFeedbackRequest
{
    /// <summary>
    /// Feedback type (Cancel/Change)
    /// </summary>
    [Id(0)] public string FeedbackType { get; set; } = string.Empty;
    
    /// <summary>
    /// Feedback reasons (multiple selection supported)
    /// </summary>
    [Id(1)] public List<FeedbackReasonEnum> Reasons { get; set; } = new();
    
    /// <summary>
    /// User input detailed feedback content (optional, max 512 characters)
    /// </summary>
    [Id(2)] public string Response { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether contact is requested
    /// </summary>
    [Id(3)] public bool ContactRequested { get; set; }
    
    /// <summary>
    /// Contact email (required when ContactRequested is true)
    /// </summary>
    [Id(4)] public string Email { get; set; } = string.Empty;

    [Id(5)] public bool SkippedFeedback { get; set; } = false;
}

/// <summary>
/// Submit feedback result
/// </summary>
[GenerateSerializer]
public class SubmitFeedbackResult
{
    /// <summary>
    /// Operation success status
    /// </summary>
    [Id(0)] public bool Success { get; set; }
    
    /// <summary>
    /// Result message
    /// </summary>
    [Id(1)] public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Error code (when failed)
    /// </summary>
    [Id(2)] public string? ErrorCode { get; set; }
}

/// <summary>
/// Check eligibility result
/// </summary>
[GenerateSerializer]
public class CheckEligibilityResult
{
    /// <summary>
    /// Whether user is eligible to submit feedback
    /// </summary>
    [Id(0)] public bool Eligible { get; set; }
    
    /// <summary>
    /// Last feedback time (optional)
    /// </summary>
    [Id(1)] public DateTime? LastFeedbackTime { get; set; }
    
    /// <summary>
    /// Next eligible time (optional)
    /// </summary>
    [Id(2)] public DateTime? NextEligibleTime { get; set; }
    
    /// <summary>
    /// Reason message
    /// </summary>
    [Id(3)] public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Get feedback history request
/// </summary>
[GenerateSerializer]
public class GetFeedbackHistoryRequest
{
    /// <summary>
    /// Page size (optional, default 10)
    /// </summary>
    [Id(0)] public int PageSize { get; set; } = 10;
    
    /// <summary>
    /// Page index (optional, default 0)
    /// </summary>
    [Id(1)] public int PageIndex { get; set; } = 0;
}

/// <summary>
/// Feedback history item
/// </summary>
[GenerateSerializer]
public class FeedbackHistoryItem
{
    /// <summary>
    /// Feedback ID
    /// </summary>
    [Id(0)] public string FeedbackId { get; set; } = string.Empty;
    
    /// <summary>
    /// Feedback type
    /// </summary>
    [Id(1)] public string FeedbackType { get; set; } = string.Empty;
    
    /// <summary>
    /// Feedback reasons (multiple selection supported)
    /// </summary>
    [Id(2)] public List<FeedbackReasonEnum> Reasons { get; set; } = new();
    
    /// <summary>
    /// User response
    /// </summary>
    [Id(3)] public string Response { get; set; } = string.Empty;
    
    /// <summary>
    /// Contact requested
    /// </summary>
    [Id(4)] public bool ContactRequested { get; set; }
    
    /// <summary>
    /// Contact email
    /// </summary>
    [Id(5)] public string Email { get; set; } = string.Empty;
    
    /// <summary>
    /// Submission time
    /// </summary>
    [Id(6)] public DateTime SubmittedAt { get; set; }
}

/// <summary>
/// Get feedback history result
/// </summary>
[GenerateSerializer]
public class GetFeedbackHistoryResult
{
    /// <summary>
    /// Feedback history list
    /// </summary>
    [Id(0)] public List<FeedbackHistoryItem> Feedbacks { get; set; } = new();
    
    /// <summary>
    /// Total count
    /// </summary>
    [Id(1)] public int TotalCount { get; set; }
    
    /// <summary>
    /// Whether there are more records
    /// </summary>
    [Id(2)] public bool HasMore { get; set; }
}