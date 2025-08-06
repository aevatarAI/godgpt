using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace Aevatar.Application.Grains.Webhook;

public interface IGooglePlayEventProcessingGrain : IGrainWithStringKey
{
    Task<(Guid UserId, string NotificationType, string PurchaseToken)> ParseEventAndGetUserIdAsync([Immutable] string json);
}

/// <summary>
/// User purchase token mapping Grain
/// Used to establish association between purchaseToken and userId
/// </summary>
public interface IUserPurchaseTokenMappingGrain : IGrainWithStringKey
{
    Task SetUserIdAsync(Guid userId);
    Task<Guid> GetUserIdAsync();
}

[StatelessWorker]
[Reentrant]
public class GooglePlayEventProcessingGrain : Grain, IGooglePlayEventProcessingGrain
{
    private readonly ILogger<GooglePlayEventProcessingGrain> _logger;
    private readonly GooglePayOptions _options;

    public GooglePlayEventProcessingGrain(
        ILogger<GooglePlayEventProcessingGrain> logger,
        IOptionsMonitor<GooglePayOptions> googlePayOptions)
    {
        _logger = logger;
        _options = googlePayOptions.CurrentValue;
    }

    [ReadOnly]
    public async Task<(Guid UserId, string NotificationType, string PurchaseToken)> ParseEventAndGetUserIdAsync([Immutable] string json)
    {
        try
        {
            _logger.LogDebug("[GooglePlayEventProcessingGrain][ParseEventAndGetUserIdAsync] Processing RTDN notification");
            
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogWarning("[GooglePlayEventProcessingGrain][ParseEventAndGetUserIdAsync] JSON payload is null or empty");
                return (Guid.Empty, string.Empty, string.Empty);
            }

            // 1. Parse RTDN notification JSON
            var notification = JsonSerializer.Deserialize<GooglePlayRTDNNotification>(json);
            if (notification?.SubscriptionNotification == null)
            {
                _logger.LogWarning("[GooglePlayEventProcessingGrain][ParseEventAndGetUserIdAsync] Invalid notification format or no subscription notification");
                return (Guid.Empty, string.Empty, string.Empty);
            }

            var subscriptionNotification = notification.SubscriptionNotification;
            var notificationType = GetNotificationTypeName(subscriptionNotification.NotificationType);
            var purchaseToken = subscriptionNotification.PurchaseToken;

            _logger.LogDebug("[GooglePlayEventProcessingGrain][ParseEventAndGetUserIdAsync] NotificationType={0}, PurchaseToken={1}",
                notificationType, purchaseToken?.Substring(0, Math.Min(10, purchaseToken?.Length ?? 0)) + "***");

            // 2. Validate package name for security
            if (!string.Equals(notification.PackageName, _options.PackageName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[GooglePlayEventProcessingGrain][ParseEventAndGetUserIdAsync] Package name mismatch. Expected: {Expected}, Actual: {Actual}",
                    _options.PackageName, notification.PackageName);
                return (Guid.Empty, notificationType, purchaseToken);
            }

            // 3. User ID mapping strategy (solving key business risks)
            if (string.IsNullOrWhiteSpace(purchaseToken))
            {
                _logger.LogWarning("[GooglePlayEventProcessingGrain][ParseEventAndGetUserIdAsync] Purchase token is null or empty");
                return (Guid.Empty, notificationType, purchaseToken);
            }

            var userId = await MapPurchaseTokenToUserIdAsync(purchaseToken);
            
            _logger.LogInformation("[GooglePlayEventProcessingGrain][ParseEventAndGetUserIdAsync] Successfully parsed notification. UserId: {UserId}, NotificationType: {NotificationType}",
                userId, notificationType);

            return (userId, notificationType, purchaseToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GooglePlayEventProcessingGrain][ParseEventAndGetUserIdAsync] Failed to parse RTDN notification");
            return (Guid.Empty, string.Empty, string.Empty);
        }
    }

    /// <summary>
    /// Map purchaseToken to user ID
    /// Strategy: Use userId mapping table recorded when purchase was created
    /// </summary>
    private async Task<Guid> MapPurchaseTokenToUserIdAsync(string purchaseToken)
    {
        try
        {
            // Query user mapping records from when purchase was created
            // This requires storing purchaseToken to userId association in database when user makes purchase
            var userMappingGrain = GrainFactory.GetGrain<IUserPurchaseTokenMappingGrain>(purchaseToken);
            var userId = await userMappingGrain.GetUserIdAsync();
            
            if (userId == Guid.Empty)
            {
                _logger.LogWarning("[GooglePlayEventProcessingGrain][MapPurchaseTokenToUserIdAsync] No user mapping found for purchase token: {Token}",
                    purchaseToken?.Substring(0, Math.Min(10, purchaseToken.Length)) + "***");
            }
            
            return userId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GooglePlayEventProcessingGrain][MapPurchaseTokenToUserIdAsync] Error mapping purchase token to user ID");
            return Guid.Empty;
        }
    }

    /// <summary>
    /// Get notification type name from enum value
    /// </summary>
    private string GetNotificationTypeName(int notificationType)
    {
        return notificationType switch
        {
            1 => "SUBSCRIPTION_RECOVERED",
            2 => "SUBSCRIPTION_RENEWED",
            3 => "SUBSCRIPTION_CANCELED",
            4 => "SUBSCRIPTION_PURCHASED",
            5 => "SUBSCRIPTION_ON_HOLD",
            6 => "SUBSCRIPTION_IN_GRACE_PERIOD",
            7 => "SUBSCRIPTION_RESTARTED",
            8 => "SUBSCRIPTION_PRICE_CHANGE_CONFIRMED",
            9 => "SUBSCRIPTION_DEFERRED",
            10 => "SUBSCRIPTION_PAUSED",
            11 => "SUBSCRIPTION_PAUSE_SCHEDULE_CHANGED",
            12 => "SUBSCRIPTION_REVOKED",
            13 => "SUBSCRIPTION_EXPIRED",
            _ => $"UNKNOWN_{notificationType}"
        };
    }
}

