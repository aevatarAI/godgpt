namespace GodGPT.GAgents.Common.Constants;

/// <summary>
/// Contains predefined prompts and prompt templates for chat functionality
/// </summary>
public static class ChatPrompts
{
    /// <summary>
    /// Prompt template for generating conversation suggestions
    /// This prompt instructs the LLM to generate 3 relevant conversation suggestions
    /// after answering the user's question to help continue deeper engagement
    /// </summary>
    public const string ConversationSuggestionsPrompt = @"

After answering the user's question, please generate 3 relevant conversation suggestions to help users continue deeper engagement. These suggestions should:

1. Be diverse in type: can be related concepts, practical tools, specific steps, alternative approaches, deeper analysis, etc., not limited to questions
2. Be concise and clear: each suggestion should be under 10 words
3. Be related to the current topic but expand conversation dimensions

Please strictly follow this format at the end of your response:

---CONVERSATION_SUGGESTIONS---
1. [suggestion 1]
2. [suggestion 2]
3. [suggestion 3]
---END_SUGGESTIONS---

Note: The suggestions section will not be displayed to users, it's only for system processing.";
} 