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

namespace Aevatar.Application.Grains.Webhook;

public interface IAppleEventProcessingGrain : IGrainWithStringKey
{
    Task<Tuple<Guid, string, string>> ParseEventAndGetUserIdAsync([Immutable] string json);
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
    public async Task<Tuple<Guid, string, string>> ParseEventAndGetUserIdAsync([Immutable] string json)
    {
        try
        {
            _logger.LogDebug("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Processing notification");
            if (json.IsNullOrWhiteSpace())
            {
                _logger.LogWarning("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] JSON payload is null or empty");
                return new Tuple<Guid, string, string>(Guid.Empty, string.Empty, string.Empty);
            }

            // 1. Try to parse V2 format notification
            var notificationV2 = JsonConvert.DeserializeObject<AppStoreServerNotificationV2>(json);
            if (notificationV2 == null || notificationV2.SignedPayload.IsNullOrWhiteSpace())
            {
                _logger.LogWarning("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Invalid V2 notification format or empty SignedPayload");
                return new Tuple<Guid, string, string>(Guid.Empty, string.Empty, string.Empty);
            }

            // 2. Decode SignedPayload
            ResponseBodyV2DecodedPayload decodedPayload = null;
            try
            {
                decodedPayload = AppStoreHelper.DecodeV2Payload(notificationV2.SignedPayload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Error decoding payload: {Error}",
                    ex.Message);
            }

            if (decodedPayload == null || decodedPayload.Data == null)
            {
                _logger.LogWarning(
                    "[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Failed to decode V2 payload");
                return new Tuple<Guid, string, string>(Guid.Empty, string.Empty, string.Empty);
            }

            // Extract notificationType and subType for return values
            var notificationType = decodedPayload.NotificationType ?? string.Empty;
            var subType = decodedPayload.Subtype ?? string.Empty;

            _logger.LogDebug("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] type={0}, subType={1}",
                notificationType, subType);

            if (decodedPayload.Data.SignedTransactionInfo.IsNullOrWhiteSpace())
            {
                _logger.LogWarning("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] SignedTransactionInfo is null or empty");
                return new Tuple<Guid, string, string>(Guid.Empty, notificationType, subType);
            }
            
            // Extract from SignedTransactionInfo
            AppStoreJWSTransactionDecodedPayload transactionPayload = null;
            try
            {
                transactionPayload =
                    AppStoreHelper.DecodeJwtPayload<AppStoreJWSTransactionDecodedPayload>(decodedPayload.Data
                        .SignedTransactionInfo);
            }
            catch (Exception e)
            {
                _logger.LogError(e,
                    "[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Error decoding SignedTransactionInfo payload");
            }

            if (transactionPayload != null && !transactionPayload.AppAccountToken.IsNullOrWhiteSpace() && Guid.TryParse(transactionPayload.AppAccountToken, out var userId))
            {
                return new Tuple<Guid, string, string>(userId, notificationType, subType);
            } 
            
            _logger.LogDebug("[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] AppAccountToken not found or invalid, trying transaction ID lookup");
            
            // 3. Extract transaction information
            string originalTransactionIdV2 = null;
            if (transactionPayload != null && !transactionPayload.OriginalTransactionId.IsNullOrWhiteSpace())
            {
                originalTransactionIdV2 = transactionPayload.OriginalTransactionId;
            }

            // If not found, try to extract from SignedRenewalInfo
            if (string.IsNullOrEmpty(originalTransactionIdV2) && !string.IsNullOrEmpty(decodedPayload.Data.SignedRenewalInfo))
            {
                JWSRenewalInfoDecodedPayload renewalPayload = null;
                try
                {
                    renewalPayload =
                        AppStoreHelper.DecodeJwtPayload<JWSRenewalInfoDecodedPayload>(decodedPayload.Data
                            .SignedRenewalInfo);
                }
                catch (Exception e)
                {
                    _logger.LogError(e,
                        "[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Error decoding SignedRenewalInfo payload");
                }

                if (renewalPayload != null && !string.IsNullOrEmpty(renewalPayload.OriginalTransactionId))
                {
                    originalTransactionIdV2 = renewalPayload.OriginalTransactionId;
                }
            }

            if (string.IsNullOrEmpty(originalTransactionIdV2))
            {
                _logger.LogWarning(
                    "[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Could not extract original transaction ID from V2 notification");
                return new Tuple<Guid, string, string>(Guid.Empty, notificationType, subType);
            }

            _logger.LogDebug(
                "[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Found original transaction ID: {Id}", originalTransactionIdV2);

            // 5. Query UserPaymentGrain to get UserId
            var paymentGrainIdV2 = CommonHelper.GetAppleUserPaymentGrainId(originalTransactionIdV2);
            var userPaymentGrainV2 = GrainFactory.GetGrain<IUserPaymentGrain>(paymentGrainIdV2);
            var paymentDetailsDtoV2 = await userPaymentGrainV2.GetPaymentDetailsAsync();

            if (paymentDetailsDtoV2 != null && paymentDetailsDtoV2.UserId != Guid.Empty)
            {
                _logger.LogInformation(
                    "[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Found user ID: {UserId} for transaction: {TransactionId}",
                    paymentDetailsDtoV2.UserId.ToString(), originalTransactionIdV2);
                return new Tuple<Guid, string, string>(paymentDetailsDtoV2.UserId, notificationType, subType);
            }

            _logger.LogWarning(
                "[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] No user found for original transaction ID: {Id}",
                originalTransactionIdV2);
            return new Tuple<Guid, string, string>(Guid.Empty, notificationType, subType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AppleEventProcessingGrain][ParseEventAndGetUserIdAsync] Error processing notification: {Message}",
                ex.Message);
            return default;
        }
    }
}