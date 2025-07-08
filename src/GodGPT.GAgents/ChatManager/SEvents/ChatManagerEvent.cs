using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Common;
using Aevatar.SignalR;
using GodGPT.GAgents.SpeechChat;


namespace Aevatar.Application.Grains.Agents.ChatManager;

[GenerateSerializer]
public class RequestCreateGodChatEvent : EventBase
{
    [Id(0)] public string SystemLLM { get; set; }
    [Id(1)] public string Prompt { get; set; }
    [Id(2)] public UserProfileDto UserProfile { get; set; }
    [Id(3)] public string? Guider { get; set; } // Role information for the conversation
}

[GenerateSerializer]
public class ResponseCreateGod : ResponseToPublisherEventBase
{
    [Id(0)] public ResponseType ResponseType { get; set; } = ResponseType.CreateSession;
    [Id(1)] public Guid SessionId { get; set; }
    [Id(2)] public String SessionVersion { get; set; }
}

[GenerateSerializer]
public class RequestGodChatEvent : EventBase
{
    [Id(0)] public Guid SessionId { get; set; }
    [Id(1)] public string SystemLLM { get; set; }
    [Id(2)] public string Content { get; set; }
}

[GenerateSerializer]
public class ResponseGodChat : ResponseToPublisherEventBase
{
    [Id(0)] public ResponseType ResponseType { get; set; } = ResponseType.ChatResponse;
    [Id(1)] public string Response { get; set; }
    [Id(2)] public string NewTitle { get; set; }
}

[GenerateSerializer]
public class RequestStreamGodChatEvent : EventBase
{
    [Id(0)] public Guid SessionId { get; set; }
    [Id(1)] public string SystemLLM { get; set; }
    [Id(2)] public string Content { get; set; }
}

[GenerateSerializer]
public class ResponseStreamGodChat : ResponseToPublisherEventBase
{
    [Id(0)] public ResponseType ResponseType { get; set; } = ResponseType.ChatResponse;
    [Id(1)] public string Response { get; set; }
    [Id(2)] public string NewTitle { get; set; }
    [Id(3)] public string ChatId { get; set; }
    [Id(4)] public bool IsLastChunk { get; set; }

    [Id(5)] public int SerialNumber { get; set; }

    [Id(6)] public Guid SessionId { get; set; }
    
    /// <summary>
    /// Binary MP3 audio data for voice response
    /// </summary>
    [Id(7)] public byte[]? AudioData { get; set; }
    
    /// <summary>
    /// Audio metadata including duration, format, language etc.
    /// </summary>
    [Id(8)] public AudioMetadata? AudioMetadata { get; set; }
    [Id(9)] public ChatErrorCode ErrorCode { get; set; }

    public ResponseStreamGodChatForHttp ConvertToHttpResponse()
    {
        return new ResponseStreamGodChatForHttp()
        {
            Response = Response,
            ChatId = ChatId,
            IsLastChunk = IsLastChunk,
            SerialNumber = SerialNumber,
            SessionId = SessionId,
            AudioData = AudioData,
            AudioMetadata = AudioMetadata,
            ErrorCode = ErrorCode
        };
    }
}

public class ResponseStreamGodChatForHttp
{
    public ResponseType ResponseType { get; set; } = ResponseType.ChatResponse;
    public string Response { get; set; }
    public string ChatId { get; set; }
    public bool IsLastChunk { get; set; }
    public int SerialNumber { get; set; }
    public int SerialChunk { get; set; }
    public Guid SessionId { get; set; }
    
    /// <summary>
    /// Binary MP3 audio data for voice response
    /// </summary>
    public byte[]? AudioData { get; set; }
    
    /// <summary>
    /// Audio metadata including duration, format, language etc.
    /// </summary>
    public AudioMetadata? AudioMetadata { get; set; } 
    public ChatErrorCode ErrorCode { get; set; }

}

[GenerateSerializer]
public class RequestGodSessionListEvent : EventBase
{
}

[GenerateSerializer]
public class ResponseGodSessionList : ResponseToPublisherEventBase
{
    [Id(0)] public ResponseType ResponseType { get; set; } = ResponseType.SessionListResponse;
    [Id(1)] public List<SessionInfoDto> SessionList { get; set; }
}

[GenerateSerializer]
public class RequestSessionChatHistoryEvent : EventBase
{
    [Id(0)] public Guid SessionId { get; set; }
}

[GenerateSerializer]
public class ResponseSessionChatHistory : ResponseToPublisherEventBase
{
    [Id(0)] public ResponseType ResponseType { get; set; } = ResponseType.SessionChatHistory;
    [Id(1)] public List<ChatMessage> ChatHistory { get; set; }
}

[GenerateSerializer]
public class RequestDeleteSessionEvent : EventBase
{
    [Id(0)] public Guid SessionId { get; set; }
}

[GenerateSerializer]
public class ResponseDeleteSession : ResponseToPublisherEventBase
{
    [Id(0)] public ResponseType ResponseType { get; set; } = ResponseType.SessionDelete;
    [Id(1)] public bool IfSuccess { get; set; }
}

