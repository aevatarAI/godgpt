namespace Aevatar.Application.Grains.Common.Constants;

public static class ExceptionMessageKeys
{
    // Authentication and Authorization
    public const string SharesReached = "SharesReached";
    public const string InvalidSession = "InvalidSession";
    public const string ConversationDeleted = "ConversationDeleted";
    public const string InvalidConversation = "InvalidConversation";
    public const string DailyUpdateLimit = "DailyUpdateLimit";
    public const string InvalidVoiceMessage = "InvalidVoiceMessage";
    public const string UnSetVoiceLanguage = "UnSetVoiceLanguage";
    public const string SpeechTimeout = "SpeechTimeout";
    public const string TranscriptUnavailable = "TranscriptUnavailable";
    public const string SpeechServiceUnavailable = "SpeechServiceUnavailable";
    public const string AudioFormatUnsupported = "AudioFormatUnsupported";
    public const string LanguageNotRecognised = "LanguageNotRecognised";
    public const string FailedGetUserInfo = "FailedGetUserInfo";
    public const string ChatRateLimit = "ChatRateLimit";
    public const string VoiceChatRateLimit = "VoiceChatRateLimit";

} 