using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Distributed;
using Volo.Abp.Caching;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Daily push Redis service that uses ABP's distributed cache for production and in-memory for development
/// </summary>
public class DailyPushRedisService
{
    private readonly ILogger<DailyPushRedisService> _logger;
    private readonly IDistributedCache<object> _distributedCache;
    
    // Fallback in-memory store for development when Redis is not available
    private static readonly Dictionary<string, object> _memoryStore = new();
    private static readonly Dictionary<string, DateTime> _expirations = new();
    private static readonly object _lock = new object();
    private readonly bool _useDistributedCache;
    
    // Redis key patterns
    private const string PUSH_READ_KEY_PATTERN = "daily_push:read:{0}:{1}"; // userId:date
    private const string CONTENT_USAGE_KEY_PATTERN = "daily_push:content:{0}"; // date
    private const string USER_UNREAD_KEY_PATTERN = "daily_push:unread:{0}"; // date
    private const string PUSH_STATS_KEY_PATTERN = "daily_push:stats:{0}"; // date
    
    public DailyPushRedisService(
        ILogger<DailyPushRedisService> logger, 
        IDistributedCache<object>? distributedCache = null)
    {
        _logger = logger;
        _distributedCache = distributedCache;
        _useDistributedCache = distributedCache != null;
        
        if (_useDistributedCache)
        {
            _logger.LogInformation("DailyPushRedisService initialized with distributed cache (Redis)");
        }
        else
        {
            _logger.LogInformation("DailyPushRedisService initialized with in-memory cache (development mode)");
            // Clean up expired entries periodically for in-memory mode
            _ = Task.Run(CleanupExpiredEntries);
        }
    }

