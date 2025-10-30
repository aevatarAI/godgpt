using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Helpers;
using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;

namespace Aevatar.Application.Grains.UserBilling.Services;

/// <summary>
/// Stripe payment service interface for handling Stripe-specific payment operations
/// </summary>
public interface IStripePaymentService
{
    /// <summary>
    /// Get available Stripe products
    /// </summary>
    /// <returns>List of Stripe products</returns>
    Task<List<StripeProductDto>> GetProductsAsync();

    /// <summary>
    /// Get or create Stripe customer
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <returns>Stripe customer ID</returns>
    Task<string> CreateCustomerAsync(string userId);

    /// <summary>
    /// Get Stripe customer details
    /// </summary>
    /// <param name="customerId">Customer ID</param>
    /// <returns>Customer response with ephemeral key</returns>
    Task<GetCustomerResponseDto> GetCustomerWithEphemeralKeyAsync(string customerId);

    /// <summary>
    /// Create Stripe checkout session
    /// </summary>
    /// <param name="dto">Checkout session creation parameters</param>
    /// <param name="options">Stripe configuration options</param>
    /// <returns>Session URL or client secret</returns>
    Task<string> CreateCheckoutSessionAsync(CreateCheckoutSessionDto dto, StripeOptions options);

    /// <summary>
    /// Create Stripe subscription
    /// </summary>
    /// <param name="dto">Subscription creation parameters</param>
    /// <param name="options">Stripe configuration options</param>
    /// <returns>Subscription response</returns>
    Task<SubscriptionResponseDto> CreateSubscriptionAsync(CreateSubscriptionDto dto, StripeOptions options);

    /// <summary>
    /// Create Stripe payment sheet
    /// </summary>
    /// <param name="dto">Payment sheet creation parameters</param>
    /// <param name="options">Stripe configuration options</param>
    /// <returns>Payment sheet response</returns>
    Task<PaymentSheetResponseDto> CreatePaymentSheetAsync(CreatePaymentSheetDto dto, StripeOptions options);

    /// <summary>
    /// Process Stripe webhook event
    /// </summary>
    /// <param name="jsonPayload">Webhook JSON payload</param>
    /// <param name="signature">Stripe signature</param>
    /// <param name="options">Stripe configuration options</param>
    /// <returns>Payment verification result</returns>
    Task<PaymentVerificationResultDto> ProcessWebhookEventAsync(string jsonPayload, string signature, StripeOptions options);

    /// <summary>
    /// Cancel Stripe subscription
    /// </summary>
    /// <param name="dto">Cancellation parameters</param>
    /// <param name="options">Stripe configuration options</param>
    /// <returns>Cancellation response</returns>
    Task<CancelSubscriptionResponseDto> CancelSubscriptionAsync(CancelSubscriptionDto dto, StripeOptions options);
}


public class StripePaymentService : IStripePaymentService
{
    private readonly ILogger<StripePaymentService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<StripeOptions> _stripeOptions;
    
    // ✅ StripeClient is thread-safe according to official Stripe.NET documentation
    // Can be safely stored as instance variable in singleton service
    private readonly IStripeClient _client;

    public StripePaymentService(
        ILogger<StripePaymentService> logger,
        IHttpClientFactory httpClientFactory, IOptionsMonitor<StripeOptions> stripeOptions)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _stripeOptions = stripeOptions;

