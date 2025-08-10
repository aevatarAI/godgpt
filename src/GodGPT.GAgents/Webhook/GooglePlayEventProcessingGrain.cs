using Aevatar.Application.Grains.Common.Dtos;
using Newtonsoft.Json;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;
using System;
using System.Text;
using System.Threading.Tasks;
using Aevatar.Application.Grains.ChatManager.Dtos;

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

            var rtdnDto = JsonConvert.DeserializeObject<GooglePlayRTDNDto>(json);
            if (rtdnDto?.Message?.Data == null)
            {
                _logger.LogWarning("[GooglePlayEventProcessingGrain][ParseEventAndGetUserIdAsync] Invalid RTDN format - missing message data.");
                return (Guid.Empty, string.Empty, string.Empty);
            }

            var decodedData = Encoding.UTF8.GetString(Convert.FromBase64String(rtdnDto.Message.Data));
            var developerNotification = JsonConvert.DeserializeObject<DeveloperNotification>(decodedData);
            
            if (developerNotification?.SubscriptionNotification == null)
            {
                _logger.LogWarning("[GooglePlayEventProcessingGrain][ParseEventAndGetUserIdAsync] Invalid notification format or no subscription notification");
                return (Guid.Empty, string.Empty, string.Empty);
            }

            var subscriptionNotification = developerNotification.SubscriptionNotification;
            var notificationType = GetNotificationTypeName(subscriptionNotification.NotificationType);
            var purchaseToken = subscriptionNotification.PurchaseToken;

            _logger.LogDebug("[GooglePlayEventProcessingGrain][ParseEventAndGetUserIdAsync] NotificationType={0}, PurchaseToken={1}",
                notificationType, purchaseToken?.Substring(0, Math.Min(10, purchaseToken?.Length ?? 0)) + "***");

            if (!string.IsNullOrWhiteSpace(_options.PackageName) &&
                !string.Equals(developerNotification.PackageName, _options.PackageName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[GooglePlayEventProcessingGrain][ParseEventAndGetUserIdAsync] Package name mismatch. Expected: {Expected}, Actual: {Actual}",
                    _options.PackageName, developerNotification.PackageName);
                return (Guid.Empty, notificationType, purchaseToken);
            }

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

    private async Task<Guid> MapPurchaseTokenToUserIdAsync(string purchaseToken)
    {
        try
        {
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
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Guid> InMemoryMap = new();

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
        var key = this.GetPrimaryKeyString();
        if (!string.IsNullOrEmpty(key))
        {
            InMemoryMap[key] = userId;
        }
        
        _logger.LogInformation("[UserPurchaseTokenMappingGrain][SetUserIdAsync] Set mapping for purchase token: {Token} -> {UserId}",
            this.GetPrimaryKeyString()?.Substring(0, Math.Min(10, this.GetPrimaryKeyString().Length)) + "***", userId);
    }

    public Task<Guid> GetUserIdAsync()
    {
        var key = this.GetPrimaryKeyString();
        _logger.LogDebug("[UserPurchaseTokenMappingGrain][GetUserIdAsync] Getting user ID for purchase token: {Token}",
            key?.Substring(0, Math.Min(10, key?.Length ?? 0)) + "***");
        
        if (State.UserId != Guid.Empty)
        {
            return Task.FromResult(State.UserId);
        }
        
        if (!string.IsNullOrEmpty(key) && InMemoryMap.TryGetValue(key, out var cachedUserId))
        {
            return Task.FromResult(cachedUserId);
        }
        
        return Task.FromResult(Guid.Empty);
    }
}
