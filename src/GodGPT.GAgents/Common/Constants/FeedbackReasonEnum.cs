namespace Aevatar.Application.Grains.Common.Constants;

/// <summary>
/// Enumeration of feedback reasons for subscription cancellation or changes
/// </summary>
public enum FeedbackReasonEnum
{
    /// <summary>
    /// Service is too expensive
    /// </summary>
    TooExpensive = 1,
    
    /// <summary>
    /// Not using the service enough
    /// </summary>
    NotUsingEnough = 2,
    
    /// <summary>
    /// Found a better alternative service
    /// </summary>
    FoundBetterAlternative = 3,
    
    /// <summary>
    /// Experiencing technical issues or bugs
    /// </summary>
    TechnicalIssues = 4,
    
    /// <summary>
    /// Content or features are not relevant
    /// </summary>
    ContentNotRelevant = 5,
    
    /// <summary>
    /// Temporary pause, might return later
    /// </summary>
    TemporaryPause = 6,
    
    /// <summary>
    /// Need more features
    /// </summary>
    NeedMoreFeatures = 7,
    
    /// <summary>
    /// Better pricing on another plan
    /// </summary>
    BetterPricingOnAnotherPlan = 8,
    
    /// <summary>
    /// My usage changed
    /// </summary>
    UsageChanged = 9,
    
    /// <summary>
    /// Payment/Invoice needs
    /// </summary>
    PaymentInvoiceNeeds = 10,
    
    /// <summary>
    /// Other reason not listed above
    /// </summary>
    Other = 11
}
