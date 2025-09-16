namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Enhanced user device information for daily push notifications (V2)
/// Designed to address data consistency and maintenance issues in V1
/// </summary>
[GenerateSerializer]
public class UserDeviceInfoV2
{
    /// <summary>
    /// Unique device identifier (permanent across token refreshes and user switches)
    /// </summary>
    [Id(0)]
    public string DeviceId { get; set; } = "";

    /// <summary>
    /// User ID that owns this device registration
    /// </summary>
    [Id(1)]
    public Guid UserId { get; set; }

    /// <summary>
    /// Current Firebase push token (changes on token refresh)
    /// </summary>
    [Id(2)]
    public string PushToken { get; set; } = "";

    /// <summary>
    /// IANA timezone ID for device-level timezone
    /// </summary>
    [Id(3)]
    public string TimeZoneId { get; set; } = "";

    /// <summary>
    /// Push language setting for this device
    /// </summary>
    [Id(4)]
    public string PushLanguage { get; set; } = "en";

    /// <summary>
    /// Device push switch enabled/disabled
    /// </summary>
    [Id(5)]
    public bool PushEnabled { get; set; } = true;

    /// <summary>
    /// When this device was first registered
    /// </summary>
    [Id(6)]
    public DateTime RegisteredAt { get; set; }

    /// <summary>
    /// Last token update timestamp (used for cleanup)
    /// </summary>
    [Id(7)]
    public DateTime LastTokenUpdate { get; set; }

    /// <summary>
    /// Last activity timestamp (used for cleanup and analytics)
    /// </summary>
    [Id(8)]
    public DateTime LastActiveAt { get; set; }

    /// <summary>
    /// Device platform information (iOS, Android, etc.)
    /// </summary>
    [Id(9)]
    public string Platform { get; set; } = "";

    /// <summary>
    /// App version when device was registered/updated
    /// </summary>
    [Id(10)]
    public string AppVersion { get; set; } = "";

    /// <summary>
    /// Data structure version for migration tracking
    /// </summary>
    [Id(11)]
    public int StructureVersion { get; set; } = 2;

    /// <summary>
    /// Status of this device registration
    /// </summary>
    [Id(12)]
    public DeviceStatus Status { get; set; } = DeviceStatus.Active;

    /// <summary>
    /// Historical push tokens for this device (for cleanup)
    /// </summary>
    [Id(13)]
    public List<HistoricalPushToken> PushTokenHistory { get; set; } = new();

    /// <summary>
    /// Last successful push timestamp
    /// </summary>
    [Id(14)]
    public DateTime? LastSuccessfulPush { get; set; }

    /// <summary>
    /// Consecutive push failure count (for token validity detection)
    /// </summary>
    [Id(15)]
    public int ConsecutiveFailures { get; set; } = 0;

    /// <summary>
    /// Additional metadata for future extensions
    /// </summary>
    [Id(16)]
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Device registration status
/// </summary>
[GenerateSerializer]
public enum DeviceStatus
{
    Active = 0,
    TokenExpired = 1,
    Disabled = 2,
    Suspended = 3,
    PendingCleanup = 4
}

/// <summary>
/// Historical push token record for cleanup tracking
/// </summary>
[GenerateSerializer]
public class HistoricalPushToken
{
    [Id(0)] public string Token { get; set; } = "";
    [Id(1)] public DateTime UsedFrom { get; set; }
    [Id(2)] public DateTime? UsedUntil { get; set; }
    [Id(3)] public string ReplacementReason { get; set; } = ""; // "refresh", "user_switch", "expired"
}
