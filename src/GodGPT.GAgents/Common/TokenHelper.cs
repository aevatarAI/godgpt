using System.Text.RegularExpressions;
using Aevatar.GAgents.AI.Common;

namespace Aevatar.Application.Grains.Common;

public static class TokenHelper
{
    private const int DEFAULT_GPT4_MAX_TOKENS = 128000;
    private const double TOKEN_RESERVE_RATIO = 0.2; // Reserve 20% tokens for model response
    private const int AVERAGE_CHARS_PER_TOKEN = 4; // Average 4 characters per token in GPT models

    public static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // Remove extra whitespace characters
        text = Regex.Replace(text, @"\s+", " ").Trim();
        
        // Estimate token count based on character length
        return (int)Math.Ceiling(text.Length / (double)AVERAGE_CHARS_PER_TOKEN);
    }

    public static int EstimateTokenCount(IEnumerable<ChatMessage> messages)
    {
        if (messages == null)
            return 0;

        return messages.Sum(msg => EstimateTokenCount(msg.Content ?? string.Empty));
    }

    /// <summary>
    /// Select history messages strictly based on token limits
    /// </summary>
    /// <param name="allHistory">All historical messages</param>
    /// <param name="newMessage">New message to be added</param>
    /// <param name="systemPrompt">System prompt</param>
    /// <returns>Filtered list of history messages</returns>
    public static List<ChatMessage> SelectHistoryMessages(
        IEnumerable<ChatMessage> allHistory,
        string newMessage,
        string systemPrompt)
    {
        var history = allHistory?.ToList() ?? new List<ChatMessage>();
        
        // Calculate tokens for system prompt and new message
        int systemTokens = EstimateTokenCount(systemPrompt);
        int newMessageTokens = EstimateTokenCount(newMessage);
        
        // Calculate available tokens for history messages
        int maxAllowedTokens = (int)(DEFAULT_GPT4_MAX_TOKENS * (1 - TOKEN_RESERVE_RATIO));
        int availableTokens = maxAllowedTokens - systemTokens - newMessageTokens;

        if (availableTokens <= 0)
        {
            return new List<ChatMessage>(); // Return empty list if no space available
        }

        // Return all history if empty or within token limit
        int totalHistoryTokens = EstimateTokenCount(history);
        if (history.Count == 0 || totalHistoryTokens <= availableTokens)
        {
            return history;
        }

        // Add messages from newest to oldest until reaching token limit
        var selectedMessages = new List<ChatMessage>();
        int usedTokens = 0;

        foreach (var msg in history.AsEnumerable().Reverse())
        {
            int msgTokens = EstimateTokenCount(msg.Content);
            if (usedTokens + msgTokens > availableTokens)
                break;
            
            selectedMessages.Insert(0, msg); // Insert at beginning to maintain message order
            usedTokens += msgTokens;
        }

        return selectedMessages;
    }
} 