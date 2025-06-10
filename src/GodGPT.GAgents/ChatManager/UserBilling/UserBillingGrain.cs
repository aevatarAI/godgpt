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
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

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
    Task<bool> HandleAppStoreNotificationAsync(Guid userId, string jsonPayload, string notificationToken);
    
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
                IsUltimate = product.IsUltimate,  // Configuration-driven Ultimate detection
                PlanDisplayName = SubscriptionHelper.GetPlanDisplayName(planType, product.IsUltimate)
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
                dailyAvgPrice = Math.Round(product.Amount / 7, 2).ToString();
            }
            else if (product.PlanType == (int)PlanType.Month)
            {
                dailyAvgPrice = Math.Round(product.Amount / 30, 2).ToString();
            }
            else if (product.PlanType == (int)PlanType.Year)
            {
                dailyAvgPrice = Math.Round(product.Amount / 390, 2).ToString();
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
                if (subscriptionInfoDto.PlanType <= (PlanType) productConfig.PlanType)
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
                    PriceId = paymentSummary.PriceId
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
        var currentSubscription = await userQuotaGrain.GetSubscriptionAsync();

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

    public async Task<bool> HandleAppStoreNotificationAsync(Guid userId, string jsonPayload, string notificationToken)
    {
        try
        {
            // 1. Verify notification authenticity
            if (!VerifyNotificationAuthenticity(notificationToken))
            {
                _logger.LogWarning("[UserBillingGrain][HandleAppStoreNotificationAsync] Invalid notification token");
                return false;
            }
            
            // 2. Parse notification data
            var notification = System.Text.Json.JsonSerializer.Deserialize<AppStoreServerNotification>(jsonPayload);

            var unifiedReceipt = notification.UnifiedReceipt;
            var latestReceipt = unifiedReceipt.LatestReceipt;
            
            var verifyResponse = await VerifyAppStoreReceiptAsync(new VerifyReceiptRequestDto
            {
                UserId = userId.ToString(),
                SandboxMode = false,
                ReceiptData = latestReceipt
            }, false);
            if (!verifyResponse.IsValid)
            {
                _logger.LogWarning("[UserBillingGrain][HandleAppStoreNotificationAsync] Invalid latestReceipt");
                return false;
            }

            // 3. Extract key information
            var notificationType = notification.NotificationType;
            var environment = notification.Environment;
            var appStoreTransactionInfo = ExtractTransactionInfo(notification);
            
            // 4. Log notification information
            _logger.LogInformation("[UserBillingGrain][HandleAppStoreNotificationAsync] Received notification type: {Type}, environment: {Env}", 
                notificationType, environment);
            
            // 5. Process based on notification type
            switch (notificationType)
            {
                case "INITIAL_BUY":
                    await HandleInitialPurchaseAsync(appStoreTransactionInfo);
                    break;
                    
                case "RENEWAL":
                    await HandleRenewalAsync(appStoreTransactionInfo);
                    break;
                    
                case "INTERACTIVE_RENEWAL":
                    await HandleInteractiveRenewalAsync(appStoreTransactionInfo);
                    break;
                    
                case "CANCEL":
                    await HandleCancellationAsync(appStoreTransactionInfo);
                    break;
                    
                case "REFUND":
                    await HandleRefundAsync(appStoreTransactionInfo);
                    break;
                
                case "DID_CHANGE_RENEWAL_PREF":
                    await HandleRenewalPreferenceChangeAsync(appStoreTransactionInfo);
                    break;
                
                case "DID_CHANGE_RENEWAL_STATUS":
                    await HandleRenewalStatusChangeAsync(appStoreTransactionInfo);
                    break;
                    
                default:
                    _logger.LogWarning("[UserBillingGrain][HandleAppStoreNotificationAsync] Unknown notification type: {Type}", notificationType);
                    break;
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UserBillingGrain][HandleAppStoreNotificationAsync] Error processing notification: {Message}", ex.Message);
            return false;
        }
    }

    private bool VerifyNotificationAuthenticity(string notificationToken)
    {
        // If notification token is not configured, reject all notifications
        if (string.IsNullOrEmpty(_appleOptions.CurrentValue.NotificationToken))
        {
            _logger.LogWarning("[UserBillingGrain][VerifyNotificationAuthenticity] No notification token configured");
            return false;
        }
        
        // Verify if the notification token matches the configured token
        if (notificationToken != _appleOptions.CurrentValue.NotificationToken)
        {
            _logger.LogWarning("[UserBillingGrain][VerifyNotificationAuthenticity] Invalid notification token provided");
            return false;
        }
        
        // Additional verification logic can be added later
        
        return true;
    }
    
    private AppStoreSubscriptionInfo ExtractTransactionInfo(AppStoreServerNotification notification)
    {
        if (notification == null || notification.UnifiedReceipt == null)
        {
            _logger.LogWarning("[UserBillingGrain][ExtractTransactionInfo] Notification or UnifiedReceipt is null");
            return new AppStoreSubscriptionInfo();
        }
        
        // Find the latest receipt information (sorted by expiration date)
        var latestReceiptInfo = notification.UnifiedReceipt.LatestReceiptInfo?
            .OrderByDescending(r => 
                long.TryParse(r.ExpiresDateMs, out var expiresMs) ? expiresMs : 0)
            .FirstOrDefault();
        
        if (latestReceiptInfo == null)
        {
            _logger.LogWarning("[UserBillingGrain][ExtractTransactionInfo] No receipt info found in notification");
            return new AppStoreSubscriptionInfo();
        }
        
        // Parse timestamp to DateTime
        DateTime purchaseDate = DateTime.UtcNow;
        DateTime expiresDate = DateTime.UtcNow.AddDays(30); // Default 30 days
        
        if (long.TryParse(latestReceiptInfo.PurchaseDateMs, out var purchaseMs))
        {
            purchaseDate = DateTimeOffset.FromUnixTimeMilliseconds(purchaseMs).DateTime;
        }
        
        if (long.TryParse(latestReceiptInfo.ExpiresDateMs, out var expiresMs))
        {
            expiresDate = DateTimeOffset.FromUnixTimeMilliseconds(expiresMs).DateTime;
        }
        
        // Parse trial period flag
        bool isTrialPeriod = false;
        if (!string.IsNullOrEmpty(latestReceiptInfo.IsTrialPeriod))
        {
            bool.TryParse(latestReceiptInfo.IsTrialPeriod, out isTrialPeriod);
        }
        
        // Parse auto-renewal status
        bool autoRenewStatus = false;
        if (notification.UnifiedReceipt.PendingRenewalInfo?.Count > 0)
        {
            var pendingRenewal = notification.UnifiedReceipt.PendingRenewalInfo
                .FirstOrDefault(p => p.OriginalTransactionId == latestReceiptInfo.OriginalTransactionId);
                
            if (pendingRenewal != null && !string.IsNullOrEmpty(pendingRenewal.AutoRenewStatus))
            {
                autoRenewStatus = pendingRenewal.AutoRenewStatus == "1";
            }
        }
        else
        {
            // If there is no PendingRenewalInfo, use the value from the notification
            autoRenewStatus = notification.AutoRenewStatus;
        }
        
        // Build and return the subscription information object
        return new AppStoreSubscriptionInfo
        {
            OriginalTransactionId = latestReceiptInfo.OriginalTransactionId,
            TransactionId = latestReceiptInfo.TransactionId,
            ProductId = latestReceiptInfo.ProductId,
            PurchaseDate = purchaseDate,
            ExpiresDate = expiresDate,
            IsTrialPeriod = isTrialPeriod,
            AutoRenewStatus = autoRenewStatus,
            Environment = notification.Environment,
            LatestReceiptData = notification.UnifiedReceipt.LatestReceipt
        };
    }
    
    private async Task<string> GetUserIdByOriginalTransactionIdAsync(string originalTransactionId)
    {
        var payment = await GetPaymentSummaryBySubscriptionIdAsync(originalTransactionId);
        return payment?.UserId.ToString();
    }
    
    private async Task HandleInitialPurchaseAsync(AppStoreSubscriptionInfo transactionInfo)
    {
        // Find user ID (associated via OriginalTransactionId)
        var userId = await GetUserIdByOriginalTransactionIdAsync(transactionInfo.OriginalTransactionId);
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGrain][HandleInitialPurchaseAsync] Cannot find user for transaction: {Id}", 
                transactionInfo.OriginalTransactionId);
            return;
        }
        
        // Update subscription status
        await UpdateSubscriptionStateAsync(userId, transactionInfo, "INITIAL_BUY");
    }
    
    private async Task HandleRenewalAsync(AppStoreSubscriptionInfo transactionInfo)
    {
        // Find user ID (associated via OriginalTransactionId)
        var userId = await GetUserIdByOriginalTransactionIdAsync(transactionInfo.OriginalTransactionId);
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGrain][HandleRenewalAsync] Cannot find user for transaction: {Id}", 
                transactionInfo.OriginalTransactionId);
            return;
        }
        
        _logger.LogInformation("[UserBillingGrain][HandleRenewalAsync] Processing renewal for user {UserId}, product {ProductId}", 
            userId, transactionInfo.ProductId);
            
        // Update subscription status
        await UpdateSubscriptionStateAsync(userId, transactionInfo, "RENEWAL");
    }
    
    private async Task HandleInteractiveRenewalAsync(AppStoreSubscriptionInfo transactionInfo)
    {
        // Find user ID (associated via OriginalTransactionId)
        var userId = await GetUserIdByOriginalTransactionIdAsync(transactionInfo.OriginalTransactionId);
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGrain][HandleInteractiveRenewalAsync] Cannot find user for transaction: {Id}", 
                transactionInfo.OriginalTransactionId);
            return;
        }
        
        _logger.LogInformation("[UserBillingGrain][HandleInteractiveRenewalAsync] Processing interactive renewal for user {UserId}, product {ProductId}", 
            userId, transactionInfo.ProductId);
            
        // Interactive renewal is similar to auto-renewal, but may have different business logic
        await UpdateSubscriptionStateAsync(userId, transactionInfo, "INTERACTIVE_RENEWAL");
    }
    
    private async Task HandleCancellationAsync(AppStoreSubscriptionInfo transactionInfo)
    {
        // Find user ID (associated via OriginalTransactionId)
        var userId = await GetUserIdByOriginalTransactionIdAsync(transactionInfo.OriginalTransactionId);
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGrain][HandleCancellationAsync] Cannot find user for transaction: {Id}", 
                transactionInfo.OriginalTransactionId);
            return;
        }
        
        _logger.LogInformation("[UserBillingGrain][HandleCancellationAsync] Processing cancellation for user {UserId}, product {ProductId}, expires on {ExpiresDate}", 
            userId, transactionInfo.ProductId, transactionInfo.ExpiresDate);
        
        // Update subscription status to cancelled
        // Note: Even after subscription cancellation, users can still enjoy the services of the paid period
        await UpdateSubscriptionStateAsync(userId, transactionInfo, "CANCEL");
    }
    
    private async Task HandleRefundAsync(AppStoreSubscriptionInfo transactionInfo)
    {
        // Find user ID (associated via OriginalTransactionId)
        var userId = await GetUserIdByOriginalTransactionIdAsync(transactionInfo.OriginalTransactionId);
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGrain][HandleRefundAsync] Cannot find user for transaction: {Id}", 
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
        // Map notification type to payment status
        return eventType switch
        {
            "INITIAL_BUY" => PaymentStatus.Completed,
            "RENEWAL" => PaymentStatus.Completed,
            "INTERACTIVE_RENEWAL" => PaymentStatus.Completed,
            "CANCEL" => DateTime.UtcNow >= subscriptionInfo.ExpiresDate 
                ? PaymentStatus.Cancelled 
                : PaymentStatus.CancelPending, // Distinguish between immediate cancellation and planned expiration cancellation
            "REFUND" => PaymentStatus.Refunded,
            "DID_CHANGE_RENEWAL_PREF" => PaymentStatus.Completed, // Changing auto-renewal settings but status remains completed
            "DID_CHANGE_RENEWAL_STATUS" => PaymentStatus.Completed, // Changing auto-renewal status but status remains completed
            _ => PaymentStatus.Unknown
        };
    }
    
    private PaymentType MapToPaymentType(string eventType)
    {
        // Map notification type to payment type
        return eventType switch
        {
            "INITIAL_BUY" => PaymentType.Subscription,
            "RENEWAL" => PaymentType.Renewal,
            "INTERACTIVE_RENEWAL" => PaymentType.Renewal,
            "CANCEL" => PaymentType.Cancellation,
            "REFUND" => PaymentType.Refund,
            "DID_CHANGE_RENEWAL_PREF" => PaymentType.Subscription, // Changing renewal settings is considered a subscription modification
            "DID_CHANGE_RENEWAL_STATUS" => PaymentType.Subscription, // Changing renewal status is considered a subscription modification
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
                case "VERIFICATION":
                    // Grant or update user rights
                    _logger.LogInformation("[UserBillingGrain][UpdateUserQuotaAsync] Updating user quota for {UserId} with product {ProductId}, expires on {ExpiresDate}",
                        userId, subscriptionInfo.ProductId, subscriptionInfo.ExpiresDate);
                    await userQuotaGrain.UpdateQuotaAsync(subscriptionInfo.ProductId, subscriptionInfo.ExpiresDate);
                    break;
                
                case "CANCEL":
                    // For cancellation of subscription, only revoke rights immediately when current has expired
                    if (DateTime.UtcNow >= subscriptionInfo.ExpiresDate)
                    {
                        _logger.LogInformation("[UserBillingGrain][UpdateUserQuotaAsync] Resetting quota for {UserId} due to cancelled subscription",
                            userId);
                        await userQuotaGrain.ResetQuotaAsync();
                    }
                    else
                    {
                        _logger.LogInformation("[UserBillingGrain][UpdateUserQuotaAsync] Subscription cancelled but still active until {ExpiresDate} for {UserId}",
                            subscriptionInfo.ExpiresDate, userId);
                    }
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
                        return appleProduct.Ultimate == isUltimate;
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
    
    public async Task<AppStoreSubscriptionResponseDto> CreateAppStoreSubscriptionAsync(CreateAppStoreSubscriptionDto createSubscriptionDto)
    {
        try
        {
            // 1. Verify App Store receipt
            var verifyReceiptRequest = new VerifyReceiptRequestDto
            {
                ReceiptData = createSubscriptionDto.ReceiptData,
                SandboxMode = createSubscriptionDto.SandboxMode,
                UserId = createSubscriptionDto.UserId
            };
            
            var verifyResponse = await VerifyAppStoreReceiptAsync(verifyReceiptRequest, true);
            
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
            await CalculateSubscriptionDurationAsync(userId, (PlanType)appleProduct.PlanType, appleProduct.Ultimate);
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
                SubscriptionStartDate = subscriptionEndDate,
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
        var subscriptionDto = appleProduct.Ultimate
            ? await userQuotaGrain.GetSubscriptionAsync(true)
            : await userQuotaGrain.GetSubscriptionAsync();
        _logger.LogDebug("[UserBillingGrain][VerifyAppStoreReceiptAsync] allocate resource {0}, {1}, {2})",
            userId, latestReceiptInfo.OriginalTransactionId, latestReceiptInfo.TransactionId);
        if (subscriptionDto.IsActive)
        {
            if (subscriptionDto.PlanType <= (PlanType)appleProduct.PlanType)
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
        await userQuotaGrain.UpdateSubscriptionAsync(subscriptionDto, appleProduct.Ultimate);

        //UpdatePremium quota
        if (appleProduct.Ultimate)
        {
            var subscriptionInfoDto = await userQuotaGrain.GetSubscriptionAsync();
            if (subscriptionInfoDto.IsActive)
            {
                subscriptionInfoDto.StartDate =
                    GetSubscriptionEndDate(subscriptionDto.PlanType, subscriptionInfoDto.StartDate);
                subscriptionInfoDto.EndDate =
                    GetSubscriptionEndDate(subscriptionDto.PlanType, subscriptionInfoDto.EndDate);
                await userQuotaGrain.UpdateSubscriptionAsync(subscriptionInfoDto);
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

    private async Task HandleRenewalPreferenceChangeAsync(AppStoreSubscriptionInfo transactionInfo)
    {
        // Find user ID (associated via OriginalTransactionId)
        var userId = await GetUserIdByOriginalTransactionIdAsync(transactionInfo.OriginalTransactionId);
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGrain][HandleRenewalPreferenceChangeAsync] Cannot find user for transaction: {Id}", 
                transactionInfo.OriginalTransactionId);
            return;
        }
        
        _logger.LogInformation("[UserBillingGrain][HandleRenewalPreferenceChangeAsync] Processing renewal preference change for user {UserId}, product {ProductId}", 
            userId, transactionInfo.ProductId);
        
        // Update subscription status but do not affect current user rights
        await UpdateSubscriptionStateAsync(userId, transactionInfo, "DID_CHANGE_RENEWAL_PREF");
    }
    
    private async Task HandleRenewalStatusChangeAsync(AppStoreSubscriptionInfo transactionInfo)
    {
        // Find user ID (associated via OriginalTransactionId)
        var userId = await GetUserIdByOriginalTransactionIdAsync(transactionInfo.OriginalTransactionId);
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[UserBillingGrain][HandleRenewalStatusChangeAsync] Cannot find user for transaction: {Id}", 
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
            .OrderByDescending(p => p.PlanType)
            .Select(p => p.PlanType)
            .DefaultIfEmpty(PlanType.None)
            .First();

        return maxPlanType;
    }
}