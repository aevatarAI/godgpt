using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserBilling.Payment;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Common.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Stripe;
using Stripe.Checkout;
using PaymentMethod = Aevatar.Application.Grains.Common.Constants.PaymentMethod;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Aevatar.Application.Grains.Common;
using System.Security.Cryptography.X509Certificates;
using Newtonsoft.Json.Linq;

namespace Aevatar.Application.Grains.ChatManager.UserBilling;

public interface IUserBillingGrain : IGrainWithStringKey
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
    Task<Guid> AddPaymentRecordAsync(PaymentSummary paymentSummary);
    Task<PaymentSummary> GetPaymentSummaryAsync(Guid paymentId);
    Task<List<PaymentSummary>> GetPaymentHistoryAsync(int page = 1, int pageSize = 10);
    Task<bool> UpdatePaymentStatusAsync(PaymentSummary payment, PaymentStatus newStatus);
    Task<bool> HandleStripeWebhookEventAsync(string jsonPayload, string stripeSignature);
    Task<CancelSubscriptionResponseDto> CancelSubscriptionAsync(CancelSubscriptionDto cancelSubscriptionDto);
    Task<object> RefundedSubscriptionAsync(object  obj);
    Task ClearAllAsync();
    
    // App Store related methods
    Task<VerifyReceiptResponseDto> VerifyAppStoreReceiptAsync(VerifyReceiptRequestDto requestDto, bool savePaymentEnabled);
    Task<AppStoreSubscriptionResponseDto> CreateAppStoreSubscriptionAsync(CreateAppStoreSubscriptionDto createSubscriptionDto);
    Task<bool> HandleAppStoreNotificationAsync(Guid userId, string jsonPayload);
    Task<GrainResultDto<AppStoreJWSTransactionDecodedPayload>> GetAppStoreTransactionInfoAsync(string transactionId, string environment);
}

public class UserBillingGrain : Grain<UserBillingState>, IUserBillingGrain
{
    private readonly ILogger<UserBillingGrain> _logger;
    private readonly IOptionsMonitor<StripeOptions> _stripeOptions;
    private readonly IOptionsMonitor<ApplePayOptions> _appleOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    
    private IStripeClient _client; 
    
    public UserBillingGrain(
        ILogger<UserBillingGrain> logger, 
        IOptionsMonitor<StripeOptions> stripeOptions,
        IOptionsMonitor<ApplePayOptions> appleOptions,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _stripeOptions = stripeOptions;
        _appleOptions = appleOptions;
        _httpClientFactory = httpClientFactory;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        StripeConfiguration.ApiKey = _stripeOptions.CurrentValue.SecretKey;
        _client ??= new StripeClient(_stripeOptions.CurrentValue.SecretKey);
        _logger.LogDebug("[UserBillingGrain][OnActivateAsync] Activating grain for user {UserId}",
            this.GetPrimaryKeyString());
        
        await ReadStateAsync();
        await base.OnActivateAsync(cancellationToken);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[UserBillingGrain][OnDeactivateAsync] Deactivating grain for user {UserId}. Reason: {Reason}",
            this.GetPrimaryKeyString(), reason);
        await WriteStateAsync();
        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    public async Task<List<StripeProductDto>> GetStripeProductsAsync()
    {
        var products = _stripeOptions.CurrentValue.Products;
        if (products.IsNullOrEmpty())
        {
            _logger.LogWarning("[UserBillingGrain][GetStripeProductsAsync] No products configured in StripeOptions");
            return new List<StripeProductDto>();
        }

        _logger.LogDebug("[UserBillingGrain][GetStripeProductsAsync] Found {Count} products in configuration",
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
        
        _logger.LogDebug("[UserBillingGrain][GetStripeProductsAsync] Successfully retrieved {Count} products",
            productDtos.Count);
        return productDtos;
    }

    public async Task<List<AppleProductDto>> GetAppleProductsAsync()
    {
        var products = _appleOptions.CurrentValue.Products;
        if (products.IsNullOrEmpty())
        {
            _logger.LogWarning("[UserBillingGrain][GetAppleProductsAsync] No products configured in ApplePayOptions");
            return new List<AppleProductDto>();
        }

        _logger.LogDebug("[UserBillingGrain][GetAppleProductsAsync] Found {Count} products in configuration",
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
        
        _logger.LogDebug("[UserBillingGrain][GetAppleProductsAsync] Successfully retrieved {Count} products",
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
                "[UserBillingGrain][GetOrCreateStripeCustomerAsync] Created Stripe Customer for user {UserId}: {CustomerId}",
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
                "[UserBillingGrain][GetOrCreateStripeCustomerAsync] Created temporary Stripe Customer: {CustomerId}",
                customer.Id);
            customerId = customer.Id;
        }

        State.CustomerId = customerId;
        await WriteStateAsync();
        return State.CustomerId;
    }

    public async Task<GetCustomerResponseDto> GetStripeCustomerAsync(string userId = null)
    {
        try
        {
            var customerId = await GetOrCreateStripeCustomerAsync(userId);
            _logger.LogInformation(
                "[UserBillingGrain][GetStripeCustomerAsync] {userId} Using customer: {CustomerId}",userId, customerId);
            
            var ephemeralKeyService = new EphemeralKeyService(_client);
            var ephemeralKeyOptions = new EphemeralKeyCreateOptions
            {
                Customer = customerId,
                StripeVersion = "2025-04-30.basil",
            };
            
            var ephemeralKey = await ephemeralKeyService.CreateAsync(ephemeralKeyOptions);
            _logger.LogInformation(
                "[UserBillingGrain][GetStripeCustomerAsync] {userId} Created ephemeral key with ID: {EphemeralKeyId}",
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
                "[UserBillingGrain][GetStripeCustomerAsync] Stripe error: {ErrorMessage}",
                ex.StripeError?.Message);
            throw new InvalidOperationException(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[UserBillingGrain][GetStripeCustomerAsync] error: {ErrorMessage}",
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
                "[UserBillingGrain][CreateCheckoutSessionAsync] Using payment method types: {PaymentMethodTypes}",
                string.Join(", ", createCheckoutSessionDto.PaymentMethodTypes));
        }

        if (!string.IsNullOrEmpty(createCheckoutSessionDto.PaymentMethodCollection))
        {
            options.PaymentMethodCollection = createCheckoutSessionDto.PaymentMethodCollection;
            _logger.LogInformation(
                "[UserBillingGrain][CreateCheckoutSessionAsync] Using payment method collection mode: {Mode}",
                createCheckoutSessionDto.PaymentMethodCollection);
        }

        if (!string.IsNullOrEmpty(createCheckoutSessionDto.PaymentMethodConfiguration))
        {
            options.PaymentMethodConfiguration = createCheckoutSessionDto.PaymentMethodConfiguration;
            _logger.LogInformation(
                "[UserBillingGrain][CreateCheckoutSessionAsync] Using payment method configuration: {ConfigId}",
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
                "[UserBillingGrain][CreateCheckoutSessionAsync] Using existing Customer: {CustomerId} for {Mode} mode",
                options.Customer, createCheckoutSessionDto.Mode);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex,
                "[UserBillingGrain][CreateCheckoutSessionAsync] Failed to create or get Stripe Customer: {ErrorMessage}",
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
                "[UserBillingGrain][CreateCheckoutSessionAsync] Created/Updated payment record with ID: {PaymentId} for session: {SessionId}",
                paymentDetails.Id, session.Id);

            if (createCheckoutSessionDto.UiMode == StripeUiMode.EMBEDDED)
            {
                _logger.LogInformation(
                    "[UserBillingGrain][CreateCheckoutSessionAsync] Created embedded checkout session with ID: {SessionId}, returning ClientSecret",
                    session.Id);
                return session.ClientSecret;
            }
            else
            {
                _logger.LogInformation(
                    "[UserBillingGrain][CreateCheckoutSessionAsync] Created hosted checkout session with ID: {SessionId}, returning URL",
                    session.Id);
            return session.Url;
            }
        }
        catch (StripeException e)
        {
            _logger.LogError(e,
                "[UserBillingGrain][CreateCheckoutSessionAsync] Failed to create checkout session: {ErrorMessage}",
                e.StripeError.Message);
            throw new InvalidOperationException(e.Message);
        }
    }

    public async Task<PaymentSheetResponseDto> CreatePaymentSheetAsync(CreatePaymentSheetDto createPaymentSheetDto)
    {
        _logger.LogInformation("[UserBillingGrain][CreatePaymentSheetAsync] Creating payment sheet for user {UserId}",
            createPaymentSheetDto.UserId);
        
        long amount;
        string currency;
        
        if (createPaymentSheetDto.Amount.HasValue && !string.IsNullOrEmpty(createPaymentSheetDto.Currency))
        {
            amount = createPaymentSheetDto.Amount.Value;
            currency = createPaymentSheetDto.Currency;
            _logger.LogInformation(
                "[UserBillingGrain][CreatePaymentSheetAsync] Using explicitly provided amount: {Amount} {Currency}",
                amount, currency);
        }
        else if (!string.IsNullOrEmpty(createPaymentSheetDto.PriceId))
        {
            var productConfig = await GetProductConfigAsync(createPaymentSheetDto.PriceId);
            amount = (long)productConfig.Amount;
            currency = productConfig.Currency;
            _logger.LogInformation(
                "[UserBillingGrain][CreatePaymentSheetAsync] Using amount from product config: {Amount} {Currency}, PriceId: {PriceId}",
                amount, currency, createPaymentSheetDto.PriceId);
        }
        else
        {
            var message = "Either Amount+Currency or PriceId must be provided";
            _logger.LogError("[UserBillingGrain][CreatePaymentSheetAsync] {Message}", message);
            throw new ArgumentException(message);
        }

        var orderId = Guid.NewGuid().ToString();
        
        try
        {
            var customerId = await GetOrCreateStripeCustomerAsync(createPaymentSheetDto.UserId.ToString());
            _logger.LogInformation(
                "[UserBillingGrain][CreatePaymentSheetAsync] Using customer: {CustomerId}", customerId);
            
            var ephemeralKeyService = new EphemeralKeyService(_client);
            var ephemeralKeyOptions = new EphemeralKeyCreateOptions
            {
                Customer = customerId,
                StripeVersion = "2025-04-30.basil",
            };
            
            var ephemeralKey = await ephemeralKeyService.CreateAsync(ephemeralKeyOptions);
            _logger.LogInformation(
                "[UserBillingGrain][CreatePaymentSheetAsync] Created ephemeral key with ID: {EphemeralKeyId}",
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
                    "[UserBillingGrain][CreatePaymentSheetAsync] Using payment method types: {PaymentMethodTypes}",
                    string.Join(", ", createPaymentSheetDto.PaymentMethodTypes));
            }
            
            var paymentIntent = await paymentIntentService.CreateAsync(paymentIntentOptions);
            _logger.LogInformation(
                "[UserBillingGrain][CreatePaymentSheetAsync] Created payment intent with ID: {PaymentIntentId}",
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
                        "[UserBillingGrain][CreatePaymentSheetAsync] Failed to initialize payment grain: {ErrorMessage}",
                        initResult.Message);
                    throw new Exception($"Failed to initialize payment grain: {initResult.Message}");
                }
                
