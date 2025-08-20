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
            GodGPTLanguage.CN => "zh",
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
                ["en.SharesReached"] = "Max {MaxShareCount} shares reached. Delete some to continue!",
                ["en.InvalidSession"] = "Invalid session to generate a share link.", 
                ["en.ConversationDeleted"] = "Sorry, this conversation has been deleted by the owner.",
                ["en.InvalidConversation"] = "Unable to load conversation {SessionId}",
                ["en.DailyUpdateLimit"] = "Daily upload limit reached. Upgrade to premium to continue.",
                ["en.InvalidVoiceMessage"] = "Invalid voice message. Please try again.",
                ["en.UnSetVoiceLanguage"] = "Please set voice language.",
                ["en.SpeechTimeout"] = "Speech recognition service timeout",
                ["en.TranscriptUnavailable"] = "Transcript Unavailable",
                ["en.SpeechServiceUnavailable"] = "Speech recognition service unavailable",
                ["en.AudioFormatUnsupported"] = "Audio file corrupted or unsupported format",
                ["en.LanguageNotRecognised"] = "Language not recognised. Please try again in the selected language.",
                ["en.FailedGetUserInfo"] = "Failed to get user information",
                ["en.ChatRateLimit"] = "Message limit reached. Please try again later.",
                ["en.VoiceChatRateLimit"] = "Voice message limit reached. Please try again later.",

                
                //chinese
                ["zh-tw.SharesReached"] = "已達到最大分享次數 {MaxShareCount}。請刪除一些以繼續！",
                ["zh-tw.InvalidSession"] = "無效的會話無法生成分享連結。  ",
                ["zh-tw.ConversationDeleted"] = "抱歉，此對話已被擁有者刪除。",
                ["zh-tw.InvalidConversation"] = "無法載入對話 {SessionId} ",
                ["zh-tw.DailyUpdateLimit"] = "已達到每日上傳限制。 陞級到高級版以繼續。",
                ["zh-tw.InvalidVoiceMessage"] = "語音資訊無效。 請重試。",
                ["zh-tw.UnSetVoiceLanguage"] = "請設定語音語言。",
                ["zh-tw.SpeechTimeout"] = "語音識別服務超時",
                ["zh-tw.TranscriptUnavailable"] = "轉錄不可用",
                ["zh-tw.SpeechServiceUnavailable"] = "語音識別服務不可用",
                ["zh-tw.AudioFormatUnsupported"] = "音頻文件損壞或格式不受支持",
                ["zh-tw.LanguageNotRecognised"] = "語言無法識別。請在選定的語言中重試。",
                ["zh-tw.FailedGetUserInfo"] = "無法獲取用戶資訊。",
                ["zh-tw.ChatRateLimit"] = "訊息已達上限。請稍後再試。",
                ["zh-tw.VoiceChatRateLimit"] = "語音訊息已達上限。請稍後再試。",
                // Spanish translations
                ["es.SharesReached"] = "Se alcanzó el máximo de {MaxShareCount} compartidos. ¡Elimina algunos para continuar! ",
                ["es.InvalidSession"] = "Sesión inválida para generar un enlace de compartido.  ",
                ["es.ConversationDeleted"] = "Lo siento, esta conversación ha sido eliminada por el propietario. ",
                ["es.InvalidConversation"] = "No se pudo cargar la conversación {SessionId} ",
                ["es.DailyUpdateLimit"] = "Se ha alcanzado el límite de carga diaria. Actualiza a Premium para continuar.",
                ["es.InvalidVoiceMessage"] = "Mensaje de voz no válido. Por favor, intente de nuevo.",
                ["es.UnSetVoiceLanguage"] = "Por favor, establezca el idioma de voz.",
                ["es.SpeechTimeout"] = "Tiempo de espera del servicio de reconocimiento de voz",
                ["es.TranscriptUnavailable"] = "Transcripción no disponible",
                ["es.SpeechServiceUnavailable"] = "Servicio de reconocimiento de voz no disponible",
                ["es.AudioFormatUnsupported"] = "Archivo de audio corrupto o formato no compatible",
                ["es.LanguageNotRecognised"] = "Idioma no reconocido. Por favor, intenta de nuevo en el idioma seleccionado.",
                ["es.FailedGetUserInfo"] = "No se pudo obtener la información del usuario.",
                ["es.ChatRateLimit"] = "Se ha alcanzado el límite de mensajes. Por favor, inténtalo de nuevo más tarde.",
                ["es.VoiceChatRateLimit"] = "Se ha alcanzado el límite de mensajes de voz. Por favor, inténtalo de nuevo más tarde.",
                
                //chinese
                ["zh.SharesReached"] = "已达到最大{MaxShareCount} 次分享。请删除一些后再继续！",
                ["zh.InvalidSession"] = "无效的会话，无法生成分享链接。", 
                ["zh.ConversationDeleted"] = "抱歉，该会话已被所有者删除。",
                ["zh.InvalidConversation"] = "无法加载会话 {SessionId}",
                ["zh.DailyUpdateLimit"] = "已达到每日上传限制。升级到高级版以继续。",
                ["zh.InvalidVoiceMessage"] = "语音信息无效。请重试。",
                ["zh.UnSetVoiceLanguage"] = "请设置语音语言",
                ["zh.SpeechTimeout"] = "语音识别服务超时",
                ["zh.TranscriptUnavailable"] = "无法获取转录内容",
                ["zh.SpeechServiceUnavailable"] = "语音识别服务不可用",
                ["zh.AudioFormatUnsupported"] = "音频文件已损坏或格式不受支持",
                ["zh.LanguageNotRecognised"] = "无法识别语言。请使用所选语言重试。",
                ["zh.FailedGetUserInfo"] = "获取用户信息失败",
                ["zh.ChatRateLimit"] = "消息数量已达上限，请稍后再试。",
                ["zh.VoiceChatRateLimit"] = "语音消息数量已达上限，请稍后再试。"

            },
            
            ["validation"] = new Dictionary<string, string>
            {
                // Add validation messages here if needed
                ["en.Required"] = "This field is required.",
                ["zh-tw.Required"] = "此欄位為必填項",
                ["es.Required"] = "Este campo es requerido.",
                ["zh.Required"] = "此字段为必填项。"

            },
            
            ["messages"] = new Dictionary<string, string>
            {
                // Add general messages here if needed
                ["en.Success"] = "Operation completed successfully.",
                ["zh-tw.Success"] = "操作成功完成。",
                ["es.Success"] = "Operación completada exitosamente.",
                ["zh.Success"] = "操作成功完成。"
            }
        };

        return translations;
    }
} 