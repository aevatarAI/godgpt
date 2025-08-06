using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.Common.Options;
using Google.Apis.AndroidPublisher.v3;
using Google.Apis.AndroidPublisher.v3.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Aevatar.Application.Grains.Common.Service;

/// <summary>
/// Google Pay service implementation for Google Play Developer API integration
/// </summary>
public class GooglePayService : IGooglePayService
{
    private readonly ILogger<GooglePayService> _logger;
    private readonly GooglePayOptions _options;
    private readonly AndroidPublisherService _publisherService;

    public GooglePayService(
        ILogger<GooglePayService> logger,
        IOptionsMonitor<GooglePayOptions> googlePayOptions)
    {
        _logger = logger;
        _options = googlePayOptions.CurrentValue;
        
        // Initialize Google APIs client
        _publisherService = InitializePublisherService();
    }

    private AndroidPublisherService InitializePublisherService()
    {
        try
        {
            // Load service account credentials from JSON file
            GoogleCredential credential;
            using (var stream = new FileStream(_options.ServiceAccountKeyPath, FileMode.Open, FileAccess.Read))
            {
                credential = GoogleCredential.FromStream(stream)
                    .CreateScoped(AndroidPublisherService.Scope.Androidpublisher);
            }

            return new AndroidPublisherService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = _options.ApplicationName ?? "GodGPT",
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GooglePayService] Failed to initialize AndroidPublisherService");
            throw;
        }
    }

    public async Task<GooglePlayPurchaseDto> VerifyPurchaseAsync(string purchaseToken, string productId, string packageName)
    {
        try
        {
            _logger.LogDebug("[GooglePayService][VerifyPurchaseAsync] Verifying purchase for productId: {ProductId}", productId);

            var request = _publisherService.Purchases.Subscriptionsv2.Get(packageName, purchaseToken);
            var subscription = await request.ExecuteAsync();

            if (subscription == null)
            {
                _logger.LogWarning("[GooglePayService][VerifyPurchaseAsync] No subscription found for token: {Token}", 
                    purchaseToken?.Substring(0, Math.Min(10, purchaseToken.Length)) + "***");
                return null;
            }

            var result = new GooglePlayPurchaseDto
            {
                PurchaseToken = purchaseToken,
                ProductId = subscription.LineItems?.FirstOrDefault()?.ProductId ?? productId,
                PurchaseTimeMillis = subscription.StartTimeDateTimeOffset?.ToUnixTimeMilliseconds() ?? 0,
                PurchaseState = GetPurchaseState(subscription.SubscriptionState),
                OrderId = "subscription_order", // Subscriptions don't have traditional order IDs
                PackageName = packageName,
                AutoRenewing = subscription.LineItems?.FirstOrDefault()?.AutoRenewingPlan != null,
                DeveloperPayload = "" // Not applicable for subscriptions
            };

            _logger.LogInformation("[GooglePayService][VerifyPurchaseAsync] Successfully verified purchase for productId: {ProductId}", productId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GooglePayService][VerifyPurchaseAsync] Error verifying purchase for productId: {ProductId}", productId);
            return null;
        }
    }

    public async Task<bool> VerifyWebPaymentAsync(string paymentToken, string orderId)
    {
        try
        {
            _logger.LogDebug("[GooglePayService][VerifyWebPaymentAsync] Verifying web payment for orderId: {OrderId}", orderId);

            // For Google Pay Web, we need to verify the payment token
            // This typically involves validating the JWT token structure and signature
            // For a complete implementation, you would need to:
            // 1. Parse the payment token JWT
            // 2. Verify the signature using Google's public keys
            // 3. Validate the token contents (amount, merchant ID, etc.)
            
            // Placeholder implementation - in production, implement proper JWT validation
            if (string.IsNullOrWhiteSpace(paymentToken))
            {
                _logger.LogWarning("[GooglePayService][VerifyWebPaymentAsync] Payment token is null or empty");
                return false;
            }

            // TODO: Implement proper Google Pay Web token verification
            // This should include JWT signature verification and payload validation
            _logger.LogWarning("[GooglePayService][VerifyWebPaymentAsync] Web payment verification not fully implemented yet");
            
            return true; // Temporary - replace with actual verification logic
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GooglePayService][VerifyWebPaymentAsync] Error verifying web payment for orderId: {OrderId}", orderId);
            return false;
        }
    }

    public async Task<GooglePlaySubscriptionDto> GetSubscriptionAsync(string subscriptionId, string packageName)
    {
        try
        {
            _logger.LogDebug("[GooglePayService][GetSubscriptionAsync] Getting subscription: {SubscriptionId}", subscriptionId);

            var request = _publisherService.Purchases.Subscriptionsv2.Get(packageName, subscriptionId);
            var subscription = await request.ExecuteAsync();

            if (subscription == null)
            {
                _logger.LogWarning("[GooglePayService][GetSubscriptionAsync] No subscription found: {SubscriptionId}", subscriptionId);
                return null;
            }

            var lineItem = subscription.LineItems?.FirstOrDefault();
            var result = new GooglePlaySubscriptionDto
            {
                SubscriptionId = subscriptionId,
                StartTimeMillis = subscription.StartTimeDateTimeOffset?.ToUnixTimeMilliseconds() ?? 0,
                ExpiryTimeMillis = lineItem?.ExpiryTimeDateTimeOffset?.ToUnixTimeMilliseconds() ?? 0,
                AutoRenewing = lineItem?.AutoRenewingPlan != null,
                PaymentState = GetPaymentState(subscription.SubscriptionState),
                OrderId = $"GPA.{subscriptionId}.{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                PriceAmountMicros = "9990000", // Default $9.99 in micros, should be extracted from actual subscription data
                PriceCurrencyCode = "USD" // Default, should be extracted from actual subscription data
            };

            _logger.LogInformation("[GooglePayService][GetSubscriptionAsync] Successfully retrieved subscription: {SubscriptionId}", subscriptionId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GooglePayService][GetSubscriptionAsync] Error getting subscription: {SubscriptionId}", subscriptionId);
            return null;
        }
    }

    public async Task<bool> GetRefundStatusAsync(string purchaseToken, string packageName)
    {
        try
        {
            _logger.LogDebug("[GooglePayService][GetRefundStatusAsync] Checking refund status for token: {Token}", 
                purchaseToken?.Substring(0, Math.Min(10, purchaseToken.Length)) + "***");

            // Query the subscription to check if it has been refunded/revoked
            var request = _publisherService.Purchases.Subscriptionsv2.Get(packageName, purchaseToken);
            var subscription = await request.ExecuteAsync();

            if (subscription == null)
            {
                _logger.LogWarning("[GooglePayService][GetRefundStatusAsync] No subscription found for token");
                return false;
            }

            // Check if subscription is in a refunded/revoked state
            bool isRefunded = subscription.SubscriptionState == "SUBSCRIPTION_STATE_CANCELED" ||
                             subscription.SubscriptionState == "SUBSCRIPTION_STATE_EXPIRED";

            _logger.LogInformation("[GooglePayService][GetRefundStatusAsync] Refund status checked: {IsRefunded}", isRefunded);
            return isRefunded;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GooglePayService][GetRefundStatusAsync] Error checking refund status");
            return false;
        }
    }

    public async Task<bool> GetCancellationStatusAsync(string subscriptionId, string packageName)
    {
        try
        {
            _logger.LogDebug("[GooglePayService][GetCancellationStatusAsync] Checking cancellation status for: {SubscriptionId}", subscriptionId);

            var subscription = await GetSubscriptionAsync(subscriptionId, packageName);
            if (subscription == null)
            {
                _logger.LogWarning("[GooglePayService][GetCancellationStatusAsync] No subscription found: {SubscriptionId}", subscriptionId);
                return false;
            }

            // Check if subscription is canceled (not auto-renewing)
            bool isCanceled = !subscription.AutoRenewing;

            _logger.LogInformation("[GooglePayService][GetCancellationStatusAsync] Cancellation status: {IsCanceled}", isCanceled);
            return isCanceled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GooglePayService][GetCancellationStatusAsync] Error checking cancellation status");
            return false;
        }
    }

    public async Task<bool> AcknowledgePurchaseAsync(string purchaseToken, string packageName)
    {
        try
        {
            _logger.LogDebug("[GooglePayService][AcknowledgePurchaseAsync] Acknowledging purchase for token: {Token}", 
                purchaseToken?.Substring(0, Math.Min(10, purchaseToken.Length)) + "***");

            // For subscriptions, we use the subscriptions acknowledge endpoint
            // Note: For subscription acknowledgment, we need the subscription ID, not just the purchase token
            // This is a simplified implementation that would need the actual subscription ID
            var acknowledgeRequest = new SubscriptionPurchasesAcknowledgeRequest();
            var request = _publisherService.Purchases.Subscriptions.Acknowledge(acknowledgeRequest, packageName, "default-subscription-id", purchaseToken);
            await request.ExecuteAsync();

            _logger.LogInformation("[GooglePayService][AcknowledgePurchaseAsync] Successfully acknowledged purchase");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GooglePayService][AcknowledgePurchaseAsync] Error acknowledging purchase");
            return false;
        }
    }

    /// <summary>
    /// Convert Google Play subscription state to our purchase state enum
    /// </summary>
    private int GetPurchaseState(string subscriptionState)
    {
        return subscriptionState switch
        {
            "SUBSCRIPTION_STATE_ACTIVE" => 1, // Purchased
            "SUBSCRIPTION_STATE_CANCELED" => 0, // Canceled
            "SUBSCRIPTION_STATE_IN_GRACE_PERIOD" => 1, // Still active
            "SUBSCRIPTION_STATE_ON_HOLD" => 2, // Pending
            "SUBSCRIPTION_STATE_PAUSED" => 2, // Pending
            "SUBSCRIPTION_STATE_EXPIRED" => 0, // Canceled
            _ => 0 // Default to canceled for unknown states
        };
    }

    /// <summary>
    /// Convert Google Play subscription state to our payment state enum
    /// </summary>
    private int GetPaymentState(string subscriptionState)
    {
        return subscriptionState switch
        {
            "SUBSCRIPTION_STATE_ACTIVE" => 1, // Payment received
            "SUBSCRIPTION_STATE_IN_GRACE_PERIOD" => 0, // Payment pending
            "SUBSCRIPTION_STATE_ON_HOLD" => 0, // Payment pending
            "SUBSCRIPTION_STATE_PAUSED" => 0, // Payment pending
            _ => 2 // Payment failed for other states
        };
    }

    public void Dispose()
    {
        _publisherService?.Dispose();
    }
}