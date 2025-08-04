using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.ChatManager.UserBilling.Payment;
using Aevatar.Application.Grains.Common;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Helpers;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Application.Grains.PaymentAnalytics;
using Aevatar.Application.Grains.UserBilling.SEvents;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stripe;
using Stripe.Checkout;
using PaymentMethod = Aevatar.Application.Grains.Common.Constants.PaymentMethod;

namespace Aevatar.Application.Grains.UserBilling;

public interface IUserBillingGAgent : IGAgent
{
    Task<List<StripeProductDto>> GetStripeProductsAsync();
    Task<List<AppleProductDto>> GetAppleProductsAsync();
    Task<string> GetOrCreateStripeCustomerAsync(string userId = null);
    Task<GetCustomerResponseDto> GetStripeCustomerAsync(string userId = null);
    /**
     * web
     */
    Task<string> CreateCheckoutSessionAsync(CreateCheckoutSessionDto createCheckoutSessionDto);
    /**
     * [app]
     * platform:android/ios
     */
    Task<SubscriptionResponseDto> CreateSubscriptionAsync(CreateSubscriptionDto createSubscriptionDto);
    Task<PaymentSheetResponseDto> CreatePaymentSheetAsync(CreatePaymentSheetDto createPaymentSheetDto);
    Task<Guid> AddPaymentRecordAsync(ChatManager.UserBilling.PaymentSummary paymentSummary);
    Task<ChatManager.UserBilling.PaymentSummary> GetPaymentSummaryAsync(Guid paymentId);
    Task<List<ChatManager.UserBilling.PaymentSummary>> GetPaymentHistoryAsync(int page = 1, int pageSize = 10);
    Task<bool> UpdatePaymentStatusAsync(ChatManager.UserBilling.PaymentSummary payment, PaymentStatus newStatus);
    Task<bool> HandleStripeWebhookEventAsync(string jsonPayload, string stripeSignature);
    Task<CancelSubscriptionResponseDto> CancelSubscriptionAsync(CancelSubscriptionDto cancelSubscriptionDto);
    Task<object> RefundedSubscriptionAsync(object  obj);
    Task ClearAllAsync();
    
    // App Store related methods
    Task<AppStoreSubscriptionResponseDto> CreateAppStoreSubscriptionAsync(CreateAppStoreSubscriptionDto createSubscriptionDto);
    Task<bool> HandleAppStoreNotificationAsync(Guid userId, string jsonPayload);
    Task<GrainResultDto<AppStoreJWSTransactionDecodedPayload>> GetAppStoreTransactionInfoAsync(string transactionId, string environment);
    /// <summary>
    /// Determines whether there is an active (renewing) Apple subscription.
    /// An active Apple subscription is defined as a payment record with Platform=AppStore,
    /// InvoiceDetails is not null or empty, and all InvoiceDetail's Status are not Cancelled.
    /// </summary>
    /// <returns>True if there is an active Apple subscription; otherwise, false.</returns>
    [Obsolete("Use GetActiveSubscriptionStatusAsync instead")]
    Task<bool> HasActiveAppleSubscriptionAsync();
    /// <summary>
    /// Gets the active subscription status for all payment platforms.
    /// Uses a single iteration through payment history for optimal performance.
    /// </summary>
    /// <returns>ActiveSubscriptionStatusDto containing status for Apple, Stripe, and overall subscriptions.</returns>
    Task<ActiveSubscriptionStatusDto> GetActiveSubscriptionStatusAsync();
}

[GAgent(nameof(UserBillingGAgent))]
public class UserBillingGAgent : GAgentBase<UserBillingGAgentState, UserBillingLogEvent>, IUserBillingGAgent
{
    private readonly ILogger<UserBillingGrain> _logger;
    private readonly IOptionsMonitor<StripeOptions> _stripeOptions;
    private readonly IOptionsMonitor<ApplePayOptions> _appleOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    
    private readonly IStripeClient _client; 
    