        // Use Lazy<T> for thread-safe initialization
        StripeConfiguration.ApiKey = _stripeOptions.CurrentValue.SecretKey;
        _client ??= new StripeClient(_stripeOptions.CurrentValue.SecretKey);
        _logger.LogDebug("[StripePaymentService] Initialized StripeClient...");
    }
    

    public async Task<List<StripeProductDto>> GetProductsAsync()
    {
        var products = _stripeOptions.CurrentValue.Products;
        if (products.IsNullOrEmpty())
        {
            _logger.LogWarning("[StripePaymentService][GetProductsAsync] No products configured in StripeOptions");
            return new List<StripeProductDto>();
        }

        _logger.LogDebug("[StripePaymentService][GetProductsAsync] Found {Count} products in configuration",
            products.Count);
        var productDtos = new List<StripeProductDto>();
        foreach (var product in products)
        {
            var planType = (PlanType)product.PlanType;

            if (planType == PlanType.Day)
            {
                continue;
            }
            
            // Use SubscriptionHelper for consistent daily average price calculation
            var dailyAvgPrice = "0.0";
            if (planType != PlanType.None)
            {
                dailyAvgPrice = SubscriptionHelper.CalculateDailyAveragePrice(planType, product.Amount).ToString();
            }

            productDtos.Add(new StripeProductDto
            {
                PlanType = planType,
                PriceId = product.PriceId,
                Mode = product.Mode,
                Amount = product.Amount,
                DailyAvgPrice = dailyAvgPrice,
                Currency = product.Currency,
                IsUltimate = product.IsUltimate,
                Credits = product.Credits
            });
        }
        
        _logger.LogDebug("[StripePaymentService][GetProductsAsync] Successfully retrieved {Count} products",
            productDtos.Count);
        return productDtos;
    }

    public async Task<string> CreateCustomerAsync(string userId)
    {
        var customerService = new CustomerService(_client);
        
        var customerId = string.Empty;
        if (!string.IsNullOrEmpty(userId))
        {
            CustomerCreateOptions customerOptions = new CustomerCreateOptions
            {
                Metadata = new Dictionary<string, string>
                {
                    { "internal_user_id", userId }
                }
            };

            var customer = await customerService.CreateAsync(customerOptions);
            _logger.LogInformation(
                "[StripePaymentService][GetOrCreateCustomerAsync] Created Stripe Customer for user {UserId}: {CustomerId}",
                userId, customer.Id);
            customerId = customer.Id;
        }
        else
        {
            CustomerCreateOptions customerOptions = new CustomerCreateOptions
            {
                Description = "Temporarily created subscription customers"
            };

            var customer = await customerService.CreateAsync(customerOptions);
            _logger.LogInformation(
                "[StripePaymentService][GetOrCreateCustomerAsync] Created temporary Stripe Customer: {CustomerId}",
                customer.Id);
            customerId = customer.Id;
        }

        return customerId;
    }

    public async Task<GetCustomerResponseDto> GetCustomerWithEphemeralKeyAsync(string customerId)
    {
        try
        {
            var ephemeralKeyService = new EphemeralKeyService(_client);
            var ephemeralKeyOptions = new EphemeralKeyCreateOptions
            {
                Customer = customerId,
                StripeVersion = "2025-04-30.basil",
            };
            
            var ephemeralKey = await ephemeralKeyService.CreateAsync(ephemeralKeyOptions);
            _logger.LogInformation(
                "[StripePaymentService][GetCustomerAsync] Customer {userId} Created ephemeral key with ID: {EphemeralKeyId}",
                customerId, ephemeralKey.Id);

            var response = new GetCustomerResponseDto()
            {
                EphemeralKey = ephemeralKey.Secret,
                Customer = customerId,
                PublishableKey = _stripeOptions.CurrentValue.PublishableKey
            };
            
            return response;
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex,
                "[StripePaymentService][GetCustomerAsync] Stripe error: {ErrorMessage}",
                ex.StripeError?.Message);
            throw new InvalidOperationException(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[StripePaymentService][GetCustomerAsync] error: {ErrorMessage}",
                ex.Message);
            throw;
        }
    }

    public async Task<string> CreateCheckoutSessionAsync(CreateCheckoutSessionDto dto, StripeOptions options)
    {
        _logger.LogDebug("[StripePaymentService][CreateCheckoutSessionAsync] Method not yet implemented");
        throw new NotImplementedException("Method will be implemented during migration");
    }

    public async Task<SubscriptionResponseDto> CreateSubscriptionAsync(CreateSubscriptionDto dto, StripeOptions options)
    {
        _logger.LogDebug("[StripePaymentService][CreateSubscriptionAsync] Method not yet implemented");
        throw new NotImplementedException("Method will be implemented during migration");
    }

    public async Task<PaymentSheetResponseDto> CreatePaymentSheetAsync(CreatePaymentSheetDto dto, StripeOptions options)
    {
        _logger.LogDebug("[StripePaymentService][CreatePaymentSheetAsync] Method not yet implemented");
        throw new NotImplementedException("Method will be implemented during migration");
    }

    public async Task<PaymentVerificationResultDto> ProcessWebhookEventAsync(string jsonPayload, string signature, StripeOptions options)
    {
        _logger.LogDebug("[StripePaymentService][ProcessWebhookEventAsync] Method not yet implemented");
        throw new NotImplementedException("Method will be implemented during migration");
    }

    public async Task<CancelSubscriptionResponseDto> CancelSubscriptionAsync(CancelSubscriptionDto dto, StripeOptions options)
    {
        _logger.LogDebug("[StripePaymentService][CancelSubscriptionAsync] Method not yet implemented");
        throw new NotImplementedException("Method will be implemented during migration");
    }
}
