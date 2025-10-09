namespace Aevatar.Application.Grains.UserFeedback.Options;

/// <summary>
/// Configuration options for User Feedback system
/// </summary>
public class UserFeedbackOptions
{
    /// <summary>
    /// Number of days between allowed feedback submissions (default: 14 days)
    /// </summary>
    public int FeedbackFrequencyDays { get; set; } = 14;
    
    /// <summary>
    /// Maximum number of archived feedback records to keep (default: 50)
    /// </summary>
    public int MaxArchivedFeedbacks { get; set; } = 50;
    
    /// <summary>
    /// Maximum length of user response text (default: 2000 characters)
    /// </summary>
    public int MaxResponseLength { get; set; } = 2000;
}
