using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GodGPT.GAgents.DailyPush.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GodGPT.GAgents.DailyPush.Services;

/// <summary>
/// Daily push content model from CSV
/// </summary>
public class DailyPushContent
{
    public string ContentKey { get; set; } = "";
    public string TitleEn { get; set; } = "";
    public string ContentEn { get; set; } = "";
    public string TitleZh { get; set; } = "";
    public string ContentZh { get; set; } = "";
    public string TitleEs { get; set; } = "";
    public string ContentEs { get; set; } = "";
    public string TitleZhSc { get; set; } = "";
    public string ContentZhSc { get; set; } = "";
}

/// <summary>
/// Service for loading and managing daily push content from CSV file
/// </summary>
public class DailyPushContentService
{
    private readonly ILogger<DailyPushContentService> _logger;
    private readonly IOptionsMonitor<DailyPushOptions> _options;
    private readonly List<DailyPushContent> _contents = new();
    private DateTime _lastLoadTime = DateTime.MinValue;
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromHours(1); // Cache for 1 hour
    
    public DailyPushContentService(
        ILogger<DailyPushContentService> logger,
        IOptionsMonitor<DailyPushOptions> options)
    {
        _logger = logger;
        _options = options;
    }
    
    /// <summary>
    /// Get random daily push content for specified language
    /// </summary>
    public async Task<(string title, string content)> GetRandomContentAsync(string language = "en")
    {
        await EnsureContentLoadedAsync();
        
        if (_contents.Count == 0)
        {
            _logger.LogWarning("No daily push content available, using fallback");
            return GetFallbackContent(language);
        }
        
        var random = new Random();
        var selectedContent = _contents[random.Next(_contents.Count)];
        
        return language.ToLowerInvariant() switch
        {
            "zh" or "zh-cn" => (selectedContent.TitleZh, selectedContent.ContentZh),
            "zh-tw" or "zh-hk" => (selectedContent.TitleZhSc, selectedContent.ContentZhSc),
            "es" => (selectedContent.TitleEs, selectedContent.ContentEs),
            _ => (selectedContent.TitleEn, selectedContent.ContentEn)
        };
    }
    
    /// <summary>
    /// Get content by specific key
    /// </summary>
    public async Task<(string title, string content)> GetContentByKeyAsync(string contentKey, string language = "en")
    {
        await EnsureContentLoadedAsync();
        
        var content = _contents.FirstOrDefault(c => c.ContentKey.Equals(contentKey, StringComparison.OrdinalIgnoreCase));
        
        if (content == null)
        {
            _logger.LogWarning("Content not found for key {ContentKey}, using fallback", contentKey);
            return GetFallbackContent(language);
        }
        
        return language.ToLowerInvariant() switch
        {
            "zh" or "zh-cn" => (content.TitleZh, content.ContentZh),
            "zh-tw" or "zh-hk" => (content.TitleZhSc, content.ContentZhSc),
            "es" => (content.TitleEs, content.ContentEs),
            _ => (content.TitleEn, content.ContentEn)
        };
    }
    
    /// <summary>
    /// Get all available content keys
    /// </summary>
    public async Task<List<string>> GetAvailableKeysAsync()
    {
        await EnsureContentLoadedAsync();
        return _contents.Select(c => c.ContentKey).ToList();
    }
    
    /// <summary>
    /// Force reload content from CSV file
    /// </summary>
    public async Task ReloadContentAsync()
    {
        await LoadContentFromCsvAsync(forceReload: true);
    }
    
    /// <summary>
    /// Ensure content is loaded and not expired
    /// </summary>
    private async Task EnsureContentLoadedAsync()
    {
        if (_contents.Count == 0 || DateTime.UtcNow - _lastLoadTime > _cacheExpiry)
        {
            await LoadContentFromCsvAsync();
        }
    }
    
    /// <summary>
    /// Load content from CSV file
    /// </summary>
    private async Task LoadContentFromCsvAsync(bool forceReload = false)
    {
        try
        {
            var filePaths = _options.CurrentValue.FilePaths;
            var csvPath = GetFullPath(filePaths.CsvDictionaryPath, filePaths.BaseDirectory);
            
            if (!File.Exists(csvPath))
            {
                _logger.LogError("CSV dictionary file not found at path: {CsvPath}", csvPath);
                return;
            }
            
            _logger.LogInformation("Loading daily push content from CSV: {CsvPath}", csvPath);
            
            var lines = await File.ReadAllLinesAsync(csvPath);
            
            if (lines.Length <= 1)
            {
                _logger.LogWarning("CSV file is empty or has no data rows");
                return;
            }
            
            // Clear existing content if force reload or first load
            if (forceReload || _contents.Count == 0)
            {
                _contents.Clear();
            }
            
            // Skip header row and process data
            var loadedCount = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                try
                {
                    var content = ParseCsvLine(lines[i]);
                    if (content != null && !string.IsNullOrEmpty(content.ContentKey))
                    {
                        _contents.Add(content);
                        loadedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse CSV line {LineNumber}: {Line}", i + 1, lines[i]);
                }
            }
            
            _lastLoadTime = DateTime.UtcNow;
            _logger.LogInformation("Successfully loaded {Count} daily push contents from CSV", loadedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load daily push content from CSV");
        }
    }
    
    /// <summary>
    /// Parse a single CSV line into DailyPushContent
    /// </summary>
    private DailyPushContent? ParseCsvLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;
            
        // Simple CSV parsing - splits by comma and handles basic quoting
        var columns = ParseCsvColumns(line);
        
        if (columns.Length < 9)
        {
            _logger.LogWarning("CSV line has insufficient columns: {Line}", line);
            return null;
        }
        
        return new DailyPushContent
        {
            ContentKey = columns[0].Trim(),
            TitleEn = columns[1].Trim(),
            ContentEn = columns[2].Trim(),
            TitleZh = columns[3].Trim(),
            ContentZh = columns[4].Trim(),
            TitleEs = columns[5].Trim(),
            ContentEs = columns[6].Trim(),
            TitleZhSc = columns[7].Trim(),
            ContentZhSc = columns[8].Trim()
        };
    }
    
    /// <summary>
    /// Parse CSV columns with basic quote handling
    /// </summary>
    private string[] ParseCsvColumns(string line)
    {
        var columns = new List<string>();
        var current = "";
        var inQuotes = false;
        
        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                columns.Add(current.Trim('"'));
                current = "";
            }
            else
            {
                current += c;
            }
        }
        
        columns.Add(current.Trim('"'));
        return columns.ToArray();
    }
    
    /// <summary>
    /// Get full file path considering base directory
    /// </summary>
    private string GetFullPath(string filePath, string baseDirectory)
    {
        if (Path.IsPathRooted(filePath))
        {
            return filePath;
        }
        
        if (!string.IsNullOrEmpty(baseDirectory))
        {
            return Path.Combine(baseDirectory, filePath);
        }
        
        return filePath;
    }
    
    /// <summary>
    /// Get fallback content when CSV is not available
    /// </summary>
    private (string title, string content) GetFallbackContent(string language)
    {
        return language.ToLowerInvariant() switch
        {
            "zh" or "zh-cn" => ("每日提醒", "今天也要保持正念，专注当下。"),
            "zh-tw" or "zh-hk" => ("每日提醒", "今天也要保持正念，專注當下。"),
            "es" => ("Recordatorio Diario", "Mantén la atención plena y concéntrate en el presente."),
            _ => ("Daily Reminder", "Stay mindful and focus on the present moment.")
        };
    }
}
