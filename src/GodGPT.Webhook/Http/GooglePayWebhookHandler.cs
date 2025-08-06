using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.UserBilling;
using Aevatar.Application.Grains.Webhook;
using Aevatar.Webhook.SDK.Handler;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GodGPT.Webhook.Http;

public class GooglePayWebhookHandler : IWebhookHandler
{
    private readonly ILogger<GooglePayWebhookHandler> _logger;
    private readonly IClusterClient _clusterClient;
    
    private static readonly string GooglePlayEventProcessingGrainId = "GooglePlayEventProcessingGrainId_1";

    public GooglePayWebhookHandler(
        IClusterClient clusterClient,
        ILogger<GooglePayWebhookHandler> logger)
    {
        _clusterClient = clusterClient;
        _logger = logger;
    }

    public string RelativePath => "api/webhooks/godgpt-googleplay-payment";
    public string HttpMethod => "POST";

    public async Task<object> HandleAsync(HttpRequest request)
    {
        try
        {
            _logger.LogDebug(
                "[GooglePayWebhookHandler][webhook] Received request: Method={method}, Path={path}, QueryString={query}",
                request.Method, request.Path, request.QueryString);

            // 1. Read RTDN notification payload
            var json = await new StreamReader(request.Body).ReadToEndAsync();
            
            // 2. Use GooglePlayEventProcessingGrain to parse notification and get userId
            var googlePlayEventProcessingGrain = _clusterClient.GetGrain<IGooglePlayEventProcessingGrain>(GooglePlayEventProcessingGrainId);
            var (userId, notificationType, purchaseToken) = await googlePlayEventProcessingGrain.ParseEventAndGetUserIdAsync(json);
            
            _logger.LogInformation("[GooglePayWebhookHandler][webhook] userId:{0}, notificationType:{1}, purchaseToken:{2} json: {3}",
                userId, notificationType, purchaseToken?.Substring(0, Math.Min(10, purchaseToken?.Length ?? 0)) + "***", json);
                
            if (userId == default)
            {
                _logger.LogWarning("[GooglePayWebhookHandler][webhook] Could not determine user ID from notification");
                // Return 200 status to avoid Google retries
                return new { success = true, message = "Notification received but no associated user found" };
            }
            
            // 3. Filter by event type (only process key business events)
            if (!IsKeyBusinessEvent(notificationType))
            {
                _logger.LogInformation("[GooglePayWebhookHandler][webhook] Filter NotificationType {0}", notificationType);
                return new { success = true, message = "Notification received but filtered by type" };
            }
            
            // 4. Use found userId to call UserBillingGAgent to process notification
            var userBillingGAgent = _clusterClient.GetGrain<IUserBillingGAgent>(userId);
            var result = await userBillingGAgent.HandleGooglePlayNotificationAsync(userId.ToString(), json);
            
            if (!result)
            {
                _logger.LogWarning("[GooglePayWebhookHandler][Webhook] Failed to process notification for user {UserId}", userId);
                return new { success = false, message = "Failed to process notification" };
            }
            
            // Return success response
            _logger.LogInformation("[GooglePayWebhookHandler][webhook] Successfully processed notification for user {UserId}", userId);
            return new { success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GooglePayWebhookHandler][webhook] Error processing webhook request");
            // Return 200 status to avoid Google retries (can be adjusted based on business requirements)
            return new { success = false, error = "Internal server error" };
        }
    }
    
    /// <summary>
    /// Determine if the notification type represents a key business event
    /// </summary>
    private bool IsKeyBusinessEvent(string notificationType)
    {
        return notificationType switch
        {
            "SUBSCRIPTION_PURCHASED" => true,     // Subscription purchase success
            "SUBSCRIPTION_RENEWED" => true,       // Subscription renewal success
            "SUBSCRIPTION_CANCELED" => true,      // Subscription cancellation
            "SUBSCRIPTION_EXPIRED" => true,       // Subscription expiration
            "SUBSCRIPTION_REVOKED" => true,       // Refund processing (equivalent to VoidedPurchaseNotification)
            "SUBSCRIPTION_RECOVERED" => true,     // Subscription recovery from issues
            "SUBSCRIPTION_ON_HOLD" => true,       // Payment issues
            "SUBSCRIPTION_IN_GRACE_PERIOD" => true, // Grace period status
            _ => false // Other types temporarily filtered
        };
    }
}