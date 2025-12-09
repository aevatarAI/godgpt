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
        /// LLM will detect user's question language and adapt the 4th suggestion accordingly
        /// </summary>
        public const string ConversationSuggestionsPrompt = @"

After answering the user's question, please generate 4 relevant conversation suggestions to help users continue deeper engagement. These suggestions should:

1. Be diverse in type: can be related concepts, practical tools, specific steps, alternative approaches, deeper analysis, etc., not limited to questions
2. Be concise and clear: each suggestion must be under 15 characters
3. Be related to the current topic but expand conversation dimensions
4. The 4th suggestion is FIXED and must match the language the user used in their question:
   - If user asked in English: ""I don't understand""
   - If user asked in Simplified Chinese: ""我不理解""
   - If user asked in Traditional Chinese: ""我不明白""
   - If user asked in Spanish: ""No entiendo""
   - For other languages, use English: ""I don't understand""

IMPORTANT for formatting:
- All 4 suggestions must follow the EXACT SAME format: just the text, no quotes, no extra punctuation
- Keep the same style and structure for all 4 items
- The 4th suggestion should look identical in format to the first 3

Please strictly follow this format at the end of your response:

[SUGGESTIONS]
1. [suggestion 1]
2. [suggestion 2]
3. [suggestion 3]
4. [appropriate ""I don't understand"" translation based on user's question language]
[/SUGGESTIONS]

Note: The suggestions section will not be displayed to users, it's only for system processing.";
    }
} 