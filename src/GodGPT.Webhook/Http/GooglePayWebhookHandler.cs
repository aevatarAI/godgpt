using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.UserBilling;
using Aevatar.Application.Grains.Webhook;
using Aevatar.Application.Grains.Common.Security;
using Aevatar.Webhook.SDK.Handler;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace GodGPT.Webhook.Http;

public class GooglePayWebhookHandler : IWebhookHandler
{
    private readonly ILogger<GooglePayWebhookHandler> _logger;
    private readonly IClusterClient _clusterClient;
    private readonly GooglePaySecurityValidator _securityValidator;

    public GooglePayWebhookHandler(
        IClusterClient clusterClient,
        ILogger<GooglePayWebhookHandler> logger,
        GooglePaySecurityValidator securityValidator)
    {
        _clusterClient = clusterClient;
        _logger = logger;
        _securityValidator = securityValidator;
    }

    public string RelativePath => "api/webhooks/godgpt-googleplay-payment";
    public string HttpMethod => "POST";

    public async Task<object> HandleAsync(HttpRequest request)
    {
        try
        {
            // 1. Security validation - verify request headers
            var userAgent = request.Headers["User-Agent"].ToString();
            var contentType = request.Headers["Content-Type"].ToString();
            var authorizationHeader = request.Headers["Authorization"].ToString();
            
            // Mask authorization header for logging
            var maskedAuth = string.IsNullOrEmpty(authorizationHeader) ? "None" : 
                authorizationHeader.Length > 4 ? authorizationHeader.Substring(0, 4) + "***" : "***";
            
            _logger.LogDebug("[GooglePayWebhookHandler][RevenueCat] Received request: Method={method}, Path={path}", 
                request.Method, request.Path);
            _logger.LogDebug("[GooglePayWebhookHandler][RevenueCat] Request headers - UserAgent: {UserAgent}, ContentType: {ContentType}, Auth: {Auth}", 
                userAgent, contentType, maskedAuth);

            // Validate request headers - only accept RevenueCat
            if (!_securityValidator.ValidateRequestHeaders(userAgent, contentType, authorizationHeader))
            {
                _logger.LogWarning("[GooglePayWebhookHandler][RevenueCat] Request failed header validation");
                return new { success = false, message = "Invalid request headers" };
            }

            // 2. Read RevenueCat webhook payload
            var json = await new StreamReader(request.Body).ReadToEndAsync();
            _logger.LogInformation("[GooglePayWebhookHandler][RevenueCat] Received webhook body: {body}", json);
            
            // 3. Process RevenueCat webhook
            return await HandleRevenueCatWebhookAsync(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GooglePayWebhookHandler][RevenueCat] Error processing webhook request");
            return new { success = false, error = "Internal server error" };
        }
    }
    
    /// <summary>
    /// Handle RevenueCat webhook events
    /// </summary>
    private async Task<object> HandleRevenueCatWebhookAsync(string json)
    {
        try
        {
            _logger.LogInformation("[GooglePayWebhookHandler][RevenueCat] Processing RevenueCat webhook");
            
            // Parse RevenueCat webhook event
            var webhookEvent = JsonConvert.DeserializeObject<RevenueCatWebhookEvent>(json);
            if (webhookEvent?.Event == null)
            {
                _logger.LogWarning("[GooglePayWebhookHandler][RevenueCat] Invalid webhook event format");
                return new { success = false, message = "Invalid webhook event format" };
            }

            var eventData = webhookEvent.Event;
            _logger.LogInformation("[GooglePayWebhookHandler][RevenueCat] Event: {eventType}, UserId: {userId}, ProductId: {productId}, TransactionId: {transactionId}",
                eventData.Type, eventData.AppUserId, eventData.ProductId, eventData.TransactionId);

            // Filter by event type (only process key business events)
            if (!IsKeyRevenueCatBusinessEvent(eventData.Type))
            {
                _logger.LogInformation("[GooglePayWebhookHandler][RevenueCat] Filtering event type: {eventType}", eventData.Type);
                return new { success = true, message = "Event received but filtered by type" };
            }

            // Extract user ID from app_user_id or original_app_user_id
            if (!TryExtractUserIdFromRevenueCat(eventData, out var userId))
            {
                _logger.LogWarning("[GooglePayWebhookHandler][RevenueCat] Could not extract user ID from event");
                return new { success = true, message = "Event received but no associated user found" };
            }

            // Process the event through UserBillingGAgent
            var userBillingGAgent = _clusterClient.GetGrain<IUserBillingGAgent>(userId);
            
            // Create a synthetic notification in Google Play format for backward compatibility
            var syntheticNotification = CreateSyntheticGooglePlayNotification(eventData);
            var result = await userBillingGAgent.HandleGooglePlayNotificationAsync(userId.ToString(), syntheticNotification);
            
            if (!result)
            {
                _logger.LogWarning("[GooglePayWebhookHandler][RevenueCat] Failed to process notification for user {UserId}", userId);
                return new { success = false, message = "Failed to process notification" };
            }
            
            _logger.LogInformation("[GooglePayWebhookHandler][RevenueCat] Successfully processed notification for user {UserId}", userId);
            return new { success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GooglePayWebhookHandler][RevenueCat] Error processing RevenueCat webhook");
            return new { success = false, error = "Internal server error" };
        }
    }
    