[GenerateSerializer]
public class RequestRenameSessionEvent : EventBase
{
    [Id(0)] public Guid SessionId { get; set; }
    [Id(1)] public string Title { get; set; }
}

[GenerateSerializer]
public class ResponseRenameSession : ResponseToPublisherEventBase
{
    [Id(0)] public ResponseType ResponseType { get; set; } = ResponseType.SessionRename;
    [Id(1)] public Guid SessionId { get; set; }
    [Id(2)] public string Title { get; set; }
}

[GenerateSerializer]
public class RequestClearAllEvent : EventBase
{
}

[GenerateSerializer]
public class ResponseClearAll : ResponseToPublisherEventBase
{
    [Id(0)] public ResponseType ResponseType { get; set; } = ResponseType.ClearAll;
    [Id(1)] public bool Success { get; set; }
}

[GenerateSerializer]
public class RequestSetUserProfileEvent : EventBase
{
    [Id(0)] public string Gender { get; set; }
    [Id(1)] public DateTime BirthDate { get; set; }
    [Id(2)] public string BirthPlace { get; set; }
    [Id(3)] public string FullName { get; set; }
}

[GenerateSerializer]
public class ResponseSetUserProfile : ResponseToPublisherEventBase
{
    [Id(0)] public ResponseType ResponseType { get; set; } = ResponseType.SetUserProfile;
    [Id(1)] public bool Success { get; set; }
}

[GenerateSerializer]
public class RequestGetUserProfileEvent : EventBase
{
}

[GenerateSerializer]
public class ResponseGetUserProfile : ResponseToPublisherEventBase
{
    [Id(0)] public ResponseType ResponseType { get; set; } = ResponseType.GetUserProfile;
    [Id(1)] public string Gender { get; set; }
    [Id(2)] public DateTime BirthDate { get; set; }
    [Id(3)] public string BirthPlace { get; set; }
    [Id(4)] public string FullName { get; set; }
}

[GenerateSerializer]
public class UserProfileDto
{
    [Id(0)] public string Gender { get; set; }
    [Id(1)] public DateTime BirthDate { get; set; }
    [Id(2)] public string BirthPlace { get; set; }
    [Id(3)] public string FullName { get; set; }
    [Id(4)] public CreditsInfoDto Credits { get; set; }
    [Id(5)] public SubscriptionInfoDto Subscription { get; set; }
    [Id(6)] public SubscriptionInfoDto UltimateSubscription { get; set; }
    [Id(7)] public Guid Id { get; set; }
    [Id(8)] public Guid? InviterId { get; set; }
    [Id(9)] public VoiceLanguageEnum VoiceLanguage { get; set; }

}

[GenerateSerializer]
public enum ResponseType
{
    CreateSession = 1,
    ChatResponse = 2,
    SessionListResponse = 3,
    SessionChatHistory = 4,
    SessionDelete = 5,
    SessionRename = 6,
    ClearAll = 7,
    SetUserProfile = 8,
    GetUserProfile = 9
}

[GenerateSerializer]
public class AudioMetadata
{
    /// <summary>
    /// Audio duration in seconds
    /// </summary>
    [Id(0)] public double Duration { get; set; }
    
    /// <summary>
    /// Audio file size in bytes
    /// </summary>
    [Id(1)] public int SizeBytes { get; set; }
    
    /// <summary>
    /// Audio sample rate (16000 Hz)
    /// </summary>
    [Id(2)] public int SampleRate { get; set; }
    
    /// <summary>
    /// Audio bit rate (24000 bps)
    /// </summary>
    [Id(3)] public int BitRate { get; set; }
    
    /// <summary>
    /// Audio format (mp3)
    /// </summary>
    [Id(4)] public string Format { get; set; }
    
    /// <summary>
    /// Language type for voice
    /// </summary>
    [Id(5)] public VoiceLanguageEnum LanguageType { get; set; }
}

[GenerateSerializer]
public class ChatMessageMeta
{
    /// <summary>
    /// Whether this message is a voice message
    /// </summary>
    [Id(0)] public bool IsVoiceMessage { get; set; }
    
    /// <summary>
    /// Voice language if this is a voice message
    /// </summary>
    [Id(1)] public VoiceLanguageEnum VoiceLanguage { get; set; }
    
    /// <summary>
    /// Whether voice parsing was successful
    /// </summary>
    [Id(2)] public bool VoiceParseSuccess { get; set; }
    
    /// <summary>
    /// Error message if voice parsing failed (in English)
    /// </summary>
    [Id(3)] public string? VoiceParseErrorMessage { get; set; }
    
    /// <summary>
    /// Duration of the voice message in seconds (from frontend)
    /// </summary>
    [Id(4)] public double VoiceDurationSeconds { get; set; }
}

public enum ChatErrorCode
{
    Success = 0,
    ParamInvalid = 20001,
    VoiceParsingFailed = 20002,
    InsufficientCredits = 20003,
    RateLimitExceeded = 20004
}