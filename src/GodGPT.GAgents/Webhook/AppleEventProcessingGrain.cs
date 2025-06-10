using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.ChatManager.UserBilling.Payment;
using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;
using System.Text.Json;

namespace Aevatar.Application.Grains.Webhook;

public interface IAppleEventProcessingGrain : IGrainWithStringKey
{
    Task<string> ParseEventAndGetUserIdAsync([Immutable] string json);
}

[StatelessWorker]
[Reentrant]
public class AppleEventProcessingGrain : Grain, IAppleEventProcessingGrain
{
    private readonly ILogger<AppleEventProcessingGrain> _logger;
    private readonly IOptionsMonitor<ApplePayOptions> _appleOptions;

    public AppleEventProcessingGrain(
        ILogger<AppleEventProcessingGrain> logger,
        IOptionsMonitor<ApplePayOptions> appleOptions)
    {
        _logger = logger;
        _appleOptions = appleOptions;
    }

    [ReadOnly]
    public async Task<string> ParseEventAndGetUserIdAsync([Immutable] string json)
    {
        try
        {
            _logger.LogInformation("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Processing notification");
            
            // 1. Parse notification data
            var notification = JsonSerializer.Deserialize<AppStoreServerNotification>(json);
            if (notification == null || notification.UnifiedReceipt == null)
            {
                _logger.LogWarning("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Invalid notification format");
                return string.Empty;
            }
            
            // 2. Extract the original transaction ID
            var originalTransactionId = GetOriginalTransactionIdFromNotification(notification);
            if (string.IsNullOrEmpty(originalTransactionId))
            {
                _logger.LogWarning("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Could not extract original transaction ID");
                return string.Empty;
            }
            
            _logger.LogDebug("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Found original transaction ID: {Id}", 
                originalTransactionId);
            
            // 3. Generate UserPaymentGrain ID from OriginalTransactionId
            var paymentGrainId = CommonHelper.GetAppleUserPaymentGrainId(originalTransactionId);
            _logger.LogDebug("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Generated payment grain ID: {Id} for transaction: {TransactionId}", 
                paymentGrainId, originalTransactionId);
            
            // 4. Query UserPaymentGrain's State to get UserId
            var userPaymentGrain = GrainFactory.GetGrain<IUserPaymentGrain>(paymentGrainId);
            var paymentDetailsDto = await userPaymentGrain.GetPaymentDetailsAsync();
            
            if (paymentDetailsDto != null && paymentDetailsDto.UserId != Guid.Empty)
            {
                var userId = paymentDetailsDto.UserId.ToString();
                _logger.LogInformation("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Found user ID: {UserId} for transaction: {TransactionId}", 
                    userId, originalTransactionId);
                return userId;
            }
            
            _logger.LogWarning("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] No user found for original transaction ID: {Id}", 
                originalTransactionId);
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Error processing notification: {Message}", 
                ex.Message);
            return string.Empty;
        }
    }
    
    private string GetOriginalTransactionIdFromNotification(AppStoreServerNotification notification)
    {
        // Try to get from latest receipt info
        if (notification.UnifiedReceipt.LatestReceiptInfo != null && notification.UnifiedReceipt.LatestReceiptInfo.Count > 0)
        {
            var latestInfo = notification.UnifiedReceipt.LatestReceiptInfo
                .OrderByDescending(r => long.TryParse(r.ExpiresDateMs, out var expiresMs) ? expiresMs : 0)
                .FirstOrDefault();
                
            if (latestInfo != null && !string.IsNullOrEmpty(latestInfo.OriginalTransactionId))
            {
                return latestInfo.OriginalTransactionId;
            }
        }
        
        // Try to get from pending renewal info
        if (notification.UnifiedReceipt.PendingRenewalInfo != null && notification.UnifiedReceipt.PendingRenewalInfo.Count > 0)
        {
            var pendingInfo = notification.UnifiedReceipt.PendingRenewalInfo.FirstOrDefault();
            if (pendingInfo != null && !string.IsNullOrEmpty(pendingInfo.OriginalTransactionId))
            {
                return pendingInfo.OriginalTransactionId;
            }
        }
        
        return string.Empty;
    }
} 