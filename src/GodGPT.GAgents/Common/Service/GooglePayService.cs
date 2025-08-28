using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Dtos;
using Aevatar.Application.Grains.Common.Options;
using Google.Apis.AndroidPublisher.v3;
using Google.Apis.AndroidPublisher.v3.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Aevatar.Application.Grains.Common.Service
{
    public class GooglePayService : IGooglePayService
    {
        private AndroidPublisherService _androidPublisherService;
        private readonly GooglePayOptions _options;
        private readonly ILogger<GooglePayService> _logger;

        public GooglePayService(
            ILogger<GooglePayService> logger,
            IOptions<GooglePayOptions> options)
        {
            _logger = logger;
            _options = options.Value;
        }
        
        

        public async Task<PaymentVerificationResultDto> VerifyGooglePlayPurchaseAsync(
            GooglePlayVerificationDto request)
        {
            EnsureAndroidPublisherServiceInitialized();
            try
            {
                _logger.LogInformation("Verifying Google Play purchase for token: {PurchaseToken}", 
                    request.PurchaseToken?[..8] + "...");

                var subscriptionResult = await GetSubscriptionPurchaseInternalAsync(
                    request.PackageName ?? _options.PackageName,
                    request.ProductId,
                    request.PurchaseToken);

                if (subscriptionResult != null)
                {
                    return CreateSuccessResult(subscriptionResult, request);
                }
                
                var productResult = await GetProductPurchaseInternalAsync(
                    request.PackageName ?? _options.PackageName,
                    request.ProductId,
                    request.PurchaseToken);

                if (productResult != null)
                {
                    return CreateSuccessResult(productResult, request);
                }

                _logger.LogWarning("Purchase verification failed for token: {PurchaseToken}", 
                    request.PurchaseToken?[..8] + "...");

                return new PaymentVerificationResultDto
                {
                    IsValid = false,
                    ErrorCode = "INVALID_PURCHASE_TOKEN",
                    Message = "Purchase token is invalid or expired"
                };
            }
            catch (Google.GoogleApiException apiEx)
            {
                _logger.LogError(apiEx, "Google Play API error during verification: {Error}", apiEx.Message);
                
                return new PaymentVerificationResultDto
                {
                    IsValid = false,
                    ErrorCode = GetErrorCodeFromApiException(apiEx),
                    Message = $"Google Play API error: {apiEx.Message}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during Google Play purchase verification");
                
                return new PaymentVerificationResultDto
                {
                    IsValid = false,
                    ErrorCode = "VERIFICATION_ERROR",
                    Message = "An unexpected error occurred during verification"
                };
            }
        }

        public async Task<GooglePlaySubscriptionDto> GetSubscriptionAsync(
            string packageName, 
            string subscriptionId, 
            string purchaseToken)
        {
            EnsureAndroidPublisherServiceInitialized();
            try
            {
                var subscription = await GetSubscriptionPurchaseInternalAsync(packageName, subscriptionId, purchaseToken);
                
                if (subscription == null)
                {
                    return new GooglePlaySubscriptionDto();
                }

                return new GooglePlaySubscriptionDto
                {
                    SubscriptionId = subscriptionId,
                    StartTimeMillis = subscription.StartTimeMillis ?? 0,
                    ExpiryTimeMillis = subscription.ExpiryTimeMillis ?? 0,
                    AutoRenewing = subscription.AutoRenewing ?? false,
                    PaymentState = subscription.PaymentState ?? 0,
                    OrderId = subscription.OrderId ?? string.Empty,
                    PriceAmountMicros = subscription.PriceAmountMicros?.ToString() ?? "0",
                    PriceCurrencyCode = subscription.PriceCurrencyCode ?? "USD",
                    PurchaseToken = purchaseToken
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting subscription details for {PurchaseToken}", purchaseToken?[..8] + "...");
                return new GooglePlaySubscriptionDto();
            }
        }

        public async Task<GooglePlayProductDto> GetProductAsync(
            string packageName, 
            string productId, 
            string purchaseToken)
        {
            EnsureAndroidPublisherServiceInitialized();
            try
            {
                var product = await GetProductPurchaseInternalAsync(packageName, productId, purchaseToken);
                
                if (product == null)
                {
                    return new GooglePlayProductDto();
                }

                return new GooglePlayProductDto
                {
                    ProductId = productId,
                    PurchaseTimeMillis = product.PurchaseTimeMillis ?? 0,
                    PurchaseState = product.PurchaseState ?? 0,
                    ConsumptionState = product.ConsumptionState ?? 0,
                    OrderId = product.OrderId ?? string.Empty,
                    PurchaseToken = purchaseToken,
                    DeveloperPayload = product.DeveloperPayload ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting product details for {PurchaseToken}", purchaseToken?[..8] + "...");
                return new GooglePlayProductDto();
            }
        }

        public async Task<bool> AcknowledgePurchaseAsync(string packageName, string productId, string purchaseToken)
        {
            EnsureAndroidPublisherServiceInitialized();
            try
            {
                try
                {
                    var subscriptionAckRequest = _androidPublisherService.Purchases.Subscriptions.Acknowledge(
                        new SubscriptionPurchasesAcknowledgeRequest(), packageName, productId, purchaseToken);
                    
                    await subscriptionAckRequest.ExecuteAsync();
                    _logger.LogInformation("Successfully acknowledged subscription purchase: {PurchaseToken}", 
                        purchaseToken?[..8] + "...");
                    return true;
                }
                catch (Google.GoogleApiException)
                {
                    var productAckRequest = _androidPublisherService.Purchases.Products.Acknowledge(
                        new ProductPurchasesAcknowledgeRequest(), packageName, productId, purchaseToken);
                    
                    await productAckRequest.ExecuteAsync();
                    _logger.LogInformation("Successfully acknowledged product purchase: {PurchaseToken}", 
                        purchaseToken?[..8] + "...");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to acknowledge purchase: {PurchaseToken}", purchaseToken?[..8] + "...");
                return false;
            }
        }

        private AndroidPublisherService CreateAndroidPublisherService()
        {
            try
            {
                GoogleCredential credential;
                
                if (!string.IsNullOrEmpty(_options.ServiceAccountJson))
                {
                    credential = GoogleCredential.FromJson(_options.ServiceAccountJson)
                        .CreateScoped(AndroidPublisherService.Scope.Androidpublisher);
                }
                else
                {
                    throw new InvalidOperationException(
                        "Google Play service account credentials not configured. " +
                        "Please set ServiceAccountJson in GooglePayOptions.");
                }

                return new AndroidPublisherService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "GodGPT Google Play Integration"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Android Publisher Service");
                throw;
            }
        }

        private void EnsureAndroidPublisherServiceInitialized()
        {
            if (_androidPublisherService != null)
            {
                return;
            }
            _androidPublisherService = CreateAndroidPublisherService();
        }

        private async Task<SubscriptionPurchase?> GetSubscriptionPurchaseInternalAsync(
            string packageName, 
            string subscriptionId, 
            string purchaseToken)
        {
            try
            {
                var request = _androidPublisherService.Purchases.Subscriptions.Get(
                    packageName, subscriptionId, purchaseToken);
                
                var subscription = await request.ExecuteAsync();
                return subscription;
            }
            catch (Google.GoogleApiException apiEx) when (apiEx.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Subscription not found for token: {PurchaseToken}", purchaseToken?[..8] + "...");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving subscription for token: {PurchaseToken}", purchaseToken?[..8] + "...");
                return null;
            }
        }

        private async Task<ProductPurchase?> GetProductPurchaseInternalAsync(
            string packageName, 
            string productId, 
            string purchaseToken)
        {
            try
            {
                var request = _androidPublisherService.Purchases.Products.Get(
                    packageName, productId, purchaseToken);
                
                var product = await request.ExecuteAsync();
                return product;
            }
            catch (Google.GoogleApiException apiEx) when (apiEx.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Product purchase not found for token: {PurchaseToken}", purchaseToken?[..8] + "...");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving product purchase for token: {PurchaseToken}", purchaseToken?[..8] + "...");
                return null;
            }
        }

        private PaymentVerificationResultDto CreateSuccessResult(
            SubscriptionPurchase subscription, 
            GooglePlayVerificationDto request)
        {
            var isValid = subscription.PaymentState == 1; // Payment received
            var startTime = DateTimeOffset.FromUnixTimeMilliseconds(subscription.StartTimeMillis ?? 0);
            var expiryTime = DateTimeOffset.FromUnixTimeMilliseconds(subscription.ExpiryTimeMillis ?? 0);

            return new PaymentVerificationResultDto
            {
                IsValid = isValid,
                ProductId = request.ProductId,
                TransactionId = subscription.OrderId,
                SubscriptionStartDate = startTime.DateTime,
                SubscriptionEndDate = expiryTime.DateTime,
                PaymentState = subscription.PaymentState,
                AutoRenewing = subscription.AutoRenewing,
                PurchaseTimeMillis = subscription.StartTimeMillis,
                Message = isValid ? "Subscription verified successfully" : "Payment not received"
            };
        }

        private PaymentVerificationResultDto CreateSuccessResult(
            ProductPurchase product, 
            GooglePlayVerificationDto request)
        {
            var isValid = product.PurchaseState == 0; // Purchased
            var purchaseTime = DateTimeOffset.FromUnixTimeMilliseconds(product.PurchaseTimeMillis ?? 0);

            return new PaymentVerificationResultDto
            {
                IsValid = isValid,
                ProductId = request.ProductId,
                TransactionId = product.OrderId,
                SubscriptionStartDate = purchaseTime.DateTime,
                PaymentState = product.PurchaseState,
                PurchaseTimeMillis = product.PurchaseTimeMillis,
                Message = isValid ? "Product purchase verified successfully" : "Purchase not completed"
            };
        }

        private string GetErrorCodeFromApiException(Google.GoogleApiException apiEx)
        {
            return apiEx.HttpStatusCode switch
            {
                System.Net.HttpStatusCode.NotFound => "PURCHASE_NOT_FOUND",
                System.Net.HttpStatusCode.Unauthorized => "UNAUTHORIZED_ACCESS",
                System.Net.HttpStatusCode.Forbidden => "INSUFFICIENT_PERMISSIONS",
                System.Net.HttpStatusCode.BadRequest => "INVALID_REQUEST",
                System.Net.HttpStatusCode.TooManyRequests => "RATE_LIMIT_EXCEEDED",
                _ => "API_ERROR"
            };
        }

        public void Dispose()
        {
            _androidPublisherService?.Dispose();
        }
    }
}