    /// <summary>
    /// Determine if the RevenueCat event type represents a key business event
    /// </summary>
    private bool IsKeyRevenueCatBusinessEvent(string eventType)
    {
        return eventType switch
        {
            RevenueCatWebhookEventTypes.INITIAL_PURCHASE => true,      // Initial purchase
            RevenueCatWebhookEventTypes.RENEWAL => true,              // Subscription renewal
            RevenueCatWebhookEventTypes.CANCELLATION => true,         // Subscription cancellation
            RevenueCatWebhookEventTypes.UNCANCELLATION => true,       // Subscription reactivation
            RevenueCatWebhookEventTypes.EXPIRATION => true,           // Subscription expiration
            RevenueCatWebhookEventTypes.BILLING_ISSUE => true,        // Payment issues
            RevenueCatWebhookEventTypes.PRODUCT_CHANGE => true,       // Product upgrade/downgrade
            _ => false // Other types temporarily filtered
        };
    }
    
    /// <summary>
    /// Try to extract user ID from RevenueCat event data
    /// </summary>
    private bool TryExtractUserIdFromRevenueCat(RevenueCatEvent eventData, out Guid userId)
    {
        userId = default;
        
        // Try app_user_id first
        if (!string.IsNullOrEmpty(eventData.AppUserId) && Guid.TryParse(eventData.AppUserId, out userId))
        {
            return true;
        }
        
        // Try original_app_user_id as fallback
        if (!string.IsNullOrEmpty(eventData.OriginalAppUserId) && Guid.TryParse(eventData.OriginalAppUserId, out userId))
        {
            return true;
        }
        
        // Try aliases
        foreach (var alias in eventData.Aliases ?? new List<string>())
        {
            if (!string.IsNullOrEmpty(alias) && Guid.TryParse(alias, out userId))
            {
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Create a synthetic Google Play notification for backward compatibility
    /// </summary>
    private string CreateSyntheticGooglePlayNotification(RevenueCatEvent eventData)
    {
        var notificationType = MapRevenueCatEventToGooglePlayNotification(eventData.Type);
        
        var syntheticNotification = new
        {
            message = new
            {
                data = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new
                {
                    version = "1.0",
                    packageName = "com.aevatar.godgpt", // Use your actual package name
                    eventTimeMillis = eventData.EventTimestampMs,
                    subscriptionNotification = new
                    {
                        version = "1.0",
                        notificationType = GetGooglePlayNotificationTypeCode(notificationType),
                        purchaseToken = eventData.TransactionId ?? eventData.OriginalTransactionId,
                        subscriptionId = eventData.ProductId
                    }
                })))
            }
        };
        
        return JsonConvert.SerializeObject(syntheticNotification);
    }
    
    /// <summary>
    /// Map RevenueCat event types to Google Play notification types
    /// </summary>
    private string MapRevenueCatEventToGooglePlayNotification(string revenueCatEventType)
    {
        return revenueCatEventType switch
        {
            RevenueCatWebhookEventTypes.INITIAL_PURCHASE => "SUBSCRIPTION_PURCHASED",
            RevenueCatWebhookEventTypes.RENEWAL => "SUBSCRIPTION_RENEWED",
            RevenueCatWebhookEventTypes.CANCELLATION => "SUBSCRIPTION_CANCELED",
            RevenueCatWebhookEventTypes.UNCANCELLATION => "SUBSCRIPTION_RECOVERED",
            RevenueCatWebhookEventTypes.EXPIRATION => "SUBSCRIPTION_EXPIRED",
            RevenueCatWebhookEventTypes.BILLING_ISSUE => "SUBSCRIPTION_ON_HOLD",
            RevenueCatWebhookEventTypes.PRODUCT_CHANGE => "SUBSCRIPTION_PURCHASED", // Treat as new purchase
            _ => "SUBSCRIPTION_PURCHASED" // Default to purchase
        };
    }
    
    /// <summary>
    /// Get Google Play notification type code for the given notification type
    /// </summary>
    private int GetGooglePlayNotificationTypeCode(string notificationType)
    {
        return notificationType switch
        {
            "SUBSCRIPTION_RECOVERED" => 1,
            "SUBSCRIPTION_RENEWED" => 2,
            "SUBSCRIPTION_CANCELED" => 3,
            "SUBSCRIPTION_PURCHASED" => 4,
            "SUBSCRIPTION_ON_HOLD" => 5,
            "SUBSCRIPTION_IN_GRACE_PERIOD" => 6,
            "SUBSCRIPTION_RESTARTED" => 7,
            "SUBSCRIPTION_PRICE_CHANGE_CONFIRMED" => 8,
            "SUBSCRIPTION_DEFERRED" => 9,
            "SUBSCRIPTION_PAUSED" => 10,
            "SUBSCRIPTION_PAUSE_SCHEDULE_CHANGED" => 11,
            "SUBSCRIPTION_REVOKED" => 12,
            "SUBSCRIPTION_EXPIRED" => 13,
            _ => 4 // Default to SUBSCRIPTION_PURCHASED
        };
    }
}