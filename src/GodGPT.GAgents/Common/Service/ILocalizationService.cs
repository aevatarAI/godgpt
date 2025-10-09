using Aevatar.Application.Grains.Agents.ChatManager.Common;

namespace Aevatar.Application.Grains.Common.Service;

public interface ILocalizationService
{
    /// <summary>
    /// Get localized message by key and language
    /// </summary>
    /// <param name="key">Message key</param>
    /// <param name="language">Target language</param>
    /// <returns>Localized message</returns>
    string GetLocalizedMessage(string key, GodGPTLanguage language);
    
    /// <summary>
    /// Get localized exception message by exception key and language
    /// </summary>
    /// <param name="exceptionKey">Exception message key</param>
    /// <param name="language">Target language</param>
    /// <returns>Localized exception message</returns>
    string GetLocalizedException(string exceptionKey, GodGPTLanguage language);
    
    /// <summary>
    /// Get localized validation message by validation key and language
    /// </summary>
    /// <param name="validationKey">Validation message key</param>
    /// <param name="language">Target language</param>
    /// <returns>Localized validation message</returns>
    string GetLocalizedValidationMessage(string validationKey, GodGPTLanguage language, Dictionary<string, string>? parameters = null);
    
    /// <summary>
    /// Get localized exception message with parameter replacement
    /// </summary>
    /// <param name="exceptionKey">Exception message key</param>
    /// <param name="language">Target language</param>
    /// <param name="parameters">Parameters to replace in the message template</param>
    /// <returns>Localized exception message with parameters replaced</returns>
    string GetLocalizedException(string exceptionKey, GodGPTLanguage language, Dictionary<string, string> parameters);
    
    /// <summary>
    /// Get localized message with parameter replacement
    /// </summary>
    /// <param name="key">Message key</param>
    /// <param name="language">Target language</param>
    /// <param name="parameters">Parameters to replace in the message template</param>
    /// <returns>Localized message with parameters replaced</returns>
    string GetLocalizedMessage(string key, GodGPTLanguage language, Dictionary<string, string> parameters);
    
    /// <summary>
    /// Get localized feedback reason text by reason key and language
    /// </summary>
    /// <param name="reasonKey">Feedback reason key</param>
    /// <param name="language">Target language</param>
    /// <returns>Localized feedback reason text</returns>
    string GetLocalizedFeedbackReason(string reasonKey, GodGPTLanguage language);
} 