using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.UserBilling;
using Aevatar.Application.Grains.Webhook;
using Aevatar.Application.Grains.Common;
using Aevatar.Webhook.SDK.Handler;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using System.Text.Json;

namespace Aevatar.Application.Grains.Http;

public class AppleStoreWebhookHandler : IWebhookHandler
{
    private readonly ILogger<AppleStoreWebhookHandler> _logger;
    private readonly IClusterClient _clusterClient;
    
    private static readonly string AppleNotificationProcessorGrainId = "AppleNotificationProcessorGrainId_1";

    public AppleStoreWebhookHandler(
        IClusterClient clusterClient,
        ILogger<AppleStoreWebhookHandler> logger)
    {
        _clusterClient = clusterClient;
        _logger = logger;
    }

    public string RelativePath => "api/webhooks/godgpt-appstore-payment";

    public string HttpMethod => "POST";

    public async Task<object> HandleAsync(HttpRequest request)
    {
        try
        {
            _logger.LogDebug(
                "[AppleStoreWebhookHandler][webhook] Received request: Method={method}, Path={path}, QueryString={query}",
                request.Method, request.Path, request.QueryString);

            // 1. Use AppleEventProcessingGrain to parse notification and get userId
            var json = await new StreamReader(request.Body).ReadToEndAsync();
            var appleEventProcessingGrain = _clusterClient.GetGrain<IAppleEventProcessingGrain>(AppleNotificationProcessorGrainId);
            var (userId, notificationType, subtype) = await appleEventProcessingGrain.ParseEventAndGetUserIdAsync(json);
            _logger.LogInformation("[AppleStoreWebhookHandler][webhook] userId:{0}, notificationtype:{1}, subtype:{2} json: {3}",
                userId, notificationType, subtype, json);
            if (userId == default)
            {
                _logger.LogWarning("[AppleStoreWebhookHandler][webhook] Could not determine user ID from notification");
                // Return 200 success even if userId is not found, to prevent Apple from retrying
                return new { success = true, message = "Notification received but no associated user found" };
            }
            
            //2. Filter by type
            if (notificationType != AppStoreNotificationType.SUBSCRIBED.ToString()
                && notificationType != AppStoreNotificationType.DID_RENEW.ToString()
                && !(notificationType ==  AppStoreNotificationType.DID_CHANGE_RENEWAL_STATUS.ToString() 
                     && subtype == AppStoreNotificationSubtype.AUTO_RENEW_DISABLED.ToString())
                && notificationType != AppStoreNotificationType.EXPIRED.ToString()
                && notificationType != AppStoreNotificationType.GRACE_PERIOD_EXPIRED.ToString()
                && notificationType != AppStoreNotificationType.REVOKE.ToString()
                && notificationType != AppStoreNotificationType.DID_CHANGE_RENEWAL_PREF.ToString()
                && notificationType != AppStoreNotificationType.REFUND.ToString()
               )
            {
                _logger.LogInformation("[AppleStoreWebhookHandler][webhook] Filter NotificationType {0}, SubType={1}",
                    notificationType, subtype);
                return new { success = true, message = "Notification received but filter by type" };
            }
            
            // 3. Use the found userId to request UserBillingGrain to process the notification
            var userBillingGAgent = _clusterClient.GetGrain<IUserBillingGAgent>(userId);
            var result = await userBillingGAgent.HandleAppStoreNotificationAsync(userId, json);
            
            if (!result)
            {
                _logger.LogWarning("[AppleStoreWebhookHandler][Webhook] Failed to process notification for user {UserId}", userId);
                return new { success = false, message = "Failed to process notification" };
            }
            
            // Return success response
            _logger.LogInformation("[AppleStoreWebhookHandler][webhook] Successfully processed notification for user {UserId}", userId);
            return new { success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AppleStoreWebhookHandler][webhook] Error processing webhook request");
            // Return 200 status code even on error to prevent Apple from retrying (can be adjusted based on business requirements)
            return new { success = false, error = "Internal server error" };
        }
    }
} 