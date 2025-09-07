using System;
using System.Threading.Tasks;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Push deduplication service using Redis for real-time atomic operations
/// </summary>
public interface IPushDeduplicationService
{
    /// <summary>
    /// Check if morning push can be sent and mark as sent atomically
    /// Uses Redis SETNX for atomic operation to prevent duplicate morning pushes
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    /// <param name="date">Push date</param>
    /// <param name="timeZoneId">Target timezone</param>
    /// <returns>True if push can be sent (first time), false if already sent</returns>
    Task<bool> TryClaimMorningPushAsync(string deviceId, DateOnly date, string timeZoneId);
    
    /// <summary>
    /// Check if retry push can be sent and mark as sent atomically
    /// Requires morning push to be already sent, prevents duplicate retry pushes
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    /// <param name="date">Push date</param>
    /// <param name="timeZoneId">Target timezone</param>
    /// <returns>True if retry push can be sent, false if conditions not met</returns>
    Task<bool> TryClaimRetryPushAsync(string deviceId, DateOnly date, string timeZoneId);
    
    /// <summary>
    /// Get current push status for debugging and monitoring
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    /// <param name="date">Push date</param>
    /// <param name="timeZoneId">Target timezone</param>
    /// <returns>Current push deduplication status</returns>
    Task<PushDeduplicationStatus> GetStatusAsync(string deviceId, DateOnly date, string timeZoneId);
    
    /// <summary>
    /// Manual reset push status for testing/debugging purposes
    /// Removes both morning and retry push markers
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    /// <param name="date">Push date</param>
    /// <param name="timeZoneId">Target timezone</param>
    Task ResetDevicePushStatusAsync(string deviceId, DateOnly date, string timeZoneId);
}

/// <summary>
/// Push deduplication status for debugging and monitoring
/// </summary>
public class PushDeduplicationStatus
{
    /// <summary>
    /// Whether morning push has been sent
    /// </summary>
    public bool MorningSent { get; set; }
    
    /// <summary>
    /// Whether retry push has been sent
    /// </summary>
    public bool RetrySent { get; set; }
    
    /// <summary>
    /// Redis key for morning push
    /// </summary>
    public string MorningKey { get; set; } = "";
    
    /// <summary>
    /// Redis key for retry push
    /// </summary>
    public string RetryKey { get; set; } = "";
    
    /// <summary>
    /// When morning push was sent (from Redis value)
    /// </summary>
    public DateTime? MorningSentTime { get; set; }
    
    /// <summary>
    /// When retry push was sent (from Redis value)
    /// </summary>
    public DateTime? RetrySentTime { get; set; }
}
