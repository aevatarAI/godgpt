using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
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
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromHours(24); // Cache for 24 hours - CSV content rarely changes
    private readonly SemaphoreSlim _loadSemaphore = new SemaphoreSlim(1, 1); // Prevent concurrent loading
    
    public DailyPushContentService(
        ILogger<DailyPushContentService> logger,
        IOptionsMonitor<DailyPushOptions> options)
    {
        _logger = logger;
        _options = options;
        
        _logger.LogDebug("🚀 Initializing DailyPushContentService...");
        _logger.LogDebug("📁 Configured CSV Path: {CsvPath}", options.CurrentValue.FilePaths.CsvDictionaryPath);
    }
    
    /// <summary>
    /// Get random daily push content for specified language
    /// </summary>
    public async Task<(string title, string content)> GetRandomContentAsync(GodGPTLanguage language = GodGPTLanguage.English)
    {
        _logger.LogDebug("🎲 Requesting random content for language: {Language}", language);
        
        await EnsureContentLoadedAsync();
        
        if (_contents.Count == 0)
        {
            _logger.LogWarning("⚠️ No daily push content available, using fallback for language: {Language}", language);
            return GetFallbackContent(language);
        }
        
        var random = new Random();
        var selectedIndex = random.Next(_contents.Count);
        var selectedContent = _contents[selectedIndex];
        
        _logger.LogDebug("🎯 Selected content #{Index}/{Total}: Key={ContentKey} for language={Language}", 
            selectedIndex + 1, _contents.Count, selectedContent.ContentKey, language);
        
        return language switch
        {
            GodGPTLanguage.CN => (selectedContent.TitleZhSc, selectedContent.ContentZhSc), // Simplified Chinese
            GodGPTLanguage.TraditionalChinese => (selectedContent.TitleZh, selectedContent.ContentZh), // Traditional Chinese
            GodGPTLanguage.Spanish => (selectedContent.TitleEs, selectedContent.ContentEs), // Spanish
            GodGPTLanguage.English => (selectedContent.TitleEn, selectedContent.ContentEn), // English
            _ => (selectedContent.TitleEn, selectedContent.ContentEn) // Default English
        };
    }
    
    /// <summary>
    /// Get content by specific key
    /// </summary>
    public async Task<(string title, string content)> GetContentByKeyAsync(string contentKey, GodGPTLanguage language = GodGPTLanguage.English)
    {
        await EnsureContentLoadedAsync();
        
        var content = _contents.FirstOrDefault(c => c.ContentKey.Equals(contentKey, StringComparison.OrdinalIgnoreCase));
        
        if (content == null)
        {
            _logger.LogWarning("Content not found for key {ContentKey}, using fallback", contentKey);
            return GetFallbackContent(language);
        }
        
        return language switch
        {
            GodGPTLanguage.CN => (content.TitleZhSc, content.ContentZhSc), // Simplified Chinese
            GodGPTLanguage.TraditionalChinese => (content.TitleZh, content.ContentZh), // Traditional Chinese
            GodGPTLanguage.Spanish => (content.TitleEs, content.ContentEs), // Spanish
            GodGPTLanguage.English => (content.TitleEn, content.ContentEn), // English
            _ => (content.TitleEn, content.ContentEn) // Default English
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
    /// Get all loaded content entries
    /// </summary>
    public async Task<List<DailyPushContent>> GetAllContentsAsync()
    {
        await EnsureContentLoadedAsync();
        return _contents.ToList(); // Return a copy to prevent external modification
    }
    
    /// <summary>
    /// Ensure content is loaded and not expired
    /// </summary>
    private async Task EnsureContentLoadedAsync()
    {
        var now = DateTime.UtcNow;
        var cacheAge = now - _lastLoadTime;
        var needsReload = _contents.Count == 0 || cacheAge > _cacheExpiry;
        
        if (needsReload)
        {
            // Use semaphore to prevent concurrent loading
            await _loadSemaphore.WaitAsync();
            try
            {
                // Double-check pattern: another thread might have loaded while we were waiting
                now = DateTime.UtcNow;
                cacheAge = now - _lastLoadTime;
                var stillNeedsReload = _contents.Count == 0 || cacheAge > _cacheExpiry;
                
                if (stillNeedsReload)
                {
                    if (_contents.Count == 0)
                    {
                        _logger.LogDebug("📋 Content cache is empty, loading CSV for first time");
                    }
                    else
                    {
                        _logger.LogDebug("🕐 Content cache expired (Age: {CacheAge}, Expiry: {CacheExpiry}), reloading CSV", 
                            cacheAge, _cacheExpiry);
                    }
                    
                    await LoadContentFromCsvAsync();
                }
                else
                {
                    _logger.LogDebug("✅ Content was loaded by another thread while waiting");
                }
            }
            finally
            {
                _loadSemaphore.Release();
            }
        }
        else
        {
            _logger.LogDebug("✅ Content cache is valid ({Count} entries, Age: {CacheAge})", _contents.Count, cacheAge);
        }
    }
    
    /// <summary>
    /// Load content from CSV file (local path)
    /// </summary>
    private async Task LoadContentFromCsvAsync(bool forceReload = false)
    {
        try
        {
            var filePaths = _options.CurrentValue.FilePaths;
            var csvPath = filePaths.CsvDictionaryPath;
            
            _logger.LogDebug("📁 Attempting to load CSV from local path: {CsvPath}", csvPath);
            
            if (string.IsNullOrEmpty(csvPath))
            {
                _logger.LogError("❌ CSV dictionary path is not configured");
                return;
            }
            
            if (!File.Exists(csvPath))
            {
                _logger.LogError("❌ CSV file not found at path: {CsvPath}", csvPath);
                return;
            }
            
            var fileInfo = new FileInfo(csvPath);
            _logger.LogInformation("📁 Found CSV file: {CsvPath} (Size: {FileSize} bytes, Modified: {LastModified})",
                csvPath, fileInfo.Length, fileInfo.LastWriteTime);
            
            var csvContent = await File.ReadAllTextAsync(csvPath);
            var contentLength = csvContent.Length;
            
            _logger.LogInformation("📁 Successfully loaded CSV from local file: {CsvPath} (Size: {ContentSize} chars)", 
                csvPath, contentLength);
            
            var lines = csvContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            _logger.LogDebug("📄 CSV file contains {TotalLines} lines (including header)", lines.Length);
            
            if (lines.Length <= 1)
            {
                _logger.LogWarning("⚠️ CSV file is empty or has no data rows (only {LineCount} lines)", lines.Length);
                return;
            }
            
            // Log header for debugging
            if (lines.Length > 0)
            {
                _logger.LogDebug("📋 CSV header: {Header}", lines[0]);
            }
            
            // Load content into temporary list to avoid intermediate empty state
            var newContents = new List<DailyPushContent>();
            var loadedCount = 0;
            var failedCount = 0;
            var dataRowCount = lines.Length - 1; // Exclude header
            
            for (int i = 1; i < lines.Length; i++)
            {
                try
                {
                    var content = ParseCsvLine(lines[i]);
                    if (content != null && !string.IsNullOrEmpty(content.ContentKey))
                    {
                        newContents.Add(content);
                        loadedCount++;
                        
                        // Log first few entries for debugging
                        if (loadedCount <= 3)
                        {
                            _logger.LogDebug("✅ Loaded content #{LoadedCount}: Key={ContentKey}, TitleEn='{TitleEn}', TitleZh='{TitleZh}'",
                                loadedCount, content.ContentKey, content.TitleEn, content.TitleZh);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Skipped line {LineNumber}: Invalid content or empty key", i + 1);
                        failedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "❌ Failed to parse CSV line {LineNumber}: {Line}", i + 1, lines[i]);
                    failedCount++;
                }
            }
            
            // Atomically replace content to avoid race conditions
            var previousCount = _contents.Count;
            _contents.Clear();
            _contents.AddRange(newContents);
            
            if (previousCount > 0 && previousCount != loadedCount)
            {
                _logger.LogInformation("🔄 Replaced {PreviousCount} existing content entries with {NewCount} entries", 
                    previousCount, loadedCount);
            }
            
            _lastLoadTime = DateTime.UtcNow;
            
            _logger.LogInformation("🎯 CSV loading completed: {LoadedCount}/{DataRowCount} entries loaded successfully, {FailedCount} failed",
                loadedCount, dataRowCount, failedCount);
            
            if (loadedCount > 0)
            {
                var sampleKeys = _contents.Take(5).Select(c => c.ContentKey).ToList();
                _logger.LogInformation("📚 Sample content keys: [{SampleKeys}]", string.Join(", ", sampleKeys));
            }
        }
        catch (FileNotFoundException fileEx)
        {
            var csvPath = _options.CurrentValue.FilePaths.CsvDictionaryPath;
            _logger.LogError(fileEx, "📁 CSV file not found: {CsvPath}", csvPath);
        }
        catch (UnauthorizedAccessException accessEx)
        {
            var csvPath = _options.CurrentValue.FilePaths.CsvDictionaryPath;
            _logger.LogError(accessEx, "🔒 Access denied reading CSV file: {CsvPath}", csvPath);
        }
        catch (Exception ex)
        {
            var csvPath = _options.CurrentValue.FilePaths.CsvDictionaryPath;
            _logger.LogError(ex, "💥 Critical error loading daily push content from local file: {CsvPath}", csvPath);
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
    /// Get fallback content when CSV is not available
    /// </summary>
    private (string title, string content) GetFallbackContent(GodGPTLanguage language)
    {
        return language switch
        {
            GodGPTLanguage.CN => ("每日提醒", "今天也要保持正念，专注当下。"), // Simplified Chinese
            GodGPTLanguage.TraditionalChinese => ("每日提醒", "今天也要保持正念，專注當下。"), // Traditional Chinese
            GodGPTLanguage.Spanish => ("Recordatorio Diario", "Mantén la atención plena y concéntrate en el presente."), // Spanish
            GodGPTLanguage.English => ("Daily Reminder", "Stay mindful and focus on the present moment."), // English
            _ => ("Daily Reminder", "Stay mindful and focus on the present moment.") // Default English
        };
    }
}