/// <summary>
/// Implementation of user purchase token mapping grain
/// </summary>
[GenerateSerializer]
public class UserPurchaseTokenMappingState
{
    [Id(0)] public Guid UserId { get; set; }
    [Id(1)] public DateTime CreatedAt { get; set; }
    [Id(2)] public DateTime? LastUpdatedAt { get; set; }
}

public class UserPurchaseTokenMappingGrain : Grain<UserPurchaseTokenMappingState>, IUserPurchaseTokenMappingGrain
{
    private readonly ILogger<UserPurchaseTokenMappingGrain> _logger;

    public UserPurchaseTokenMappingGrain(ILogger<UserPurchaseTokenMappingGrain> logger)
    {
        _logger = logger;
    }

    public async Task SetUserIdAsync(Guid userId)
    {
        State.UserId = userId;
        State.CreatedAt = DateTime.UtcNow;
        State.LastUpdatedAt = DateTime.UtcNow;
        
        await WriteStateAsync();
        
        _logger.LogInformation("[UserPurchaseTokenMappingGrain][SetUserIdAsync] Set mapping for purchase token: {Token} -> {UserId}",
            this.GetPrimaryKeyString()?.Substring(0, Math.Min(10, this.GetPrimaryKeyString().Length)) + "***", userId);
    }

    public Task<Guid> GetUserIdAsync()
    {
        _logger.LogDebug("[UserPurchaseTokenMappingGrain][GetUserIdAsync] Getting user ID for purchase token: {Token}",
            this.GetPrimaryKeyString()?.Substring(0, Math.Min(10, this.GetPrimaryKeyString().Length)) + "***");
        
        return Task.FromResult(State.UserId);
    }
}