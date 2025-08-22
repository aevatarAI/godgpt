using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.UserBilling;
using Aevatar.Application.Grains.Webhook;
using Aevatar.Application.Grains.Common.Security;
using Aevatar.Application.Grains.Common.Constants;
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
            _logger.LogInformation("[GooglePayWebhookHandler][RevenueCat] Event: {eventType}, UserId: {userId}, ProductId: {productId}, TransactionId: {transactionId}, OriginalTransactionId: {originalTransactionId}, Price: {price}",
                eventData.Type, eventData.AppUserId, eventData.ProductId, eventData.TransactionId, eventData.OriginalTransactionId, eventData.PriceInPurchasedCurrency);

            // Filter by event type and payment amount (only process paid events)
            if (!IsKeyRevenueCatBusinessEvent(eventData.Type, eventData.PriceInPurchasedCurrency))
            {
                _logger.LogInformation("[GooglePayWebhookHandler][RevenueCat] Filtering event: Type={eventType}, Price={price}", 
                    eventData.Type, eventData.PriceInPurchasedCurrency);
                return new { success = true, message = "Event received but filtered by type or amount" };
            }

            // Extract user ID from app_user_id or original_app_user_id
            if (!TryExtractUserIdFromRevenueCat(eventData, out var userId))
            {
                _logger.LogWarning("[GooglePayWebhookHandler][RevenueCat] Could not extract user ID from event");
                return new { success = true, message = "Event received but no associated user found" };
            }

            // Process the event through UserBillingGAgent
            var userBillingGAgent = _clusterClient.GetGrain<IUserBillingGAgent>(userId);
            
            // Create RevenueCat verification result from webhook data
            var verificationResult = CreateRevenueCatVerificationResult(eventData);
            var result = await userBillingGAgent.ProcessRevenueCatWebhookEventAsync(userId, eventData.Type, verificationResult);
            
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
    /// Determine if the RevenueCat event should be processed
    /// Process INITIAL_PURCHASE, RENEWAL events with payment amount > 0
    /// Process CANCELLATION events (including refunds with negative amounts)
    /// Process EXPIRATION events (subscription expiration, typically price = 0)
    /// </summary>
    private bool IsKeyRevenueCatBusinessEvent(string eventType, double? priceInPurchasedCurrency)
    {
        // Process core payment and cancellation events
        bool isCoreBusinessEvent = eventType switch
        {
            RevenueCatWebhookEventTypes.INITIAL_PURCHASE => true,      // Initial purchase
            RevenueCatWebhookEventTypes.RENEWAL => true,              // Subscription renewal
            RevenueCatWebhookEventTypes.CANCELLATION => true,         // Cancellation/refund events
            RevenueCatWebhookEventTypes.EXPIRATION => true,           // Subscription expiration events
            _ => false
        };
        
        if (!isCoreBusinessEvent)
        {
            return false;
        }
        
        // For CANCELLATION and EXPIRATION events, allow any price including 0 or negative
        if (eventType == RevenueCatWebhookEventTypes.CANCELLATION || eventType == RevenueCatWebhookEventTypes.EXPIRATION)
        {
            // CANCELLATION events should be processed regardless of price (negative prices = refunds)
            // EXPIRATION events typically have price = 0 and should always be processed
            _logger.LogInformation("[GooglePayWebhookHandler][IsKeyRevenueCatBusinessEvent] Processing {EventType} event with price {Price}", 
                eventType, priceInPurchasedCurrency);
            return true;
        }
        
        // For payment events (INITIAL_PURCHASE, RENEWAL), must have valid payment amount > 0
        bool hasValidPayment = priceInPurchasedCurrency.HasValue && priceInPurchasedCurrency.Value > 0;
        
        if (!hasValidPayment)
        {
            _logger.LogInformation("[GooglePayWebhookHandler][IsKeyRevenueCatBusinessEvent] Filtering event {EventType} with price {Price} - requires payment > 0", 
                eventType, priceInPurchasedCurrency);
        }
        
        return hasValidPayment;
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
    /// Create PaymentVerificationResultDto from RevenueCat webhook event data
    /// </summary>
    private PaymentVerificationResultDto CreateRevenueCatVerificationResult(RevenueCatEvent eventData)
    {
        var purchaseDate = eventData.PurchasedAtMs.HasValue ? 
            DateTimeOffset.FromUnixTimeMilliseconds(eventData.PurchasedAtMs.Value).DateTime : DateTime.UtcNow;
        var expirationDate = eventData.ExpirationAtMs.HasValue ?
            DateTimeOffset.FromUnixTimeMilliseconds(eventData.ExpirationAtMs.Value).DateTime : (DateTime?)null;

        // Log ProductId format and payment amount for verification  
        _logger.LogInformation("[GooglePayWebhookHandler][CreateRevenueCatVerificationResult] Using ProductId: {ProductId}, TransactionId: {TransactionId}, OriginalTransactionId: {OriginalTransactionId}, Price: {Price} {Currency}, CancelReason: {CancelReason}", 
            eventData.ProductId, eventData.TransactionId, eventData.OriginalTransactionId, eventData.PriceInPurchasedCurrency, eventData.Currency, eventData.CancelReason);

        // Determine payment state based on event context
        int paymentState = 1; // Default: Purchased state
        bool autoRenewing = true;
        
        // Adjust state based on event type and cancel reason
        if (!string.IsNullOrEmpty(eventData.CancelReason))
        {
            autoRenewing = false;
            // For cancellation/refund events, payment state can remain as purchased since we're tracking the cancellation separately
        }
        
        // Set AutoRenewing based on period type and cancel reason
        if (eventData.PeriodType != "NORMAL" || !string.IsNullOrEmpty(eventData.CancelReason))
        {
            autoRenewing = false;
        }

        return new PaymentVerificationResultDto
        {
            IsValid = true,
            TransactionId = eventData.TransactionId ?? eventData.OriginalTransactionId, // Current transaction ID for invoice details
            ProductId = eventData.ProductId, // This is already in key1:key2 format from RevenueCat
            SubscriptionStartDate = purchaseDate,
            SubscriptionEndDate = expirationDate,
            Platform = PaymentPlatform.GooglePlay,
            PurchaseToken = eventData.OriginalTransactionId ?? eventData.TransactionId, // Keep for backward compatibility
            OriginalTransactionId = eventData.OriginalTransactionId ?? eventData.TransactionId, // RevenueCat's original_transaction_id (stable subscription identifier)
            Message = $"RevenueCat webhook verification successful. CancelReason: {eventData.CancelReason}, Price: {eventData.PriceInPurchasedCurrency} {eventData.Currency}",
            PaymentState = paymentState,
            AutoRenewing = autoRenewing,
            PurchaseTimeMillis = eventData.PurchasedAtMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            PriceInPurchasedCurrency = eventData.PriceInPurchasedCurrency, // Direct price value for refund detection
            // Fix: Always provide a non-null OrderId - use OriginalTransactionId as stable identifier, similar to Apple Pay approach
            OrderId = eventData.OriginalTransactionId ?? eventData.TransactionId ?? Guid.NewGuid().ToString()
        };
    }

}