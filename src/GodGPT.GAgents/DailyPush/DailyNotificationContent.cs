namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Daily notification content with multi-language support
/// </summary>
[GenerateSerializer]
public class DailyNotificationContent
{
    /// <summary>
    /// Content key from Excel (e.g., task01, task02)
    /// </summary>
    [Id(0)]
    public string Id { get; set; } = "";

    /// <summary>
    /// Localized content for different languages
    /// </summary>
    [Id(1)]
    public Dictionary<string, LocalizedContentData> LocalizedContents { get; set; } = new();

    /// <summary>
    /// Whether content is active for selection
    /// </summary>
    [Id(2)]
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Localized content data for specific language
/// </summary>
[GenerateSerializer]
public class LocalizedContentData
{
    /// <summary>
    /// Localized title
    /// </summary>
    [Id(0)]
    public string Title { get; set; } = "";

    /// <summary>
    /// Localized content body
    /// </summary>
    [Id(1)]
    public string Content { get; set; } = "";
}

/// <summary>
/// Extension methods for DailyNotificationContent
/// </summary>
public static class DailyNotificationContentExtensions
{
    /// <summary>
    /// Get localized content for specific language with fallback
    /// </summary>
    public static LocalizedContentData GetLocalizedContent(this DailyNotificationContent content, string languageCode)
    {
        // Debug logging for language selection
        var availableLanguages = string.Join(", ", content.LocalizedContents.Keys);

        if (content.LocalizedContents.TryGetValue(languageCode, out var localized))
        {
            // Log successful language match with title preview
            var titlePreview = localized.Title.Length > 20 ? localized.Title.Substring(0, 20) + "..." : localized.Title;
            // Note: Can't use ILogger here as this is extension method - will be logged from calling context
            return localized;
        }

        // Fallback to English
        if (content.LocalizedContents.TryGetValue("en", out var englishContent))
        {
            var titlePreview = englishContent.Title.Length > 20
                ? englishContent.Title.Substring(0, 20) + "..."
                : englishContent.Title;
            // Note: Can't use ILogger here as this is extension method - will be logged from calling context
            return englishContent;
        }

        // Fallback to first available language
        if (content.LocalizedContents.Count > 0)
        {
            var firstContent = content.LocalizedContents.Values.First();
            var firstLanguage = content.LocalizedContents.Keys.First();
            var titlePreview = firstContent.Title.Length > 20
                ? firstContent.Title.Substring(0, 20) + "..."
                : firstContent.Title;
            // Note: Can't use ILogger here as this is extension method - will be logged from calling context
            return firstContent;
        }

        // Last resort - empty content
        return new LocalizedContentData
        {
            Title = $"Content {content.Id}",
            Content = "Content not available in requested language"
        };
    }

    /// <summary>
    /// Check if content supports specific language
    /// </summary>
    public static bool SupportsLanguage(this DailyNotificationContent content, string languageCode)
    {
        return content.LocalizedContents.ContainsKey(languageCode);
    }

    /// <summary>
    /// Get all supported languages for this content
    /// </summary>
    public static IEnumerable<string> GetSupportedLanguages(this DailyNotificationContent content)
    {
        return content.LocalizedContents.Keys;
    }
}