namespace GodGPT.GAgents.Common.Constants;

/// <summary>
/// Contains constants related to voice chat functionality
/// </summary>
public static class VoiceChatConstants
{
    /// <summary>
    /// Voice synthesis sentence detection
    /// Extended sentence ending characters including punctuation marks for semantic pauses
    /// </summary>
    public static readonly List<char> SentenceEnders =
    [
        '.', '?', '!', '。', '？', '！', // Complete sentence endings
        // Removed comma separators to reduce TTS call frequency
        // ',', ';', ':', '，', '；', '：', // Semantic pause markers - REMOVED to fix 429 errors
        '\n', '\r'                        // Line breaks
    ];
} 