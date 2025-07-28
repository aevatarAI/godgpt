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
        ',', ';', ':', '，', '；', '：', // Semantic pause markers
        '\n', '\r'                        // Line breaks
    ];
} 