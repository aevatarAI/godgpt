using Aevatar.Core.Abstractions;

// DailyPush types are in same namespace

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Daily content selection and management GAgent
/// </summary>
public interface IDailyContentGAgent : IGAgent, IGrainWithGuidKey
{
    /// <summary>
    /// Get smart-selected contents for specific date (with deduplication)
    /// </summary>
    Task<List<DailyNotificationContent>> GetSmartSelectedContentsAsync(int count, DateTime targetDate);

    /// <summary>
    /// Add new content to the pool
    /// </summary>
    Task AddContentAsync(DailyNotificationContent content);

    /// <summary>
    /// Update existing content
    /// </summary>
    Task UpdateContentAsync(string contentId, DailyNotificationContent content);

    /// <summary>
    /// Get all available contents
    /// </summary>
    Task<List<DailyNotificationContent>> GetAllContentsAsync();

    /// <summary>
    /// Import contents from Excel data
    /// </summary>
    Task ImportContentsAsync(List<DailyNotificationContent> contents);

    /// <summary>
    /// Get content statistics
    /// </summary>
    Task<ContentStatistics> GetStatisticsAsync();

    /// <summary>
    /// Register timezone GUID mapping (called when timezone GAgent is first created)
    /// </summary>
    Task RegisterTimezoneGuidMappingAsync(Guid timezoneGuid, string timezoneId);

    /// <summary>
    /// Get timezone ID from GUID (for reverse lookup)
    /// </summary>
    Task<string?> GetTimezoneFromGuidAsync(Guid timezoneGuid);
    
    /// <summary>
    /// Get all registered timezone mappings (for global operations)
    /// </summary>
    Task<Dictionary<Guid, string>> GetAllTimezoneMappingsAsync();
}

/// <summary>
/// Content usage statistics
/// </summary>
[GenerateSerializer]
public class ContentStatistics
{
    [Id(0)] public int TotalContents { get; set; }
    [Id(1)] public int ActiveContents { get; set; }
    [Id(2)] public Dictionary<string, int> LanguageDistribution { get; set; } = new();
    [Id(3)] public DateTime LastSelection { get; set; }
    [Id(4)] public int TotalSelections { get; set; }
}