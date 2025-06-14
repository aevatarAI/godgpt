using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.ChatManager.UserBilling.Payment;
using Aevatar.Application.Grains.Common;
using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Aevatar.Application.Grains.Webhook;

public interface IAppleEventProcessingGrain : IGrainWithStringKey
{
    Task<Guid> ParseEventAndGetUserIdAsync([Immutable] string json);
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
    public async Task<Guid> ParseEventAndGetUserIdAsync([Immutable] string json)
    {
        try
        {
            _logger.LogInformation("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Processing notification");
            
            // 1. Try to parse V2 format notification
            var notificationV2 = JsonConvert.DeserializeObject<AppStoreServerNotificationV2>(json);
            if (notificationV2 != null && !string.IsNullOrEmpty(notificationV2.SignedPayload))
            {
                _logger.LogInformation("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Processing V2 format notification");
                
                // 2. Decode SignedPayload
                ResponseBodyV2DecodedPayload decodedPayload = null;
                try
                {
                    decodedPayload = AppStoreHelper.DecodeV2Payload(notificationV2.SignedPayload);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Error decoding payload: {Error}", ex.Message);
                }
                
                if (decodedPayload == null || decodedPayload.Data == null)
                {
                    _logger.LogWarning("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Failed to decode V2 payload");
                    return default;
                }
                
                _logger.LogDebug("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] type={0}, subType={1}",
                    decodedPayload.NotificationType, decodedPayload.Subtype ?? string.Empty);
                _logger.LogDebug("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] type={0}, SubType={1}, Json: {2}",
                    decodedPayload.NotificationType, decodedPayload.Subtype ?? string.Empty, json);
                //Filter by type
                if (decodedPayload.NotificationType != AppStoreNotificationType.DID_RENEW.ToString() 
                    /*&& decodedPayload.NotificationType != AppStoreNotificationType.REFUND.ToString()*/
                    && !(decodedPayload.NotificationType ==  AppStoreNotificationType.DID_CHANGE_RENEWAL_STATUS.ToString() 
                         && decodedPayload.Subtype == AppStoreNotificationSubtype.AUTO_RENEW_DISABLED.ToString())
                    && decodedPayload.NotificationType != AppStoreNotificationType.EXPIRED.ToString()
                    && decodedPayload.NotificationType != AppStoreNotificationType.REVOKE.ToString()
                   )
                {
                    _logger.LogDebug("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Filter NotificationType {0}, SubType={1}",
                        decodedPayload.NotificationType, decodedPayload.Subtype);
                    return default;
                }

                // 3. Extract transaction information
                string originalTransactionIdV2 = null;
                
                // Extract from SignedTransactionInfo
                if (!string.IsNullOrEmpty(decodedPayload.Data.SignedTransactionInfo))
                {
                    AppStoreJWSTransactionDecodedPayload transactionPayload = null;
                    try
                    {
                        transactionPayload = AppStoreHelper.DecodeJwtPayload<AppStoreJWSTransactionDecodedPayload>(decodedPayload.Data.SignedTransactionInfo);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Error decoding SignedTransactionInfo payload");
                    }
                    if (transactionPayload != null && !string.IsNullOrEmpty(transactionPayload.OriginalTransactionId))
                    {
                        originalTransactionIdV2 = transactionPayload.OriginalTransactionId;
                    }
                }
                
                // If not found, try to extract from SignedRenewalInfo
                if (string.IsNullOrEmpty(originalTransactionIdV2) && !string.IsNullOrEmpty(decodedPayload.Data.SignedRenewalInfo))
                {
                    JWSRenewalInfoDecodedPayload renewalPayload = null;
                    try
                    {
                        renewalPayload = AppStoreHelper.DecodeJwtPayload<JWSRenewalInfoDecodedPayload>(decodedPayload.Data.SignedRenewalInfo);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Error decoding SignedRenewalInfo payload");
                    }
                    if (renewalPayload != null && !string.IsNullOrEmpty(renewalPayload.OriginalTransactionId))
                    {
                        originalTransactionIdV2 = renewalPayload.OriginalTransactionId;
                    }
                }
                
                if (string.IsNullOrEmpty(originalTransactionIdV2))
                {
                    _logger.LogWarning("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Could not extract original transaction ID from V2 notification");
                    return default;
                }
                
                _logger.LogDebug("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Found original transaction ID: {Id}", 
                    originalTransactionIdV2);
                
                // 4. Generate UserPaymentGrain ID
                var paymentGrainIdV2 = CommonHelper.GetAppleUserPaymentGrainId(originalTransactionIdV2);
                _logger.LogDebug("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Generated payment grain ID: {Id} for transaction: {TransactionId}", 
                    paymentGrainIdV2, originalTransactionIdV2);
                
                // 5. Query UserPaymentGrain to get UserId
                var userPaymentGrainV2 = GrainFactory.GetGrain<IUserPaymentGrain>(paymentGrainIdV2);
                var paymentDetailsDtoV2 = await userPaymentGrainV2.GetPaymentDetailsAsync();
                
                if (paymentDetailsDtoV2 != null && paymentDetailsDtoV2.UserId != Guid.Empty)
                {
                    _logger.LogInformation("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Found user ID: {UserId} for transaction: {TransactionId}", 
                        paymentDetailsDtoV2.UserId.ToString(), originalTransactionIdV2);
                    return paymentDetailsDtoV2.UserId;
                }
                
                _logger.LogWarning("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] No user found for original transaction ID: {Id}", 
                    originalTransactionIdV2);
                return default;
            }
            
            // If not V2 format, try to parse with V1 format (preserve original logic)
            // 1. Parse notification using V1 format
            var notification = JsonSerializer.Deserialize<AppStoreServerNotification>(json);
            if (notification == null || notification.UnifiedReceipt == null)
            {
                _logger.LogWarning("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Invalid notification format");
                return default;
            }
            
            // 2. Extract the original transaction ID
            var originalTransactionId = GetOriginalTransactionIdFromNotification(notification);
            if (string.IsNullOrEmpty(originalTransactionId))
            {
                _logger.LogWarning("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Could not extract original transaction ID");
                return default;
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
                _logger.LogInformation("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Found user ID: {UserId} for transaction: {TransactionId}", 
                    paymentDetailsDto.UserId.ToString(), originalTransactionId);
                return paymentDetailsDto.UserId;
            }
            
            _logger.LogWarning("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] No user found for original transaction ID: {Id}", 
                originalTransactionId);
            return default;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Error processing notification: {Message}", 
                ex.Message);
            return default;
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