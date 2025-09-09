using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Threading.Tasks;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Redis-based push deduplication service
/// Provides atomic operations to prevent duplicate push notifications
/// </summary>
public class PushDeduplicationService : IPushDeduplicationService
{
    private readonly IDatabase _redis;
    private readonly ILogger<PushDeduplicationService> _logger;
    
    // Redis key prefixes for different push types
    private const string MORNING_KEY_PREFIX = "godgpt:push:morning";
    private const string RETRY_KEY_PREFIX = "godgpt:push:retry";
    
    // Testing suffix for deployment control - modify this value before each release testing
    // Set to null for production, set to any string for testing (e.g., "v1.2.3", "test_round_2")
    private static readonly string? TESTING_SUFFIX = null; // Production mode: using standard Redis keys
    
    // TTL for Redis keys (24 hours for daily push deduplication)
    private static readonly TimeSpan KEY_TTL = TimeSpan.FromHours(24);
    
    public PushDeduplicationService(IConnectionMultiplexer redis, ILogger<PushDeduplicationService> logger)
    {
        _redis = redis.GetDatabase();
        _logger = logger;
    }
    
    public async Task<bool> TryClaimMorningPushAsync(string deviceId, DateOnly date, string timeZoneId)
    {
        var key = BuildMorningKey(deviceId, date, timeZoneId);
        
        try
        {
            // Atomic operation: Set key only if it doesn't exist (SETNX)
            var timestamp = DateTime.UtcNow.ToString("O"); // ISO 8601 format
            var success = await _redis.StringSetAsync(key, timestamp, KEY_TTL, When.NotExists);
            
            if (success)
            {
                _logger.LogInformation("✅ Morning push claimed: {Key}", key);
                return true;
            }
            else
            {
                // Get existing timestamp for logging
                var existingValue = await _redis.StringGetAsync(key);
                _logger.LogInformation("❌ Morning push blocked: {Key} already exists (sent at: {ExistingTime})", 
                    key, existingValue.HasValue ? existingValue.ToString() : "unknown");
                return false;
            }
        }
        catch (RedisException ex)
        {
            _logger.LogWarning("Redis error in TryClaimMorningPushAsync, degrading to allow push: {Error}", ex.Message);
            return true; // Graceful degradation: allow push when Redis is unavailable
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in TryClaimMorningPushAsync for key: {Key}", key);
            return true; // Graceful degradation
        }
    }
    