                var paymentDetails = initResult.Data;
                var paymentSummary = new PaymentSummary
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
                    "[UserBillingGrain][CreatePaymentSheetAsync] Created payment record with ID: {PaymentId}",
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
                "[UserBillingGrain][CreatePaymentSheetAsync] Successfully created payment sheet for order: {OrderId}",
                orderId);
            
            return response;
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex,
                "[UserBillingGrain][CreatePaymentSheetAsync] Stripe error: {ErrorMessage}",
                ex.StripeError?.Message);
            throw new InvalidOperationException(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[UserBillingGrain][CreatePaymentSheetAsync] Error creating payment sheet: {ErrorMessage}",
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
            "[UserBillingGrain][CreateSubscriptionAsync] Creating subscription for user {UserId} with price {PriceId}",
            createSubscriptionDto.UserId, createSubscriptionDto.PriceId);

        try
        {
            var productConfig = await GetProductConfigAsync(createSubscriptionDto.PriceId);
            
            await ValidateSubscriptionUpgradePath(createSubscriptionDto.UserId.ToString(), productConfig);

            var customerId = await GetOrCreateStripeCustomerAsync(createSubscriptionDto.UserId.ToString());
            _logger.LogInformation(
                "[UserBillingGrain][CreateSubscriptionAsync] Using customer: {CustomerId}",
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
                "[UserBillingGrain][CreateSubscriptionAsync] Created subscription with ID: {SubscriptionId}, status: {Status}",
                subscription.Id, subscription.Status);
            _logger.LogDebug("[UserBillingGrain][CreateSubscriptionAsync] subscription {0}", JsonConvert.SerializeObject(subscription));

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
            
            _logger.LogDebug("[UserBillingGrain][CreateSubscriptionAsync] InitializePaymentAsync start.. {0}", subscription.Id);
            var initResult = await paymentGrain.InitializePaymentAsync(paymentState);
            _logger.LogDebug("[UserBillingGrain][CreateSubscriptionAsync] InitializePaymentAsync end..{0}", subscription.Id);
            if (!initResult.Success)
            {
                _logger.LogError(
                    "[UserBillingGrain][CreateSubscriptionAsync] Failed to initialize payment grain: {ErrorMessage}",
                    initResult.Message);
                throw new Exception($"Failed to initialize payment grain: {initResult.Message}");
            }
            var paymentDetails = initResult.Data;
            _logger.LogDebug("[UserBillingGrain][CreateSubscriptionAsync] CreateOrUpdatePaymentSummaryAsync start..{0}", subscription.Id);
            try
            {
                await CreateOrUpdatePaymentSummaryAsync(paymentDetails, null);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[UserBillingGrain][CreateSubscriptionAsync] CreateOrUpdatePaymentSummaryAsync error..{0}, {1}", subscription.Id, e.Message);
                throw;
            }
            _logger.LogDebug("[UserBillingGrain][CreateSubscriptionAsync] CreateOrUpdatePaymentSummaryAsync end..{0}", subscription.Id);
            _logger.LogInformation(
                "[UserBillingGrain][CreateSubscriptionAsync] Created/Updated payment record with ID: {PaymentId} for session: {subscription}",
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
                "[UserBillingGrain][CreateSubscriptionAsync] Stripe error: {ErrorMessage}",
                ex.StripeError?.Message);
            throw new InvalidOperationException(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[UserBillingGrain][CreateSubscriptionAsync] Error creating subscription: {ErrorMessage}",
                ex.Message);
            throw;
        }
    }

    public async Task<CancelSubscriptionResponseDto> CancelSubscriptionAsync(CancelSubscriptionDto cancelSubscriptionDto)
    {
        if (cancelSubscriptionDto.UserId == Guid.Empty)
        {
            _logger.LogWarning(
                "[UserBillingGrain][CancelSubscriptionAsync] UserId is required. {SubscriptionId}",
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
                "[UserBillingGrain][CancelSubscriptionAsync] SubscriptionId is required. {SubscriptionId}, {UserId}",
                cancelSubscriptionDto.SubscriptionId, cancelSubscriptionDto.UserId);
            return new CancelSubscriptionResponseDto
            {
                Success = false,
                Message = "SubscriptionId is required.",
                SubscriptionId = cancelSubscriptionDto.SubscriptionId
            };
        }
        
        _logger.LogDebug(
            "[UserBillingGrain][CancelSubscriptionAsync] Cancelling subscription {SubscriptionId} for user {UserId}",
            cancelSubscriptionDto.SubscriptionId, cancelSubscriptionDto.UserId);

        var paymentSummary = State.PaymentHistory
            .FirstOrDefault(p => p.SubscriptionId == cancelSubscriptionDto.SubscriptionId);
        if (paymentSummary == null)
        {
            _logger.LogWarning(
                "[UserBillingGrain][CancelSubscriptionAsync] SubscriptionId not found {SubscriptionId} for user {UserId}",
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
                "[UserBillingGrain][CancelSubscriptionAsync] Subscription canceled {SubscriptionId} for user {UserId}",
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
                "[UserBillingGrain][CancelSubscriptionAsync] Successfully cancelled subscription {SubscriptionId}, status: {Status}",
                subscription.Id, subscription.Status);

            paymentSummary.Status = PaymentStatus.Cancelled_In_Processing;
            _logger.LogInformation(
                "[UserBillingGrain][CancelSubscriptionAsync] Updated payment record {SubscriptionId} status to Cancelled",
                paymentSummary.SubscriptionId);
            await WriteStateAsync();

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
                "[UserBillingGrain][CancelSubscriptionAsync] Stripe error: {ErrorMessage}",
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
                "[UserBillingGrain][CancelSubscriptionAsync] Error cancelling subscription: {ErrorMessage}",
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
        State = new UserBillingState();
        await WriteStateAsync();
    }

    public async Task<bool> HandleStripeWebhookEventAsync(string jsonPayload, string stripeSignature)
    {
        _logger.LogInformation("[UserBillingGrain][HandleStripeWebhookEventAsync] Processing Stripe webhook event");
        if (string.IsNullOrEmpty(jsonPayload) || string.IsNullOrEmpty(stripeSignature))
        {
            _logger.LogError("[UserBillingGrain][HandleStripeWebhookEventAsync] Invalid webhook parameters");
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
                "[UserBillingGrain][HandleStripeWebhookEventAsync] Error validating webhook: {Message}", ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[UserBillingGrain][HandleStripeWebhookEventAsync] Unexpected error processing webhook: {Message}",
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
            _logger.LogError("[UserBillingGrain][HandleStripeWebhookEventAsync] error. {0}, {1}",
                this.GetPrimaryKey(), grainResultDto.Message);
            return false;
        }
        
        var paymentSummary = await CreateOrUpdatePaymentSummaryAsync(detailsDto, null);
        
        var userId = detailsDto.UserId;
        var productConfig = await GetProductConfigAsync(detailsDto.PriceId);
        var userQuotaGrain = GrainFactory.GetGrain<IUserQuotaGrain>(CommonHelper.GetUserQuotaGAgentId(userId));
        _logger.LogDebug("[UserBillingGrain][HandleStripeWebhookEventAsync] allocate resource {0}, {1}, {2}, {3})",
            userId, detailsDto.OrderId, detailsDto.SubscriptionId, detailsDto.InvoiceId);
        var subscriptionInfoDto = await userQuotaGrain.GetSubscriptionAsync(productConfig.IsUltimate);

        var subscriptionIds = subscriptionInfoDto.SubscriptionIds ?? new List<string>();
        var invoiceIds = subscriptionInfoDto.InvoiceIds ?? new List<string>();
        var invoiceDetail = paymentSummary.InvoiceDetails.LastOrDefault();
        if (invoiceDetail != null && invoiceDetail.Status == PaymentStatus.Completed && !invoiceIds.Contains(invoiceDetail.InvoiceId))
        {
            _logger.LogDebug("[UserBillingGrain][HandleStripeWebhookEventAsync] Update for complete invoice {0}, {1}, {2}",
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
                await userQuotaGrain.ResetRateLimitsAsync();
            }
            subscriptionInfoDto.Status = PaymentStatus.Completed;
            subscriptionInfoDto.SubscriptionIds = subscriptionIds;
            subscriptionInfoDto.InvoiceIds = invoiceIds;
            await userQuotaGrain.UpdateSubscriptionAsync(subscriptionInfoDto, productConfig.IsUltimate);

            if (productConfig.IsUltimate)
            {
                var premiumSubscription = await userQuotaGrain.GetSubscriptionAsync(false);
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
                    await userQuotaGrain.UpdateSubscriptionAsync(premiumSubscription);
                }
            }
        } else if (invoiceDetail != null && invoiceDetail.Status == PaymentStatus.Cancelled && subscriptionIds.Contains(paymentSummary.SubscriptionId))
        {
            _logger.LogDebug("[UserBillingGrain][HandleStripeWebhookEventAsync] Cancel User subscription {0}, {1}, {2}",
                userId, paymentSummary.SubscriptionId, invoiceDetail.InvoiceId);
            subscriptionIds.Remove(paymentSummary.SubscriptionId);
            subscriptionInfoDto.SubscriptionIds = subscriptionIds;
            await userQuotaGrain.UpdateSubscriptionAsync(subscriptionInfoDto);
        }
        else if (invoiceDetail != null && invoiceDetail.Status == PaymentStatus.Refunded && invoiceIds.Contains(invoiceDetail.InvoiceId))
        {
            _logger.LogDebug("[UserBillingGrain][HandleStripeWebhookEventAsync] Refund User subscription {0}, {1}, {2}",
                userId, paymentSummary.SubscriptionId, invoiceDetail.InvoiceId);
            
            var diff = GetDaysForPlanType(paymentSummary.PlanType);
            subscriptionInfoDto.EndDate = subscriptionInfoDto.EndDate.AddDays(-diff);
            subscriptionIds.Remove(paymentSummary.SubscriptionId);
            
            //reset plantype
            subscriptionInfoDto.PlanType = await GetMaxPlanTypeAsync(DateTime.UtcNow, productConfig.IsUltimate);
            
            subscriptionInfoDto.SubscriptionIds = subscriptionIds;
            await userQuotaGrain.UpdateSubscriptionAsync(subscriptionInfoDto, productConfig.IsUltimate);

            if (productConfig.IsUltimate)
            {
                var diffTimeSpan = (invoiceDetail.SubscriptionEndDate - DateTime.UtcNow);
                if (diffTimeSpan.TotalMilliseconds > 0)
                {
                    var premiumSubscription = await userQuotaGrain.GetSubscriptionAsync();
                    if (premiumSubscription.IsActive)
                    {
                        premiumSubscription.StartDate = premiumSubscription.StartDate.Add(- diffTimeSpan);
                        premiumSubscription.EndDate = premiumSubscription.EndDate.Add(- diffTimeSpan);
                        await userQuotaGrain.UpdateSubscriptionAsync(premiumSubscription);
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
        var userQuotaGrain = GrainFactory.GetGrain<IUserQuotaGrain>(CommonHelper.GetUserQuotaGAgentId(userId));
        
        // Use unified subscription interface
        var subscription = await userQuotaGrain.GetAndSetSubscriptionAsync(productConfig.IsUltimate);
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
        var userQuotaGrain = GrainFactory.GetGrain<IUserQuotaGrain>(CommonHelper.GetUserQuotaGAgentId(userId));
        var subscriptionInfoDto = await userQuotaGrain.GetSubscriptionAsync(ultimate);
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

    public async Task<Guid> AddPaymentRecordAsync(PaymentSummary paymentSummary)
    {
        _logger.LogInformation(
            "[UserBillingGrain][AddPaymentRecordAsync] Adding payment record with status {Status} for amount {Amount} {Currency}",
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

        // Add to payment history
        State.PaymentHistory.Add(paymentSummary);

        // Update counters
        State.TotalPayments++;
        if (paymentSummary.Status == PaymentStatus.Refunded)
        {
            State.RefundedPayments++;
        }

        // Save state
        await WriteStateAsync();

        _logger.LogInformation("[UserBillingGrain][AddPaymentRecordAsync] Payment record added with ID: {PaymentId}",
            paymentSummary.PaymentGrainId);

        return paymentSummary.PaymentGrainId;
    }

    public async Task<PaymentSummary> GetPaymentSummaryAsync(Guid paymentId)
    {
        _logger.LogInformation(
            "[UserBillingGrain][GetPaymentSummaryAsync] Getting payment summary for payment ID: {PaymentId}",
            paymentId);

        var payment = State.PaymentHistory.FirstOrDefault(p => p.PaymentGrainId == paymentId);
        if (payment == null)
        {
            _logger.LogWarning("[UserBillingGrain][GetPaymentSummaryAsync] Payment with ID {PaymentId} not found",
                paymentId);
            return null;
        }

        return payment;
    }

    public async Task<List<PaymentSummary>> GetPaymentHistoryAsync(int page = 1, int pageSize = 10)
    {
        _logger.LogInformation(
            "[UserBillingGrain][GetPaymentHistoryAsync] Getting payment history page {Page} with size {PageSize}",
            page, pageSize);

        // Ensure valid pagination parameters
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        if (State.PaymentHistory == null)
        {
            State.PaymentHistory = new List<PaymentSummary>();
        }
        
        //Filter unpaid orders
        var originalCount = State.PaymentHistory.Count;
        var recordsToRemove = State.PaymentHistory
            .Where(payment => 
                payment.InvoiceDetails.IsNullOrEmpty() && 
                payment.Status == PaymentStatus.Processing && 
                payment.CreatedAt <= DateTime.UtcNow.AddDays(-1))
            .ToList();
            
        foreach (var record in recordsToRemove)
        {
            State.PaymentHistory.Remove(record);
        }

        if (recordsToRemove.Count > 0)
        {
            _logger.LogInformation(
                "[UserBillingGrain][GetPaymentHistoryAsync] Removed {0} invalid records from payment history (original count: {1}, new count: {2})",
                recordsToRemove.Count, originalCount, State.PaymentHistory.Count);
            await WriteStateAsync();
        }

        // Calculate skip and take values
        int skip = (page - 1) * pageSize;

        var paymentHistories = new List<PaymentSummary>();
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
                    _logger.LogWarning("[UserBillingGrain][GetPaymentHistoryAsync] {0}. {1}", e.Message, paymentSummary.PriceId);
                }
                paymentSummary.MembershipLevel = membershipLevel;
            }
            
            if (paymentSummary.InvoiceDetails.IsNullOrEmpty())
            {
                paymentHistories.Add(paymentSummary);
            }
            else
            {
                paymentHistories.AddRange(paymentSummary.InvoiceDetails.Select(invoiceDetail => new PaymentSummary
                {
                    PaymentGrainId = paymentSummary.PaymentGrainId,
                    OrderId = paymentSummary.OrderId,
                    PlanType = paymentSummary.PlanType,
                    Amount = paymentSummary.Amount,
                    Currency = paymentSummary.Currency,
                    CreatedAt = invoiceDetail.CreatedAt,
                    CompletedAt = invoiceDetail.CompletedAt,
                    Status = invoiceDetail.Status,
                    SubscriptionId = paymentSummary.SubscriptionId,
                    SubscriptionStartDate = invoiceDetail.SubscriptionStartDate,
                    SubscriptionEndDate = invoiceDetail.SubscriptionEndDate,
                    UserId = paymentSummary.UserId,
                    PriceId = paymentSummary.PriceId,
                    Platform = paymentSummary.Platform,
                    MembershipLevel = paymentSummary.MembershipLevel
                }));
            }
        }

        _logger.LogInformation(
            "[UserBillingGrain][GetPaymentHistoryAsync] Returning {0} payment records after pagination",
            Math.Min(pageSize, paymentHistories.Count - skip));

        // Return paginated results ordered by most recent first
        return paymentHistories.Where(t => t.Status != PaymentStatus.Processing)
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
            .ToList();
    }

    public async Task<bool> UpdatePaymentStatusAsync(PaymentSummary payment, PaymentStatus newStatus)
    {
        _logger.LogInformation(
            "[UserBillingGrain][UpdatePaymentStatusAsync] Updating payment status for ID {PaymentId} to {NewStatus}",
            payment.PaymentGrainId, newStatus);

        // Skip update if status is already the same
        if (payment.Status == newStatus)
        {
            _logger.LogInformation(
                "[UserBillingGrain][UpdatePaymentStatusAsync] Payment {PaymentId} already has status {Status}",
                payment.PaymentGrainId, newStatus);
            return true;
        }

        // Update status
        var oldStatus = payment.Status;
        payment.Status = newStatus;

        // Set completed time if status is being changed to Completed
        if (newStatus == PaymentStatus.Completed && !payment.CompletedAt.HasValue)
        {
            payment.CompletedAt = DateTime.UtcNow;
        }

        // Update refunded count if status is being changed to Refunded
        if (newStatus == PaymentStatus.Refunded && oldStatus != PaymentStatus.Refunded)
        {
            State.RefundedPayments++;
        }
        else if (oldStatus == PaymentStatus.Refunded && newStatus != PaymentStatus.Refunded)
        {
            State.RefundedPayments--;
        }

        // Save state
        await WriteStateAsync();

        _logger.LogInformation(
            "[UserBillingGrain][UpdatePaymentStatusAsync] Payment status updated from {OldStatus} to {NewStatus} for ID {PaymentId}",
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
                "[UserBillingGrain][InitializePaymentGrainAsync] Failed to initialize payment grain: {ErrorMessage}",
                initResult.Message);
            throw new Exception($"Failed to initialize payment grain: {initResult.Message}");
        }

        var paymentDetails = initResult.Data;
        _logger.LogInformation(
            "[UserBillingGrain][InitializePaymentGrainAsync] Initialized payment grain with ID: {PaymentId}",
            paymentDetails.Id);

        return paymentDetails;
    }
    
    private async Task<PaymentSummary> CreateOrUpdatePaymentSummaryAsync(PaymentDetailsDto paymentDetails, Session session = null)
    {
        var existingPaymentSummary = State.PaymentHistory.FirstOrDefault(p => p.PaymentGrainId == paymentDetails.Id);
        var productConfig = await GetProductConfigAsync(paymentDetails.PriceId);

        if (existingPaymentSummary != null)
        {
            _logger.LogInformation(
                "[UserBillingGrain][CreateOrUpdatePaymentSummaryAsync] Updating existing payment record with ID: {PaymentId}",
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
            await WriteStateAsync();

            _logger.LogInformation(
                "[UserBillingGrain][CreateOrUpdatePaymentSummaryAsync] Updated payment record with ID: {PaymentId}",
                existingPaymentSummary.PaymentGrainId);

            return existingPaymentSummary;
        }
        else
        {
            _logger.LogInformation(
                "[UserBillingGrain][CreateOrUpdatePaymentSummaryAsync] Creating new payment record for ID: {PaymentId}",
                paymentDetails.Id);

            var newPaymentSummary = new PaymentSummary
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
                "[UserBillingGrain][CreateOrUpdatePaymentSummaryAsync] Created new payment record with ID: {PaymentId}",
                newPaymentSummary.PaymentGrainId);

            return newPaymentSummary;
        }
    }

    private async Task CreateOrUpdateInvoiceDetailAsync(PaymentDetailsDto paymentDetails, StripeProduct productConfig,
        PaymentSummary paymentSummary)
    {
        if (paymentDetails.InvoiceId.IsNullOrWhiteSpace())
        { 
            return;
        }

        if (paymentSummary.InvoiceDetails == null)
        {
            paymentSummary.InvoiceDetails = new List<UserBillingInvoiceDetail>();
        }
        var invoiceDetail =
            paymentSummary.InvoiceDetails.FirstOrDefault(t => t.InvoiceId == paymentDetails.InvoiceId);
        if (invoiceDetail == null)
        {
            invoiceDetail = new UserBillingInvoiceDetail
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
                "[UserBillingGrain][ValidateSubscriptionUpgradePath] Invalid or missing UserId, skipping subscription path validation");
            return;
        }

        var userQuotaGrain = GrainFactory.GetGrain<IUserQuotaGrain>(CommonHelper.GetUserQuotaGAgentId(parsedUserId));
        var currentSubscription = await userQuotaGrain.GetSubscriptionAsync(productConfig.IsUltimate);

        if (!currentSubscription.IsActive)
        {
            _logger.LogInformation(
                "[UserBillingGrain][ValidateSubscriptionUpgradePath] User has no active subscription, allowing purchase of any plan");
            return;
        }

        var currentPlanType = currentSubscription.PlanType;
        var targetPlanType = (PlanType)productConfig.PlanType;

        _logger.LogInformation(
            "[UserBillingGrain][ValidateSubscriptionUpgradePath] Validating upgrade path: Current={CurrentPlan}, Target={TargetPlan}",
            currentPlanType, targetPlanType);

        // Use SubscriptionHelper to validate upgrade path
        if (!SubscriptionHelper.IsUpgradePathValid(currentPlanType, targetPlanType))
        {
            var currentPlanName = SubscriptionHelper.GetPlanDisplayName(currentPlanType);
            var targetPlanName = SubscriptionHelper.GetPlanDisplayName(targetPlanType);
            
            _logger.LogWarning(
                "[UserBillingGrain][ValidateSubscriptionUpgradePath] Invalid upgrade path: {CurrentPlan} -> {TargetPlan}",
                currentPlanName, targetPlanName);
            
            throw new InvalidOperationException(
                $"Invalid upgrade path: {currentPlanName} users cannot downgrade or purchase incompatible plans. Target: {targetPlanName}");
        }

        _logger.LogInformation(
            "[UserBillingGrain][ValidateSubscriptionUpgradePath] Valid upgrade path: {CurrentPlan} -> {NewPlan}",
            currentPlanType, targetPlanType);
    }

    private async Task<StripeProduct> GetProductConfigAsync(string priceId)
    {
        var productConfig = _stripeOptions.CurrentValue.Products.FirstOrDefault(p => p.PriceId == priceId);
        if (productConfig == null)
        {
            _logger.LogError(
                "[UserBillingGrain][GetProductConfigAsync] Invalid priceId: {PriceId}. Product not found in configuration.",
                priceId);
            throw new ArgumentException($"Invalid priceId: {priceId}. Product not found in configuration.");
        }

        _logger.LogInformation(
            "[UserBillingGrain][GetProductConfigAsync] Found product with priceId: {PriceId}, planType: {PlanType}, amount: {Amount} {Currency}",
            productConfig.PriceId, productConfig.PlanType, productConfig.Amount, productConfig.Currency);

        return productConfig;
    }
    
    private async Task<AppleProduct> GetAppleProductConfigAsync(string productId)
    {
        var productConfig = _appleOptions.CurrentValue.Products.FirstOrDefault(p => p.ProductId == productId);
        if (productConfig == null)
        {
            _logger.LogError(
                "[UserBillingGrain][GetAppleProductConfigAsync] Invalid ProductId: {ProductId}. Product not found in configuration.",
                productId);
            throw new ArgumentException($"Invalid ProductId: {productId}. Product not found in configuration.");
        }

        _logger.LogInformation(
            "[UserBillingGrain][GetAppleProductConfigAsync] Found product with ProductId: {ProductId}, planType: {PlanType}, amount: {Amount} {Currency}",
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
                "[UserBillingGrain][ExtractBusinessDataAsync] Type={0}-{1}, userId={2}, orderId={3}, priceId={4},",
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
            _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Received notification payload: {Length} bytes", 
                jsonPayload?.Length ?? 0);
            
            var notificationV2 = JsonConvert.DeserializeObject<AppStoreServerNotificationV2>(jsonPayload);
            if (notificationV2?.SignedPayload == null)
            {
                _logger.LogWarning("[UserBillingGrain][HandleAppStoreNotificationAsync] Invalid notification format - missing SignedPayload");
                return false;
            }
                
            _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Received V2 format notification");
                
            // 2. First decode the JWT payload (without verification) to get environment info
            ResponseBodyV2DecodedPayload decodedPayload = null;
            try
            {
                decodedPayload = AppStoreHelper.DecodeV2Payload(notificationV2.SignedPayload);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[UserBillingGrain][HandleAppStoreNotificationAsync] Error decoding payload: {Error}", e.Message);
            }
            
            if (decodedPayload == null)
            {
                _logger.LogWarning("[UserBillingGrain][HandleAppStoreNotificationAsync] Failed to decode V2 payload");
                return false;
            }
            
            string environment = decodedPayload.Data.Environment;
            _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Notification environment: {Environment}", environment);
                
            // 3. Verify JWT signature authenticity using the correct environment
            if (!VerifyJwtSignature(notificationV2.SignedPayload, environment))
            {
                _logger.LogWarning("[UserBillingGrain][HandleAppStoreNotificationAsync] Invalid JWT signature for {Environment} environment", environment);
                return false;
            }
                
            // 4. Extract notification details from decoded payload
            string notificationType = decodedPayload.NotificationType;
            string subtype = decodedPayload.Subtype ?? "";
                
            // 5. Extract transaction info from decoded payload
            var (appStoreTransactionInfo, signedTransactionInfo, signedRenewalInfo) = ExtractTransactionInfoFromV2(decodedPayload);
            if (appStoreTransactionInfo == null)
            {
                _logger.LogWarning("[UserBillingGrain][HandleAppStoreNotificationAsync] Failed to extract transaction info from payload");
                return false;
            }

            // 7. Log notification information
            _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Received notification type: {Type}, subtype: {Subtype}, environment: {Env}", 
                notificationType, subtype, environment);
            
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

            _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Processing notification type: {Type}, subtype: {SubType}", notificationTypeEnum, subtypeEnum);
            
            // // 9. If userId is still empty at this point, try to get it from the transaction info
            // if (userId == Guid.Empty && appStoreTransactionInfo != null)
            // {
            //     var userIdStr = await GetUserIdFromTransactionInfoAsync(appStoreTransactionInfo);
            //     if (!string.IsNullOrEmpty(userIdStr) && Guid.TryParse(userIdStr, out var parsedId))
            //     {
            //         userId = parsedId;
            //         _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Found userId from transaction info: {UserId}", userId);
            //     }
            //     else
            //     {
            //         _logger.LogWarning("[UserBillingGrain][HandleAppStoreNotificationAsync] Could not determine userId from notification");
            //     }
            // }
            
            // 10. Process based on notification type and subtype
            switch (notificationTypeEnum)
            {
                case AppStoreNotificationType.DID_RENEW:
                    // Handle successful renewal
                    if (subtypeEnum == AppStoreNotificationSubtype.BILLING_RECOVERY)
                    {
                        _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Subscription recovered after billing failure");
                    }
                    await HandleDidRenewAsync(userId, signedTransactionInfo, signedRenewalInfo);
                    break;
                case AppStoreNotificationType.DID_CHANGE_RENEWAL_STATUS:
                    // Handle auto-renewal status changes
                    if (subtypeEnum == AppStoreNotificationSubtype.AUTO_RENEW_ENABLED)
                    {
                        _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Auto-renewal enabled");
                        await HandleRenewalStatusChangeAsync(userId.ToString(), appStoreTransactionInfo);
                    }
                    else if (subtypeEnum == AppStoreNotificationSubtype.AUTO_RENEW_DISABLED)
                    {
                        //cancel subscription
                        _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Auto-renewal disabled userId={0}, otxnId={1}, txnId={2}",
                            userId.ToString(), signedTransactionInfo.OriginalTransactionId, signedTransactionInfo.TransactionId);
                        await HandleAppStoreSubscriptionCancellationAsync(userId, signedTransactionInfo, signedRenewalInfo);
                    }
                    break;
                case AppStoreNotificationType.EXPIRED:
                    //cancel subscription
                    //subtypeEnum: AppStoreNotificationSubtype.VOLUNTARY/AppStoreNotificationSubtype.BILLING_RETRY
                    //             AppStoreNotificationSubtype.PRICE_INCREASE/AppStoreNotificationSubtype.PRODUCT_NOT_FOR_SALE
                    _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Subscription expired userId={0}, otxnId={1}, txnId={2}", 
                        userId.ToString(), signedTransactionInfo.OriginalTransactionId, signedTransactionInfo.TransactionId);
                    await HandleAppStoreSubscriptionCancellationAsync(userId, signedTransactionInfo, signedRenewalInfo);
                    break;
                case AppStoreNotificationType.REVOKE:
                    _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Family Sharing purchase revoked userId={0}, otxnId={1}, txnId={2}",
                        userId.ToString(), signedTransactionInfo.OriginalTransactionId, signedTransactionInfo.TransactionId);
                    await HandleAppStoreSubscriptionCancellationAsync(userId, signedTransactionInfo, signedRenewalInfo);
                    break;

                case AppStoreNotificationType.CONSUMPTION_REQUEST:
                    // Handle consumption data request for refund
                    _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Received consumption data request for refund");
                    break;

                case AppStoreNotificationType.DID_CHANGE_RENEWAL_PREF:
                    // Handle subscription plan changes
                    if (subtypeEnum == AppStoreNotificationSubtype.UPGRADE)
                    {
                        _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] User upgraded subscription, effective immediately");
                        await HandleInteractiveRenewalAsync(userId.ToString(), appStoreTransactionInfo);
                    }
                    else if (subtypeEnum == AppStoreNotificationSubtype.DOWNGRADE)
                    {
                        _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] User downgraded subscription, effective at next renewal");
                        await HandleRenewalPreferenceChangeAsync(userId.ToString(), appStoreTransactionInfo);
                    }
                    else
                    {
                        _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] User reverted to current subscription");
                        await HandleRenewalPreferenceChangeAsync(userId.ToString(), appStoreTransactionInfo);
                    }
                    break;

                case AppStoreNotificationType.DID_FAIL_TO_RENEW:
                    // Handle renewal failure
                    if (subtypeEnum == AppStoreNotificationSubtype.GRACE_PERIOD)
                    {
                        _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Subscription in grace period");
                        // Continue providing service through grace period
                    }
                    await HandleFailedRenewalAsync(userId.ToString(), appStoreTransactionInfo);
                    break;

                case AppStoreNotificationType.GRACE_PERIOD_EXPIRED:
                    _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Grace period expired");
                    await HandleGracePeriodExpiredAsync(userId.ToString(), appStoreTransactionInfo);
                    break;

                case AppStoreNotificationType.OFFER_REDEEMED:
                    // Handle offer redemption
                    if (subtypeEnum == AppStoreNotificationSubtype.UPGRADE)
                    {
                        _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Offer redeemed for upgrade");
                    }
                    else if (subtypeEnum == AppStoreNotificationSubtype.DOWNGRADE)
                    {
                        _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Offer redeemed for downgrade");
                    }
                    await HandleOfferRedeemedAsync(userId.ToString(), appStoreTransactionInfo);
                    break;

                case AppStoreNotificationType.PRICE_INCREASE:
                    // Handle price increase
                    switch (subtypeEnum)
                    {
                        case AppStoreNotificationSubtype.PENDING:
                            _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Price increase pending customer consent");
                            break;
                        case AppStoreNotificationSubtype.ACCEPTED:
                            _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Price increase accepted");
                            break;
                    }
                    await HandlePriceIncreaseAsync(userId.ToString(), appStoreTransactionInfo);
                    break;

                case AppStoreNotificationType.REFUND:
                    _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Purchase refunded");
                    await HandleRefundAsync(userId.ToString(), appStoreTransactionInfo);
                    break;

                case AppStoreNotificationType.REFUND_DECLINED:
                    _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Refund request declined");
                    await HandleRefundDeclinedAsync(userId.ToString(), appStoreTransactionInfo);
                    break;

                case AppStoreNotificationType.REFUND_REVERSED:
                    _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Refund reversed");
                    // Reinstate content or services that were revoked
                    break;

                case AppStoreNotificationType.RENEWAL_EXTENDED:
                    _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Subscription renewal date extended");
                    await HandleRenewalExtendedAsync(userId.ToString(), appStoreTransactionInfo);
                    break;

                case AppStoreNotificationType.RENEWAL_EXTENSION:
                    // Handle renewal extension status
                    if (subtypeEnum == AppStoreNotificationSubtype.SUMMARY)
                    {
                        _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Renewal extension completed for all eligible subscribers");
                    }
                    else if (subtypeEnum == AppStoreNotificationSubtype.FAILURE)
                    {
                        _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Renewal extension failed for specific subscription");
                    }
                    await HandleRenewalExtensionAsync(userId.ToString(), appStoreTransactionInfo);
                    break;

                case AppStoreNotificationType.SUBSCRIBED:
                    // Handle new subscription
                    if (subtypeEnum == AppStoreNotificationSubtype.INITIAL_BUY)
                    {
                        _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Initial subscription purchase");
                        await HandleInitialPurchaseAsync(userId.ToString(), appStoreTransactionInfo);
                    }
                    else if (subtypeEnum == AppStoreNotificationSubtype.RESUBSCRIBE)
                    {
                        _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Resubscription to same or different subscription in group");
                        await HandleRenewalAsync(userId.ToString(), appStoreTransactionInfo);
                    }
                    break;

                case AppStoreNotificationType.TEST:
                    _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Test notification received");
                    break;

                default:
                    _logger.LogWarning("[UserBillingGrain][HandleAppStoreNotificationAsync] Unknown notification type: {NotificationType}", notificationTypeEnum);
                    break;
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UserBillingGrain][HandleAppStoreNotificationAsync] Error processing notification");
            return false;
        }
    }

    private async Task HandleAppStoreSubscriptionCancellationAsync(Guid userId, 
        AppStoreJWSTransactionDecodedPayload signedTransactionInfo, JWSRenewalInfoDecodedPayload signedRenewalInfo)
    {
        var existingSubscription = await GetPaymentSummaryBySubscriptionIdAsync(signedTransactionInfo.OriginalTransactionId);
        if (existingSubscription == null)
        {
            _logger.LogError("[UserBillingGrain][HandleSubscriptionCancellationAsync] Subscription not found. userId={0}, otxnId={1}, txnId={2}", 
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
        await WriteStateAsync();
        _logger.LogDebug("[UserBillingGrain][HandleSubscriptionCancellationAsync] Cancel subscription complated. userId={0}, otxnId={1}, txnId={2}", 
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
                _logger.LogWarning("[UserBillingGrain][ExtractTransactionInfoFromV2] No data in payload");
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
                    _logger.LogError(e, "[UserBillingGrain][ExtractTransactionInfoFromV2] Error decoding SignedTransactionInfo  payload");
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
                    _logger.LogError(e, "[UserBillingGrain][ExtractTransactionInfoFromV2] Error decoding SignedRenewalInfo  payload");
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
            _logger.LogError(ex, "[UserBillingGrain][ExtractTransactionInfoFromV2] Error extracting transaction info: {Error}", ex.Message);
            return null;
        }
    }

    private async Task HandleInitialPurchaseAsync(string userId, AppStoreSubscriptionInfo transactionInfo)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGrain][HandleInitialPurchaseAsync] UserId is empty for transaction: {Id}", 
                transactionInfo.OriginalTransactionId);
            return;
        }
        
        _logger.LogInformation("[UserBillingGrain][HandleInitialPurchaseAsync] Processing initial purchase for user: {UserId}", userId);
        await UpdateSubscriptionStateAsync(userId, transactionInfo, "INITIAL_BUY");
    }
    
    private async Task HandleRenewalAsync(string userId, AppStoreSubscriptionInfo transactionInfo)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGrain][HandleRenewalAsync] UserId is empty for transaction: {Id}", 
                transactionInfo.OriginalTransactionId);
            return;
        }
        
        _logger.LogInformation("[UserBillingGrain][HandleRenewalAsync] Processing renewal for user: {UserId}", userId);
        await UpdateSubscriptionStateAsync(userId, transactionInfo, "RENEWAL");
    }
    
    private async Task HandleInteractiveRenewalAsync(string userId, AppStoreSubscriptionInfo transactionInfo)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGrain][HandleInteractiveRenewalAsync] UserId is empty for transaction: {Id}", 
                transactionInfo.OriginalTransactionId);
            return;
        }
        
        _logger.LogInformation("[UserBillingGrain][HandleInteractiveRenewalAsync] Processing interactive renewal for user: {UserId}", userId);
        await UpdateSubscriptionStateAsync(userId, transactionInfo, "INTERACTIVE_RENEWAL");
    }
    
    private async Task HandleRefundAsync(string userId, AppStoreSubscriptionInfo transactionInfo)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGrain][HandleRefundAsync] UserId is empty for transaction: {Id}", 
                transactionInfo.OriginalTransactionId);
            return;
        }
        
        _logger.LogInformation("[UserBillingGrain][HandleRefundAsync] Processing refund for user {UserId}, product {ProductId}", 
            userId, transactionInfo.ProductId);
        
        // Update subscription status to refunded and immediately revoke user rights
        await UpdateSubscriptionStateAsync(userId, transactionInfo, "REFUND");
    }
    
    private async Task UpdateSubscriptionStateAsync(string userId, AppStoreSubscriptionInfo subscriptionInfo, string eventType)
    {
        // Find existing subscription record
        var existingSubscription = await GetPaymentSummaryBySubscriptionIdAsync(subscriptionInfo.OriginalTransactionId);
        // Create invoice details
        var invoiceDetail = new UserBillingInvoiceDetail
        {
            InvoiceId = subscriptionInfo.TransactionId, 
            CreatedAt = subscriptionInfo.PurchaseDate,
            CompletedAt = DateTime.UtcNow
        };
        
        // Update PaymentSummary record
        var paymentStatus = MapToPaymentStatus(eventType, subscriptionInfo);
        var paymentSummary = new PaymentSummary
        {
            UserId = Guid.Parse(userId),
            // PaymentMethod attribute has been removed, no longer used
            Amount = GetProductAmount(subscriptionInfo.ProductId),
            Currency = "USD",
            Status = paymentStatus,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            PaymentType = MapToPaymentType(eventType),
            SubscriptionId = subscriptionInfo.OriginalTransactionId, // Use OriginalTransactionId as SubscriptionId
            PriceId = subscriptionInfo.ProductId, // Use ProductId as PriceId
            AppStoreEnvironment = subscriptionInfo.Environment,
            SubscriptionStartDate = subscriptionInfo.PurchaseDate,
            SubscriptionEndDate = subscriptionInfo.ExpiresDate
        };
        
        if (existingSubscription != null)
        {
            // Get existing invoice details and add new record
            var invoiceDetails = existingSubscription.InvoiceDetails ?? new List<UserBillingInvoiceDetail>();
            invoiceDetails.Add(invoiceDetail);
            paymentSummary.InvoiceDetails = invoiceDetails;
            
            // Update existing record
            await UpdatePaymentRecordAsync(existingSubscription.PaymentGrainId, paymentSummary);
        }
        else
        {
            // Add new record
            paymentSummary.InvoiceDetails = new List<UserBillingInvoiceDetail> { invoiceDetail };
            await AddPaymentRecordAsync(paymentSummary);
        }
        
        // Grant or revoke user rights
        await UpdateUserQuotaAsync(userId, subscriptionInfo, eventType);
    }
    
    private PaymentStatus MapToPaymentStatus(string eventType, AppStoreSubscriptionInfo subscriptionInfo)
    {
        // Map notification type to payment status - V2 format only
        return eventType switch
        {
            "INITIAL_BUY" => PaymentStatus.Completed,
            "RENEWAL" => PaymentStatus.Completed,
            "INTERACTIVE_RENEWAL" => PaymentStatus.Completed,
            "REFUND" => PaymentStatus.Refunded,
            "DID_CHANGE_RENEWAL_PREF" => PaymentStatus.Completed, // Changing renewal preferences but status remains completed
            "DID_CHANGE_RENEWAL_STATUS" => PaymentStatus.Completed, // Changing renewal status but status remains completed
            "DID_FAIL_TO_RENEW" => PaymentStatus.Processing, // Payment failed but in retry period
            "DID_RENEW" => PaymentStatus.Completed, // Successfully renewed
            "EXPIRED" => PaymentStatus.Cancelled, // Subscription has fully expired
            "GRACE_PERIOD_EXPIRED" => PaymentStatus.Cancelled, // Grace period ended without payment
            "OFFER_REDEEMED" => PaymentStatus.Completed, // Offer was redeemed successfully
            "PRICE_INCREASE" => PaymentStatus.Completed, // Price increase notification doesn't change status
            "REFUND_DECLINE" => PaymentStatus.Completed, // Refund was declined, subscription remains active
            "RENEWAL_EXTENDED" => PaymentStatus.Completed, // Renewal date was extended, subscription remains active
            "RENEWAL_EXTENSION" => PaymentStatus.Completed, // Renewal extension attempt, status doesn't change
            "REVOKE" => PaymentStatus.Cancelled, // Access revoked (Family Sharing)
            "TEST" => PaymentStatus.Unknown, // Test notification
            _ => PaymentStatus.Unknown
        };
    }
    
    private PaymentType MapToPaymentType(string eventType)
    {
        // Map notification type to payment type - V2 format only
        return eventType switch
        {
            "INITIAL_BUY" => PaymentType.Subscription,
            "RENEWAL" => PaymentType.Renewal,
            "INTERACTIVE_RENEWAL" => PaymentType.Renewal,
            "REFUND" => PaymentType.Refund,
            "DID_CHANGE_RENEWAL_PREF" => PaymentType.Subscription, // Changing renewal settings is considered a subscription modification
            "DID_CHANGE_RENEWAL_STATUS" => PaymentType.Subscription, // Changing renewal status is considered a subscription modification
            "DID_FAIL_TO_RENEW" => PaymentType.Renewal, // Failed renewal attempt
            "DID_RENEW" => PaymentType.Renewal, // Successful renewal
            "EXPIRED" => PaymentType.Cancellation, // Subscription expired
            "GRACE_PERIOD_EXPIRED" => PaymentType.Cancellation, // Grace period ended without payment
            "OFFER_REDEEMED" => PaymentType.Subscription, // Offer redemption is a subscription event
            "PRICE_INCREASE" => PaymentType.Subscription, // Price increase notification related to subscription
            "REFUND_DECLINE" => PaymentType.Subscription, // Refund was declined, subscription continues
            "RENEWAL_EXTENDED" => PaymentType.Renewal, // Renewal date extension
            "RENEWAL_EXTENSION" => PaymentType.Renewal, // Renewal extension attempt
            "REVOKE" => PaymentType.Cancellation, // Access revoked (Family Sharing)
            "TEST" => PaymentType.Unknown, // Test notification
            _ => PaymentType.Unknown
        };
    }
    
    private async Task UpdateUserQuotaAsync(string userId, AppStoreSubscriptionInfo subscriptionInfo, string eventType)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGrain][UpdateUserQuotaAsync] UserId is null or empty");
            return;
        }
        
        try
        {
            var userQuotaGrain = GrainFactory.GetGrain<IUserQuotaGrain>(userId);
            
            switch (eventType)
            {
                case "INITIAL_BUY":
                case "RENEWAL":
                case "INTERACTIVE_RENEWAL":
                    // Grant or update user rights
                    _logger.LogInformation("[UserBillingGrain][UpdateUserQuotaAsync] Updating user quota for {UserId} with product {ProductId}, expires on {ExpiresDate}",
                        userId, subscriptionInfo.ProductId, subscriptionInfo.ExpiresDate);
                    await userQuotaGrain.UpdateQuotaAsync(subscriptionInfo.ProductId, subscriptionInfo.ExpiresDate);
                    break;
                
                case "REFUND":
                    // For refund, immediately revoke user rights
                    _logger.LogInformation("[UserBillingGrain][UpdateUserQuotaAsync] Resetting quota for {UserId} due to refund",
                        userId);
                    await userQuotaGrain.ResetQuotaAsync();
                    break;
                
                case "DID_CHANGE_RENEWAL_PREF":
                case "DID_CHANGE_RENEWAL_STATUS":
                    // These events do not affect the rights of the current subscription period, only record status changes
                    _logger.LogInformation("[UserBillingGrain][UpdateUserQuotaAsync] Renewal preferences changed for {UserId}, no action needed",
                        userId);
                    break;
                
                default:
                    _logger.LogWarning("[UserBillingGrain][UpdateUserQuotaAsync] Unknown event type: {EventType} for {UserId}",
                        eventType, userId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UserBillingGrain][UpdateUserQuotaAsync] Error updating user quota for {UserId}: {Message}",
                userId, ex.Message);
        }
    }
    
    private async Task RevokeUserQuotaAsync(string userId)
    {
        // In actual implementation, this should call the user quota management service to revoke user rights
        var userQuotaGrain = GrainFactory.GetGrain<IUserQuotaGrain>(userId);
        await userQuotaGrain.ResetQuotaAsync();
    }
    
    private async Task<bool> UpdatePaymentRecordAsync(Guid paymentId, PaymentSummary newPaymentSummary)
    {
        var paymentIndex = State.PaymentHistory.FindIndex(p => p.PaymentGrainId == paymentId);
        if (paymentIndex >= 0)
        {
            State.PaymentHistory[paymentIndex] = newPaymentSummary;
            await WriteStateAsync();
            return true;
        }
        return false;
    }

    // Filter payment history by ultimate status
    private List<PaymentSummary> GetFilteredPaymentHistoryByUltimate(bool isUltimate)
    {
        if (State.PaymentHistory == null || !State.PaymentHistory.Any())
        {
            return new List<PaymentSummary>();
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

    public async Task<VerifyReceiptResponseDto> VerifyAppStoreReceiptAsync(VerifyReceiptRequestDto requestDto, bool savePaymentEnabled)
    {
        try
        {
            // 1. Determine verification environment (production or sandbox)
            string verificationUrl = requestDto.SandboxMode 
                ? "https://sandbox.itunes.apple.com/verifyReceipt"
                : "https://buy.itunes.apple.com/verifyReceipt";
            
            // 2. Build verification request
            var requestBody = new
            {
                receipt_data = requestDto.ReceiptData,
                password = _appleOptions.CurrentValue.SharedSecret,
                exclude_old_transactions = true
            };
            
            // 3. Send verification request to App Store
            using var client = _httpClientFactory.CreateClient();
            var response = await client.PostAsJsonAsync(verificationUrl, requestBody);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[UserBillingGrain][VerifyAppStoreReceiptAsync] Failed to verify receipt: {StatusCode}", response.StatusCode);
                return new VerifyReceiptResponseDto { IsValid = false, Error = $"HTTP Error: {response.StatusCode}" };
            }
            
            // 4. Parse response
            var appleResponse = await response.Content.ReadFromJsonAsync<AppleVerifyReceiptResponse>();
            
            // 5. Check verification status
            if (appleResponse.Status != 0)
            {
                // If verification fails in production environment and status code is 21007, retry sandbox environment
                if (!requestDto.SandboxMode && appleResponse.Status == 21007)
                {
                    _logger.LogInformation("[UserBillingGrain][VerifyAppStoreReceiptAsync] Receipt is from sandbox, retrying with sandbox URL");
                    requestDto.SandboxMode = true;
                    return await VerifyAppStoreReceiptAsync(requestDto, savePaymentEnabled);
                }
                
                _logger.LogError("[UserBillingGrain][VerifyAppStoreReceiptAsync] Receipt validation failed with status: {Status}", appleResponse.Status);
                return new VerifyReceiptResponseDto 
                { 
                    IsValid = false, 
                    Error = $"Apple verification failed with status: {appleResponse.Status}" 
                };
            }
            
            // 6. Extract latest receipt information
            var latestReceiptInfo = appleResponse.LatestReceiptInfo?.OrderByDescending(r => 
                long.TryParse(r.ExpiresDateMs, out var expiresMs) ? expiresMs : 0).FirstOrDefault();
                
            if (latestReceiptInfo == null)
            {
                _logger.LogError("[UserBillingGrain][VerifyAppStoreReceiptAsync] No receipt info found in response");
                return new VerifyReceiptResponseDto { IsValid = false, Error = "No receipt information found" };
            }
            
            // 7. Parse date
            var purchaseDate = DateTime.UtcNow;
            var expiresDate = DateTime.UtcNow.AddDays(30); // Default 30 days
            
            if (long.TryParse(latestReceiptInfo.PurchaseDateMs, out var purchaseMs))
            {
                purchaseDate = DateTimeOffset.FromUnixTimeMilliseconds(purchaseMs).DateTime;
            }
            
            if (long.TryParse(latestReceiptInfo.ExpiresDateMs, out var expiresMs))
            {
                expiresDate = DateTimeOffset.FromUnixTimeMilliseconds(expiresMs).DateTime;
            }
            
            bool isTrialPeriod = false;
            if (latestReceiptInfo.IsTrialPeriod != null)
            {
                bool.TryParse(latestReceiptInfo.IsTrialPeriod, out isTrialPeriod);
            }
            
            // 8. If there is a user ID, create or update subscription
            if (!string.IsNullOrEmpty(requestDto.UserId) && Guid.TryParse(requestDto.UserId, out var userId) && savePaymentEnabled)
            {
                await CreateAppStoreSubscriptionAsync(latestReceiptInfo, userId, purchaseDate, appleResponse);
            }
            
            // 9. Return verification result
            return new VerifyReceiptResponseDto
            {
                IsValid = true,
                Environment = appleResponse.Environment,
                ProductId = latestReceiptInfo.ProductId,
                ExpiresDate = expiresDate,
                IsTrialPeriod = isTrialPeriod,
                OriginalTransactionId = latestReceiptInfo.OriginalTransactionId,
                Subscription = new SubscriptionDto
                {
                    ProductId = latestReceiptInfo.ProductId,
                    StartDate = purchaseDate,
                    EndDate = expiresDate,
                    Status = "active"
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UserBillingGrain][VerifyAppStoreReceiptAsync] Error verifying receipt: {Message}", ex.Message);
            return new VerifyReceiptResponseDto { IsValid = false, Error = ex.Message };
        }
    }
    
    public async Task<VerifyReceiptResponseDto> VerifyAppStoreTransactionAsync(VerifyReceiptRequestDto requestDto, bool savePaymentEnabled)
    {
        try
        {
            string transactionId = requestDto.TransactionId;
            string environment = requestDto.SandboxMode ? "Sandbox" : "Production";
            
            // If transactionId is not provided, first validate the receipt to get it
            if (string.IsNullOrEmpty(transactionId))
            {
                _logger.LogInformation("[UserBillingGrain][VerifyAppStoreTransactionAsync] No transactionId provided, verifying receipt first");
                
                // Use the existing receipt verification method to get transaction details
                return await VerifyAppStoreReceiptAsync(requestDto, savePaymentEnabled);
            }
            
            // Now we have a transactionId, verify it using App Store API
            _logger.LogInformation("[UserBillingGrain][VerifyAppStoreTransactionAsync] Verifying transaction: {TransactionId}", transactionId);
            
            var transactionResult = await GetAppStoreTransactionInfoAsync(transactionId, environment);
            
            if (!transactionResult.Success || transactionResult.Data == null)
            {
                _logger.LogError("[UserBillingGrain][VerifyAppStoreTransactionAsync] Failed to verify transaction: {Error}", 
                    transactionResult.Message);
                return new VerifyReceiptResponseDto 
                { 
                    IsValid = false, 
                    Error = $"Transaction verification failed: {transactionResult.Message}" 
                };
            }
            
            var transactionInfo = transactionResult.Data;
            
            // Extract transaction details
            var purchaseDate = DateTimeOffset.FromUnixTimeMilliseconds(transactionInfo.PurchaseDate).DateTime;
            var expiresDate = transactionInfo.ExpiresDate.HasValue 
                ? DateTimeOffset.FromUnixTimeMilliseconds(transactionInfo.ExpiresDate.Value).DateTime 
                : purchaseDate.AddDays(30); // Default 30 days if no expiration date
            
            // If there is a user ID, create or update subscription
            if (!string.IsNullOrEmpty(requestDto.UserId) && Guid.TryParse(requestDto.UserId, out var userId) && savePaymentEnabled)
            {
                // We need to create or update subscription record
                _logger.LogInformation("[UserBillingGrain][VerifyAppStoreTransactionAsync] Creating subscription for user: {UserId}", userId);
                //await CreateAppStoreSubscriptionAsync(userId, transactionInfo);
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
            _logger.LogError(ex, "[UserBillingGrain][VerifyAppStoreTransactionAsync] Error verifying transaction: {Message}", ex.Message);
            return new VerifyReceiptResponseDto { IsValid = false, Error = ex.Message };
        }
    }
    
    public async Task<AppStoreSubscriptionResponseDto> CreateAppStoreSubscriptionAsync(CreateAppStoreSubscriptionDto createSubscriptionDto)
    {
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
            _logger.LogError(ex, "[UserBillingGrain][CreateAppStoreSubscriptionAsync] Error creating subscription: {Message}", ex.Message);
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
            _logger.LogWarning("[UserBillingGrain][VerifyAppStoreTransactionAsync] transaction exists {0}, {1}, {2})",
                userId, appleResponse.OriginalTransactionId, appleResponse.TransactionId);
            return;
        }
        
        var purchaseDate = DateTimeOffset.FromUnixTimeMilliseconds(appleResponse.PurchaseDate).DateTime;
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
        var newPayment = new PaymentSummary
        {
            PaymentGrainId = paymentGrainId,
            OrderId = appleResponse.OriginalTransactionId,
            PlanType = (PlanType)appleProduct.PlanType,
            Amount = GetProductAmount(appleResponse.ProductId),
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
        var invoiceDetail = new UserBillingInvoiceDetail
        {
            InvoiceId = appleResponse.TransactionId,
            CreatedAt = purchaseDate,
            CompletedAt = DateTime.UtcNow,
            Status = PaymentStatus.Completed,
            SubscriptionStartDate = subscriptionStartDate,
            SubscriptionEndDate = subscriptionEndDate
        };

        newPayment.InvoiceDetails = new List<UserBillingInvoiceDetail> { invoiceDetail };
        await AddPaymentRecordAsync(newPayment);
        await UpdateUserQuotaOnApplePaySuccess(userId, appleResponse, appleProduct);
    }

    private async Task UpdateUserQuotaOnApplePaySuccess(Guid userId, AppStoreJWSTransactionDecodedPayload appleResponse,
        AppleProduct appleProduct)
    {
        // Update user quota
        var userQuotaGrain = GrainFactory.GetGrain<IUserQuotaGrain>(CommonHelper.GetUserQuotaGAgentId(userId));
        var subscriptionDto = appleProduct.IsUltimate
            ? await userQuotaGrain.GetSubscriptionAsync(true)
            : await userQuotaGrain.GetSubscriptionAsync();
        _logger.LogDebug("[UserBillingGrain][VerifyAppStoreTransactionAsync] allocate resource {0}, {1}, {2})",
            userId, appleResponse.OriginalTransactionId, appleResponse.TransactionId);
        var subscriptionIds = subscriptionDto.SubscriptionIds;
        if (!subscriptionIds.IsNullOrEmpty())
        {
            _logger.LogDebug(
                "[UserBillingGrain][VerifyAppStoreTransactionAsync] cancel stripe subscription, userId: {0}, subscriptionId: {1}, cancel: {2}",
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
            await userQuotaGrain.ResetRateLimitsAsync();
        }

        subscriptionDto.Status = PaymentStatus.Completed;
        await userQuotaGrain.UpdateSubscriptionAsync(subscriptionDto, appleProduct.IsUltimate);

        //UpdatePremium quota
        if (appleProduct.IsUltimate)
        {
            var premiumSubscriptionDto = await userQuotaGrain.GetSubscriptionAsync();
            if (!premiumSubscriptionDto.SubscriptionIds.IsNullOrEmpty())
            {
                _logger.LogDebug(
                    "[UserBillingGrain][VerifyAppStoreTransactionAsync] cancel stripe premiumSubscription, userId: {0}, subscriptionId: {1}, cancel: {2}",
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
                await userQuotaGrain.UpdateSubscriptionAsync(premiumSubscriptionDto);
            }
        }
    }

    private async Task CreateAppStoreSubscriptionAsync(LatestReceiptInfo latestReceiptInfo, Guid userId,
        DateTime purchaseDate, AppleVerifyReceiptResponse appleResponse)
    {
        var appleProduct = await GetAppleProductConfigAsync(latestReceiptInfo.ProductId);

        var paymentGrainId = CommonHelper.GetAppleUserPaymentGrainId(latestReceiptInfo.OriginalTransactionId);
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
            OrderId = latestReceiptInfo.OriginalTransactionId,
            SubscriptionId = latestReceiptInfo.OriginalTransactionId,
            InvoiceId = latestReceiptInfo.TransactionId
        });

        // Check if there is previous subscription record
        var existingPayment = await GetPaymentSummaryBySubscriptionIdAsync(latestReceiptInfo.OriginalTransactionId);
        var (subscriptionStartDate, subscriptionEndDate) =
            await CalculateSubscriptionDurationAsync(userId, (PlanType)appleProduct.PlanType, appleProduct.IsUltimate);
        if (existingPayment == null)
        {
            var newPayment = new PaymentSummary
            {
                PaymentGrainId = paymentGrainId,
                OrderId = latestReceiptInfo.OriginalTransactionId,
                PlanType = (PlanType)appleProduct.PlanType,
                Amount = GetProductAmount(latestReceiptInfo.ProductId),
                Currency = appleProduct.Currency,
                UserId = userId,
                CreatedAt = purchaseDate,
                CompletedAt = DateTime.UtcNow,
                Status = PaymentStatus.Completed,
                SubscriptionId = latestReceiptInfo.OriginalTransactionId,
                PriceId = latestReceiptInfo.ProductId,
                SubscriptionStartDate = subscriptionStartDate,
                SubscriptionEndDate = subscriptionEndDate,
                Platform = PaymentPlatform.AppStore,

                AppStoreEnvironment = appleResponse.Environment
            };

            // Add invoice details
            var invoiceDetail = new UserBillingInvoiceDetail
            {
                InvoiceId = latestReceiptInfo.TransactionId,
                CreatedAt = purchaseDate,
                CompletedAt = DateTime.UtcNow,
                Status = PaymentStatus.Completed,
                SubscriptionStartDate = subscriptionStartDate,
                SubscriptionEndDate = subscriptionEndDate
            };

            newPayment.InvoiceDetails = new List<UserBillingInvoiceDetail> { invoiceDetail };
            await AddPaymentRecordAsync(newPayment);
        }
        else
        {
            _logger.LogWarning("[UserBillingGrain][VerifyAppStoreReceiptAsync] transaction exists {0}, {1}, {2})",
                userId, latestReceiptInfo.OriginalTransactionId, latestReceiptInfo.TransactionId);
        }

        // Update user quota
        var userQuotaGrain = GrainFactory.GetGrain<IUserQuotaGrain>(CommonHelper.GetUserQuotaGAgentId(userId));
        var subscriptionDto = appleProduct.IsUltimate
            ? await userQuotaGrain.GetSubscriptionAsync(true)
            : await userQuotaGrain.GetSubscriptionAsync();
        _logger.LogDebug("[UserBillingGrain][VerifyAppStoreReceiptAsync] allocate resource {0}, {1}, {2})",
            userId, latestReceiptInfo.OriginalTransactionId, latestReceiptInfo.TransactionId);
        var subscriptionIds = subscriptionDto.SubscriptionIds;
        if (!subscriptionIds.IsNullOrEmpty())
        {
            _logger.LogDebug("[UserBillingGrain][VerifyAppStoreReceiptAsync] cancel stripe subscription, userId: {0}, subscriptionId: {1}, cancel: {2}",
                userId, latestReceiptInfo.OriginalTransactionId, JsonConvert.SerializeObject(subscriptionIds));
            foreach (var subscriptionId in subscriptionIds)
            {
                await CancelSubscriptionAsync(new CancelSubscriptionDto
                {
                    UserId = userId,
                    SubscriptionId = subscriptionId,
                    CancellationReason = $"Upgrade to a new IAP {latestReceiptInfo.OriginalTransactionId}",
                    CancelAtPeriodEnd = true
                });
            }
        }
        if (subscriptionDto.IsActive)
        {
            if (SubscriptionHelper.GetPlanTypeLogicalOrder(subscriptionDto.PlanType) <= SubscriptionHelper.GetPlanTypeLogicalOrder((PlanType)appleProduct.PlanType))
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
            await userQuotaGrain.ResetRateLimitsAsync();
        }

        subscriptionDto.Status = PaymentStatus.Completed;
        await userQuotaGrain.UpdateSubscriptionAsync(subscriptionDto, appleProduct.IsUltimate);

        //UpdatePremium quota
        if (appleProduct.IsUltimate)
        {
            var premiumSubscriptionDto = await userQuotaGrain.GetSubscriptionAsync();
            if (!premiumSubscriptionDto.SubscriptionIds.IsNullOrEmpty())
            {
                _logger.LogDebug("[UserBillingGrain][VerifyAppStoreReceiptAsync] cancel stripe premiumSubscription, userId: {0}, subscriptionId: {1}, cancel: {2}",
                    userId, latestReceiptInfo.OriginalTransactionId, JsonConvert.SerializeObject(premiumSubscriptionDto.SubscriptionIds));
                foreach (var subscriptionId in premiumSubscriptionDto.SubscriptionIds)
                {
                    await CancelSubscriptionAsync(new CancelSubscriptionDto
                    {
                        UserId = userId,
                        SubscriptionId = subscriptionId,
                        CancellationReason = $"Upgrade to a new IAP {latestReceiptInfo.OriginalTransactionId}",
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
                await userQuotaGrain.UpdateSubscriptionAsync(premiumSubscriptionDto);
            }
        }
    }

    private async Task<PaymentSummary> GetPaymentSummaryBySubscriptionIdAsync(string subscriptionId)
    {
        if (string.IsNullOrEmpty(subscriptionId))
        {
            return null;
        }
        
        return State.PaymentHistory.FirstOrDefault(p => p.SubscriptionId == subscriptionId);
    }
    
    private decimal GetProductAmount(string productId)
    {
        // Try to get amount from Apple product configuration
        var appleProduct = _appleOptions.CurrentValue.Products.FirstOrDefault(p => p.ProductId == productId);
        if (appleProduct != null)
        {
            return appleProduct.Amount;
        }
        
        // If no configuration is found for the product, return default amount
        // Price can be inferred from product ID naming rules
        if (productId.Contains("monthly") || productId.Contains("month"))
        {
            return 9.99m; // Default monthly subscription price
        }
        else if (productId.Contains("yearly") || productId.Contains("year"))
        {
            return 99.99m; // Default annual subscription price
        }
        else if (productId.Contains("weekly") || productId.Contains("week"))
        {
            return 2.99m; // Default weekly subscription price
        }
        
        // Default value if price cannot be determined
        return 9.99m;
    }

    private async Task HandleRenewalPreferenceChangeAsync(string userId, AppStoreSubscriptionInfo transactionInfo)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGrain][HandleRenewalPreferenceChangeAsync] UserId is empty for transaction: {Id}", 
                transactionInfo.OriginalTransactionId);
            return;
        }
        
        _logger.LogInformation("[UserBillingGrain][HandleRenewalPreferenceChangeAsync] Processing renewal preference change for user {UserId}, product {ProductId}", 
            userId, transactionInfo.ProductId);
        
        // Update subscription status but do not affect current user rights
        await UpdateSubscriptionStateAsync(userId, transactionInfo, "DID_CHANGE_RENEWAL_PREF");
    }
    
    private async Task HandleRenewalStatusChangeAsync(string userId, AppStoreSubscriptionInfo transactionInfo)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGrain][HandleRenewalStatusChangeAsync] UserId is empty for transaction: {Id}", 
                transactionInfo.OriginalTransactionId);
            return;
        }
        
        _logger.LogInformation("[UserBillingGrain][HandleRenewalStatusChangeAsync] Processing renewal status change for user {UserId}, product {ProductId}, autoRenew: {AutoRenew}", 
            userId, transactionInfo.ProductId, transactionInfo.AutoRenewStatus);
        
        // Update subscription status but do not affect current user rights
        await UpdateSubscriptionStateAsync(userId, transactionInfo, "DID_CHANGE_RENEWAL_STATUS");
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

    private async Task HandleFailedRenewalAsync(string userId, AppStoreSubscriptionInfo transactionInfo)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGrain][HandleFailedRenewalAsync] UserId is empty for transaction: {Id}", 
                transactionInfo.OriginalTransactionId);
            return;
        }
        
        _logger.LogInformation("[UserBillingGrain][HandleFailedRenewalAsync] Processing failed renewal for user {UserId}, product {ProductId}", 
            userId, transactionInfo.ProductId);
        
        // Update subscription status - failed to renew
        await UpdateSubscriptionStateAsync(userId, transactionInfo, "DID_FAIL_TO_RENEW");
        
        // Depending on your business logic, you might want to:
        // 1. Send an email to the user about the failed payment
        // 2. Keep providing service if grace period is enabled
        // 3. Revoke access if no grace period
    }
    
    private async Task HandleDidRenewAsync(Guid userId, AppStoreJWSTransactionDecodedPayload transactionInfo,
        JWSRenewalInfoDecodedPayload jwsRenewalInfoDecodedPayload)
    {
        if (userId == default)
        {
            _logger.LogWarning("[UserBillingGrain][HandleDidRenewAsync] UserId is empty for transaction: {Id}, ", 
                transactionInfo.OriginalTransactionId);
            return;
        }
        
        _logger.LogInformation("[UserBillingGrain][HandleDidRenewAsync] Processing successful renewal for user {UserId}, product {ProductId}, OriginalTransaction: {Id}", 
            userId, transactionInfo.ProductId, transactionInfo.OriginalTransactionId);
        
        // Find existing subscription record
        var existingSubscription = await GetPaymentSummaryBySubscriptionIdAsync(transactionInfo.OriginalTransactionId);
        if (existingSubscription == null)
        {
            _logger.LogWarning("[UserBillingGrain][UpdateSubscriptionStateAsync] PaymentSummary not exist.{0}, {1}, {2}", 
                userId, transactionInfo.OriginalTransactionId, transactionInfo.TransactionId);
            await CreateAppStoreSubscriptionAsync(userId, transactionInfo);
            return;
        }

        var invoiceDetails = existingSubscription.InvoiceDetails ?? new List<UserBillingInvoiceDetail>();
        var invoiceDetail = invoiceDetails.FirstOrDefault(t => t.InvoiceId == transactionInfo.TransactionId);
        if (invoiceDetail != null)
        {
            _logger.LogWarning("[UserBillingGrain][UpdateSubscriptionStateAsync] Transaction processed user {UserId}, originaltransaction: {Id}, trancaction: {trancactionId}",
                userId, transactionInfo.OriginalTransactionId, transactionInfo.TransactionId);
            return;
        }

        // Create invoice details
        var appleProduct = await GetAppleProductConfigAsync(transactionInfo.ProductId);
        var purchaseDate = DateTimeOffset.FromUnixTimeMilliseconds(transactionInfo.PurchaseDate).DateTime;
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
        invoiceDetail = new UserBillingInvoiceDetail
        {
            InvoiceId = transactionInfo.TransactionId,
            CreatedAt = purchaseDate,
            CompletedAt = DateTime.UtcNow,
            Status = PaymentStatus.Completed,
            SubscriptionStartDate = subscriptionStartDate,
            SubscriptionEndDate = subscriptionEndDate
        };
        invoiceDetails.Add(invoiceDetail);
        existingSubscription.InvoiceDetails = invoiceDetails;
        await WriteStateAsync();
        
        // Grant or revoke user rights
        await UpdateUserQuotaOnApplePaySuccess(userId, transactionInfo, appleProduct);
        _logger.LogWarning("[UserBillingGrain][UpdateSubscriptionStateAsync] Transaction processed user {UserId}, product {ProductId}, originaltransaction: {Id}, trancaction: {trancactionId}",
            userId, transactionInfo.ProductId, transactionInfo.OriginalTransactionId, transactionInfo.TransactionId);
    }

    private async Task HandleGracePeriodExpiredAsync(string userId, AppStoreSubscriptionInfo transactionInfo)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGrain][HandleGracePeriodExpiredAsync] UserId is empty for transaction: {Id}", 
                transactionInfo.OriginalTransactionId);
            return;
        }
        
        _logger.LogInformation("[UserBillingGrain][HandleGracePeriodExpiredAsync] Processing grace period expiration for user {UserId}, product {ProductId}", 
            userId, transactionInfo.ProductId);
        
        // Update subscription status - grace period expired
        await UpdateSubscriptionStateAsync(userId, transactionInfo, "GRACE_PERIOD_EXPIRED");
        
        // Revoke user access
        await RevokeUserQuotaAsync(userId);
    }
    
    private async Task HandleOfferRedeemedAsync(string userId, AppStoreSubscriptionInfo transactionInfo)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGrain][HandleOfferRedeemedAsync] UserId is empty for transaction: {Id}", 
                transactionInfo.OriginalTransactionId);
            return;
        }
        
        _logger.LogInformation("[UserBillingGrain][HandleOfferRedeemedAsync] Processing offer redemption for user {UserId}, product {ProductId}", 
            userId, transactionInfo.ProductId);
        
        // Update subscription status - offer redeemed
        await UpdateSubscriptionStateAsync(userId, transactionInfo, "OFFER_REDEEMED");
    }
    
    private async Task HandlePriceIncreaseAsync(string userId, AppStoreSubscriptionInfo transactionInfo)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGrain][HandlePriceIncreaseAsync] UserId is empty for transaction: {Id}", 
                transactionInfo.OriginalTransactionId);
            return;
        }
        
        _logger.LogInformation("[UserBillingGrain][HandlePriceIncreaseAsync] Processing price increase for user {UserId}, product {ProductId}", 
            userId, transactionInfo.ProductId);
        
        // Just log the price increase event
        // The user's consent status will determine if the subscription continues
    }
    
    private async Task HandleRefundDeclinedAsync(string userId, AppStoreSubscriptionInfo transactionInfo)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGrain][HandleRefundDeclinedAsync] UserId is empty for transaction: {Id}", 
                transactionInfo.OriginalTransactionId);
            return;
        }
        
        _logger.LogInformation("[UserBillingGrain][HandleRefundDeclinedAsync] Processing refund decline for user {UserId}, product {ProductId}", 
            userId, transactionInfo.ProductId);
        
        // Just log the refund decline event
        // No action needed as the subscription status remains unchanged
    }
    
    private async Task HandleRenewalExtendedAsync(string userId, AppStoreSubscriptionInfo transactionInfo)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGrain][HandleRenewalExtendedAsync] UserId is empty for transaction: {Id}", 
                transactionInfo.OriginalTransactionId);
            return;
        }
        
        _logger.LogInformation("[UserBillingGrain][HandleRenewalExtendedAsync] Processing renewal extension for user {UserId}, product {ProductId}", 
            userId, transactionInfo.ProductId);
        
        // Update subscription status with the extended renewal date
        await UpdateSubscriptionStateAsync(userId, transactionInfo, "RENEWAL_EXTENDED");
    }
    
    private async Task HandleRenewalExtensionAsync(string userId, AppStoreSubscriptionInfo transactionInfo)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGrain][HandleRenewalExtensionAsync] UserId is empty for transaction: {Id}", 
                transactionInfo.OriginalTransactionId);
            return;
        }
        
        _logger.LogInformation("[UserBillingGrain][HandleRenewalExtensionAsync] Processing renewal extension attempt for user {UserId}, product {ProductId}", 
            userId, transactionInfo.ProductId);
        
        // Just log the renewal extension attempt
        // The actual extension will be reported in a subsequent RENEWAL_EXTENDED notification
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
            _logger.LogInformation("[UserBillingGrain][VerifyJwtSignature] Starting JWT signature verification using x5c certificate chain for environment: {Environment}", environment);
            
            // Split the JWT into its components
            var parts = jwt.Split('.');
            if (parts.Length != 3)
            {
                _logger.LogWarning("[UserBillingGrain][VerifyJwtSignature] Invalid JWT format: does not have three parts");
                return false;
            }

            // Decode and parse the header
            var headerJson = DecodeBase64Url(parts[0]);
            var header = JsonConvert.DeserializeObject<Dictionary<string, object>>(headerJson);

            // Validate algorithm
            if (!header.TryGetValue("alg", out var algorithm) || algorithm.ToString() != "ES256")
            {
                _logger.LogWarning("[UserBillingGrain][VerifyJwtSignature] Invalid or missing algorithm: {Algorithm}", algorithm);
                return false;
            }

            // Extract x5c certificate chain
            if (!header.TryGetValue("x5c", out var x5cObj) || x5cObj is not JArray x5cArray || x5cArray.Count == 0)
            {
                _logger.LogWarning("[UserBillingGrain][VerifyJwtSignature] Missing or invalid x5c certificate chain");
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
                    _logger.LogError(ex, "[UserBillingGrain][VerifyJwtSignature] Error decoding certificate from x5c chain");
                    return false;
                }
            }

            if (certificateChain.Count < 2)
            {
                _logger.LogWarning("[UserBillingGrain][VerifyJwtSignature] Certificate chain too short: {Count} certificates", certificateChain.Count);
                return false;
            }

            // Load Apple's root CA certificate
            var rootCertPath = Path.Combine(Path.GetDirectoryName(typeof(UserBillingGrain).Assembly.Location), 
                "ChatManager", "UserBilling", "Certificates", "AppleRootCA-G3.cer");
            if (!System.IO.File.Exists(rootCertPath))
            {
                _logger.LogError("[UserBillingGrain][VerifyJwtSignature] Apple Root CA certificate not found at: {Path}", rootCertPath);
                return false;
            }

            var rootCert = new X509Certificate2(rootCertPath);
            _logger.LogInformation("[UserBillingGrain][VerifyJwtSignature] Loaded Apple Root CA: {Subject}", rootCert.Subject);

            // Build and validate the certificate chain
            var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(rootCert);

            // Create leaf certificate from the first certificate in the chain
            var leafCert = new X509Certificate2(certificateChain[0]);
            _logger.LogInformation("[UserBillingGrain][VerifyJwtSignature] Validating leaf certificate: {Subject}", leafCert.Subject);

            // Validate the certificate chain
            var chainBuilt = chain.Build(leafCert);
            if (!chainBuilt)
            {
                foreach (var element in chain.ChainElements)
                {
                    foreach (var status in element.ChainElementStatus)
                    {
                        _logger.LogWarning("[UserBillingGrain][VerifyJwtSignature] Chain validation error: {Status} - {StatusInformation}",
                            status.Status, status.StatusInformation);
                    }
                }
                return false;
            }

            _logger.LogInformation("[UserBillingGrain][VerifyJwtSignature] Certificate chain validation successful");

            // Extract public key from leaf certificate for JWT verification
            var publicKey = leafCert.GetECDsaPublicKey();
            if (publicKey == null)
            {
                _logger.LogWarning("[UserBillingGrain][VerifyJwtSignature] Failed to extract ECDsa public key from leaf certificate");
                return false;
            }

            // Verify JWT signature using the public key from the certificate
            var headerAndPayload = $"{parts[0]}.{parts[1]}";
            var signature = Base64UrlDecodeToBytes(parts[2]);
            
            var dataToVerify = System.Text.Encoding.UTF8.GetBytes(headerAndPayload);
            var isValid = publicKey.VerifyData(dataToVerify, signature, HashAlgorithmName.SHA256);

            if (isValid)
            {
                _logger.LogInformation("[UserBillingGrain][VerifyJwtSignature] JWT signature verification successful");
            }
            else
            {
                _logger.LogWarning("[UserBillingGrain][VerifyJwtSignature] JWT signature verification failed");
            }

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UserBillingGrain][VerifyJwtSignature] Unexpected error during JWT signature verification");
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
    /// Generate JWT token for App Store API using RSA private key (RS256)
    /// </summary>
    /// <returns>JWT token string</returns>
    private string GenerateAppStoreApiJwtWithRsa()
    {
        try
        {
            _logger.LogInformation("[UserPaymentGrain][GenerateAppStoreApiJwtWithRsa] Generating JWT token for App Store API using RSA");
            string keyId = _appleOptions.CurrentValue.KeyId;
            string issuerId = _appleOptions.CurrentValue.IssuerId;
            string bundleId = _appleOptions.CurrentValue.BundleId;
            string privateKeyContent = _appleOptions.CurrentValue.PrivateKey; // Should be PKCS#8 Base64 encoded RSA private key

            // Step 1: Create JWT header
            var header = new Dictionary<string, object>
            {
                { "alg", "RS256" }, // Algorithm must be RS256 (RSA with SHA-256)
                { "kid", keyId },    // Key ID from App Store Connect
                { "typ", "JWT" }
            };

            // Step 2: Create JWT payload
            var now = DateTimeOffset.UtcNow;
            var expirationTime = now.AddMinutes(10);
            var claims = new Dictionary<string, object>
            {
                { "iss", issuerId },
                { "iat", now.ToUnixTimeSeconds() },
                { "exp", expirationTime.ToUnixTimeSeconds() },
                { "aud", "appstoreconnect-v1" },
                { "bid", bundleId }
            };

            // Step 3: Decode the Base64 encoded private key
            byte[] privateKeyBytes = Convert.FromBase64String(privateKeyContent);
            using (var rsa = RSA.Create())
            {
                rsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);
                var securityKey = new RsaSecurityKey(rsa) { KeyId = Guid.NewGuid().ToString() };
                var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

                // Step 4: Create and sign the JWT token
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
                _logger.LogDebug("[UserPaymentGrain][GenerateAppStoreApiJwtWithRsa] JWT token generated successfully");
                return token;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UserPaymentGrain][GenerateAppStoreApiJwtWithRsa] Error generating JWT token: {ErrorMessage}", ex.Message);
            throw new InvalidOperationException("Failed to generate App Store API JWT token with RSA. Please check your configuration.", ex);
        }
    }
}