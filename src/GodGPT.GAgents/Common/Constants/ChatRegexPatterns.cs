using System.Text.RegularExpressions;

namespace GodGPT.GAgents.Common.Constants;

/// <summary>
/// Contains precompiled regex patterns for text processing in chat functionality
/// </summary>
public static class ChatRegexPatterns
{
    #region Markdown Processing Patterns
    
    /// <summary>
    /// Matches markdown links [text](url) and extracts the text part
    /// </summary>
    public static readonly Regex MarkdownLink = new(@"\[([^\]]+)\]\([^\)]+\)", RegexOptions.Compiled);
    
    /// <summary>
    /// Matches bold markdown **text** and extracts the inner text
    /// </summary>
    public static readonly Regex MarkdownBold = new(@"\*\*([^*]+)\*\*", RegexOptions.Compiled);
    
    /// <summary>
    /// Matches italic markdown *text* and extracts the inner text
    /// </summary>
    public static readonly Regex MarkdownItalic = new(@"\*([^*]+)\*", RegexOptions.Compiled);
    
    /// <summary>
    /// Matches markdown headers (# ## ### etc.) and extracts the header text
    /// </summary>
    public static readonly Regex MarkdownHeader = new(@"^#+\s*(.+)$", RegexOptions.Compiled | RegexOptions.Multiline);
    
    /// <summary>
    /// Matches markdown code blocks ```code``` for removal or replacement
    /// </summary>
    public static readonly Regex MarkdownCodeBlock = new(@"```[\s\S]*?```", RegexOptions.Compiled);
    
    /// <summary>
    /// Matches inline code `code` and extracts the inner text
    /// </summary>
    public static readonly Regex MarkdownInlineCode = new(@"`([^`]+)`", RegexOptions.Compiled);
    
    /// <summary>
    /// Matches markdown table syntax |column|column| for removal
    /// </summary>
    public static readonly Regex MarkdownTable = new(@"\|.*?\|", RegexOptions.Compiled);
    
    /// <summary>
    /// Matches strikethrough markdown ~~text~~ and extracts the inner text
    /// </summary>
    public static readonly Regex MarkdownStrikethrough = new(@"~~([^~]+)~~", RegexOptions.Compiled);
    
    /// <summary>
    /// Matches common emoji characters for removal in voice synthesis
    /// </summary>
    public static readonly Regex Emoji = new(@"[\u2600-\u26FF]|[\u2700-\u27BF]", RegexOptions.Compiled);
    
    #endregion
    
    #region Text Cleaning Patterns
    
    /// <summary>
    /// Matches non-word characters except Chinese characters for text cleaning
    /// </summary>
    public static readonly Regex NonWordChars = new(@"[^\w\u4e00-\u9fff]", RegexOptions.Compiled);
    
    /// <summary>
    /// Matches multiple whitespace characters for normalization to single space
    /// </summary>
    public static readonly Regex WhitespaceNormalize = new(@"\s+", RegexOptions.Compiled);
    
    #endregion
    
    #region Conversation Suggestions Patterns
    
    /// <summary>
    /// Pattern to match conversation suggestions block between delimiters
    /// Uses short, distinctive markers: [SUGGESTIONS] and [/SUGGESTIONS]
    /// Properly handles incomplete end markers without including them in content
    /// </summary>
    public const string ConversationSuggestionsPattern = @"\[SUGGESTIONS\](.*?)(?=\[/SUGGESTIONS\]|\[/SUGGE|$)";
    
    /// <summary>
    /// Pattern to match numbered list items (1. item, 2) item, etc.)
    /// </summary>
    public const string NumberedItemPattern = @"^\d+[\.\)]\s*(.+)$";
    
    /// <summary>
    /// Precompiled regex for extracting conversation suggestions block content
    /// </summary>
    public static readonly Regex ConversationSuggestionsBlock = new(
        ConversationSuggestionsPattern, 
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
    
    /// <summary>
    /// Precompiled regex for extracting numbered list items
    /// </summary>
    public static readonly Regex NumberedItem = new(
        NumberedItemPattern, 
        RegexOptions.Compiled);
    
    #endregion
} 