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
    public string GetLocalizedValidationMessage(string validationKey, GodGPTLanguage language, Dictionary<string, string>? parameters = null)
    {
        var message = GetTranslation(validationKey, language, "validation");
        return ReplaceParameters(message, parameters);
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
    /// Get localized feedback reason text by reason enum and language
    /// </summary>
    public string GetLocalizedFeedbackReason(string reasonKey, GodGPTLanguage language)
    {
        return GetTranslation(reasonKey, language, "feedback_reasons");
    }
    
    /// <summary>
    /// Replace parameters in message template using {parameterName} format
    /// </summary>
    /// <param name="message">Message template</param>
    /// <param name="parameters">Parameters to replace</param>
    /// <returns>Message with parameters replaced</returns>
    private string ReplaceParameters(string message, Dictionary<string, string>? parameters)
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
                ["zh-tw.LanguageNotRecognised"] = "抱歉,語言未被識別。請使用所選語言再次嘗試。",
                ["zh-tw.FailedGetUserInfo"] = "無法獲取用戶資訊。",
                ["zh-tw.ChatRateLimit"] = "訊息已達上限。請稍後再試。",
                ["zh-tw.VoiceChatRateLimit"] = "語音訊息已達上限。請稍後再試。",
                // Spanish translations
                ["es.SharesReached"] = "Se alcanzó el máximo de {MaxShareCount} compartidos. ¡Elimina algunos para continuar! ",
                ["es.InvalidSession"] = "Sesión inválida para generar un enlace de compartido.  ",
                ["es.ConversationDeleted"] = "Lo siento, esta conversación ha sido eliminada por el propietario. ",
                ["es.InvalidConversation"] = "No se pudo cargar la conversación {SessionId} ",
                ["es.DailyUpdateLimit"] = "Límite de carga diaria alcanzado. Actualiza a premium para continuar.",
                ["es.InvalidVoiceMessage"] = "Mensaje de voz no válido. Por favor, intente de nuevo.",
                ["es.UnSetVoiceLanguage"] = "Por favor, establezca el idioma de voz.",
                ["es.SpeechTimeout"] = "Tiempo de espera del servicio de reconocimiento de voz",
                ["es.TranscriptUnavailable"] = "Transcripción no disponible",
                ["es.SpeechServiceUnavailable"] = "Servicio de reconocimiento de voz no disponible",
                ["es.AudioFormatUnsupported"] = "Archivo de audio corrupto o formato no compatible",
                ["es.LanguageNotRecognised"] = "¡Lo siento, el idioma no fue reconocido! Por favor, intenta de nuevo en el idioma seleccionado.",
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
                ["zh.Required"] = "此字段为必填项。",
                
                // User Feedback validation messages

                ["en.invalid_feedback_type"] = "Invalid feedback type. Must be 'Cancel' or 'Change'.",
                ["zh-tw.invalid_feedback_type"] = "無效的反饋類型。必須是「取消」或「更改」。",
                ["es.invalid_feedback_type"] = "Tipo de comentario no válido. Debe ser 'Cancelar' o 'Cambiar'.",
                ["zh.invalid_feedback_type"] = "无效的反馈类型。必须是\"取消\"或\"更改\"。",
                
                ["en.reason_required"] = "Feedback reason is required.",
                ["zh-tw.reason_required"] = "需要反饋原因。",
                ["es.reason_required"] = "Se requiere razón del comentario.",
                ["zh.reason_required"] = "需要反馈原因。",
                
                ["en.reasons_required"] = "At least one feedback reason must be selected.",
                ["zh-tw.reasons_required"] = "必須選擇至少一個反饋原因。",
                ["es.reasons_required"] = "Debe seleccionar al menos una razón de comentario.",
                ["zh.reasons_required"] = "必须选择至少一个反馈原因。",
                
                ["en.response_too_long"] = "Response is too long. Maximum {maxLength} characters allowed.",
                ["zh-tw.response_too_long"] = "回應太長。最多允許 {maxLength} 個字元。",
                ["es.response_too_long"] = "La respuesta es demasiado larga. Máximo {maxLength} caracteres permitidos.",
                ["zh.response_too_long"] = "回复太长。最多允许 {maxLength} 个字符。"
            },
            
            ["messages"] = new Dictionary<string, string>
            {
                // Add general messages here if needed
                ["en.Success"] = "Operation completed successfully.",
                ["zh-tw.Success"] = "操作成功完成。",
                ["es.Success"] = "Operación completada exitosamente.",
                ["zh.Success"] = "操作成功完成。",
                
                // User Feedback message
                ["en.feedback_submission_failed"] = "Failed to submit feedback.",
                ["zh-tw.feedback_submission_failed"] = "反饋提交失敗。",
                ["es.feedback_submission_failed"] = "Error al enviar comentario.",
                ["zh.feedback_submission_failed"] = "反馈提交失败。",

                ["en.feedback_frequency_limit"] = "You can submit feedback again in {days} days. Please wait.",
                ["zh-tw.feedback_frequency_limit"] = "您可以在 {days} 天後再次提交反饋。請等待。",
                ["es.feedback_frequency_limit"] = "Puedes enviar comentarios nuevamente en {days} días. Por favor espera.",
                ["zh.feedback_frequency_limit"] = "您可以在 {days} 天后再次提交反馈。请等待。",
                
                //HomeDosAndDontPrompt
                ["en.home_dos_and_dont_prompt"] = "What’s my daily guide for today?",
                ["zh-tw.home_dos_and_dont_prompt"] = "展示我的今日宜忌",
                ["es.home_dos_and_dont_prompt"] = "¿Cuál es mi guía diaria de hoy?",
                ["zh.home_dos_and_dont_prompt"] = "展示我的今日宜忌",
                
                //ChatPageMessageAfterSync
                ["en.chat_page_message_after_sync"] = "Google calendar is synced, what’s my daily guide for today?",
                ["zh-tw.chat_page_message_after_sync"] = "谷歌日曆已同步，請結合日曆展示我的今日宜忌",
                ["es.chat_page_message_after_sync"] = "El calendario de Google está sincronizado, ¿cuál es mi guía diaria para hoy?",
                ["zh.chat_page_message_after_sync"] = "谷歌日历已同步，请结合日历展示我的今日宜忌"
            },
            
            ["feedback_reasons"] = new Dictionary<string, string>
            {
                // English feedback reasons
                ["en.TooExpensive"] = "Too expensive",
                ["en.NotUsingEnough"] = "Not using it enough",
                ["en.FoundBetterAlternative"] = "Found a better alternative",
                ["en.TechnicalIssues"] = "Technical issues or bugs",
                ["en.ContentNotRelevant"] = "Content/features not relevant",
                ["en.TemporaryPause"] = "Temporary pause (might return)",
                ["en.NeedMoreFeatures"] = "Need more features",
                ["en.BetterPricingOnAnotherPlan"] = "Better pricing on another plan",
                ["en.UsageChanged"] = "My usage changed",
                ["en.PaymentInvoiceNeeds"] = "Payment/Invoice needs",
                ["en.Other"] = "Other",
                
                // Traditional Chinese feedback reasons
                ["zh-tw.TooExpensive"] = "太昂貴",
                ["zh-tw.NotUsingEnough"] = "使用不夠頻繁",
                ["zh-tw.FoundBetterAlternative"] = "找到更好的替代方案",
                ["zh-tw.TechnicalIssues"] = "技術問題或錯誤",
                ["zh-tw.ContentNotRelevant"] = "內容/功能不相關",
                ["zh-tw.TemporaryPause"] = "暫時暫停（可能會回來）",
                ["zh-tw.NeedMoreFeatures"] = "需要更多功能",
                ["zh-tw.BetterPricingOnAnotherPlan"] = "其他方案有更好的價格",
                ["zh-tw.UsageChanged"] = "我的使用情況改變了",
                ["zh-tw.PaymentInvoiceNeeds"] = "付款/發票需求",
                ["zh-tw.Other"] = "其他",
                
                // Spanish feedback reasons
                ["es.TooExpensive"] = "Demasiado caro",
                ["es.NotUsingEnough"] = "No lo uso lo suficiente",
                ["es.FoundBetterAlternative"] = "Encontré una mejor alternativa",
                ["es.TechnicalIssues"] = "Problemas técnicos o errores",
                ["es.ContentNotRelevant"] = "Contenido/características no relevantes",
                ["es.TemporaryPause"] = "Pausa temporal (podría volver)",
                ["es.NeedMoreFeatures"] = "Necesito más funciones",
                ["es.BetterPricingOnAnotherPlan"] = "Mejor precio en otro plan",
                ["es.UsageChanged"] = "Mi uso cambió",
                ["es.PaymentInvoiceNeeds"] = "Necesidades de pago/factura",
                ["es.Other"] = "Otro",
                
                // Simplified Chinese feedback reasons
                ["zh.TooExpensive"] = "太昂贵",
                ["zh.NotUsingEnough"] = "使用不够频繁",
                ["zh.FoundBetterAlternative"] = "找到更好的替代方案",
                ["zh.TechnicalIssues"] = "技术问题或错误",
                ["zh.ContentNotRelevant"] = "内容/功能不相关",
                ["zh.TemporaryPause"] = "暂时暂停（可能会回来）",
                ["zh.NeedMoreFeatures"] = "需要更多功能",
                ["zh.BetterPricingOnAnotherPlan"] = "其他方案有更好的价格",
                ["zh.UsageChanged"] = "我的使用情况改变了",
                ["zh.PaymentInvoiceNeeds"] = "付款/发票需求",
                ["zh.Other"] = "其他"
            }
        };

        return translations;
    }
} 