    public async Task<bool> TryClaimRetryPushAsync(string deviceId, DateOnly date, string timeZoneId)
    {
        var morningKey = BuildMorningKey(deviceId, date, timeZoneId);
        var retryKey = BuildRetryKey(deviceId, date, timeZoneId);
        
        try
        {
            // 1. Check if morning push was sent
            var morningExists = await _redis.KeyExistsAsync(morningKey);
            if (!morningExists)
            {
                _logger.LogInformation("❌ Retry push blocked: {MorningKey} not found (no morning push sent)", morningKey);
                return false;
            }
            
            // 2. Try to claim retry push atomically
            var timestamp = DateTime.UtcNow.ToString("O");
            var success = await _redis.StringSetAsync(retryKey, timestamp, KEY_TTL, When.NotExists);
            
            if (success)
            {
                _logger.LogInformation("✅ Retry push claimed: {Key} (morning push confirmed)", retryKey);
                return true;
            }
            else
            {
                // Get existing timestamp for logging
                var existingValue = await _redis.StringGetAsync(retryKey);
                _logger.LogInformation("❌ Retry push blocked: {Key} already exists (sent at: {ExistingTime})", 
                    retryKey, existingValue.HasValue ? existingValue.ToString() : "unknown");
                return false;
            }
        }
        catch (RedisException ex)
        {
            _logger.LogWarning("Redis error in TryClaimRetryPushAsync, degrading to allow push: {Error}", ex.Message);
            return true; // Graceful degradation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in TryClaimRetryPushAsync for keys: {MorningKey}, {RetryKey}", 
                morningKey, retryKey);
            return true; // Graceful degradation
        }
    }
    
    public async Task<PushDeduplicationStatus> GetStatusAsync(string deviceId, DateOnly date, string timeZoneId)
    {
        var morningKey = BuildMorningKey(deviceId, date, timeZoneId);
        var retryKey = BuildRetryKey(deviceId, date, timeZoneId);
        
        try
        {
            // Use batch to get both values efficiently
            var batch = _redis.CreateBatch();
            var morningTask = batch.StringGetAsync(morningKey);
            var retryTask = batch.StringGetAsync(retryKey);
            batch.Execute();
            
            var morningValue = await morningTask;
            var retryValue = await retryTask;
            
            return new PushDeduplicationStatus
            {
                MorningSent = morningValue.HasValue,
                RetrySent = retryValue.HasValue,
                MorningKey = morningKey,
                RetryKey = retryKey,
                MorningSentTime = morningValue.HasValue && DateTime.TryParse(morningValue, out var dt1) ? dt1 : null,
                RetrySentTime = retryValue.HasValue && DateTime.TryParse(retryValue, out var dt2) ? dt2 : null
            };
        }
        catch (RedisException ex)
        {
            _logger.LogWarning("Redis error in GetStatusAsync: {Error}", ex.Message);
            return new PushDeduplicationStatus 
            { 
                MorningKey = morningKey, 
                RetryKey = retryKey 
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in GetStatusAsync for device: {DeviceId}", deviceId);
            return new PushDeduplicationStatus 
            { 
                MorningKey = morningKey, 
                RetryKey = retryKey 
            };
        }
    }
    
    public async Task ResetDevicePushStatusAsync(string deviceId, DateOnly date, string timeZoneId)
    {
        var morningKey = BuildMorningKey(deviceId, date, timeZoneId);
        var retryKey = BuildRetryKey(deviceId, date, timeZoneId);
        
        try
        {
            // Use batch to delete both keys efficiently
            var batch = _redis.CreateBatch();
            var morningDeleteTask = batch.KeyDeleteAsync(morningKey);
            var retryDeleteTask = batch.KeyDeleteAsync(retryKey);
            batch.Execute();
            
            var morningDeleted = await morningDeleteTask;
            var retryDeleted = await retryDeleteTask;
            
            _logger.LogInformation("🔄 Reset push status for device {DeviceId} on {Date} in {TimeZone}: " +
                "morning={MorningDeleted}, retry={RetryDeleted}", 
                deviceId, date, timeZoneId, morningDeleted, retryDeleted);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning("Redis error in ResetDevicePushStatusAsync: {Error}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in ResetDevicePushStatusAsync for device: {DeviceId}", deviceId);
        }
    }
    
    public async Task ReleasePushClaimAsync(string deviceId, DateOnly date, string timeZoneId, bool isRetryPush)
    {
        var key = isRetryPush ? BuildRetryKey(deviceId, date, timeZoneId) : BuildMorningKey(deviceId, date, timeZoneId);
        var pushType = isRetryPush ? "retry" : "morning";
        
        try
        {
            var deleted = await _redis.KeyDeleteAsync(key);
            
            if (deleted)
            {
                _logger.LogInformation("🔄 Released {PushType} push claim: {Key} (push failed, allowing retry)", 
                    pushType, key);
            }
            else
            {
                _logger.LogWarning("⚠️ Failed to release {PushType} push claim: {Key} (key not found)", 
                    pushType, key);
            }
        }
        catch (RedisException ex)
        {
            _logger.LogWarning("Redis error in ReleasePushClaimAsync for {PushType}: {Error}", pushType, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in ReleasePushClaimAsync for {PushType} push: {DeviceId}", 
                pushType, deviceId);
        }
    }
    
    /// <summary>
    /// Build Redis key for morning push
    /// Format: "godgpt:push:morning:{deviceId}:{yyyy-MM-dd}" (Global device deduplication)
    /// </summary>
    private static string BuildMorningKey(string deviceId, DateOnly date, string timeZoneId)
    {
        var baseKey = $"{MORNING_KEY_PREFIX}:{deviceId}:{date:yyyy-MM-dd}";
        return string.IsNullOrEmpty(TESTING_SUFFIX) ? baseKey : $"{baseKey}:{TESTING_SUFFIX}";
    }
    
    /// <summary>
    /// Build Redis key for retry push
    /// Format: "godgpt:push:retry:{deviceId}:{yyyy-MM-dd}" (Global device deduplication)
    /// </summary>
    private static string BuildRetryKey(string deviceId, DateOnly date, string timeZoneId)
    {
        var baseKey = $"{RETRY_KEY_PREFIX}:{deviceId}:{date:yyyy-MM-dd}";
        return string.IsNullOrEmpty(TESTING_SUFFIX) ? baseKey : $"{baseKey}:{TESTING_SUFFIX}";
    }
    
}
