using System;

namespace GodGPT.GAgents.Common.Constants
{
    /// <summary>
    /// Contains predefined prompts for chat functionality
    /// </summary>
    public static class ChatPrompts
    {
        /// <summary>
        /// Prompt to instruct LLM to generate conversation suggestions for text chat
        /// Format must match ChatRegexPatterns.ConversationSuggestionsPattern
        /// </summary>
        public const string ConversationSuggestionsPrompt = @"

After answering the user's question, please generate 3 relevant conversation suggestions to help users continue deeper engagement. These suggestions should:

1. Be diverse in type: can be related concepts, practical tools, specific steps, alternative approaches, deeper analysis, etc., not limited to questions
2. Be concise and clear: each suggestion must be under 15 characters
3. Be related to the current topic but expand conversation dimensions

Please strictly follow this format at the end of your response:

[SUGGESTIONS]
1. [suggestion 1]
2. [suggestion 2]
3. [suggestion 3]
[/SUGGESTIONS]

Note: The suggestions section will not be displayed to users, it's only for system processing.";
    }
} 