    /// <summary>
    /// Mark daily push as read for a user
    /// </summary>
    public async Task<bool> MarkDailyPushAsReadAsync(Guid userId, DateTime date)
    {
        try
        {
            var dateKey = date.ToString("yyyy-MM-dd");
            var key = string.Format(PUSH_READ_KEY_PATTERN, userId, dateKey);
            
            if (_useDistributedCache)
            {
                var value = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(DailyPushConstants.REDIS_DATA_TTL_DAYS)
                };
                
                await _distributedCache.SetAsync(key, value, options);
                _logger.LogDebug($"[REDIS] Marked daily push as read for user {userId} on {dateKey}");
            }
            else
            {
                lock (_lock)
                {
                    _memoryStore[key] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    _expirations[key] = DateTime.UtcNow.AddDays(DailyPushConstants.REDIS_DATA_TTL_DAYS);
                    
                    // Update unread set (remove user from unread list)
                    var unreadKey = string.Format(USER_UNREAD_KEY_PATTERN, dateKey);
                    if (_memoryStore.ContainsKey(unreadKey) && _memoryStore[unreadKey] is HashSet<string> unreadSet)
                    {
                        unreadSet.Remove(userId.ToString());
                    }
                }
                _logger.LogDebug($"[MEMORY] Marked daily push as read for user {userId} on {dateKey}");
            }
            
            // Update statistics
            await UpdatePushStatisticsAsync(dateKey, readCount: 1);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to mark daily push as read for user {userId}");
            return false;
        }
    }

    /// <summary>
    /// Check if daily push has been read by user
    /// </summary>
    public async Task<bool> IsDailyPushReadAsync(Guid userId, DateTime date)
    {
        try
        {
            var dateKey = date.ToString("yyyy-MM-dd");
            var key = string.Format(PUSH_READ_KEY_PATTERN, userId, dateKey);
            
            if (_useDistributedCache)
            {
                var value = await _distributedCache.GetAsync(key);
                return value != null;
            }
            else
            {
                lock (_lock)
                {
                    CleanupExpiredEntries();
                    return _memoryStore.ContainsKey(key);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to check read status for user {userId}");
            return false;
        }
    }

    /// <summary>
    /// Record content usage for deduplication
    /// </summary>
    public async Task<bool> RecordContentUsageAsync(DateTime date, List<string> contentIds)
    {
        try
        {
            var dateKey = date.ToString("yyyy-MM-dd");
            var key = string.Format(CONTENT_USAGE_KEY_PATTERN, dateKey);
            
            lock (_lock)
            {
                _memoryStore[key] = contentIds;
                _expirations[key] = DateTime.UtcNow.AddDays(DailyPushConstants.CONTENT_HISTORY_DAYS);
            }
            
            _logger.LogDebug($"[MEMORY] Recorded content usage for {dateKey}: {string.Join(", ", contentIds)}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to record content usage for {date:yyyy-MM-dd}");
            return false;
        }
    }

    /// <summary>
    /// Get used content IDs for a date
    /// </summary>
    public async Task<List<string>> GetUsedContentIdsAsync(DateTime date)
    {
        try
        {
            var dateKey = date.ToString("yyyy-MM-dd");
            var key = string.Format(CONTENT_USAGE_KEY_PATTERN, dateKey);
            
            lock (_lock)
            {
                CleanupExpiredEntries();
                
                if (_memoryStore.ContainsKey(key) && _memoryStore[key] is List<string> contentIds)
                {
                    return new List<string>(contentIds);
                }
            }
            
            return new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to get used content IDs for {date:yyyy-MM-dd}");
            return new List<string>();
        }
    }

    /// <summary>
    /// Get users who haven't read daily push (for afternoon retry)
    /// </summary>
    public async Task<List<Guid>> GetUnreadUsersAsync(List<Guid> candidateUsers, DateTime date)
    {
        try
        {
            var dateKey = date.ToString("yyyy-MM-dd");
            var unreadUsers = new List<Guid>();
            
            lock (_lock)
            {
                CleanupExpiredEntries();
                
                foreach (var userId in candidateUsers)
                {
                    var key = string.Format(PUSH_READ_KEY_PATTERN, userId, dateKey);
                    if (!_memoryStore.ContainsKey(key))
                    {
                        unreadUsers.Add(userId);
                    }
                }
            }
            
            _logger.LogDebug($"[MEMORY] Found {unreadUsers.Count} unread users out of {candidateUsers.Count} candidates for {dateKey}");
            return unreadUsers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to get unread users for {date:yyyy-MM-dd}");
            return candidateUsers; // Fallback to all users
        }
    }

    /// <summary>
    /// Initialize unread user set for a date
    /// </summary>
    public async Task<bool> InitializeUnreadUsersAsync(DateTime date, List<Guid> allUsers)
    {
        try
        {
            var dateKey = date.ToString("yyyy-MM-dd");
            var key = string.Format(USER_UNREAD_KEY_PATTERN, dateKey);
            
            lock (_lock)
            {
                var userSet = new HashSet<string>(allUsers.Select(u => u.ToString()));
                _memoryStore[key] = userSet;
                _expirations[key] = DateTime.UtcNow.AddDays(2);
            }
            
            _logger.LogDebug($"[MEMORY] Initialized unread user set for {dateKey} with {allUsers.Count} users");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to initialize unread users for {date:yyyy-MM-dd}");
            return false;
        }
    }

    /// <summary>
    /// Update push statistics
    /// </summary>
    public async Task UpdatePushStatisticsAsync(string dateKey, int sentCount = 0, int readCount = 0, int failureCount = 0)
    {
        try
        {
            var key = string.Format(PUSH_STATS_KEY_PATTERN, dateKey);
            
            lock (_lock)
            {
                if (!_memoryStore.ContainsKey(key))
                {
                    _memoryStore[key] = new PushStatistics { Date = DateTime.ParseExact(dateKey, "yyyy-MM-dd", null) };
                    _expirations[key] = DateTime.UtcNow.AddDays(30);
                }
                
                if (_memoryStore[key] is PushStatistics stats)
                {
                    stats.SentCount += sentCount;
                    stats.ReadCount += readCount;
                    stats.FailureCount += failureCount;
                    stats.LastUpdated = DateTime.UtcNow;
                }
            }
            
            _logger.LogDebug($"[MEMORY] Updated push statistics for {dateKey}: sent={sentCount}, read={readCount}, failures={failureCount}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to update push statistics for {dateKey}");
        }
    }

    /// <summary>
    /// Get push statistics for a date
    /// </summary>
    public async Task<PushStatistics> GetPushStatisticsAsync(DateTime date)
    {
        try
        {
            var dateKey = date.ToString("yyyy-MM-dd");
            var key = string.Format(PUSH_STATS_KEY_PATTERN, dateKey);
            
            lock (_lock)
            {
                CleanupExpiredEntries();
                
                if (_memoryStore.ContainsKey(key) && _memoryStore[key] is PushStatistics stats)
                {
                    return new PushStatistics
                    {
                        Date = stats.Date,
                        SentCount = stats.SentCount,
                        ReadCount = stats.ReadCount,
                        FailureCount = stats.FailureCount,
                        LastUpdated = stats.LastUpdated
                    };
                }
            }
            
            return new PushStatistics { Date = date };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to get push statistics for {date:yyyy-MM-dd}");
            return new PushStatistics { Date = date };
        }
    }

    /// <summary>
    /// Cleanup expired data
    /// </summary>
    public async Task CleanupExpiredDataAsync()
    {
        CleanupExpiredEntries();
    }

    /// <summary>
    /// Health check for Redis connection
    /// </summary>
    public async Task<bool> HealthCheckAsync()
    {
        try
        {
            var testKey = "daily_push:health_check";
            var testValue = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            
            lock (_lock)
            {
                _memoryStore[testKey] = testValue;
                _expirations[testKey] = DateTime.UtcNow.AddSeconds(10);
                
                var retrieved = _memoryStore.ContainsKey(testKey) ? _memoryStore[testKey].ToString() : null;
                var isHealthy = retrieved == testValue;
                
                if (isHealthy)
                {
                    _memoryStore.Remove(testKey);
                    _expirations.Remove(testKey);
                    _logger.LogDebug("[MEMORY] Health check passed");
                }
                else
                {
                    _logger.LogWarning("[MEMORY] Health check failed");
                }
                
                return isHealthy;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MEMORY] Health check failed with exception");
            return false;
        }
    }

    private void CleanupExpiredEntries()
    {
        try
        {
            var now = DateTime.UtcNow;
            var expiredKeys = new List<string>();
            
            lock (_lock)
            {
                foreach (var kvp in _expirations)
                {
                    if (kvp.Value < now)
                    {
                        expiredKeys.Add(kvp.Key);
                    }
                }
                
                foreach (var key in expiredKeys)
                {
                    _memoryStore.Remove(key);
                    _expirations.Remove(key);
                }
            }
            
            if (expiredKeys.Count > 0)
            {
                _logger.LogDebug($"[MEMORY] Cleaned up {expiredKeys.Count} expired entries");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MEMORY] Failed to cleanup expired entries");
        }
    }
}

/// <summary>
/// Push statistics model
/// </summary>
public class PushStatistics
{
    public DateTime Date { get; set; }
    public int SentCount { get; set; }
    public int ReadCount { get; set; }
    public int FailureCount { get; set; }
    public DateTime LastUpdated { get; set; }
    
    public double ReadRate => SentCount > 0 ? (double)ReadCount / SentCount : 0;
    public double FailureRate => SentCount > 0 ? (double)FailureCount / SentCount : 0;
}