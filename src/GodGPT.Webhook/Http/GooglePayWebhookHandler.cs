using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.UserBilling;
using Aevatar.Application.Grains.Webhook;
using Aevatar.Application.Grains.Common.Security;
using Aevatar.Webhook.SDK.Handler;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GodGPT.Webhook.Http;

public class GooglePayWebhookHandler : IWebhookHandler
{
    private readonly ILogger<GooglePayWebhookHandler> _logger;
    private readonly IClusterClient _clusterClient;
    private readonly GooglePaySecurityValidator _securityValidator;
    
    private static readonly string GooglePlayEventProcessingGrainId = "GooglePlayEventProcessingGrainId_1";

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
            _logger.LogDebug(
                "[GooglePayWebhookHandler][webhook] Received request: Method={method}, Path={path}, QueryString={query}",
                request.Method, request.Path, request.QueryString);

            // 1. Security validation - verify request headers
            var userAgent = request.Headers["User-Agent"].ToString();
            var contentType = request.Headers["Content-Type"].ToString();
            
            _logger.LogDebug("[GooglePayWebhookHandler][webhook] Request headers - UserAgent: {UserAgent}, ContentType: {ContentType}", 
                userAgent, contentType);

            // Validate request headers first
            if (!_securityValidator.ValidateRequestHeaders(userAgent, contentType))
            {
                _logger.LogWarning("[GooglePayWebhookHandler][webhook] Request failed header validation");
                dynamic resp = new System.Dynamic.ExpandoObject();
                resp.success = false;
                resp.message = "Invalid request headers";
                return resp;
            }

            // 2. Read RTDN notification payload
            var json = await new StreamReader(request.Body).ReadToEndAsync();
            
            // 3. CRITICAL SECURITY: Verify Pub/Sub message signature
            var signature = request.Headers["X-Goog-Signature"].FirstOrDefault();
            if (!string.IsNullOrEmpty(signature))
            {
                _logger.LogDebug("[GooglePayWebhookHandler][webhook] Verifying Pub/Sub message signature");
                
                // Verify signature using GooglePaySecurityValidator
                bool isSignatureValid = _securityValidator.VerifyPubSubSignature(json, signature);
                if (!isSignatureValid)
                {
                    _logger.LogError("[GooglePayWebhookHandler][webhook] Signature verification failed - potential security attack");
                    dynamic resp = new System.Dynamic.ExpandoObject();
                    resp.success = false;
                    resp.message = "Signature verification failed";
                    return resp;
                }
                
                _logger.LogInformation("[GooglePayWebhookHandler][webhook] Signature verification successful");
            }
            else
            {
                _logger.LogWarning("[GooglePayWebhookHandler][webhook] No X-Goog-Signature header found - potential security risk");
                // Depending on security policy, you might want to reject requests without signatures
                // For now, we'll log the warning but continue processing
            }
            
            // 2. Use GooglePlayEventProcessingGrain to parse notification and get userId
            var googlePlayEventProcessingGrain = _clusterClient.GetGrain<IGooglePlayEventProcessingGrain>(GooglePlayEventProcessingGrainId);
            var (userId, notificationType, purchaseToken) = await googlePlayEventProcessingGrain.ParseEventAndGetUserIdAsync(json);
            
            _logger.LogInformation("[GooglePayWebhookHandler][webhook] userId:{0}, notificationType:{1}, purchaseToken:{2} json: {3}",
                userId, notificationType, purchaseToken?.Substring(0, Math.Min(10, purchaseToken?.Length ?? 0)) + "***", json);
                
            if (userId == default)
            {
                _logger.LogWarning("[GooglePayWebhookHandler][webhook] Could not determine user ID from notification");
                // Return 200 status to avoid Google retries
                dynamic resp = new System.Dynamic.ExpandoObject();
                resp.success = true;
                resp.message = "Notification received but no associated user found";
                return resp;
            }
            
            // 3. Filter by event type (only process key business events)
            if (!IsKeyBusinessEvent(notificationType))
            {
                _logger.LogInformation("[GooglePayWebhookHandler][webhook] Filter NotificationType {0}", notificationType);
                dynamic resp = new System.Dynamic.ExpandoObject();
                resp.success = true;
                resp.message = "Notification received but filtered by type";
                return resp;
            }
            
            // 4. Use found userId to call UserBillingGAgent to process notification
            var userBillingGAgent = _clusterClient.GetGrain<IUserBillingGAgent>(userId);
            var result = await userBillingGAgent.HandleGooglePlayNotificationAsync(userId.ToString(), json);
            
            if (!result)
            {
                _logger.LogWarning("[GooglePayWebhookHandler][Webhook] Failed to process notification for user {UserId}", userId);
                dynamic resp = new System.Dynamic.ExpandoObject();
                resp.success = false;
                resp.message = "Failed to process notification";
                return resp;
            }
            
            // Return success response
            _logger.LogInformation("[GooglePayWebhookHandler][webhook] Successfully processed notification for user {UserId}", userId);
            dynamic successResp = new System.Dynamic.ExpandoObject();
            successResp.success = true;
            return successResp;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GooglePayWebhookHandler][webhook] Error processing webhook request");
            // Return 200 status to avoid Google retries (can be adjusted based on business requirements)
            dynamic resp = new System.Dynamic.ExpandoObject();
            resp.success = false;
            resp.error = "Internal server error";
            return resp;
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