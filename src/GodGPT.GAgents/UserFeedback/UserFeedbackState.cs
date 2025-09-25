using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.UserFeedback;

/// <summary>
/// User feedback state data
/// </summary>
[GenerateSerializer]
public class UserFeedbackState : StateBase
{
    /// <summary>
    /// User ID
    /// </summary>
    [Id(0)] public Guid UserId { get; set; }
    
    /// <summary>
    /// Current feedback information
    /// </summary>
    [Id(1)] public UserFeedbackInfo? CurrentFeedback { get; set; }
    
    /// <summary>
    /// Archived feedback records (serialized as JSON strings)
    /// </summary>
    [Id(2)] public List<string> ArchivedFeedbacks { get; set; } = new();
    
    /// <summary>
    /// Last feedback submission time
    /// </summary>
    [Id(3)] public DateTime? LastFeedbackTime { get; set; }
    
    /// <summary>
    /// Total feedback count
    /// </summary>
    [Id(4)] public int FeedbackCount { get; set; }
    
    /// <summary>
    /// Creation time
    /// </summary>
    [Id(5)] public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Last update time
    /// </summary>
    [Id(6)] public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// User feedback information model
/// </summary>
[GenerateSerializer]
public class UserFeedbackInfo
{
    /// <summary>
    /// Feedback unique identifier
    /// </summary>
    [Id(0)] public string FeedbackId { get; set; } = string.Empty;
    
    /// <summary>
    /// Feedback type (Cancel/Change)
    /// </summary>
    [Id(1)] public string FeedbackType { get; set; } = string.Empty;
    
    /// <summary>
    /// Feedback reasons (multiple selection supported)
    /// </summary>
    [Id(2)] public List<FeedbackReasonEnum> Reasons { get; set; } = new();
    
    /// <summary>
    /// User detailed feedback content (optional, max 512 characters)
    /// </summary>
    [Id(3)] public string Response { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether contact is requested
    /// </summary>
    [Id(4)] public bool ContactRequested { get; set; }
    
    /// <summary>
    /// Contact email (required when ContactRequested is true)
    /// </summary>
    [Id(5)] public string Email { get; set; } = string.Empty;

    [Id(6)] public DateTime SubmittedAt { get; set; } = default;
    
    /// <summary>
    /// English text representations of the selected reasons
    /// </summary>
    [Id(7)] public List<string> ReasonTextsEnglish { get; set; } = new();
}