    public UserBillingGAgent(
        ILogger<UserBillingGrain> logger, 
        IOptionsMonitor<StripeOptions> stripeOptions,
        IOptionsMonitor<ApplePayOptions> appleOptions,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _stripeOptions = stripeOptions;
        _appleOptions = appleOptions;
        _httpClientFactory = httpClientFactory;
        
        StripeConfiguration.ApiKey = _stripeOptions.CurrentValue.SecretKey;
        _client ??= new StripeClient(_stripeOptions.CurrentValue.SecretKey);
        _logger.LogDebug("[UserBillingGAgent] Activating agent for user {UserId}", this.GetPrimaryKey().ToString());
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"UserBillingGAgent for user {this.GetPrimaryKey().ToString()}, CustomerId: {State.CustomerId}, PaymentHistory count: {State.PaymentHistory?.Count ?? 0}");
        throw new NotImplementedException();
    }

    public async Task<List<StripeProductDto>> GetStripeProductsAsync()
    {
        var products = _stripeOptions.CurrentValue.Products;
        if (products.IsNullOrEmpty())
        {
            _logger.LogWarning("[UserBillingGAgent][GetStripeProductsAsync] No products configured in StripeOptions");
            return new List<StripeProductDto>();
        }

        _logger.LogDebug("[UserBillingGAgent][GetStripeProductsAsync] Found {Count} products in configuration",
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
            var dailyAvgPrice = SubscriptionHelper.CalculateDailyAveragePrice(planType, product.Amount).ToString();

            productDtos.Add(new StripeProductDto
            {
                PlanType = planType,
                PriceId = product.PriceId,
                Mode = product.Mode,
                Amount = product.Amount,
                DailyAvgPrice = dailyAvgPrice,
                Currency = product.Currency,
                IsUltimate = product.IsUltimate
            });
        }
        
        _logger.LogDebug("[UserBillingGAgent][GetStripeProductsAsync] Successfully retrieved {Count} products",
            productDtos.Count);
        return productDtos;
    }

    public async Task<List<AppleProductDto>> GetAppleProductsAsync()
    {
        var products = _appleOptions.CurrentValue.Products;
        if (products.IsNullOrEmpty())
        {
            _logger.LogWarning("[UserBillingGAgent][GetAppleProductsAsync] No products configured in ApplePayOptions");
            return new List<AppleProductDto>();
        }

        _logger.LogDebug("[UserBillingGAgent][GetAppleProductsAsync] Found {Count} products in configuration",
            products.Count);
        var productDtos = new List<AppleProductDto>();
        foreach (var product in products)
        {
            var dailyAvgPrice = string.Empty;
            if (product.PlanType == (int)PlanType.Day)
            {
                dailyAvgPrice = product.Amount.ToString();
            } else if (product.PlanType == (int)PlanType.Week)
            {
                dailyAvgPrice = Math.Round(product.Amount / 7, 2, MidpointRounding.ToZero).ToString();
            }
            else if (product.PlanType == (int)PlanType.Month)
            {
                dailyAvgPrice = Math.Round(product.Amount / 30, 2, MidpointRounding.ToZero).ToString();
            }
            else if (product.PlanType == (int)PlanType.Year)
            {
                dailyAvgPrice = Math.Round(product.Amount / 390, 2, MidpointRounding.ToZero).ToString();
            }

            productDtos.Add(new AppleProductDto
            {
                PlanType = product.PlanType,
                ProductId = product.ProductId,
                Name = product.Name,
                Description = product.Description,
                Amount = product.Amount,
                DailyAvgPrice = dailyAvgPrice,
                Currency = product.Currency
            });
        }
        
        _logger.LogDebug("[UserBillingGAgent][GetAppleProductsAsync] Successfully retrieved {Count} products",
            productDtos.Count);
        return productDtos;
    }

    public async Task<string> GetOrCreateStripeCustomerAsync(string userId = null)
    {
        if (!State.CustomerId.IsNullOrWhiteSpace())
        {
            return State.CustomerId;
        }

        var customerService = new CustomerService(_client);
        CustomerCreateOptions customerOptions;
        var customerId = string.Empty;
        if (!string.IsNullOrEmpty(userId))
        {
            customerOptions = new CustomerCreateOptions
            {
                Metadata = new Dictionary<string, string>
                {
                    { "internal_user_id", userId }
                }
            };

            var customer = await customerService.CreateAsync(customerOptions);
            _logger.LogInformation(
                "[UserBillingGAgent][GetOrCreateStripeCustomerAsync] Created Stripe Customer for user {UserId}: {CustomerId}",
                userId, customer.Id);
            customerId = customer.Id;
        }
        else
        {
            customerOptions = new CustomerCreateOptions
            {
                Description = "Temporarily created subscription customers"
            };

            var customer = await customerService.CreateAsync(customerOptions);
            _logger.LogInformation(
                "[UserBillingGAgent][GetOrCreateStripeCustomerAsync] Created temporary Stripe Customer: {CustomerId}",
                customer.Id);
            customerId = customer.Id;
        }

        RaiseEvent(new UpdateCustomerIdLogEvent
        {
            CustomerId = customerId
        });
        await ConfirmEvents();
        
        return State.CustomerId;
    }

    public async Task<GetCustomerResponseDto> GetStripeCustomerAsync(string userId = null)
    {
        try
        {
            var customerId = await GetOrCreateStripeCustomerAsync(userId);
            _logger.LogInformation(
                "[UserBillingGAgent][GetStripeCustomerAsync] {userId} Using customer: {CustomerId}",userId, customerId);
            
            var ephemeralKeyService = new EphemeralKeyService(_client);
            var ephemeralKeyOptions = new EphemeralKeyCreateOptions
            {
                Customer = customerId,
                StripeVersion = "2025-04-30.basil",
            };
            
            var ephemeralKey = await ephemeralKeyService.CreateAsync(ephemeralKeyOptions);
            _logger.LogInformation(
                "[UserBillingGAgent][GetStripeCustomerAsync] {userId} Created ephemeral key with ID: {EphemeralKeyId}",
                userId, ephemeralKey.Id);

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
                "[UserBillingGAgent][GetStripeCustomerAsync] Stripe error: {ErrorMessage}",
                ex.StripeError?.Message);
            throw new InvalidOperationException(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[UserBillingGAgent][GetStripeCustomerAsync] error: {ErrorMessage}",
                ex.Message);
            throw;
        }
    }

    public async Task<string> CreateCheckoutSessionAsync(CreateCheckoutSessionDto createCheckoutSessionDto)
    {
        var productConfig = await GetProductConfigAsync(createCheckoutSessionDto.PriceId);
        await ValidateSubscriptionUpgradePath(createCheckoutSessionDto.UserId, productConfig);

        var orderId = Guid.NewGuid().ToString();
        var options = new SessionCreateOptions
        {
            ClientReferenceId = orderId,
            Mode = createCheckoutSessionDto.Mode,
            LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    Price = createCheckoutSessionDto.PriceId,
                    Quantity = createCheckoutSessionDto.Quantity
                },
            },
            Metadata = new Dictionary<string, string>
            {
                { "internal_user_id", createCheckoutSessionDto.UserId ?? string.Empty },
                { "price_id", createCheckoutSessionDto.PriceId },
                { "quantity", createCheckoutSessionDto.Quantity.ToString() },
                { "order_id", orderId }
            },
            SavedPaymentMethodOptions = new SessionSavedPaymentMethodOptionsOptions
            {
                PaymentMethodSave = "enabled"
            },
            PaymentIntentData = createCheckoutSessionDto.Mode == PaymentMode.PAYMENT
                ? new SessionPaymentIntentDataOptions
                {
                    SetupFutureUsage = "off_session",
                    Metadata = new Dictionary<string, string>
                    {
                        { "internal_user_id", createCheckoutSessionDto.UserId ?? string.Empty },
                        { "price_id", createCheckoutSessionDto.PriceId },
                        { "quantity", createCheckoutSessionDto.Quantity.ToString() },
                        { "order_id", orderId }
                    }
                }
                : null,
            SubscriptionData = createCheckoutSessionDto.Mode == PaymentMode.SUBSCRIPTION
                ? new SessionSubscriptionDataOptions
                {
                    Metadata = new Dictionary<string, string>
                    {
                        { "internal_user_id", createCheckoutSessionDto.UserId ?? string.Empty },
                        { "price_id", createCheckoutSessionDto.PriceId },
                        { "quantity", createCheckoutSessionDto.Quantity.ToString() },
                        { "order_id", orderId }
                    }
                }
                : null
        };

        if (createCheckoutSessionDto.PaymentMethodTypes != null && createCheckoutSessionDto.PaymentMethodTypes.Any())
        {
            options.PaymentMethodTypes = createCheckoutSessionDto.PaymentMethodTypes;
            _logger.LogInformation(
                "[UserBillingGAgent][CreateCheckoutSessionAsync] Using payment method types: {PaymentMethodTypes}",
                string.Join(", ", createCheckoutSessionDto.PaymentMethodTypes));
        }

        if (!string.IsNullOrEmpty(createCheckoutSessionDto.PaymentMethodCollection))
        {
            options.PaymentMethodCollection = createCheckoutSessionDto.PaymentMethodCollection;
            _logger.LogInformation(
                "[UserBillingGAgent][CreateCheckoutSessionAsync] Using payment method collection mode: {Mode}",
                createCheckoutSessionDto.PaymentMethodCollection);
        }

        if (!string.IsNullOrEmpty(createCheckoutSessionDto.PaymentMethodConfiguration))
        {
            options.PaymentMethodConfiguration = createCheckoutSessionDto.PaymentMethodConfiguration;
            _logger.LogInformation(
                "[UserBillingGAgent][CreateCheckoutSessionAsync] Using payment method configuration: {ConfigId}",
                createCheckoutSessionDto.PaymentMethodConfiguration);
        }

        if (createCheckoutSessionDto.UiMode == StripeUiMode.EMBEDDED)
        {
            options.UiMode = "embedded";
            options.ReturnUrl = _stripeOptions.CurrentValue.ReturnUrl;
            options.SuccessUrl = null;
            options.CancelUrl = null;
        }
        else
        {
            options.SuccessUrl = _stripeOptions.CurrentValue.SuccessUrl;
            if (createCheckoutSessionDto.CancelUrl.IsNullOrWhiteSpace())
            {
                options.CancelUrl = _stripeOptions.CurrentValue.CancelUrl;
            }
            else
            {
                options.CancelUrl = createCheckoutSessionDto.CancelUrl;
            }
        }

        try
        {
            options.Customer = await GetOrCreateStripeCustomerAsync(createCheckoutSessionDto.UserId);
            _logger.LogInformation(
                "[UserBillingGAgent][CreateCheckoutSessionAsync] Using existing Customer: {CustomerId} for {Mode} mode",
                options.Customer, createCheckoutSessionDto.Mode);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex,
                "[UserBillingGAgent][CreateCheckoutSessionAsync] Failed to create or get Stripe Customer: {ErrorMessage}",
                ex.Message);
            throw new InvalidOperationException(ex.Message);
        }

        var service = new SessionService(_client);
        try
        {
            var session = await service.CreateAsync(options);
            var paymentDetails =
                await InitializePaymentGrainAsync(orderId, createCheckoutSessionDto, productConfig, session);
            await CreateOrUpdatePaymentSummaryAsync(paymentDetails, session);
            _logger.LogInformation(
                "[UserBillingGAgent][CreateCheckoutSessionAsync] Created/Updated payment record with ID: {PaymentId} for session: {SessionId}",
                paymentDetails.Id, session.Id);

            if (createCheckoutSessionDto.UiMode == StripeUiMode.EMBEDDED)
            {
                _logger.LogInformation(
                    "[UserBillingGAgent][CreateCheckoutSessionAsync] Created embedded checkout session with ID: {SessionId}, returning ClientSecret",
                    session.Id);
                return session.ClientSecret;
            }
            else
            {
                _logger.LogInformation(
                    "[UserBillingGAgent][CreateCheckoutSessionAsync] Created hosted checkout session with ID: {SessionId}, returning URL",
                    session.Id);
            return session.Url;
            }
        }
        catch (StripeException e)
        {
            _logger.LogError(e,
                "[UserBillingGAgent][CreateCheckoutSessionAsync] Failed to create checkout session: {ErrorMessage}",
                e.StripeError.Message);
            throw new InvalidOperationException(e.Message);
        }
    }

    public async Task<PaymentSheetResponseDto> CreatePaymentSheetAsync(CreatePaymentSheetDto createPaymentSheetDto)
    {
        _logger.LogInformation("[UserBillingGAgent][CreatePaymentSheetAsync] Creating payment sheet for user {UserId}",
            createPaymentSheetDto.UserId);
        
        long amount;
        string currency;
        
        if (createPaymentSheetDto.Amount.HasValue && !string.IsNullOrEmpty(createPaymentSheetDto.Currency))
        {
            amount = createPaymentSheetDto.Amount.Value;
            currency = createPaymentSheetDto.Currency;
            _logger.LogInformation(
                "[UserBillingGAgent][CreatePaymentSheetAsync] Using explicitly provided amount: {Amount} {Currency}",
                amount, currency);
        }
        else if (!string.IsNullOrEmpty(createPaymentSheetDto.PriceId))
        {
            var productConfig = await GetProductConfigAsync(createPaymentSheetDto.PriceId);
            amount = (long)productConfig.Amount;
            currency = productConfig.Currency;
            _logger.LogInformation(
                "[UserBillingGAgent][CreatePaymentSheetAsync] Using amount from product config: {Amount} {Currency}, PriceId: {PriceId}",
                amount, currency, createPaymentSheetDto.PriceId);
        }
        else
        {
            var message = "Either Amount+Currency or PriceId must be provided";
            _logger.LogError("[UserBillingGAgent][CreatePaymentSheetAsync] {Message}", message);
            throw new ArgumentException(message);
        }

        var orderId = Guid.NewGuid().ToString();
        
        try
        {
            var customerId = await GetOrCreateStripeCustomerAsync(createPaymentSheetDto.UserId.ToString());
            _logger.LogInformation(
                "[UserBillingGAgent][CreatePaymentSheetAsync] Using customer: {CustomerId}", customerId);
            
            var ephemeralKeyService = new EphemeralKeyService(_client);
            var ephemeralKeyOptions = new EphemeralKeyCreateOptions
            {
                Customer = customerId,
                StripeVersion = "2025-04-30.basil",
            };
            
            var ephemeralKey = await ephemeralKeyService.CreateAsync(ephemeralKeyOptions);
            _logger.LogInformation(
                "[UserBillingGAgent][CreatePaymentSheetAsync] Created ephemeral key with ID: {EphemeralKeyId}",
                ephemeralKey.Id);
            
            var paymentIntentService = new PaymentIntentService(_client);
            var paymentIntentOptions = new PaymentIntentCreateOptions
            {
                Amount = amount,
                Currency = currency,
                Customer = customerId,
                Description = createPaymentSheetDto.Description,
                Metadata = new Dictionary<string, string>
                {
                    { "internal_user_id", createPaymentSheetDto.UserId.ToString() },
                    { "order_id", orderId },
                    { "price_id", createPaymentSheetDto.PriceId},
                    { "quantity", "1" }
                },
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true,
                },
            };
            
            if (createPaymentSheetDto.PaymentMethodTypes != null && createPaymentSheetDto.PaymentMethodTypes.Any())
            {
                paymentIntentOptions.PaymentMethodTypes = createPaymentSheetDto.PaymentMethodTypes;
                _logger.LogInformation(
                    "[UserBillingGAgent][CreatePaymentSheetAsync] Using payment method types: {PaymentMethodTypes}",
                    string.Join(", ", createPaymentSheetDto.PaymentMethodTypes));
            }
            
            var paymentIntent = await paymentIntentService.CreateAsync(paymentIntentOptions);
            _logger.LogInformation(
                "[UserBillingGAgent][CreatePaymentSheetAsync] Created payment intent with ID: {PaymentIntentId}",
                paymentIntent.Id);
            
            if (!string.IsNullOrEmpty(createPaymentSheetDto.PriceId))
            {
                var productConfig = await GetProductConfigAsync(createPaymentSheetDto.PriceId);
                var paymentGrainId = Guid.NewGuid();
                var paymentGrain = GrainFactory.GetGrain<IUserPaymentGrain>(paymentGrainId);
                
                var paymentState = new UserPaymentState
                {
                    Id = paymentGrainId,
                    UserId = createPaymentSheetDto.UserId,
                    PriceId = createPaymentSheetDto.PriceId,
                    Amount = amount,
                    Currency = currency,
                    PaymentType = PaymentType.OneTime,
                    Status = PaymentStatus.Processing,
                    Mode = PaymentMode.PAYMENT,
                    Platform = PaymentPlatform.Stripe,
                    Description = createPaymentSheetDto.Description ?? $"Payment sheet for {amount} {currency}",
                    CreatedAt = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow,
                    OrderId = orderId,
                    SessionId = paymentIntent.Id 
                };
                
                var initResult = await paymentGrain.InitializePaymentAsync(paymentState);
                if (!initResult.Success)
                {
                    _logger.LogError(
                        "[UserBillingGAgent][CreatePaymentSheetAsync] Failed to initialize payment grain: {ErrorMessage}",
                        initResult.Message);
                    throw new Exception($"Failed to initialize payment grain: {initResult.Message}");
                }
                
                var paymentDetails = initResult.Data;
                var paymentSummary = new ChatManager.UserBilling.PaymentSummary
                {
                    PaymentGrainId = paymentDetails.Id,
                    OrderId = orderId,
                    UserId = createPaymentSheetDto.UserId,
                    Amount = amount,
                    Currency = currency,
                    CreatedAt = DateTime.UtcNow,
                    Status = PaymentStatus.Processing,
                    PaymentType = PaymentType.OneTime,
                    Method = PaymentMethod.Card,
                    Platform = PaymentPlatform.Stripe,
                    SessionId = paymentIntent.Id 
                };
                
                await AddPaymentRecordAsync(paymentSummary);
                _logger.LogInformation(
                    "[UserBillingGAgent][CreatePaymentSheetAsync] Created payment record with ID: {PaymentId}",
                    paymentDetails.Id);
            }
            
            var response = new PaymentSheetResponseDto
            {
                PaymentIntent = paymentIntent.ClientSecret,
                EphemeralKey = ephemeralKey.Secret,
                Customer = customerId,
                PublishableKey = _stripeOptions.CurrentValue.PublishableKey
            };
            
            _logger.LogInformation(
                "[UserBillingGAgent][CreatePaymentSheetAsync] Successfully created payment sheet for order: {OrderId}",
                orderId);
            
            return response;
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex,
                "[UserBillingGAgent][CreatePaymentSheetAsync] Stripe error: {ErrorMessage}",
                ex.StripeError?.Message);
            throw new InvalidOperationException(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[UserBillingGAgent][CreatePaymentSheetAsync] Error creating payment sheet: {ErrorMessage}",
                ex.Message);
            throw;
        }
    }

    public async Task<SubscriptionResponseDto> CreateSubscriptionAsync(CreateSubscriptionDto createSubscriptionDto)
    {

        if (string.IsNullOrEmpty(createSubscriptionDto.PriceId))
        {
            throw new ArgumentException("PriceId is required.", nameof(createSubscriptionDto));
        }

        if (createSubscriptionDto.UserId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required.", nameof(createSubscriptionDto));
        }

        _logger.LogInformation(
            "[UserBillingGAgent][CreateSubscriptionAsync] Creating subscription for user {UserId} with price {PriceId}",
            createSubscriptionDto.UserId, createSubscriptionDto.PriceId);

        try
        {
            var productConfig = await GetProductConfigAsync(createSubscriptionDto.PriceId);
            
            await ValidateSubscriptionUpgradePath(createSubscriptionDto.UserId.ToString(), productConfig);

            var customerId = await GetOrCreateStripeCustomerAsync(createSubscriptionDto.UserId.ToString());
            _logger.LogInformation(
                "[UserBillingGAgent][CreateSubscriptionAsync] Using customer: {CustomerId}",
                customerId);

            var orderId = Guid.NewGuid().ToString();
            var options = new SubscriptionCreateOptions
            {
                Customer = customerId,
                Items = new List<SubscriptionItemOptions>
                {
                    new SubscriptionItemOptions
                    {
                        Price = createSubscriptionDto.PriceId,
                        Quantity = createSubscriptionDto.Quantity ?? 1
                    }
                },
                PaymentBehavior = "default_incomplete",
                PaymentSettings = new SubscriptionPaymentSettingsOptions
                {
                    SaveDefaultPaymentMethod = "on_subscription",
                    // Set payment methods based on platform, default to card
                    // "card","apple_pay","google_pay", "bank_transfer","alipay"，"wechat_pay"
                    PaymentMethodTypes = new List<string> { "card" }
                },
                Metadata = new Dictionary<string, string>
                {
                    { "internal_user_id", createSubscriptionDto.UserId.ToString() },
                    { "price_id", createSubscriptionDto.PriceId },
                    { "quantity", (createSubscriptionDto.Quantity ?? 1).ToString() },
                    { "order_id", orderId },
                    { "platform", createSubscriptionDto.Platform ?? "android" }
                },
                TrialPeriodDays = createSubscriptionDto.TrialPeriodDays,
                Expand = new List<string> { "latest_invoice.confirmation_secret" },
            };

            if (!string.IsNullOrEmpty(createSubscriptionDto.PaymentMethodId))
            {
                options.DefaultPaymentMethod = createSubscriptionDto.PaymentMethodId;
            }

            if (createSubscriptionDto.Metadata != null && createSubscriptionDto.Metadata.Count > 0)
            {
                foreach (var item in createSubscriptionDto.Metadata)
                {
                    options.Metadata[item.Key] = item.Value;
                }
            }

            var service = new SubscriptionService(_client);
            var subscription = await service.CreateAsync(options);
            _logger.LogInformation(
                "[UserBillingGAgent][CreateSubscriptionAsync] Created subscription with ID: {SubscriptionId}, status: {Status}",
                subscription.Id, subscription.Status);
            _logger.LogDebug("[UserBillingGAgent][CreateSubscriptionAsync] subscription {0}", JsonConvert.SerializeObject(subscription));

            var paymentGrainId = Guid.NewGuid();
            var paymentGrain = GrainFactory.GetGrain<IUserPaymentGrain>(paymentGrainId);
            
            var paymentState = new UserPaymentState
            {
                Id = paymentGrainId,
                UserId = createSubscriptionDto.UserId,
                PriceId = createSubscriptionDto.PriceId,
                Amount = productConfig.Amount,
                Currency = productConfig.Currency,
                PaymentType = PaymentType.Subscription,
                Status = PaymentStatus.Processing,
                Mode = PaymentMode.SUBSCRIPTION,
                Platform = PaymentPlatform.Stripe,
                Description = createSubscriptionDto.Description ?? $"Subscription for {createSubscriptionDto.PriceId}",
                CreatedAt = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
                OrderId = orderId,
                SubscriptionId = subscription.Id,
                InvoiceId = subscription.LatestInvoiceId
            };
            
            _logger.LogDebug("[UserBillingGAgent][CreateSubscriptionAsync] InitializePaymentAsync start.. {0}", subscription.Id);
            var initResult = await paymentGrain.InitializePaymentAsync(paymentState);
            _logger.LogDebug("[UserBillingGAgent][CreateSubscriptionAsync] InitializePaymentAsync end..{0}", subscription.Id);
            if (!initResult.Success)
            {
                _logger.LogError(
                    "[UserBillingGAgent][CreateSubscriptionAsync] Failed to initialize payment grain: {ErrorMessage}",
                    initResult.Message);
                throw new Exception($"Failed to initialize payment grain: {initResult.Message}");
            }
            var paymentDetails = initResult.Data;
            _logger.LogDebug("[UserBillingGAgent][CreateSubscriptionAsync] CreateOrUpdatePaymentSummaryAsync start..{0}", subscription.Id);
            try
            {
                await CreateOrUpdatePaymentSummaryAsync(paymentDetails, null);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[UserBillingGAgent][CreateSubscriptionAsync] CreateOrUpdatePaymentSummaryAsync error..{0}, {1}", subscription.Id, e.Message);
                throw;
            }
            _logger.LogDebug("[UserBillingGAgent][CreateSubscriptionAsync] CreateOrUpdatePaymentSummaryAsync end..{0}", subscription.Id);
            _logger.LogInformation(
                "[UserBillingGAgent][CreateSubscriptionAsync] Created/Updated payment record with ID: {PaymentId} for session: {subscription}",
                paymentDetails.Id, subscription.Id);
            
            var response = new SubscriptionResponseDto
            {
                SubscriptionId = subscription.Id,
                CustomerId = customerId,
                ClientSecret = subscription.LatestInvoice?.ConfirmationSecret?.ClientSecret
            };

            return response;
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex,
                "[UserBillingGAgent][CreateSubscriptionAsync] Stripe error: {ErrorMessage}",
                ex.StripeError?.Message);
            throw new InvalidOperationException(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[UserBillingGAgent][CreateSubscriptionAsync] Error creating subscription: {ErrorMessage}",
                ex.Message);
            throw;
        }
    }

    public async Task<CancelSubscriptionResponseDto> CancelSubscriptionAsync(CancelSubscriptionDto cancelSubscriptionDto)
    {
        if (cancelSubscriptionDto.UserId == Guid.Empty)
        {
            _logger.LogWarning(
                "[UserBillingGAgent][CancelSubscriptionAsync] UserId is required. {SubscriptionId}",
                cancelSubscriptionDto.SubscriptionId);
            return new CancelSubscriptionResponseDto
            {
                Success = false,
                Message = "UserId is required.",
                SubscriptionId = cancelSubscriptionDto.SubscriptionId
            };
        }

        if (string.IsNullOrEmpty(cancelSubscriptionDto.SubscriptionId))
        {
            _logger.LogWarning(
                "[UserBillingGAgent][CancelSubscriptionAsync] SubscriptionId is required. {SubscriptionId}, {UserId}",
                cancelSubscriptionDto.SubscriptionId, cancelSubscriptionDto.UserId);
            return new CancelSubscriptionResponseDto
            {
                Success = false,
                Message = "SubscriptionId is required.",
                SubscriptionId = cancelSubscriptionDto.SubscriptionId
            };
        }
        
        _logger.LogDebug(
            "[UserBillingGAgent][CancelSubscriptionAsync] Cancelling subscription {SubscriptionId} for user {UserId}",
            cancelSubscriptionDto.SubscriptionId, cancelSubscriptionDto.UserId);

        var paymentSummary = State.PaymentHistory
            .FirstOrDefault(p => p.SubscriptionId == cancelSubscriptionDto.SubscriptionId);
        if (paymentSummary == null)
        {
            _logger.LogWarning(
                "[UserBillingGAgent][CancelSubscriptionAsync] SubscriptionId not found {SubscriptionId} for user {UserId}",
                cancelSubscriptionDto.SubscriptionId, cancelSubscriptionDto.UserId);
            return new CancelSubscriptionResponseDto
            {
                Success = false,
                Message = "SubscriptionId not found.",
                SubscriptionId = cancelSubscriptionDto.SubscriptionId
            };
        }

        if (paymentSummary.Status is PaymentStatus.Cancelled_In_Processing or PaymentStatus.Cancelled or PaymentStatus.Refunded_In_Processing or PaymentStatus.Refunded)
        {
            _logger.LogWarning(
                "[UserBillingGAgent][CancelSubscriptionAsync] Subscription canceled {SubscriptionId} for user {UserId}",
                cancelSubscriptionDto.SubscriptionId, cancelSubscriptionDto.UserId);
            return new CancelSubscriptionResponseDto
            {
                Success = false,
                Message = "Subscription canceled.",
                SubscriptionId = cancelSubscriptionDto.SubscriptionId
            };
        }
        
        try
        {
            var service = new SubscriptionService(_client);
            var options = new SubscriptionUpdateOptions
            {
                CancelAtPeriodEnd = cancelSubscriptionDto.CancelAtPeriodEnd
            };

            var subscription = await service.UpdateAsync(cancelSubscriptionDto.SubscriptionId, options);
            
            _logger.LogInformation(
                "[UserBillingGAgent][CancelSubscriptionAsync] Successfully cancelled subscription {SubscriptionId}, status: {Status}",
                subscription.Id, subscription.Status);

            RaiseEvent(new UpdatePaymentStatusLogEvent
            {
                PaymentId = paymentSummary.PaymentGrainId,
                NewStatus = PaymentStatus.Cancelled_In_Processing
            });
            await ConfirmEvents();

            _logger.LogInformation(
                "[UserBillingGAgent][CancelSubscriptionAsync] Updated payment record {SubscriptionId} status to Cancelled",
                paymentSummary.SubscriptionId);

            var response = new CancelSubscriptionResponseDto
            {
                Success = true,
                SubscriptionId = subscription.Id,
                Status = subscription.Status,
                CancelledAt = DateTime.UtcNow
            };

            return response;
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex,
                "[UserBillingGAgent][CancelSubscriptionAsync] Stripe error: {ErrorMessage}",
                ex.StripeError?.Message);
            
            return new CancelSubscriptionResponseDto
            {
                Success = false,
                Message = $"Stripe error: {ex.StripeError?.Message}",
                SubscriptionId = cancelSubscriptionDto.SubscriptionId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[UserBillingGAgent][CancelSubscriptionAsync] Error cancelling subscription: {ErrorMessage}",
                ex.Message);
            
            return new CancelSubscriptionResponseDto
            {
                Success = false,
                Message = $"Error: {ex.Message}",
                SubscriptionId = cancelSubscriptionDto.SubscriptionId
            };
        }
    }

    public Task<object> RefundedSubscriptionAsync(object obj)
    {
        throw new NotImplementedException();
    }

    public async Task ClearAllAsync()
    {
        RaiseEvent(new ClearAllLogEvent());
        await ConfirmEvents();
    }

    public async Task<bool> HandleStripeWebhookEventAsync(string jsonPayload, string stripeSignature)
    {
        _logger.LogInformation("[UserBillingGAgent][HandleStripeWebhookEventAsync] Processing Stripe webhook event");
        if (string.IsNullOrEmpty(jsonPayload) || string.IsNullOrEmpty(stripeSignature))
        {
            _logger.LogError("[UserBillingGAgent][HandleStripeWebhookEventAsync] Invalid webhook parameters");
            return false;
        }

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                jsonPayload,
                stripeSignature,
                _stripeOptions.CurrentValue.WebhookSecret
            );
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex,
                "[UserBillingGAgent][HandleStripeWebhookEventAsync] Error validating webhook: {Message}", ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[UserBillingGAgent][HandleStripeWebhookEventAsync] Unexpected error processing webhook: {Message}",
                ex.Message);
            return false;
        }

        Guid paymentGrainId;
        var (_, orderId, _) = await ExtractBusinessDataAsync(stripeEvent);
        var existingPaymentSummary = State.PaymentHistory.FirstOrDefault(p => p.OrderId == orderId);
        if (existingPaymentSummary != null)
        {
            paymentGrainId = existingPaymentSummary.PaymentGrainId;
        }
        else
        {
            paymentGrainId = Guid.NewGuid();
        }
        var paymentGrain = GrainFactory.GetGrain<IUserPaymentGrain>(paymentGrainId);
        var grainResultDto = await paymentGrain.ProcessPaymentCallbackAsync(jsonPayload, stripeSignature);
        var detailsDto = grainResultDto.Data;
        if (!grainResultDto.Success || detailsDto == null)
        {
            _logger.LogError("[UserBillingGAgent][HandleStripeWebhookEventAsync] error. {0}, {1}",
                this.GetPrimaryKey(), grainResultDto.Message);
            return false;
        }

        try
        {
            // Report payment success to Google Analytics
            var analyticsGrain = GrainFactory.GetGrain<IPaymentAnalyticsGrain>("payment-analytics"+PaymentPlatform.Stripe);
            var analyticsResult = await analyticsGrain.ReportPaymentSuccessAsync(
                PaymentPlatform.Stripe,
                detailsDto.InvoiceId,
                detailsDto.UserId.ToString()
            );

            if (analyticsResult.IsSuccess)
            {
                _logger.LogInformation($"[UserBillingGAgent][HandleStripeWebhookEventAsync] Successfully reported payment analytics for order {detailsDto.InvoiceId}, user {detailsDto.UserId}");
            }
            else
            {
                _logger.LogWarning($"[UserBillingGAgent][HandleStripeWebhookEventAsync] Failed to report payment analytics for order {detailsDto.InvoiceId}, user {detailsDto.UserId}: {analyticsResult.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[UserBillingGAgent][HandleStripeWebhookEventAsync] Exception while reporting payment analytics for order {A}, user {B}",
                detailsDto.InvoiceId, detailsDto.UserId);
            // Don't throw - analytics reporting shouldn't block payment processing
        }
        
        var paymentSummary = await CreateOrUpdatePaymentSummaryAsync(detailsDto, null);
        
        var userId = detailsDto.UserId;
        var productConfig = await GetProductConfigAsync(detailsDto.PriceId);
        var userQuotaGAgent = GrainFactory.GetGrain<IUserQuotaGAgent>(userId);
        _logger.LogDebug("[UserBillingGAgent][HandleStripeWebhookEventAsync] allocate resource {0}, {1}, {2}, {3})",
            userId, detailsDto.OrderId, detailsDto.SubscriptionId, detailsDto.InvoiceId);
        var subscriptionInfoDto = await userQuotaGAgent.GetSubscriptionAsync(productConfig.IsUltimate);

        var subscriptionIds = subscriptionInfoDto.SubscriptionIds ?? new List<string>();
        var invoiceIds = subscriptionInfoDto.InvoiceIds ?? new List<string>();
        var invoiceDetail = paymentSummary.InvoiceDetails.LastOrDefault();
        if (invoiceDetail != null && invoiceDetail.Status == PaymentStatus.Completed && !invoiceIds.Contains(invoiceDetail.InvoiceId))
        {
            _logger.LogDebug("[UserBillingGAgent][HandleStripeWebhookEventAsync] Update for complete invoice {0}, {1}, {2}",
                userId, paymentSummary.SubscriptionId, invoiceDetail.InvoiceId);
            foreach (var subscriptionId in subscriptionIds.Where(subscriptionId => subscriptionId != paymentSummary.SubscriptionId))
            {
                await CancelSubscriptionAsync(new CancelSubscriptionDto
                {
                    UserId = userId,
                    SubscriptionId = subscriptionId,
                    CancellationReason = $"Upgrade to a new subscription {paymentSummary.SubscriptionId}",
                    CancelAtPeriodEnd = true
                });
            }
            
            subscriptionIds.Clear();
            subscriptionIds.Add(paymentSummary.SubscriptionId);
            invoiceIds.Add(invoiceDetail.InvoiceId);

            if (subscriptionInfoDto.IsActive)
            {
                if (SubscriptionHelper.GetPlanTypeLogicalOrder(subscriptionInfoDto.PlanType) <= SubscriptionHelper.GetPlanTypeLogicalOrder((PlanType) productConfig.PlanType))
                {
                    subscriptionInfoDto.PlanType = (PlanType) productConfig.PlanType;
                }
                subscriptionInfoDto.EndDate =
                    GetSubscriptionEndDate(subscriptionInfoDto.PlanType, subscriptionInfoDto.EndDate);
            }
            else
            {
                subscriptionInfoDto.IsActive = true;
                subscriptionInfoDto.PlanType = (PlanType) productConfig.PlanType;
                subscriptionInfoDto.StartDate = DateTime.UtcNow;
                subscriptionInfoDto.EndDate =
                    GetSubscriptionEndDate(subscriptionInfoDto.PlanType, subscriptionInfoDto.StartDate);
                await userQuotaGAgent.ResetRateLimitsAsync();
            }
            subscriptionInfoDto.Status = PaymentStatus.Completed;
            subscriptionInfoDto.SubscriptionIds = subscriptionIds;
            subscriptionInfoDto.InvoiceIds = invoiceIds;
            await userQuotaGAgent.UpdateSubscriptionAsync(subscriptionInfoDto, productConfig.IsUltimate);

            if (productConfig.IsUltimate)
            {
                var premiumSubscription = await userQuotaGAgent.GetSubscriptionAsync(false);
                if (!premiumSubscription.SubscriptionIds.IsNullOrEmpty())
                {
                    foreach (var subscriptionId in premiumSubscription.SubscriptionIds)
                    {
                        await CancelSubscriptionAsync(new CancelSubscriptionDto
                        {
                            UserId = userId,
                            SubscriptionId = subscriptionId,
                            CancellationReason = $"Upgrade to a new subscription {paymentSummary.SubscriptionId}",
                            CancelAtPeriodEnd = true
                        });
                    }
                }
                if (premiumSubscription.IsActive)
                {
                    premiumSubscription.StartDate =
                        GetSubscriptionEndDate((PlanType)productConfig.PlanType, premiumSubscription.StartDate);
                    premiumSubscription.EndDate =
                        GetSubscriptionEndDate((PlanType)productConfig.PlanType, premiumSubscription.EndDate);
                    await userQuotaGAgent.UpdateSubscriptionAsync(premiumSubscription);
                }
            }
            
            //Invite users to pay rewards
            await ProcessInviteeSubscriptionAsync(userId, (PlanType) productConfig.PlanType, productConfig.IsUltimate, invoiceDetail.InvoiceId);
            _logger.LogWarning("[UserBillingGAgent][HandleStripeWebhookEventAsync] Process invitee subscription completed, user {UserId}",
                userId);
            
        } else if (invoiceDetail != null && invoiceDetail.Status == PaymentStatus.Cancelled && subscriptionIds.Contains(paymentSummary.SubscriptionId))
        {
            _logger.LogDebug("[UserBillingGAgent][HandleStripeWebhookEventAsync] Cancel User subscription {0}, {1}, {2}",
                userId, paymentSummary.SubscriptionId, invoiceDetail.InvoiceId);
            subscriptionIds.Remove(paymentSummary.SubscriptionId);
            subscriptionInfoDto.SubscriptionIds = subscriptionIds;
            await userQuotaGAgent.UpdateSubscriptionAsync(subscriptionInfoDto, productConfig.IsUltimate);
        }
        else if (invoiceDetail != null && invoiceDetail.Status == PaymentStatus.Refunded && invoiceIds.Contains(invoiceDetail.InvoiceId))
        {
            _logger.LogDebug("[UserBillingGAgent][HandleStripeWebhookEventAsync] Refund User subscription {0}, {1}, {2}",
                userId, paymentSummary.SubscriptionId, invoiceDetail.InvoiceId);
            
            var diff = GetDaysForPlanType(paymentSummary.PlanType);
            subscriptionInfoDto.EndDate = subscriptionInfoDto.EndDate.AddDays(-diff);
            subscriptionIds.Remove(paymentSummary.SubscriptionId);
            
            //reset plantype
            subscriptionInfoDto.PlanType = await GetMaxPlanTypeAsync(DateTime.UtcNow, productConfig.IsUltimate);
            
            subscriptionInfoDto.SubscriptionIds = subscriptionIds;
            await userQuotaGAgent.UpdateSubscriptionAsync(subscriptionInfoDto, productConfig.IsUltimate);

            if (productConfig.IsUltimate)
            {
                var diffTimeSpan = (invoiceDetail.SubscriptionEndDate - DateTime.UtcNow);
                if (diffTimeSpan.TotalMilliseconds > 0)
                {
                    var premiumSubscription = await userQuotaGAgent.GetSubscriptionAsync();
                    if (premiumSubscription.IsActive)
                    {
                        premiumSubscription.StartDate = premiumSubscription.StartDate.Add(- diffTimeSpan);
                        premiumSubscription.EndDate = premiumSubscription.EndDate.Add(- diffTimeSpan);
                        await userQuotaGAgent.UpdateSubscriptionAsync(premiumSubscription);
                    }
                }
            }
        }
        
        return true;
    }

    private async Task<Tuple<DateTime, DateTime>> CalculateSubscriptionDurationAsync(Guid userId, StripeProduct productConfig)
    {
        DateTime subscriptionStartDate;
        DateTime subscriptionEndDate;
        var userQuotaGAgent = GrainFactory.GetGrain<IUserQuotaGAgent>(userId);
        
        // Use unified subscription interface
        var subscription = await userQuotaGAgent.GetAndSetSubscriptionAsync(productConfig.IsUltimate);
        var targetPlanType = (PlanType)productConfig.PlanType;
        
        if (subscription.IsActive)
        {
            subscriptionStartDate = subscription.EndDate;
        }
        else
        {
            // No active subscription, start fresh
            subscriptionStartDate = DateTime.UtcNow;
        }
        
        subscriptionEndDate = GetSubscriptionEndDate(targetPlanType, subscriptionStartDate);
        return new Tuple<DateTime, DateTime>(subscriptionStartDate, subscriptionEndDate);
    }
    
    private async Task<Tuple<DateTime, DateTime>> CalculateSubscriptionDurationAsync(Guid userId, PlanType planType, bool ultimate)
    {
        DateTime subscriptionStartDate;
        DateTime subscriptionEndDate;
        var userQuotaGAgent = GrainFactory.GetGrain<IUserQuotaGAgent>(userId);
        var subscriptionInfoDto = await userQuotaGAgent.GetSubscriptionAsync(ultimate);
        if (subscriptionInfoDto.IsActive)
        {
            subscriptionStartDate = subscriptionInfoDto.EndDate;
        }
        else
        {
            subscriptionStartDate = DateTime.UtcNow;
        }
        subscriptionEndDate = GetSubscriptionEndDate(planType, subscriptionStartDate);
        return new Tuple<DateTime, DateTime>(subscriptionStartDate, subscriptionEndDate);
    }

    public async Task<Guid> AddPaymentRecordAsync(ChatManager.UserBilling.PaymentSummary paymentSummary)
    {
        _logger.LogInformation(
            "[UserBillingGAgent][AddPaymentRecordAsync] Adding payment record with status {Status} for amount {Amount} {Currency}",
            paymentSummary.Status, paymentSummary.Amount, paymentSummary.Currency);

        // Ensure PaymentGrainId is generated if not set
        if (paymentSummary.PaymentGrainId == Guid.Empty)
        {
            paymentSummary.PaymentGrainId = Guid.NewGuid();
        }

        // Set created time if not set
        if (paymentSummary.CreatedAt == default)
        {
            paymentSummary.CreatedAt = DateTime.UtcNow;
        }

        // Set completed time if status is Completed
        if (paymentSummary.Status == PaymentStatus.Completed && !paymentSummary.CompletedAt.HasValue)
        {
            paymentSummary.CompletedAt = DateTime.UtcNow;
        }

        RaiseEvent(new AddPaymentLogEvent
        {
            PaymentSummary = paymentSummary
        });
        await ConfirmEvents();

        _logger.LogInformation("[UserBillingGAgent][AddPaymentRecordAsync] Payment record added with ID: {PaymentId}",
            paymentSummary.PaymentGrainId);

        return paymentSummary.PaymentGrainId;
    }

    public async Task<ChatManager.UserBilling.PaymentSummary> GetPaymentSummaryAsync(Guid paymentId)
    {
        _logger.LogInformation(
            "[UserBillingGAgent][GetPaymentSummaryAsync] Getting payment summary for payment ID: {PaymentId}",
            paymentId);

        var payment = State.PaymentHistory.FirstOrDefault(p => p.PaymentGrainId == paymentId);
        if (payment == null)
        {
            _logger.LogWarning("[UserBillingGAgent][GetPaymentSummaryAsync] Payment with ID {PaymentId} not found",
                paymentId);
            return null;
        }

        return payment;
    }

    public async Task<List<ChatManager.UserBilling.PaymentSummary>> GetPaymentHistoryAsync(int page = 1, int pageSize = 10)
    {
        _logger.LogInformation(
            "[UserBillingGAgent][GetPaymentHistoryAsync] Getting payment history page {Page} with size {PageSize}",
            page, pageSize);

        // Ensure valid pagination parameters
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        if (State.PaymentHistory == null)
        {
            State.PaymentHistory = new List<ChatManager.UserBilling.PaymentSummary>();
        }
        
        //Filter unpaid orders
        var originalCount = State.PaymentHistory.Count;
        var recordsToRemove = State.PaymentHistory
            .Where(payment => 
                payment.InvoiceDetails.IsNullOrEmpty() && 
                payment.Status == PaymentStatus.Processing && 
                payment.CreatedAt <= DateTime.UtcNow.AddDays(-1))
            .ToList();
            
        if (recordsToRemove.Count > 0)
        {
            _logger.LogInformation(
                "[UserBillingGAgent][GetPaymentHistoryAsync] Removed {0} invalid records from payment history (original count: {1}, new count: {2})",
                recordsToRemove.Count, originalCount, State.PaymentHistory.Count);
            RaiseEvent(new RemovePaymentHistoryLogEvent
            {
                RecordsToRemove = recordsToRemove
            });
            await ConfirmEvents();
        }

        // Calculate skip and take values
        int skip = (page - 1) * pageSize;

        var paymentHistories = new List<ChatManager.UserBilling.PaymentSummary>();
        var paymentSummaries = State.PaymentHistory;
        foreach (var paymentSummary in paymentSummaries)
        {
            if (paymentSummary.MembershipLevel.IsNullOrWhiteSpace())
            {
                var membershipLevel = MembershipLevel.Membership_Level_Premium;
                try
                {
                    if (paymentSummary.Platform == PaymentPlatform.AppStore)
                    {
                        var productConfig = await GetAppleProductConfigAsync(paymentSummary.PriceId);
                        membershipLevel = SubscriptionHelper.GetMembershipLevel(productConfig.IsUltimate);
                    }
                    else
                    {
                        var productConfig = await GetProductConfigAsync(paymentSummary.PriceId);
                        membershipLevel = SubscriptionHelper.GetMembershipLevel(productConfig.IsUltimate);
                    }
                }
                catch (ArgumentException e)
                {
                    _logger.LogWarning("[UserBillingGAgent][GetPaymentHistoryAsync] {0}. {1}", e.Message, paymentSummary.PriceId);
                }
                paymentSummary.MembershipLevel = membershipLevel;
            }
            
            if (paymentSummary.InvoiceDetails.IsNullOrEmpty())
            {
                paymentHistories.Add(paymentSummary);
            }
            else
            {
                paymentHistories.AddRange(paymentSummary.InvoiceDetails.Select(invoiceDetail => new ChatManager.UserBilling.PaymentSummary
                {
                    PaymentGrainId = paymentSummary.PaymentGrainId,
                    OrderId = paymentSummary.OrderId,
                    PlanType = invoiceDetail.PlanType == PlanType.None ?  paymentSummary.PlanType : invoiceDetail.PlanType,
                    Amount = invoiceDetail.Amount == null ? paymentSummary.Amount : (decimal) invoiceDetail.Amount,
                    Currency = paymentSummary.Currency,
                    CreatedAt = invoiceDetail.CreatedAt,
                    CompletedAt = invoiceDetail.CompletedAt,
                    Status = invoiceDetail.Status,
                    SubscriptionId = paymentSummary.SubscriptionId,
                    SubscriptionStartDate = invoiceDetail.SubscriptionStartDate,
                    SubscriptionEndDate = invoiceDetail.SubscriptionEndDate,
                    UserId = paymentSummary.UserId,
                    PriceId = invoiceDetail.PriceId.IsNullOrWhiteSpace() ? paymentSummary.PriceId : invoiceDetail.PriceId,
                    Platform = paymentSummary.Platform,
                    MembershipLevel = invoiceDetail.MembershipLevel.IsNullOrWhiteSpace() ? paymentSummary.MembershipLevel : invoiceDetail.MembershipLevel
                }));
            }
        }

        _logger.LogInformation(
            "[UserBillingGAgent][GetPaymentHistoryAsync] Returning {0} payment records after pagination",
            Math.Min(pageSize, paymentHistories.Count - skip));

        // Return paginated results ordered by most recent first
        return paymentHistories.Where(t => t.Status != PaymentStatus.Processing)
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
            .ToList();
    }

    public async Task<bool> UpdatePaymentStatusAsync(ChatManager.UserBilling.PaymentSummary payment, PaymentStatus newStatus)
    {
        _logger.LogInformation(
            "[UserBillingGAgent][UpdatePaymentStatusAsync] Updating payment status for ID {PaymentId} to {NewStatus}",
            payment.PaymentGrainId, newStatus);

        // Skip update if status is already the same
        if (payment.Status == newStatus)
        {
            _logger.LogInformation(
                "[UserBillingGAgent][UpdatePaymentStatusAsync] Payment {PaymentId} already has status {Status}",
                payment.PaymentGrainId, newStatus);
            return true;
        }

        RaiseEvent(new UpdatePaymentStatusLogEvent
        {
            PaymentId = payment.PaymentGrainId,
            NewStatus = newStatus
        });
        await ConfirmEvents();

        var oldStatus = payment.Status;
    
        _logger.LogInformation(
            "[UserBillingGAgent][UpdatePaymentStatusAsync] Payment status updated from {OldStatus} to {NewStatus} for ID {PaymentId}",
            oldStatus, newStatus, payment.PaymentGrainId);

        return true;
    }

    private async Task<PaymentDetailsDto> InitializePaymentGrainAsync(
        string orderId,
        CreateCheckoutSessionDto createCheckoutSessionDto,
        StripeProduct productConfig,
        Session session)
    {
        var paymentGrainId = Guid.NewGuid();
        var paymentGrain = GrainFactory.GetGrain<IUserPaymentGrain>(paymentGrainId);

        var paymentState = new UserPaymentState
        {
            Id = paymentGrainId,
            UserId = Guid.Parse(createCheckoutSessionDto.UserId),
            PriceId = createCheckoutSessionDto.PriceId,
            Amount = productConfig.Amount,
            Currency = productConfig.Currency,
            PaymentType = productConfig.Mode == PaymentMode.SUBSCRIPTION
                ? PaymentType.Subscription
                : PaymentType.OneTime,
            Status = PaymentStatus.Processing,
            Mode = createCheckoutSessionDto.Mode,
            Platform = PaymentPlatform.Stripe,
            Description = $"Checkout session for {productConfig.PriceId}",
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            OrderId = orderId,
            SubscriptionId = session.SubscriptionId,
            InvoiceId = null
        };

        var initResult = await paymentGrain.InitializePaymentAsync(paymentState);
        if (!initResult.Success)
        {
            _logger.LogError(
                "[UserBillingGAgent][InitializePaymentGrainAsync] Failed to initialize payment grain: {ErrorMessage}",
                initResult.Message);
            throw new Exception($"Failed to initialize payment grain: {initResult.Message}");
        }

        var paymentDetails = initResult.Data;
        _logger.LogInformation(
            "[UserBillingGAgent][InitializePaymentGrainAsync] Initialized payment grain with ID: {PaymentId}",
            paymentDetails.Id);

        return paymentDetails;
    }
    
    private async Task<ChatManager.UserBilling.PaymentSummary> CreateOrUpdatePaymentSummaryAsync(PaymentDetailsDto paymentDetails, Session session = null)
    {
        var existingPaymentSummary = State.PaymentHistory.FirstOrDefault(p => p.PaymentGrainId == paymentDetails.Id);
        var productConfig = await GetProductConfigAsync(paymentDetails.PriceId);

        if (existingPaymentSummary != null)
        {
            _logger.LogInformation(
                "[UserBillingGAgent][CreateOrUpdatePaymentSummaryAsync] Updating existing payment record with ID: {PaymentId}",
                existingPaymentSummary.PaymentGrainId);

            existingPaymentSummary.PaymentGrainId = paymentDetails.Id;
            existingPaymentSummary.OrderId = paymentDetails.OrderId;
            existingPaymentSummary.UserId = paymentDetails.UserId;
            existingPaymentSummary.PriceId = paymentDetails.PriceId;
            existingPaymentSummary.PlanType = (PlanType)productConfig.PlanType;
            existingPaymentSummary.MembershipLevel = SubscriptionHelper.GetMembershipLevel(productConfig.IsUltimate);
            existingPaymentSummary.Amount = productConfig.Amount;
            existingPaymentSummary.Currency = productConfig.Currency;
            existingPaymentSummary.Status = paymentDetails.Status;
            existingPaymentSummary.SubscriptionId = paymentDetails.SubscriptionId;
            await CreateOrUpdateInvoiceDetailAsync(paymentDetails, productConfig, existingPaymentSummary);
            
            RaiseEvent(new UpdatePaymentLogEvent
            {
                PaymentId = existingPaymentSummary.PaymentGrainId,
                PaymentSummary = existingPaymentSummary
            });
            await ConfirmEvents();  

            _logger.LogInformation(
                "[UserBillingGAgent][CreateOrUpdatePaymentSummaryAsync] Updated payment record with ID: {PaymentId}",
                existingPaymentSummary.PaymentGrainId);

            return existingPaymentSummary;
        }
        else
        {
            _logger.LogInformation(
                "[UserBillingGAgent][CreateOrUpdatePaymentSummaryAsync] Creating new payment record for ID: {PaymentId}",
                paymentDetails.Id);

            var newPaymentSummary = new ChatManager.UserBilling.PaymentSummary
            {
                PaymentGrainId = paymentDetails.Id,
                OrderId = paymentDetails.OrderId,
                UserId = paymentDetails.UserId,
                PriceId = paymentDetails.PriceId,
                PlanType = (PlanType)productConfig.PlanType,
                MembershipLevel = SubscriptionHelper.GetMembershipLevel(productConfig.IsUltimate),
                Amount = productConfig.Amount,
                Currency = productConfig.Currency,
                CreatedAt = paymentDetails.CreatedAt,
                Status = paymentDetails.Status,
                SubscriptionId = paymentDetails.SubscriptionId
            };
            await CreateOrUpdateInvoiceDetailAsync(paymentDetails, productConfig, newPaymentSummary);
            await AddPaymentRecordAsync(newPaymentSummary);
            _logger.LogInformation(
                "[UserBillingGAgent][CreateOrUpdatePaymentSummaryAsync] Created new payment record with ID: {PaymentId}",
                newPaymentSummary.PaymentGrainId);

            return newPaymentSummary;
        }
    }

    private async Task CreateOrUpdateInvoiceDetailAsync(PaymentDetailsDto paymentDetails, StripeProduct productConfig,
        ChatManager.UserBilling.PaymentSummary paymentSummary)
    {
        if (paymentDetails.InvoiceId.IsNullOrWhiteSpace())
        { 
            return;
        }

        if (paymentSummary.InvoiceDetails == null)
        {
            paymentSummary.InvoiceDetails = new List<ChatManager.UserBilling.UserBillingInvoiceDetail>();
        }
        var invoiceDetail =
            paymentSummary.InvoiceDetails.FirstOrDefault(t => t.InvoiceId == paymentDetails.InvoiceId);
        if (invoiceDetail == null)
        {
            invoiceDetail = new ChatManager.UserBilling.UserBillingInvoiceDetail
            {
                InvoiceId = paymentDetails.InvoiceId,
                CreatedAt = paymentDetails.CreatedAt,
                Status = paymentDetails.Status
            };
            if (paymentDetails.Status == PaymentStatus.Completed)
            {
                invoiceDetail.CompletedAt = paymentDetails.CompletedAt ?? DateTime.UtcNow;
                var (subscriptionStartDate, subscriptionEndDate) =
                    await CalculateSubscriptionDurationAsync(paymentDetails.UserId, productConfig);
                invoiceDetail.SubscriptionStartDate = subscriptionStartDate;
                invoiceDetail.SubscriptionEndDate = subscriptionEndDate;
            }
            paymentSummary.InvoiceDetails.Add(invoiceDetail);
        }
        else
        {
            invoiceDetail.Status = paymentDetails.Status;
            if (paymentDetails.Status == PaymentStatus.Completed)
            {
                invoiceDetail.CompletedAt = paymentDetails.CompletedAt ?? DateTime.UtcNow;
            }
            if (paymentDetails.Status == PaymentStatus.Completed && invoiceDetail.SubscriptionStartDate == default)
            {
                var (subscriptionStartDate, subscriptionEndDate) = await CalculateSubscriptionDurationAsync(paymentDetails.UserId, productConfig);
                invoiceDetail.SubscriptionStartDate = subscriptionStartDate;
                invoiceDetail.SubscriptionEndDate = subscriptionEndDate;
            }
        }
    }


    private async Task ValidateSubscriptionUpgradePath(string userId, StripeProduct productConfig)
    {
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var parsedUserId))
        {
            _logger.LogWarning(
                "[UserBillingGAgent][ValidateSubscriptionUpgradePath] Invalid or missing UserId, skipping subscription path validation");
            return;
        }

        var userQuotaGAgent = GrainFactory.GetGrain<IUserQuotaGAgent>(parsedUserId);
        var currentSubscription = await userQuotaGAgent.GetSubscriptionAsync(productConfig.IsUltimate);

        if (!currentSubscription.IsActive)
        {
            _logger.LogInformation(
                "[UserBillingGAgent][ValidateSubscriptionUpgradePath] User has no active subscription, allowing purchase of any plan");
            return;
        }

        var currentPlanType = currentSubscription.PlanType;
        var targetPlanType = (PlanType)productConfig.PlanType;

        _logger.LogInformation(
            "[UserBillingGAgent][ValidateSubscriptionUpgradePath] Validating upgrade path: Current={CurrentPlan}, Target={TargetPlan}",
            currentPlanType, targetPlanType);

        // Use SubscriptionHelper to validate upgrade path
        if (!SubscriptionHelper.IsUpgradePathValid(currentPlanType, targetPlanType))
        {
            var currentPlanName = SubscriptionHelper.GetPlanDisplayName(currentPlanType);
            var targetPlanName = SubscriptionHelper.GetPlanDisplayName(targetPlanType);
            
            _logger.LogWarning(
                "[UserBillingGAgent][ValidateSubscriptionUpgradePath] Invalid upgrade path: {CurrentPlan} -> {TargetPlan}",
                currentPlanName, targetPlanName);
            
            throw new InvalidOperationException(
                $"Invalid upgrade path: {currentPlanName} users cannot downgrade or purchase incompatible plans. Target: {targetPlanName}");
        }

        _logger.LogInformation(
            "[UserBillingGAgent][ValidateSubscriptionUpgradePath] Valid upgrade path: {CurrentPlan} -> {NewPlan}",
            currentPlanType, targetPlanType);
    }

    private async Task<StripeProduct> GetProductConfigAsync(string priceId)
    {
        var productConfig = _stripeOptions.CurrentValue.Products.FirstOrDefault(p => p.PriceId == priceId);
        if (productConfig == null)
        {
            _logger.LogError(
                "[UserBillingGAgent][GetProductConfigAsync] Invalid priceId: {PriceId}. Product not found in configuration.",
                priceId);
            throw new ArgumentException($"Invalid priceId: {priceId}. Product not found in configuration.");
        }

        _logger.LogInformation(
            "[UserBillingGAgent][GetProductConfigAsync] Found product with priceId: {PriceId}, planType: {PlanType}, amount: {Amount} {Currency}",
            productConfig.PriceId, productConfig.PlanType, productConfig.Amount, productConfig.Currency);

        return productConfig;
    }
    
    private async Task<AppleProduct> GetAppleProductConfigAsync(string productId)
    {
        var productConfig = _appleOptions.CurrentValue.Products.FirstOrDefault(p => p.ProductId == productId);
        if (productConfig == null)
        {
            _logger.LogError(
                "[UserBillingGAgent][GetAppleProductConfigAsync] Invalid ProductId: {ProductId}. Product not found in configuration.",
                productId);
            throw new ArgumentException($"Invalid ProductId: {productId}. Product not found in configuration.");
        }

        _logger.LogInformation(
            "[UserBillingGAgent][GetAppleProductConfigAsync] Found product with ProductId: {ProductId}, planType: {PlanType}, amount: {Amount} {Currency}",
            productConfig.ProductId, productConfig.PlanType, productConfig.Amount, productConfig.Currency);

        return productConfig;
    }

    private DateTime GetSubscriptionEndDate(PlanType planType, DateTime startDate)
    {
        var endDate = startDate;
        switch (planType)
        {
            case PlanType.Day:
                return endDate.AddDays(1);
            case PlanType.Week:
                return endDate.AddDays(7);
            case PlanType.Month:
                return endDate.AddDays(30);
            case PlanType.Year:
                return endDate.AddDays(390);
            default:
                throw new ArgumentException($"Invalid plan type: {planType}");
        }
    }

    private int GetDaysForPlanType(PlanType planType)
    {
        // Use SubscriptionHelper for consistent days calculation with Ultimate support and historical compatibility
        return SubscriptionHelper.GetDaysForPlanType(planType);
    }

    private async Task<Tuple<string, string, string>> ExtractBusinessDataAsync(Event stripeEvent)
    {
        var userId = string.Empty;
        var orderId = string.Empty;
        var priceId = string.Empty;
        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
            {
                var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
                userId = TryGetFromMetadata(session.Metadata, "internal_user_id");
                orderId = TryGetFromMetadata(session.Metadata, "order_id");
                priceId = TryGetFromMetadata(session.Metadata, "price_id");
                break;
            }
            case "invoice.paid":
            case "invoice.payment_failed" :
            {
                var invoice = stripeEvent.Data.Object as Stripe.Invoice;
                userId = TryGetFromMetadata(invoice?.Parent?.SubscriptionDetails?.Metadata, "internal_user_id");
                orderId = TryGetFromMetadata(invoice?.Parent?.SubscriptionDetails?.Metadata, "order_id");
                priceId = TryGetFromMetadata(invoice?.Parent?.SubscriptionDetails?.Metadata, "price_id");
                break;
            }
            case "customer.subscription.deleted":
            case "customer.subscription.updated":
            {
                var subscription = stripeEvent.Data.Object as Stripe.Subscription;
                userId = TryGetFromMetadata(subscription.Metadata, "internal_user_id");
                orderId = TryGetFromMetadata(subscription.Metadata, "order_id");
                priceId = TryGetFromMetadata(subscription.Metadata, "price_id");
                if (userId.IsNullOrWhiteSpace())
                {
                    var paymentSummary = State.PaymentHistory.FirstOrDefault(t => t.SubscriptionId == subscription.Id);
                    if (paymentSummary != null)
                    {
                        userId = paymentSummary.UserId.ToString();
                        orderId = paymentSummary.OrderId;
                        priceId = paymentSummary.PriceId;
                    }
                }
                break;
            }
            case "charge.refunded":
                var charge = stripeEvent.Data.Object as Stripe.Charge;
                userId = TryGetFromMetadata(charge.Metadata, "internal_user_id");
                orderId = TryGetFromMetadata(charge.Metadata, "order_id");
                priceId = TryGetFromMetadata(charge.Metadata, "price_id");
                if (userId.IsNullOrWhiteSpace())
                {
                    var paymentIntentService = new PaymentIntentService(_client);
                    var paymentIntent = paymentIntentService.Get(charge.PaymentIntentId);
                    userId = TryGetFromMetadata(paymentIntent.Metadata, "internal_user_id");
                    orderId = TryGetFromMetadata(paymentIntent.Metadata, "order_id");
                    priceId = TryGetFromMetadata(paymentIntent.Metadata, "price_id");
                }
                break;
            default:
                userId = string.Empty;
                orderId = string.Empty;
                priceId = string.Empty;
                break;
        }

        if (userId.IsNullOrWhiteSpace() || orderId.IsNullOrWhiteSpace() || priceId.IsNullOrWhiteSpace())
        {
            _logger.LogWarning(
                "[UserBillingGAgent][ExtractBusinessDataAsync] Type={0}-{1}, userId={2}, orderId={3}, priceId={4},",
                stripeEvent.Type, stripeEvent.Id, userId, orderId, priceId);
            throw new ArgumentException(
                $"Business Data not found in StripeEvent. {stripeEvent.Type}, {stripeEvent.Id}");
        }

        return new Tuple<string, string, string>(userId, orderId, priceId);
    }

    private string TryGetFromMetadata(IDictionary<string, string> metadata, string key)
    {
        if (metadata != null && metadata.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
        {
            return value;
        }

        return string.Empty;
    }

    public async Task<bool> HandleAppStoreNotificationAsync(Guid userId, string jsonPayload)
    {
        try
        {
            // 1. Parse V2 format notification
            _logger.LogDebug("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId} Received notification payload", 
                userId.ToString());
            
            var notificationV2 = JsonConvert.DeserializeObject<AppStoreServerNotificationV2>(jsonPayload);
            if (notificationV2?.SignedPayload == null)
            {
                _logger.LogWarning("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId} Invalid notification format - missing SignedPayload",
                    userId.ToString());
                return false;
            }
                
            _logger.LogDebug("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId} Received V2 format notification", userId.ToString());
                
            // 2. First decode the JWT payload (without verification) to get environment info
            ResponseBodyV2DecodedPayload decodedPayload = null;
            try
            {
                decodedPayload = AppStoreHelper.DecodeV2Payload(notificationV2.SignedPayload);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId} Error decoding payload: {Error}", 
                    userId.ToString() ,e.Message);
            }
            
            if (decodedPayload == null)
            {
                _logger.LogWarning("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId} Failed to decode V2 payload", userId.ToString());
                return false;
            }
            
            var environment = decodedPayload.Data.Environment;
            _logger.LogInformation("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId} Notification environment: {Environment}",
                userId.ToString(), environment);
                
            // 3. Verify JWT signature authenticity using the correct environment
            if (!VerifyJwtSignature(notificationV2.SignedPayload, environment))
            {
                _logger.LogWarning("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId} Invalid JWT signature for {Environment} environment", 
                    userId, environment);
                return false;
            }
                
            // 4. Extract notification details from decoded payload
            var notificationType = decodedPayload.NotificationType;
            var subtype = decodedPayload.Subtype ?? string.Empty;
                
            // 5. Extract transaction info from decoded payload
            var (appStoreTransactionInfo, signedTransactionInfo, signedRenewalInfo) = ExtractTransactionInfoFromV2(decodedPayload);
            if (appStoreTransactionInfo == null)
            {
                _logger.LogWarning("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId} Failed to extract transaction info from payload", userId.ToString());
                return false;
            }

            // 7. Log notification information
            _logger.LogDebug("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId} Received notification type: {Type}, subtype: {Subtype}, environment: {Env}", 
                userId.ToString(), notificationType, subtype, environment);
            
            // 8. Parse notification type and subtype to enums
            AppStoreNotificationType notificationTypeEnum;
            if (!Enum.TryParse(notificationType, out notificationTypeEnum))
            {
                notificationTypeEnum = AppStoreNotificationType.UNKNOWN;
            }

            // Parse notification subtype
            AppStoreNotificationSubtype subtypeEnum;
            if (!Enum.TryParse(subtype, out subtypeEnum))
            {
                subtypeEnum = AppStoreNotificationSubtype.NONE;
            }

            _logger.LogInformation("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId} Processing notification type: {Type}, subtype: {SubType}, transactionId: {Transaction}, originalTransaction: {OriginalTransactionId}",
                userId.ToString(), notificationTypeEnum, subtypeEnum, signedTransactionInfo.TransactionId, signedTransactionInfo.OriginalTransactionId);

            // 10. Process based on notification type and subtype
            switch (notificationTypeEnum)
            {
                case AppStoreNotificationType.SUBSCRIBED:
                    // Handle new subscription
                    if (subtypeEnum == AppStoreNotificationSubtype.INITIAL_BUY)
                    {
                        _logger.LogInformation("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId}, {transactionId}, Initial subscription purchase",
                            userId.ToString(), signedTransactionInfo.TransactionId);
                    }
                    else if (subtypeEnum == AppStoreNotificationSubtype.RESUBSCRIBE)
                    {
                        _logger.LogInformation("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId}, {transactionId}, Resubscription to same or different subscription in group",
                        userId.ToString(), signedTransactionInfo.TransactionId);
                    }
                    await HandleDidRenewAsync(userId, signedTransactionInfo, signedRenewalInfo);
                    _logger.LogInformation(
                        "[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId}, {transactionId}, Subscribed",
                        userId.ToString(), signedTransactionInfo.TransactionId);
                    //Report payment success to Google Analytics for completed payments
                    _ = ReportPaymentSuccessAsync(userId, signedTransactionInfo.TransactionId);
                    break;
                case AppStoreNotificationType.DID_RENEW:
                    // Handle successful renewal
                    await HandleDidRenewAsync(userId, signedTransactionInfo, signedRenewalInfo);
                    _logger.LogInformation(
                        "[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId}, {transactionId}, Renewed",
                        userId.ToString(), signedTransactionInfo.TransactionId);

                    //Report payment success to Google Analytics for completed payments
                    _ = ReportPaymentSuccessAsync(userId, signedTransactionInfo.TransactionId);

                    break;
                case AppStoreNotificationType.DID_CHANGE_RENEWAL_STATUS:
                    // Handle auto-renewal status changes
                    switch (subtypeEnum)
                    {
                        case AppStoreNotificationSubtype.AUTO_RENEW_ENABLED:
                            _logger.LogInformation("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId}, {transactionId}, Auto-renewal enabled",
                                userId.ToString(), signedTransactionInfo.TransactionId);
                            break;
                        case AppStoreNotificationSubtype.AUTO_RENEW_DISABLED:
                            //cancel subscription
                            await HandleAppStoreSubscriptionCancellationAsync(userId, signedTransactionInfo, signedRenewalInfo);
                            _logger.LogInformation("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId}, {transactionId}, Auto-renewal disabled",
                                userId.ToString(), signedTransactionInfo.TransactionId);
                            break;
                    }
                    break;
                case AppStoreNotificationType.EXPIRED:
                    //cancel subscription
                    //subtypeEnum: AppStoreNotificationSubtype.VOLUNTARY/AppStoreNotificationSubtype.BILLING_RETRY
                    //             AppStoreNotificationSubtype.PRICE_INCREASE/AppStoreNotificationSubtype.PRODUCT_NOT_FOR_SALE
                    await HandleAppStoreSubscriptionCancellationAsync(userId, signedTransactionInfo, signedRenewalInfo);
                    _logger.LogInformation("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId}, {transactionId}, Subscription expired", 
                        userId.ToString(), signedTransactionInfo.TransactionId);
                    break;
                case AppStoreNotificationType.GRACE_PERIOD_EXPIRED:
                    await HandleAppStoreSubscriptionCancellationAsync(userId, signedTransactionInfo, signedRenewalInfo);
                    _logger.LogInformation("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId}, {transactionId}, Grace period expired",
                        userId.ToString(), signedTransactionInfo.TransactionId);
                    break;
                case AppStoreNotificationType.REVOKE:
                    await HandleAppStoreSubscriptionCancellationAsync(userId, signedTransactionInfo, signedRenewalInfo);
                    _logger.LogInformation("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId}, {transactionId}, Family Sharing purchase revoked",
                        userId.ToString(), signedTransactionInfo.TransactionId);
                    break;
                
                case AppStoreNotificationType.DID_CHANGE_RENEWAL_PREF:
                    switch (subtypeEnum)
                    {
                        // Handle subscription plan changes
                        case AppStoreNotificationSubtype.UPGRADE:
                            await HandleDidRenewAsync(userId, signedTransactionInfo, signedRenewalInfo);
                            _logger.LogInformation("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId}, {transactionId}, User upgraded subscription, effective immediately",
                                userId.ToString(), signedTransactionInfo.TransactionId);
                            break;
                        case AppStoreNotificationSubtype.DOWNGRADE:
                            _logger.LogInformation("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId}, {transactionId}, User downgraded subscription, effective at next renewal",
                                userId.ToString(), signedTransactionInfo.TransactionId);
                            break;
                        default:
                            _logger.LogInformation("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId}, {transactionId}, User reverted to current subscription",
                                userId.ToString(), signedTransactionInfo.TransactionId);
                            break;
                    }
                    break;
                case AppStoreNotificationType.TEST:
                    _logger.LogInformation("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId}, {transactionId}, Test notification received",
                        userId.ToString(), signedTransactionInfo.TransactionId);
                    break;

                //----------------------------------------------------------------------
                case AppStoreNotificationType.REFUND:
                    _logger.LogInformation("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId}, {transactionId}, Purchase refunded",
                        userId.ToString(), signedTransactionInfo.TransactionId);
                    await HandleRefundAsync(userId.ToString(), appStoreTransactionInfo);
                    break;
                case AppStoreNotificationType.CONSUMPTION_REQUEST: // Handle consumption data request for refund
                case AppStoreNotificationType.DID_FAIL_TO_RENEW: // Handle renewal failure
                case AppStoreNotificationType.OFFER_REDEEMED: // Handle offer redemption
                case AppStoreNotificationType.PRICE_INCREASE: // Handle price increase
                case AppStoreNotificationType.REFUND_DECLINED: //Handle refund request declined
                case AppStoreNotificationType.REFUND_REVERSED: // Reinstate content or services that were revoked
                case AppStoreNotificationType.RENEWAL_EXTENDED: // Subscription renewal date extended
                case AppStoreNotificationType.RENEWAL_EXTENSION: // Handle renewal extension status
                default:
                    _logger.LogWarning("[UserBillingGAgent][HandleAppStoreNotificationAsync] {userId}, {transactionId}, Filter notification type",
                        userId.ToString(), signedTransactionInfo.TransactionId);
                    break;
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UserBillingGAgent][HandleAppStoreNotificationAsync] Error processing notification");
            return false;
        }
    }

    private async Task ReportPaymentSuccessAsync(Guid userId, string transactionId)
    {
        try
        {
            var analyticsGrain =
                GrainFactory.GetGrain<IPaymentAnalyticsGrain>("payment-analytics" + PaymentPlatform.AppStore);
            var analyticsResult = await analyticsGrain.ReportPaymentSuccessAsync(
                PaymentPlatform.AppStore,
                transactionId,
                userId.ToString()
            );

            if (analyticsResult.IsSuccess)
            {
                _logger.LogInformation(
                    $"[UserBillingGAgent][UpdateSubscriptionStateAsync] Successfully reported AppStore payment analytics for user {userId}, TransactionId {transactionId}, event {AppStoreNotificationType.DID_RENEW}");
            }
            else
            {
                _logger.LogWarning(
                    $"[UserBillingGAgent][UpdateSubscriptionStateAsync] Failed to report AppStore payment analytics for user {userId}, TransactionId {transactionId}, event {AppStoreNotificationType.DID_RENEW}: {analyticsResult.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[UserBillingGAgent][UpdateSubscriptionStateAsync] Exception while reporting AppStore payment analytics for user {UserId}, product {ProductId}, event {EventType}",
                userId, transactionId, AppStoreNotificationType.DID_RENEW);
            // Don't throw - analytics reporting shouldn't block payment processing
        }
    }

    private async Task HandleAppStoreSubscriptionCancellationAsync(Guid userId, 
        AppStoreJWSTransactionDecodedPayload signedTransactionInfo, JWSRenewalInfoDecodedPayload signedRenewalInfo)
    {
        var existingSubscription = await GetPaymentSummaryBySubscriptionIdAsync(signedTransactionInfo.OriginalTransactionId);
        if (existingSubscription == null)
        {
            _logger.LogError("[UserBillingGAgent][HandleSubscriptionCancellationAsync] Subscription not found. userId={0}, otxnId={1}, txnId={2}", 
                userId.ToString(), signedTransactionInfo.OriginalTransactionId, signedTransactionInfo.TransactionId);
            return;
        }

        if (existingSubscription.Status == PaymentStatus.Cancelled)
        {
            _logger.LogWarning("[UserBillingGAgent][HandleSubscriptionCancellationAsync] Subscription is cancelled. userId={0}, otxnId={1}, txnId={2}", 
                userId.ToString(), signedTransactionInfo.OriginalTransactionId, signedTransactionInfo.TransactionId);
            return;
        }

        existingSubscription.Status = PaymentStatus.Cancelled;
        var invoiceDetails = existingSubscription.InvoiceDetails;
        if (!invoiceDetails.IsNullOrEmpty())
        {
            var invoiceDetail = invoiceDetails.FirstOrDefault(t => t.InvoiceId == signedTransactionInfo.TransactionId);
            if (invoiceDetail != null)
            {
                invoiceDetail.Status = PaymentStatus.Cancelled;
            }
        }

        RaiseEvent(new UpdatePaymentBySubscriptionIdLogEvent
        {
            SubscriptionId = existingSubscription.SubscriptionId,
            PaymentSummary = existingSubscription
        });
        await ConfirmEvents();

        _logger.LogDebug("[UserBillingGAgent][HandleSubscriptionCancellationAsync] Cancel subscription complated. userId={0}, otxnId={1}, txnId={2}", 
            userId.ToString(), signedTransactionInfo.OriginalTransactionId, signedTransactionInfo.TransactionId);
    }
    
    /// <summary>
    /// Extracts transaction info from a JWT payload in V2 format
    /// </summary>
    private new Tuple<AppStoreSubscriptionInfo, AppStoreJWSTransactionDecodedPayload, JWSRenewalInfoDecodedPayload> ExtractTransactionInfoFromV2(ResponseBodyV2DecodedPayload decodedPayload)
    {
        try
        {
            if (decodedPayload?.Data == null)
            {
                _logger.LogWarning("[UserBillingGAgent][ExtractTransactionInfoFromV2] No data in payload");
                return new Tuple<AppStoreSubscriptionInfo, AppStoreJWSTransactionDecodedPayload, JWSRenewalInfoDecodedPayload>(null, null, null);
            }
            
            var transactionInfo = new AppStoreSubscriptionInfo();
            AppStoreJWSTransactionDecodedPayload transactionPayload = null;
            JWSRenewalInfoDecodedPayload renewalPayload = null;
            
            // Try to decode transaction info
            if (!string.IsNullOrEmpty(decodedPayload.Data.SignedTransactionInfo))
            {
                try
                {
                    transactionPayload = AppStoreHelper.DecodeJwtPayload<AppStoreJWSTransactionDecodedPayload>(decodedPayload.Data.SignedTransactionInfo);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "[UserBillingGAgent][ExtractTransactionInfoFromV2] Error decoding SignedTransactionInfo  payload");
                }
                if (transactionPayload != null)
                {
                    transactionInfo.OriginalTransactionId = transactionPayload.OriginalTransactionId;
                    transactionInfo.TransactionId = transactionPayload.TransactionId;
                    transactionInfo.ProductId = transactionPayload.ProductId;
                    transactionInfo.PurchaseDate = DateTimeOffset.FromUnixTimeMilliseconds(transactionPayload.PurchaseDate).DateTime;
                    
                    if (transactionPayload.ExpiresDate.HasValue)
                    {
                        transactionInfo.ExpiresDate = DateTimeOffset.FromUnixTimeMilliseconds(transactionPayload.ExpiresDate.Value).DateTime;
                    }
                    transactionInfo.IsTrialPeriod = false;
                }
            }
            
            // Try to decode renewal info
            if (!string.IsNullOrEmpty(decodedPayload.Data.SignedRenewalInfo))
            {
                try
                {
                    renewalPayload = AppStoreHelper.DecodeJwtPayload<JWSRenewalInfoDecodedPayload>(decodedPayload.Data.SignedRenewalInfo);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "[UserBillingGAgent][ExtractTransactionInfoFromV2] Error decoding SignedRenewalInfo  payload");
                }
                if (renewalPayload != null)
                {
                    transactionInfo.AutoRenewStatus = renewalPayload.AutoRenewStatus == 1;
                    
                    // If we couldn't get originalTransactionId from transaction info, try from renewal info
                    if (string.IsNullOrEmpty(transactionInfo.OriginalTransactionId) && 
                        !string.IsNullOrEmpty(renewalPayload.OriginalTransactionId))
                    {
                        transactionInfo.OriginalTransactionId = renewalPayload.OriginalTransactionId;
                    }
                    
                    // If we couldn't get productId from transaction info, try from renewal info
                    if (string.IsNullOrEmpty(transactionInfo.ProductId) && 
                        !string.IsNullOrEmpty(renewalPayload.ProductId))
                    {
                        transactionInfo.ProductId = renewalPayload.ProductId;
                    }
                }
            }
            
            transactionInfo.Environment = decodedPayload.Data?.Environment;
            
            return new (transactionInfo, transactionPayload, renewalPayload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UserBillingGAgent][ExtractTransactionInfoFromV2] Error extracting transaction info: {Error}", ex.Message);
            return null;
        }
    }

    private async Task HandleRefundAsync(string userId, AppStoreSubscriptionInfo transactionInfo)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGAgent][HandleRefundAsync] UserId is empty for transaction: {Id}", 
                transactionInfo.OriginalTransactionId);
            return;
        }
        
        _logger.LogInformation("[UserBillingGAgent][HandleRefundAsync] Processing refund for user {UserId}, product {ProductId}", 
            userId, transactionInfo.ProductId);
        
        // Update subscription status to refunded and immediately revoke user rights
    }
 

    // Filter payment history by ultimate status
    private List<ChatManager.UserBilling.PaymentSummary> GetFilteredPaymentHistoryByUltimate(bool isUltimate)
    {
        if (State.PaymentHistory == null || !State.PaymentHistory.Any())
        {
            return new List<ChatManager.UserBilling.PaymentSummary>();
        }
        
        // Filter payments by Ultimate status
        // For Apple payments, check the product configuration
        return State.PaymentHistory
            .Where(payment => 
            {
                // For Apple payments, determine Ultimate status from the product config
                if (payment.Platform == PaymentPlatform.AppStore && !string.IsNullOrEmpty(payment.PriceId))
                {
                    var appleProduct = _appleOptions.CurrentValue.Products
                        .FirstOrDefault(p => p.ProductId == payment.PriceId);
                    
                    // If product config found, filter by Ultimate status
                    if (appleProduct != null)
                    {
                        return appleProduct.IsUltimate == isUltimate;
                    }
                }
                
                // For other payment platforms (Stripe), determine from metadata or product
                if (payment.Platform == PaymentPlatform.Stripe && !string.IsNullOrEmpty(payment.PriceId))
                {
                    var stripeProduct = _stripeOptions.CurrentValue.Products
                        .FirstOrDefault(p => p.PriceId == payment.PriceId);
                    
                    if (stripeProduct != null)
                    {
                        return stripeProduct.IsUltimate == isUltimate;
                    }
                }
                
                // Default case - include in non-Ultimate list if we can't determine
                return false;
            })
            .ToList();
    }

    public async Task<VerifyReceiptResponseDto> VerifyAppStoreTransactionAsync(VerifyReceiptRequestDto requestDto, bool savePaymentEnabled)
    {
        _logger.LogDebug("[UserBillingGAgent][VerifyAppStoreTransactionAsync] verify transaction. {UserId}, {TransactionId}, {IsSandbox}",
            requestDto.UserId, requestDto.TransactionId, requestDto.SandboxMode);
        try
        {
            var transactionId = requestDto.TransactionId;
            var environment = requestDto.SandboxMode ? "Sandbox" : "Production";
            
            // If transactionId is not provided, first validate the receipt to get it
            if (string.IsNullOrEmpty(transactionId))
            {
                _logger.LogError("[UserBillingGAgent][VerifyAppStoreTransactionAsync] {UserId}, {TransactionId} No transactionId provided, verifying receipt first",
                    requestDto.UserId, transactionId);
                return new VerifyReceiptResponseDto 
                { 
                    IsValid = false, 
                    Error = $"No transactionId provided: {transactionId}" 
                };
            }
            
            // Now we have a transactionId, verify it using App Store API
            _logger.LogInformation("[UserBillingGAgent][VerifyAppStoreTransactionAsync] {UserId}, {TransactionId} Verifying transaction",
                requestDto.UserId, transactionId);
            
            var transactionResult = await GetAppStoreTransactionInfoAsync(transactionId, environment);
            
            if (!transactionResult.Success || transactionResult.Data == null)
            {
                _logger.LogError("[UserBillingGAgent][VerifyAppStoreTransactionAsync] Failed to verify transaction: {Error}", 
                    transactionResult.Message);
                return new VerifyReceiptResponseDto 
                { 
                    IsValid = false, 
                    Error = $"Transaction verification failed: {transactionResult.Message}" 
                };
            }

            var transactionInfo = transactionResult.Data;
            if (!transactionInfo.AppAccountToken.IsNullOrWhiteSpace() && Guid.TryParse(transactionInfo.AppAccountToken, out var accountToken))
            {
                _logger.LogDebug("[UserBillingGAgent][VerifyAppStoreTransactionAsync] {UserId}, {TransactionId}, {OriginalTransactionId}, verify AppAccountToken", 
                    requestDto.UserId, transactionInfo.TransactionId, transactionInfo.OriginalTransactionId);
                if (accountToken.ToString() != requestDto.UserId)
                {
                    _logger.LogError("[UserBillingGAgent][VerifyAppStoreTransactionAsync] {UserId}, {TransactionId}, {OriginalTransactionId}, Failed to verify AppAccountToken", 
                        requestDto.UserId, transactionInfo.TransactionId, transactionInfo.OriginalTransactionId);
                    return new VerifyReceiptResponseDto 
                    { 
                        IsValid = false, 
                        Error = $"AppAccountToken verification failed: invalid user data" 
                    };
                }
            }
            else
            {
                _logger.LogDebug("[UserBillingGAgent][VerifyAppStoreTransactionAsync] {UserId}, {TransactionId}, {OriginalTransactionId}, verify transaction", 
                    requestDto.UserId, transactionInfo.TransactionId, transactionInfo.OriginalTransactionId);
                var paymentGrainId = CommonHelper.GetAppleUserPaymentGrainId(transactionInfo.OriginalTransactionId);
                var paymentGrain = GrainFactory.GetGrain<IUserPaymentGrain>(paymentGrainId);
                var paymentDetailsDto = await paymentGrain.GetPaymentDetailsAsync();
                if (paymentDetailsDto != null && paymentDetailsDto.UserId != Guid.Empty && paymentDetailsDto.UserId.ToString() != requestDto.UserId )
                {
                    _logger.LogError("[UserBillingGAgent][VerifyAppStoreTransactionAsync] {UserId}, {TransactionId}, {OriginalTransactionId}, Failed to verify transaction", 
                        requestDto.UserId, transactionInfo.TransactionId, transactionInfo.OriginalTransactionId);
                    return new VerifyReceiptResponseDto 
                    { 
                        IsValid = false, 
                        Error = $"Transaction verification failed: invalid user data" 
                    };
                }
            }

            // Extract transaction details
            var purchaseDate = DateTimeOffset.FromUnixTimeMilliseconds(transactionInfo.PurchaseDate).DateTime;
            var expiresDate = transactionInfo.ExpiresDate.HasValue 
                ? DateTimeOffset.FromUnixTimeMilliseconds(transactionInfo.ExpiresDate.Value).DateTime 
                : purchaseDate.AddDays(30); // Default 30 days if no expiration date
            
            // If there is a user ID, create or update subscription
            if (!string.IsNullOrEmpty(requestDto.UserId) && Guid.TryParse(requestDto.UserId, out var userId) && savePaymentEnabled)
            {
                // We need to create or update subscription record
                _logger.LogInformation("[UserBillingGAgent][VerifyAppStoreTransactionAsync] {UserId}, {TransactionId}, {OriginalTransactionId}, Creating subscription", 
                    requestDto.UserId, transactionInfo.TransactionId, transactionInfo.OriginalTransactionId);
                await HandleDidRenewAsync(userId, transactionInfo, null);
            }
            
            // Return verification result
            return new VerifyReceiptResponseDto
            {
                IsValid = true,
                Environment = environment,
                ProductId = transactionInfo.ProductId,
                ExpiresDate = expiresDate,
                IsTrialPeriod = transactionInfo.InAppOwnershipType == "PURCHASED", // Check if this mapping is correct
                OriginalTransactionId = transactionInfo.OriginalTransactionId,
                Subscription = new SubscriptionDto
                {
                    ProductId = transactionInfo.ProductId,
                    StartDate = purchaseDate,
                    EndDate = expiresDate,
                    Status = "active"
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UserBillingGAgent][VerifyAppStoreTransactionAsync] Error verifying transaction: {Message}", ex.Message);
            return new VerifyReceiptResponseDto { IsValid = false, Error = ex.Message };
        }
    }
    
    public async Task<AppStoreSubscriptionResponseDto> CreateAppStoreSubscriptionAsync(CreateAppStoreSubscriptionDto createSubscriptionDto)
    {
        _logger.LogDebug("[UserBillingGAgent][CreateAppStoreSubscriptionAsync] create app store subscription {UserId}, {TransactionId}, {IsSandbox}",
            this.GetPrimaryKey().ToString(), createSubscriptionDto.TransactionId, createSubscriptionDto.SandboxMode);
        try
        {
            // 1. Verify App Store receipt
            var verifyReceiptRequest = new VerifyReceiptRequestDto
            {
                SandboxMode = createSubscriptionDto.SandboxMode,
                UserId = createSubscriptionDto.UserId,
                TransactionId = createSubscriptionDto.TransactionId
            };
            
            var verifyResponse = await VerifyAppStoreTransactionAsync(verifyReceiptRequest, true);
            
            // 2. Return verification result
            if (verifyResponse.IsValid)
            {
                return new AppStoreSubscriptionResponseDto
                {
                    Success = true,
                    SubscriptionId = verifyResponse.OriginalTransactionId,
                    ExpiresDate = verifyResponse.ExpiresDate,
                    Status = "active"
                };
            }
            else
            {
                return new AppStoreSubscriptionResponseDto
                {
                    Success = false,
                    Error = verifyResponse.Error
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UserBillingGAgent][CreateAppStoreSubscriptionAsync] Error creating subscription: {Message}", ex.Message);
            return new AppStoreSubscriptionResponseDto
            {
                Success = false,
                Error = ex.Message
            };
        }
    }
    
    private async Task CreateAppStoreSubscriptionAsync(Guid userId, AppStoreJWSTransactionDecodedPayload appleResponse)
    {
        // Check if there is previous subscription record
        var existingPayment = await GetPaymentSummaryBySubscriptionIdAsync(appleResponse.OriginalTransactionId);
        if (existingPayment != null)
        {
            _logger.LogWarning("[UserBillingGAgent][VerifyAppStoreTransactionAsync] transaction exists {0}, {1}, {2})",
                userId, appleResponse.OriginalTransactionId, appleResponse.TransactionId);
            return;
        }
        
        var purchaseDate = DateTimeOffset.FromUnixTimeMilliseconds(appleResponse.PurchaseDate).UtcDateTime;
        var appleProduct = await GetAppleProductConfigAsync(appleResponse.ProductId);
        var paymentGrainId = CommonHelper.GetAppleUserPaymentGrainId(appleResponse.OriginalTransactionId);
        var paymentGrain = GrainFactory.GetGrain<IUserPaymentGrain>(paymentGrainId);
        await paymentGrain.InitializePaymentAsync(new UserPaymentState
        {
            Id = paymentGrainId,
            UserId = userId,
            PriceId = appleProduct.ProductId,
            Amount = appleProduct.Amount,
            Currency = appleProduct.Currency,
            PaymentType = PaymentType.Subscription,
            Status = PaymentStatus.Completed,
            Method = PaymentMethod.ApplePay,
            Platform = PaymentPlatform.AppStore,
            Mode = null,
            Description = null,
            CreatedAt = purchaseDate,
            CompletedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            OrderId = appleResponse.OriginalTransactionId,
            SubscriptionId = appleResponse.OriginalTransactionId,
            InvoiceId = appleResponse.TransactionId
        });

        
        var (subscriptionStartDate, subscriptionEndDate) =
            await CalculateSubscriptionDurationAsync(userId, (PlanType)appleProduct.PlanType, appleProduct.IsUltimate);
        var newPayment = new ChatManager.UserBilling.PaymentSummary
        {
            PaymentGrainId = paymentGrainId,
            OrderId = appleResponse.OriginalTransactionId,
            PlanType = (PlanType)appleProduct.PlanType,
            Amount = appleProduct.Amount,
            Currency = appleProduct.Currency,
            UserId = userId,
            CreatedAt = purchaseDate,
            CompletedAt = DateTime.UtcNow,
            Status = PaymentStatus.Completed,
            SubscriptionId = appleResponse.OriginalTransactionId,
            PriceId = appleResponse.ProductId,
            SubscriptionStartDate = subscriptionStartDate,
            SubscriptionEndDate = subscriptionEndDate,
            Platform = PaymentPlatform.AppStore,
            MembershipLevel = SubscriptionHelper.GetMembershipLevel(appleProduct.IsUltimate),
            AppStoreEnvironment = appleResponse.Environment
        };

        // Add invoice details
        var invoiceDetail = new ChatManager.UserBilling.UserBillingInvoiceDetail
        {
            InvoiceId = appleResponse.TransactionId,
            CreatedAt = purchaseDate,
            CompletedAt = DateTime.UtcNow,
            Status = PaymentStatus.Completed,
            SubscriptionStartDate = subscriptionStartDate,
            SubscriptionEndDate = subscriptionEndDate,
            PriceId = appleResponse.ProductId,
            MembershipLevel = SubscriptionHelper.GetMembershipLevel(appleProduct.IsUltimate),
            Amount = appleProduct.Amount,
            PlanType = (PlanType)appleProduct.PlanType
        };

        newPayment.InvoiceDetails = new List<ChatManager.UserBilling.UserBillingInvoiceDetail> { invoiceDetail };
        await AddPaymentRecordAsync(newPayment);
        await UpdateUserQuotaOnApplePaySuccess(userId, appleResponse, appleProduct);
        //Invite users to pay rewards
        await ProcessInviteeSubscriptionAsync(userId, (PlanType) appleProduct.PlanType, appleProduct.IsUltimate, appleResponse.TransactionId);
        _logger.LogWarning("[UserBillingGAgent][CreateAppStoreSubscriptionAsync] Process invitee subscription completed, user {UserId}",
            userId);
    }

    private async Task UpdateUserQuotaOnApplePaySuccess(Guid userId, AppStoreJWSTransactionDecodedPayload appleResponse,
        AppleProduct appleProduct)
    {
        // Update user quota
        var userQuotaGAgent = GrainFactory.GetGrain<IUserQuotaGAgent>(userId);
        var subscriptionDto = appleProduct.IsUltimate
            ? await userQuotaGAgent.GetSubscriptionAsync(true)
            : await userQuotaGAgent.GetSubscriptionAsync();
        _logger.LogDebug("[UserBillingGAgent][VerifyAppStoreTransactionAsync] allocate resource {0}, {1}, {2})",
            userId, appleResponse.OriginalTransactionId, appleResponse.TransactionId);
        var subscriptionIds = subscriptionDto.SubscriptionIds;
        if (!subscriptionIds.IsNullOrEmpty())
        {
            _logger.LogDebug(
                "[UserBillingGAgent][VerifyAppStoreTransactionAsync] cancel stripe subscription, userId: {0}, subscriptionId: {1}, cancel: {2}",
                userId, appleResponse.OriginalTransactionId, JsonConvert.SerializeObject(subscriptionIds));
            foreach (var subscriptionId in subscriptionIds)
            {
                await CancelSubscriptionAsync(new CancelSubscriptionDto
                {
                    UserId = userId,
                    SubscriptionId = subscriptionId,
                    CancellationReason = $"Upgrade to a new IAP {appleResponse.OriginalTransactionId}",
                    CancelAtPeriodEnd = true
                });
            }
        }

        if (subscriptionDto.IsActive)
        {
            if (SubscriptionHelper.GetPlanTypeLogicalOrder(subscriptionDto.PlanType) <=
                SubscriptionHelper.GetPlanTypeLogicalOrder((PlanType)appleProduct.PlanType))
            {
                subscriptionDto.PlanType = (PlanType)appleProduct.PlanType;
            }

            subscriptionDto.EndDate =
                GetSubscriptionEndDate(subscriptionDto.PlanType, subscriptionDto.EndDate);
        }
        else
        {
            subscriptionDto.IsActive = true;
            subscriptionDto.PlanType = (PlanType)appleProduct.PlanType;
            subscriptionDto.StartDate = DateTime.UtcNow;
            subscriptionDto.EndDate =
                GetSubscriptionEndDate(subscriptionDto.PlanType, subscriptionDto.StartDate);
            await userQuotaGAgent.ResetRateLimitsAsync();
        }

        subscriptionDto.Status = PaymentStatus.Completed;
        await userQuotaGAgent.UpdateSubscriptionAsync(subscriptionDto, appleProduct.IsUltimate);

        //UpdatePremium quota
        if (appleProduct.IsUltimate)
        {
            var premiumSubscriptionDto = await userQuotaGAgent.GetSubscriptionAsync();
            if (!premiumSubscriptionDto.SubscriptionIds.IsNullOrEmpty())
            {
                _logger.LogDebug(
                    "[UserBillingGAgent][VerifyAppStoreTransactionAsync] cancel stripe premiumSubscription, userId: {0}, subscriptionId: {1}, cancel: {2}",
                    userId, appleResponse.OriginalTransactionId,
                    JsonConvert.SerializeObject(premiumSubscriptionDto.SubscriptionIds));
                foreach (var subscriptionId in premiumSubscriptionDto.SubscriptionIds)
                {
                    await CancelSubscriptionAsync(new CancelSubscriptionDto
                    {
                        UserId = userId,
                        SubscriptionId = subscriptionId,
                        CancellationReason = $"Upgrade to a new IAP {appleResponse.OriginalTransactionId}",
                        CancelAtPeriodEnd = true
                    });
                }
            }

            if (premiumSubscriptionDto.IsActive)
            {
                premiumSubscriptionDto.StartDate =
                    GetSubscriptionEndDate(subscriptionDto.PlanType, premiumSubscriptionDto.StartDate);
                premiumSubscriptionDto.EndDate =
                    GetSubscriptionEndDate(subscriptionDto.PlanType, premiumSubscriptionDto.EndDate);
                await userQuotaGAgent.UpdateSubscriptionAsync(premiumSubscriptionDto);
            }
        }
    }
    
    private async Task<ChatManager.UserBilling.PaymentSummary> GetPaymentSummaryBySubscriptionIdAsync(string subscriptionId)
    {
        if (string.IsNullOrEmpty(subscriptionId))
        {
            return null;
        }
        
        return State.PaymentHistory.FirstOrDefault(p => p.SubscriptionId == subscriptionId);
    }

    private async Task<PlanType> GetMaxPlanTypeAsync(DateTime? dateTime = null, bool? isUltimate = null)
    {
        _logger.LogInformation("GetMaxPlanTypeAsync isUltimate={IsUltimate}", isUltimate);
        var now = dateTime ?? DateTime.UtcNow;

        // Filter payment history by Ultimate status if specified
        var filteredPaymentHistory = isUltimate.HasValue 
            ? GetFilteredPaymentHistoryByUltimate(isUltimate.Value)
            : State.PaymentHistory;
        
        var maxPlanType = filteredPaymentHistory
            .Where(p =>
                ((p.Status is PaymentStatus.Completed or PaymentStatus.Cancelled or PaymentStatus.Cancelled_In_Processing) && p.SubscriptionEndDate != null && p.SubscriptionEndDate > now) ||
                (p.InvoiceDetails != null && p.InvoiceDetails.Any(i => 
                    (i.Status is PaymentStatus.Completed or PaymentStatus.Cancelled or PaymentStatus.Cancelled_In_Processing) && i.SubscriptionEndDate != null && i.SubscriptionEndDate > now))
            )
            .OrderByDescending(p => SubscriptionHelper.GetPlanTypeLogicalOrder(p.PlanType))
            .Select(p => p.PlanType)
            .DefaultIfEmpty(PlanType.None)
            .First();

        return maxPlanType;
    }
    
    private async Task HandleDidRenewAsync(Guid userId, AppStoreJWSTransactionDecodedPayload transactionInfo,
        JWSRenewalInfoDecodedPayload jwsRenewalInfoDecodedPayload)
    {
        if (userId == default)
        {
            _logger.LogWarning("[UserBillingGAgent][HandleDidRenewAsync] UserId is empty for transaction: {Id}", 
                transactionInfo.OriginalTransactionId);
            return;
        }

        _logger.LogInformation("[UserBillingGAgent][HandleDidRenewAsync] Processing successful renewal for user {UserId}, product {ProductId}, OriginalTransaction: {Id}", 
            userId, transactionInfo.ProductId, transactionInfo.OriginalTransactionId);
        
        // Find existing subscription record
        var existingSubscription = await GetPaymentSummaryBySubscriptionIdAsync(transactionInfo.OriginalTransactionId);
        if (existingSubscription == null)
        {
            _logger.LogWarning("[UserBillingGAgent][HandleDidRenewAsync] PaymentSummary not exist.{0}, {1}, {2}", 
                userId, transactionInfo.OriginalTransactionId, transactionInfo.TransactionId);
            await CreateAppStoreSubscriptionAsync(userId, transactionInfo);
            return;
        }

        var invoiceDetails = existingSubscription.InvoiceDetails ?? new List<ChatManager.UserBilling.UserBillingInvoiceDetail>();
        var invoiceDetail = invoiceDetails.FirstOrDefault(t => t.InvoiceId == transactionInfo.TransactionId);
        if (invoiceDetail != null)
        {
            _logger.LogWarning("[UserBillingGAgent][HandleDidRenewAsync] {UserId}, {trancactionId}, {Id}, Transaction processed.",
                userId, transactionInfo.TransactionId, transactionInfo.OriginalTransactionId);
            return;
        }

        // Create invoice details
        var appleProduct = await GetAppleProductConfigAsync(transactionInfo.ProductId);
        var purchaseDate = DateTimeOffset.FromUnixTimeMilliseconds(transactionInfo.PurchaseDate).UtcDateTime;
        var (subscriptionStartDate, subscriptionEndDate) =
            await CalculateSubscriptionDurationAsync(userId, (PlanType)appleProduct.PlanType, appleProduct.IsUltimate);
        existingSubscription.CreatedAt = purchaseDate;
        existingSubscription.CompletedAt = DateTime.UtcNow;
        existingSubscription.Status = PaymentStatus.Completed;
        existingSubscription.SubscriptionId = transactionInfo.OriginalTransactionId;
        existingSubscription.PriceId = transactionInfo.ProductId;
        existingSubscription.PlanType = (PlanType)appleProduct.PlanType;
        existingSubscription.MembershipLevel = SubscriptionHelper.GetMembershipLevel(appleProduct.IsUltimate);
        existingSubscription.SubscriptionStartDate = subscriptionStartDate;
        existingSubscription.SubscriptionEndDate = subscriptionEndDate;
        existingSubscription.Platform = PaymentPlatform.AppStore;
        existingSubscription.AppStoreEnvironment = transactionInfo.Environment;

        // Add invoice details
        invoiceDetail = new ChatManager.UserBilling.UserBillingInvoiceDetail
        {
            InvoiceId = transactionInfo.TransactionId,
            CreatedAt = purchaseDate,
            CompletedAt = DateTime.UtcNow,
            Status = PaymentStatus.Completed,
            SubscriptionStartDate = subscriptionStartDate,
            SubscriptionEndDate = subscriptionEndDate,
            PriceId = transactionInfo.ProductId,
            MembershipLevel = SubscriptionHelper.GetMembershipLevel(appleProduct.IsUltimate),
            Amount = appleProduct.Amount,
            PlanType = (PlanType)appleProduct.PlanType
        };
        invoiceDetails.Add(invoiceDetail);
        existingSubscription.InvoiceDetails = invoiceDetails;

        RaiseEvent(new UpdateExistingSubscriptionLogEvent
        {
            SubscriptionId = existingSubscription.SubscriptionId,
            ExistingSubscription = existingSubscription
        });
        await ConfirmEvents();
        
        //Check OriginTransactionId-user binding
        var paymentGrainId = CommonHelper.GetAppleUserPaymentGrainId(transactionInfo.OriginalTransactionId);
        var paymentGrain = GrainFactory.GetGrain<IUserPaymentGrain>(paymentGrainId);
        var resultDto = await paymentGrain.UpdateUserIdAsync(userId);
        if (resultDto.Success)
        {
            _logger.LogWarning("[UserBillingGAgent][HandleDidRenewAsync] {UserId}, {TransactionId}, {Id}, OriginTransactionId-user bound.",
                userId.ToString(), transactionInfo.TransactionId, transactionInfo.OriginalTransactionId);
        }
        // Grant or revoke user rights
        await UpdateUserQuotaOnApplePaySuccess(userId, transactionInfo, appleProduct);
        _logger.LogWarning("[UserBillingGAgent][UpdateSubscriptionStateAsync] Transaction processed user {UserId}, product {ProductId}, originaltransaction: {Id}, trancaction: {trancactionId}",
            userId, transactionInfo.ProductId, transactionInfo.OriginalTransactionId, transactionInfo.TransactionId);
        //Invite users to pay rewards
        await ProcessInviteeSubscriptionAsync(userId, (PlanType) appleProduct.PlanType, appleProduct.IsUltimate, transactionInfo.TransactionId);
        _logger.LogWarning("[UserBillingGAgent][UpdateSubscriptionStateAsync] Process invitee subscription completed, user {UserId}, product {ProductId}, originaltransaction: {Id}, trancaction: {trancactionId}",
            userId, transactionInfo.ProductId, transactionInfo.OriginalTransactionId, transactionInfo.TransactionId);
    }

    public async Task<GrainResultDto<AppStoreJWSTransactionDecodedPayload>> GetAppStoreTransactionInfoAsync(string transactionId, string environment)
    {
        try
        {
            _logger.LogInformation("[UserPaymentGrain][GetAppStoreTransactionInfoAsync] Getting transaction info for transaction: {TransactionId}", 
                transactionId);
            
            // Determine API environment URL
            string baseUrl = environment.ToLower() == "sandbox" 
                ? "https://api.storekit-sandbox.itunes.apple.com" 
                : "https://api.storekit.itunes.apple.com";
            
            // Construct request URL
            string requestUrl = $"{baseUrl}/inApps/v1/transactions/{transactionId}";
            
            // Create HTTP client
            using var client = _httpClientFactory.CreateClient();
            
            // Generate JWT token for authentication
            // Note: This is a placeholder. In a production environment, you would need to implement
            // JWT generation according to Apple's documentation.
            string jwtToken = await GenerateAppStoreApiJwtAsync();
            //string jwtToken = GenerateAppStoreApiJwtWithRsa();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);
            
            // Send request
            var response = await client.GetAsync(requestUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[UserPaymentGrain][GetAppStoreTransactionInfoAsync] Failed to get transaction info: {StatusCode}", 
                    response.StatusCode);
                return new GrainResultDto<AppStoreJWSTransactionDecodedPayload>
                {
                    Success = false,
                    Message = $"HTTP Error: {response.StatusCode}"
                };
            }
            
            // Parse response
            var transactionInfoString = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(transactionInfoString)) {
                _logger.LogError("[UserPaymentGrain][GetAppStoreTransactionInfoAsync] transaction info response is empty");
                return new GrainResultDto<AppStoreJWSTransactionDecodedPayload>
                {
                    Success = false,
                    Message = "transaction info is empty"
                };
            }
            
            // Parse response to get signedTransactionInfo
            var transactionResponse = JsonConvert.DeserializeObject<AppStoreTransactionResponse>(transactionInfoString);
            
            if (transactionResponse == null || string.IsNullOrEmpty(transactionResponse.SignedTransactionInfo))
            {
                _logger.LogError("[UserPaymentGrain][GetAppStoreTransactionInfoAsync] Response doesn't contain signedTransactionInfo");
                return new GrainResultDto<AppStoreJWSTransactionDecodedPayload>
                {
                    Success = false,
                    Message = "Response doesn't contain signedTransactionInfo"
                };
            }
            
            // Decode JWT payload to get actual transaction info
            AppStoreJWSTransactionDecodedPayload decodedTransactionInfo = null;
            try
            {
                decodedTransactionInfo = AppStoreHelper.DecodeJwtPayload<AppStoreJWSTransactionDecodedPayload>(transactionResponse.SignedTransactionInfo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[UserPaymentGrain][GetAppStoreTransactionInfoAsync] Error decoding payload");
            }
            
            if (decodedTransactionInfo == null)
            {
                _logger.LogError("[UserPaymentGrain][GetAppStoreTransactionInfoAsync] Failed to decode signedTransactionInfo");
                return new GrainResultDto<AppStoreJWSTransactionDecodedPayload>
                {
                    Success = false,
                    Message = "Failed to decode signedTransactionInfo"
                };
            }
            
            _logger.LogInformation("[UserPaymentGrain][GetAppStoreTransactionInfoAsync] Successfully retrieved transaction info for: {TransactionId}", 
                transactionId);
            
            return new GrainResultDto<AppStoreJWSTransactionDecodedPayload>
            {
                Success = true,
                Data = decodedTransactionInfo
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UserPaymentGrain][GetAppStoreTransactionInfoAsync] Error getting transaction info: {ErrorMessage}", 
                ex.Message);
            return new GrainResultDto<AppStoreJWSTransactionDecodedPayload>
            {
                Success = false,
                Message = $"Error getting transaction info: {ex.Message}"
            };
        }
    }

    // Generate JWT token for App Store API authentication
    private async Task<string> GenerateAppStoreApiJwtAsync()
    {
        try
        {
            _logger.LogInformation("[UserPaymentGrain][GenerateAppStoreApiJwt] Generating JWT token for App Store API");
            
            // These values should be added to your configuration (e.g., in appsettings.json)
            // and made available through the ApplePayOptions class
            string keyId = _appleOptions.CurrentValue.KeyId; 
            string issuerId = _appleOptions.CurrentValue.IssuerId; 
            string bundleId = _appleOptions.CurrentValue.BundleId;
            
            // The private key content from the .p8 file downloaded from App Store Connect
            // This should be stored securely and accessed through configuration
            string privateKeyContent = _appleOptions.CurrentValue.PrivateKey;
            
            // Step 1: Create JWT header with required parameters
            var header = new Dictionary<string, object>
            {
                { "alg", "ES256" }, // Algorithm must be ES256 (ECDSA with SHA-256)
                { "kid", keyId },   // Key ID from App Store Connect
                { "typ", "JWT" }    // Type is JWT
            };
            
            // Step 2: Create JWT payload with required claims
            var now = DateTimeOffset.UtcNow;
            var expirationTime = now.AddMinutes(10); 
            
            var claims = new Dictionary<string, object>
            {
                { "iss", issuerId },                                    // Issuer ID (Team ID)
                { "iat", now.ToUnixTimeSeconds() },                     // Issued at time
                { "exp", expirationTime.ToUnixTimeSeconds() },          // Expiration time
                { "aud", "appstoreconnect-v1" },                        // Audience is always "appstoreconnect-v1"
                { "bid", bundleId }                                     // Bundle ID of your app
                // Optional: Add a nonce for additional security
                // { "nonce", Guid.NewGuid().ToString("N") }
            };
            
            // Decode the Base64 encoded private key
            byte[] privateKeyBytes = Convert.FromBase64String(privateKeyContent);
            
            // Import the private key
            using (var ecdsa = ECDsa.Create()) {
                ecdsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);

                // Create the signing credentials using the EC key
                //var securityKey = new ECDsaSecurityKey(ecdsa) { KeyId = keyId };
                var securityKey = new ECDsaSecurityKey(ecdsa)
                {
                    KeyId = Guid.NewGuid().ToString(),
                    CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
                };
                var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256);
                
                // Step 3: Create and sign the JWT token
                var securityTokenDescriptor = new SecurityTokenDescriptor
                {
                    Claims = claims,
                    SigningCredentials = signingCredentials
                };
                
                var tokenHandler = new JwtSecurityTokenHandler();
                var securityToken = tokenHandler.CreateJwtSecurityToken(securityTokenDescriptor);
                
                // Add custom header parameters
                foreach (var item in header)
                {
                    securityToken.Header[item.Key] = item.Value;
                }
                
                // Generate the final token string
                string token = tokenHandler.WriteToken(securityToken);
                
                _logger.LogDebug("[UserPaymentGrain][GenerateAppStoreApiJwt] JWT token generated successfully");
                
                return token;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UserPaymentGrain][GenerateAppStoreApiJwt] Error generating JWT token: {ErrorMessage}", ex.Message);
            
            // In a production environment, you might want to handle this more gracefully
            // For now, we'll throw the exception to make it clear there's a configuration issue
            throw new InvalidOperationException("Failed to generate App Store API JWT token. Please check your configuration.", ex);
        }
    }

    /// <summary>
    /// Verifies the JWT signature using x5c certificate chain validation for App Store Server Notifications V2
    /// </summary>
    /// <param name="jwt">The JWT token to verify</param>
    /// <param name="environment">The environment (Sandbox or Production) - not used for x5c verification</param>
    /// <returns>True if the signature is valid, false otherwise</returns>
    private bool VerifyJwtSignature(string jwt, string environment = "Production")
    {
        try
        {
            _logger.LogInformation("[UserBillingGAgent][VerifyJwtSignature] Starting JWT signature verification using x5c certificate chain for environment: {Environment}", environment);
            
            // Split the JWT into its components
            var parts = jwt.Split('.');
            if (parts.Length != 3)
            {
                _logger.LogWarning("[UserBillingGAgent][VerifyJwtSignature] Invalid JWT format: does not have three parts");
                return false;
            }

            // Decode and parse the header
            var headerJson = DecodeBase64Url(parts[0]);
            var header = JsonConvert.DeserializeObject<Dictionary<string, object>>(headerJson);

            // Validate algorithm
            if (!header.TryGetValue("alg", out var algorithm) || algorithm.ToString() != "ES256")
            {
                _logger.LogWarning("[UserBillingGAgent][VerifyJwtSignature] Invalid or missing algorithm: {Algorithm}", algorithm);
                return false;
            }

            // Extract x5c certificate chain
            if (!header.TryGetValue("x5c", out var x5cObj) || x5cObj is not JArray x5cArray || x5cArray.Count == 0)
            {
                _logger.LogWarning("[UserBillingGAgent][VerifyJwtSignature] Missing or invalid x5c certificate chain");
                return false;
            }

            // Convert certificate chain from base64 to byte arrays
            var certificateChain = new List<byte[]>();
            foreach (var certBase64 in x5cArray)
            {
                try
                {
                    var certBytes = Convert.FromBase64String(certBase64.ToString());
                    certificateChain.Add(certBytes);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[UserBillingGAgent][VerifyJwtSignature] Error decoding certificate from x5c chain");
                    return false;
                }
            }

            if (certificateChain.Count < 2)
            {
                _logger.LogWarning("[UserBillingGAgent][VerifyJwtSignature] Certificate chain too short: {Count} certificates", certificateChain.Count);
                return false;
            }

            // Load Apple's root CA certificate
            var rootCertPath = Path.Combine(Path.GetDirectoryName(typeof(UserBillingGrain).Assembly.Location), 
                "ChatManager", "UserBilling", "Certificates", "AppleRootCA-G3.cer");
            if (!System.IO.File.Exists(rootCertPath))
            {
                _logger.LogError("[UserBillingGAgent][VerifyJwtSignature] Apple Root CA certificate not found at: {Path}", rootCertPath);
                return false;
            }

            var rootCert = new X509Certificate2(rootCertPath);
            _logger.LogInformation("[UserBillingGAgent][VerifyJwtSignature] Loaded Apple Root CA: {Subject}", rootCert.Subject);

            // Build and validate the certificate chain
            var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(rootCert);

            // Create leaf certificate from the first certificate in the chain
            var leafCert = new X509Certificate2(certificateChain[0]);
            _logger.LogInformation("[UserBillingGAgent][VerifyJwtSignature] Validating leaf certificate: {Subject}", leafCert.Subject);

            // Validate the certificate chain
            var chainBuilt = chain.Build(leafCert);
            if (!chainBuilt)
            {
                foreach (var element in chain.ChainElements)
                {
                    foreach (var status in element.ChainElementStatus)
                    {
                        _logger.LogWarning("[UserBillingGAgent][VerifyJwtSignature] Chain validation error: {Status} - {StatusInformation}",
                            status.Status, status.StatusInformation);
                    }
                }
                return false;
            }

            _logger.LogInformation("[UserBillingGAgent][VerifyJwtSignature] Certificate chain validation successful");

            // Extract public key from leaf certificate for JWT verification
            var publicKey = leafCert.GetECDsaPublicKey();
            if (publicKey == null)
            {
                _logger.LogWarning("[UserBillingGAgent][VerifyJwtSignature] Failed to extract ECDsa public key from leaf certificate");
                return false;
            }

            // Verify JWT signature using the public key from the certificate
            var headerAndPayload = $"{parts[0]}.{parts[1]}";
            var signature = Base64UrlDecodeToBytes(parts[2]);
            
            var dataToVerify = System.Text.Encoding.UTF8.GetBytes(headerAndPayload);
            var isValid = publicKey.VerifyData(dataToVerify, signature, HashAlgorithmName.SHA256);

            if (isValid)
            {
                _logger.LogInformation("[UserBillingGAgent][VerifyJwtSignature] JWT signature verification successful");
            }
            else
            {
                _logger.LogWarning("[UserBillingGAgent][VerifyJwtSignature] JWT signature verification failed");
            }

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UserBillingGAgent][VerifyJwtSignature] Unexpected error during JWT signature verification");
            return false;
        }
        finally
        {
            // Clean up any native resources
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    /// <summary>
    /// Decodes a base64url encoded string to a UTF-8 string
    /// </summary>
    /// <param name="input">The base64url encoded string</param>
    /// <returns>The decoded UTF-8 string</returns>
    private string DecodeBase64Url(string input)
    {
        var bytes = Base64UrlDecodeToBytes(input);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Decodes a Base64Url string to a byte array
    /// </summary>
    private byte[] Base64UrlDecodeToBytes(string input)
    {
        // Replace URL-safe characters
        string base64 = input.Replace("-", "+").Replace("_", "/");
        
        // Add padding if needed
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        
        // Convert from Base64
        return Convert.FromBase64String(base64);
    }

    /// <summary>
    /// Determines whether there is an active (renewing) Apple subscription.
    /// An active Apple subscription is defined as a payment record with Platform=AppStore,
    /// InvoiceDetails is not null or empty, and all InvoiceDetail's Status are not Cancelled.
    /// </summary>
    /// <returns>True if there is an active Apple subscription; otherwise, false.</returns>
    public async Task<bool> HasActiveAppleSubscriptionAsync()
    {
        var hasActive = State.PaymentHistory.Any(payment =>
            payment.Platform == PaymentPlatform.AppStore &&
            payment.InvoiceDetails != null && payment.InvoiceDetails.Any() &&
            payment.InvoiceDetails.All(item => item.Status != PaymentStatus.Cancelled));

        _logger.LogInformation("[UserBillingGAgent][HasActiveAppleSubscriptionAsync] Has active Apple subscription: {HasActive}", hasActive);
        return hasActive;
    }

    public async Task<ActiveSubscriptionStatusDto> GetActiveSubscriptionStatusAsync()
    {
        var result = new ActiveSubscriptionStatusDto();
        
        // Single iteration through payment history for optimal performance
        foreach (var payment in State.PaymentHistory)
        {
            // Check if payment has active subscription (same logic as HasActiveAppleSubscriptionAsync)
            var isActiveSubscription = payment.InvoiceDetails != null && 
                                     payment.InvoiceDetails.Any() &&
                                     payment.InvoiceDetails.All(item => item.Status != PaymentStatus.Cancelled);
            
            if (!isActiveSubscription) continue;
            
            // Check platform and set corresponding flags
            switch (payment.Platform)
            {
                case PaymentPlatform.AppStore:
                    result.HasActiveAppleSubscription = true;
                    break;
                case PaymentPlatform.Stripe:
                    result.HasActiveStripeSubscription = true;
                    break;
            }
            
            // Early termination: if both platforms have active subscriptions, no need to continue
            if (result.HasActiveAppleSubscription && result.HasActiveStripeSubscription)
            {
                break;
            }
        }
        
        // Set overall subscription status
        result.HasActiveSubscription = result.HasActiveAppleSubscription || result.HasActiveStripeSubscription;
        
        _logger.LogInformation("[UserBillingGAgent][GetActiveSubscriptionStatusAsync] Apple: {Apple}, Stripe: {Stripe}, Overall: {Overall}", 
            result.HasActiveAppleSubscription, result.HasActiveStripeSubscription, result.HasActiveSubscription);
            
        return result;
    }
    
    private async Task ProcessInviteeSubscriptionAsync(Guid userId, PlanType planType, bool isUltimate, string invoiceId)
    {
        var chatManagerGAgent = GrainFactory.GetGrain<IChatManagerGAgent>(userId);
        var inviterId = await chatManagerGAgent.GetInviterAsync();
        if (inviterId != null && inviterId != Guid.Empty)
        {
            var invitationGAgent = GrainFactory.GetGrain<IInvitationGAgent>((Guid)inviterId);
            await invitationGAgent.ProcessInviteeSubscriptionAsync(userId.ToString(), planType, isUltimate, invoiceId);
        }
    }

    protected sealed override void GAgentTransitionState(UserBillingGAgentState state, StateLogEventBase<UserBillingLogEvent> @event)
    {
        switch (@event)
        {
            case AddPaymentLogEvent addPayment:
                state.PaymentHistory.Add(addPayment.PaymentSummary);
                state.TotalPayments++;
                if (addPayment.PaymentSummary.Status == PaymentStatus.Refunded)
                {
                    state.RefundedPayments++;
                }
                break;

            case UpdatePaymentLogEvent updatePayment:
                var paymentIndex = state.PaymentHistory.FindIndex(p => p.PaymentGrainId == updatePayment.PaymentId);
                if (paymentIndex >= 0)
                {
                    state.PaymentHistory[paymentIndex] = updatePayment.PaymentSummary;
                }
                break;

            case UpdatePaymentStatusLogEvent updateStatus:
                var payment = state.PaymentHistory.FirstOrDefault(p => p.PaymentGrainId == updateStatus.PaymentId);

                if (payment != null) {
                    if (updateStatus.NewStatus == PaymentStatus.Completed && !payment.CompletedAt.HasValue)
                    {
                        payment.CompletedAt = DateTime.UtcNow;
                    }
                    if (updateStatus.NewStatus == PaymentStatus.Refunded && payment.Status != PaymentStatus.Refunded)
                    {
                        state.RefundedPayments++;
                    }
                    else if (payment.Status == PaymentStatus.Refunded && updateStatus.NewStatus != PaymentStatus.Refunded)
                    {
                        state.RefundedPayments--;
                    }
                    payment.Status = updateStatus.NewStatus;
                }
                break;

            case ClearAllLogEvent:
                state.PaymentHistory.Clear();
                break;

            case UpdateExistingSubscriptionLogEvent updateSubscription:
                var subscription = state.PaymentHistory.FirstOrDefault(p => p.SubscriptionId == updateSubscription.SubscriptionId);
                if (subscription != null)
                {
                    var index = state.PaymentHistory.IndexOf(subscription);
                    state.PaymentHistory[index] = updateSubscription.ExistingSubscription;
                }
                break;

            case UpdateCustomerIdLogEvent updateCustomerId:
                state.CustomerId = updateCustomerId.CustomerId;
                break;

            case UpdatePaymentBySubscriptionIdLogEvent updatePaymentBySubscription:
                var existingPayment = state.PaymentHistory.FirstOrDefault(p => p.SubscriptionId == updatePaymentBySubscription.SubscriptionId);
                if (existingPayment != null)
                {
                    var index = state.PaymentHistory.IndexOf(existingPayment);
                    state.PaymentHistory[index] = updatePaymentBySubscription.PaymentSummary;
                }
                break;

            case RemovePaymentHistoryLogEvent removePayment:
                foreach (var record in removePayment.RecordsToRemove)
                {
                    state.PaymentHistory.Remove(record);
                }
                break;

            case InitializeFromGrainLogEvent initializeFromGrain:
                state.UserId = this.GetPrimaryKey().ToString();
                state.IsInitializedFromGrain = true;
                state.CustomerId = initializeFromGrain.CustomerId;
                state.PaymentHistory = initializeFromGrain.PaymentHistory;
                state.TotalPayments = initializeFromGrain.TotalPayments;
                state.RefundedPayments = initializeFromGrain.RefundedPayments;
                break;

            case MarkInitializedLogEvent:
                state.UserId = this.GetPrimaryKey().ToString();
                state.IsInitializedFromGrain = true;
                break;
        }
    }
    
    protected override async Task OnGAgentActivateAsync(CancellationToken cancellationToken)
    {
        if (!State.IsInitializedFromGrain)
        {
            var userBilling =
                GrainFactory.GetGrain<IUserBillingGrain>(CommonHelper.GetUserBillingGAgentId(this.GetPrimaryKey()));
            var userBillingState = await userBilling.GetUserBillingGrainStateAsync();
            if (userBillingState != null)
            {
                _logger.LogInformation(
                    "[UserBillingGAgent][OnGAgentActivateAsync] Initializing state from IUserBillingGrain for user {UserId}",
                    this.GetPrimaryKey().ToString());

                RaiseEvent(new InitializeFromGrainLogEvent
                {
                    CustomerId = userBillingState.CustomerId,
                    PaymentHistory = userBillingState.PaymentHistory,
                    TotalPayments = userBillingState.TotalPayments,
                    RefundedPayments = userBillingState.RefundedPayments
                });
                await ConfirmEvents();

                _logger.LogDebug(
                    "[UserBillingGAgent][OnGAgentActivateAsync] State initialized from IUserBillingGrain for user {UserId}",
                    this.GetPrimaryKey().ToString());
            }
            else
            {
                _logger.LogDebug(
                    "[UserBillingGAgent][OnGAgentActivateAsync] No state found in IUserBillingGrain for user {UserId}, marking as initialized",
                    this.GetPrimaryKey().ToString());

                RaiseEvent(new MarkInitializedLogEvent());
                await ConfirmEvents();
            }
        }
    }
}