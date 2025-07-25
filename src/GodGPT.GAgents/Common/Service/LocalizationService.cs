using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Microsoft.Extensions.Logging;

namespace Aevatar.Application.Grains.Common.Service;

public class LocalizationService : ILocalizationService
{
    private readonly ILogger<LocalizationService> _logger;
    private readonly Dictionary<string, Dictionary<string, string>> _translations;

    public LocalizationService(ILogger<LocalizationService> logger)
    {
        _logger = logger;
        _translations = LoadTranslations();
    }

    /// <summary>
    /// Get localized message by key and language
    /// </summary>
    public string GetLocalizedMessage(string key, GodGPTLanguage language)
    {
        return GetTranslation(key, language, "messages");
    }

    /// <summary>
    /// Get localized exception message by exception key and language
    /// </summary>
    public string GetLocalizedException(string exceptionKey, GodGPTLanguage language)
    {
        return GetTranslation(exceptionKey, language, "exceptions");
    }

    /// <summary>
    /// Get localized validation message by validation key and language
    /// </summary>
    public string GetLocalizedValidationMessage(string validationKey, GodGPTLanguage language)
    {
        return GetTranslation(validationKey, language, "validation");
    }
    
    /// <summary>
    /// Get localized exception message with parameter replacement
    /// </summary>
    public string GetLocalizedException(string exceptionKey, GodGPTLanguage language, Dictionary<string, string> parameters)
    {
        var message = GetTranslation(exceptionKey, language, "exceptions");
        return ReplaceParameters(message, parameters);
    }
    
    /// <summary>
    /// Get localized message with parameter replacement
    /// </summary>
    public string GetLocalizedMessage(string key, GodGPTLanguage language, Dictionary<string, string> parameters)
    {
        var message = GetTranslation(key, language, "messages");
        return ReplaceParameters(message, parameters);
    }
    
    /// <summary>
    /// Replace parameters in message template using {parameterName} format
    /// </summary>
    /// <param name="message">Message template</param>
    /// <param name="parameters">Parameters to replace</param>
    /// <returns>Message with parameters replaced</returns>
    private string ReplaceParameters(string message, Dictionary<string, string> parameters)
    {
        if (parameters == null || !parameters.Any())
            return message;
            
        var result = message;
        foreach (var parameter in parameters)
        {
            result = result.Replace($"{{{parameter.Key}}}", parameter.Value);
        }
        return result;
    }

    /// <summary>
    /// Get translation for specific key, language and category
    /// </summary>
    private string GetTranslation(string key, GodGPTLanguage language, string category)
    {
        try
        {
            var languageCode = GetLanguageCode(language);
            
            if (_translations.TryGetValue(category, out var categoryTranslations) &&
                categoryTranslations.TryGetValue($"{languageCode}.{key}", out var translation))
            {
                return translation;
            }

            // Fallback to English if translation not found
            if (language != GodGPTLanguage.English)
            {
                _logger.LogWarning("Translation not found for key: {Key}, language: {Language}, category: {Category}. Falling back to English.", 
                    key, language, category);
                return GetTranslation(key, GodGPTLanguage.English, category);
            }

            // Final fallback to key itself
            _logger.LogError("Translation not found for key: {Key}, language: {Language}, category: {Category}. Using key as fallback.", 
                key, language, category);
            return key;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting translation for key: {Key}, language: {Language}, category: {Category}", 
                key, language, category);
            return key;
        }
    }

    /// <summary>
    /// Get language code for the enum
    /// </summary>
    private string GetLanguageCode(GodGPTLanguage language)
    {
        return language switch
        {
            GodGPTLanguage.English => "en",
            GodGPTLanguage.TraditionalChinese => "zh-tw",
            GodGPTLanguage.Spanish => "es",
            _ => "en"
        };
    }

    /// <summary>
    /// Load translations from embedded resources or configuration
    /// </summary>
    private Dictionary<string, Dictionary<string, string>> LoadTranslations()
    {
        var translations = new Dictionary<string, Dictionary<string, string>>
        {
            ["exceptions"] = new Dictionary<string, string>
            {
                // English translations
                ["en.Unauthorized"] = "Unauthorized: User is not authenticated.",

                // Spanish translations
                ["es.Unauthorized"] = "No autorizado: El usuario no está autenticado. ",
     
            },
            
            ["validation"] = new Dictionary<string, string>
            {
                // Add validation messages here if needed
                ["en.Required"] = "This field is required.",
                ["zh-tw.Required"] = "此字段为必填项。",
                ["es.Required"] = "Este campo es requerido."
            },
            
            ["messages"] = new Dictionary<string, string>
            {
                // Add general messages here if needed
                ["en.Success"] = "Operation completed successfully.",
                ["zh-tw.Success"] = "操作成功完成。",
                ["es.Success"] = "Operación completada exitosamente."
            }
        };

        return translations;
    }
} 