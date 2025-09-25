using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Service;

namespace Aevatar.Application.Grains.UserFeedback;

/// <summary>
/// Helper class for feedback reason localization and utilities
/// </summary>
public static class FeedbackReasonHelper
{
    /// <summary>
    /// Get localized text for a feedback reason enum
    /// </summary>
    /// <param name="reason">Feedback reason enum</param>
    /// <param name="language">Target language</param>
    /// <param name="localizationService">Localization service instance</param>
    /// <returns>Localized reason text</returns>
    public static string GetLocalizedReasonText(FeedbackReasonEnum reason, GodGPTLanguage language, ILocalizationService localizationService)
    {
        var reasonKey = reason.ToString();
        return localizationService.GetLocalizedFeedbackReason(reasonKey, language);
    }
    
    /// <summary>
    /// Get localized texts for multiple feedback reasons
    /// </summary>
    /// <param name="reasons">List of feedback reason enums</param>
    /// <param name="language">Target language</param>
    /// <param name="localizationService">Localization service instance</param>
    /// <returns>List of localized reason texts</returns>
    public static List<string> GetLocalizedReasonTexts(List<FeedbackReasonEnum> reasons, GodGPTLanguage language, ILocalizationService localizationService)
    {
        return reasons.Select(reason => GetLocalizedReasonText(reason, language, localizationService)).ToList();
    }
    
    /// <summary>
    /// Get comma-separated localized reason texts
    /// </summary>
    /// <param name="reasons">List of feedback reason enums</param>
    /// <param name="language">Target language</param>
    /// <param name="localizationService">Localization service instance</param>
    /// <returns>Comma-separated localized reason texts</returns>
    public static string GetLocalizedReasonTextsAsString(List<FeedbackReasonEnum> reasons, GodGPTLanguage language, ILocalizationService localizationService)
    {
        var localizedTexts = GetLocalizedReasonTexts(reasons, language, localizationService);
        return string.Join(", ", localizedTexts);
    }
    
    /// <summary>
    /// Validate that at least one reason is selected
    /// </summary>
    /// <param name="reasons">List of feedback reasons</param>
    /// <returns>True if valid, false otherwise</returns>
    public static bool ValidateReasons(List<FeedbackReasonEnum>? reasons)
    {
        return reasons != null && reasons.Count > 0;
    }
    
    /// <summary>
    /// Get all available feedback reasons
    /// </summary>
    /// <returns>Array of all feedback reason enums</returns>
    public static FeedbackReasonEnum[] GetAllReasons()
    {
        return Enum.GetValues<FeedbackReasonEnum>();
    }
    
    /// <summary>
    /// Get English texts for multiple feedback reasons
    /// </summary>
    /// <param name="reasons">List of feedback reason enums</param>
    /// <param name="localizationService">Localization service instance</param>
    /// <returns>List of English reason texts</returns>
    public static List<string> GetEnglishReasonTexts(List<FeedbackReasonEnum> reasons, ILocalizationService localizationService)
    {
        return GetLocalizedReasonTexts(reasons, GodGPTLanguage.English, localizationService);
    }
}
