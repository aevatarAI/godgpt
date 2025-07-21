using Aevatar.GAgents.AI.Common;
using GodGPT.GAgents.SpeechChat;

namespace Aevatar.Application.Grains.Agents.ChatManager.Dtos;

/// <summary>
/// Chat message DTO that includes voice/audio metadata for history records
/// </summary>
[GenerateSerializer]
public class ChatMessageWithMetaDto
{
    /// <summary>
    /// Chat role (User or Assistant)
    /// </summary>
    [Id(0)] public ChatRole ChatRole { get; set; }
    
    /// <summary>
    /// Message content text
    /// </summary>
    [Id(1)] public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether this message is a voice message
    /// </summary>
    [Id(2)] public bool IsVoiceMessage { get; set; }
    
    /// <summary>
    /// Voice language if this is a voice message
    /// </summary>
    [Id(3)] public VoiceLanguageEnum VoiceLanguage { get; set; } = VoiceLanguageEnum.English;
    
    /// <summary>
    /// Whether voice parsing was successful
    /// </summary>
    [Id(4)] public bool VoiceParseSuccess { get; set; } = true;
    
    /// <summary>
    /// Error message if voice parsing failed
    /// </summary>
    [Id(5)] public string? VoiceParseErrorMessage { get; set; }
    
    /// <summary>
    /// Duration of the voice message in seconds
    /// </summary>
    [Id(6)] public double VoiceDurationSeconds { get; set; }
    
    /// <summary>
    /// List of uploaded image keys for this message
    /// </summary>
    [Id(7)] public List<string> ImageKeys { get; set; } = new List<string>();
    
    /// <summary>
    /// Create from ChatMessage and optional ChatMessageMeta
    /// </summary>
    public static ChatMessageWithMetaDto Create(ChatMessage message, ChatMessageMeta? meta = null)
    {
        return new ChatMessageWithMetaDto
        {
            ChatRole = message.ChatRole,
            Content = message.Content,
            IsVoiceMessage = meta?.IsVoiceMessage ?? false,
            VoiceLanguage = meta?.VoiceLanguage ?? VoiceLanguageEnum.English,
            VoiceParseSuccess = meta?.VoiceParseSuccess ?? true,
            VoiceParseErrorMessage = meta?.VoiceParseErrorMessage,
            VoiceDurationSeconds = meta?.VoiceDurationSeconds ?? 0.0,
            ImageKeys = message.ImageKeys ?? new List<string>()
        };
    }
} 