using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserBilling.Payment;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using PaymentMethod = Aevatar.Application.Grains.Common.Constants.PaymentMethod;

namespace Aevatar.Application.Grains.ChatManager.UserBilling;

public interface IUserBillingGrain : IGrainWithStringKey
{
    Task<List<StripeProductDto>> GetStripeProductsAsync();
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
}

public class UserBillingGrain : Grain<UserBillingState>, IUserBillingGrain
{
    private readonly ILogger<UserBillingGrain> _logger;
    private readonly IOptionsMonitor<StripeOptions> _stripeOptions;
    
    private IStripeClient _client; 
    
    public UserBillingGrain(ILogger<UserBillingGrain> logger, IOptionsMonitor<StripeOptions> stripeOptions)
    {
        _logger = logger;
        _stripeOptions = stripeOptions;
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
            var dailyAvgPrice = string.Empty;
            if (product.PlanType == (int)PlanType.Day)
            {
                dailyAvgPrice = product.Amount.ToString();
            }
            else if (product.PlanType == (int)PlanType.Month)
            {
                dailyAvgPrice = Math.Round(product.Amount / 30, 2).ToString();
            }
            else if (product.PlanType == (int)PlanType.Year)
            {
                dailyAvgPrice = Math.Round(product.Amount / 390, 2).ToString();
            }

            productDtos.Add(new StripeProductDto
            {
                PlanType = (PlanType)product.PlanType,
                PriceId = product.PriceId,
                Mode = product.Mode,
                Amount = product.Amount,
                DailyAvgPrice = dailyAvgPrice,
                Currency = product.Currency
            });
        }
        
        _logger.LogDebug("[UserBillingGrain][GetStripeProductsAsync] Successfully retrieved {Count} products",
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
            throw;
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
            options.CancelUrl = _stripeOptions.CurrentValue.CancelUrl;
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
            throw;
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
            throw;
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
            throw;
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
                    PaymentMethodTypes = createSubscriptionDto.Platform?.ToLower() == "ios" 
                        ? new List<string> { "card", "apple_pay" }
                        : new List<string> { "card" }
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
            
            var initResult = await paymentGrain.InitializePaymentAsync(paymentState);
            if (!initResult.Success)
            {
                _logger.LogError(
                    "[UserBillingGrain][CreateSubscriptionAsync] Failed to initialize payment grain: {ErrorMessage}",
                    initResult.Message);
                throw new Exception($"Failed to initialize payment grain: {initResult.Message}");
            }
            var paymentDetails = initResult.Data;
            await CreateOrUpdatePaymentSummaryAsync(paymentDetails, null);
            _logger.LogInformation(
                "[UserBillingGrain][CreateSubscriptionAsync] Created/Updated payment record with ID: {PaymentId} for session: {subscription}",
                paymentDetails.Id, subscription.Id);
            
            var response = new SubscriptionResponseDto
            {
                SubscriptionId = subscription.Id,
                CustomerId = customerId,
                ClientSecret = subscription.LatestInvoice.ConfirmationSecret.ClientSecret
            };

            return response;
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex,
                "[UserBillingGrain][CreateSubscriptionAsync] Stripe error: {ErrorMessage}",
                ex.StripeError?.Message);
            throw;
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
        var subscriptionInfoDto = await userQuotaGrain.GetSubscriptionAsync();

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
            await userQuotaGrain.UpdateSubscriptionAsync(subscriptionInfoDto);
        } else if (invoiceDetail != null && invoiceDetail.Status == PaymentStatus.Cancelled && subscriptionIds.Contains(paymentSummary.SubscriptionId))
        {
            _logger.LogDebug("[UserBillingGrain][HandleStripeWebhookEventAsync] Cancel User subscription {0}, {1}, {2}",
                userId, paymentSummary.SubscriptionId, invoiceDetail.InvoiceId);
            subscriptionIds.Remove(paymentSummary.SubscriptionId);
            if (subscriptionIds.IsNullOrEmpty())
            {
                subscriptionInfoDto.PlanType = PlanType.None;
            }
            subscriptionInfoDto.SubscriptionIds = subscriptionIds;
            await userQuotaGrain.UpdateSubscriptionAsync(subscriptionInfoDto);
        }
        else if (invoiceDetail != null && invoiceDetail.Status == PaymentStatus.Refunded && invoiceIds.Contains(invoiceDetail.InvoiceId))
        {
            _logger.LogDebug("[UserBillingGrain][HandleStripeWebhookEventAsync] Refund User subscription {0}, {1}, {2}",
                userId, paymentSummary.SubscriptionId, invoiceDetail.InvoiceId);
            var diff = (paymentSummary.SubscriptionEndDate - DateTime.UtcNow).Days;
            if (diff < 0)
            {
                diff = 0;
            }
            subscriptionInfoDto.EndDate = subscriptionInfoDto.EndDate.AddDays(-diff);
            subscriptionIds.Remove(paymentSummary.SubscriptionId);
            if (subscriptionIds.IsNullOrEmpty())
            {
                subscriptionInfoDto.PlanType = PlanType.None;
            }

            subscriptionInfoDto.SubscriptionIds = subscriptionIds;
            await userQuotaGrain.UpdateSubscriptionAsync(subscriptionInfoDto);
        }
        
        return true;
    }

    private async Task<Tuple<DateTime, DateTime>> CalculateSubscriptionDurationAsync(Guid userId, StripeProduct productConfig)
    {
        DateTime subscriptionStartDate;
        DateTime subscriptionEndDate;
        var userQuotaGrain = GrainFactory.GetGrain<IUserQuotaGrain>(CommonHelper.GetUserQuotaGAgentId(userId));
        var subscriptionInfoDto = await userQuotaGrain.GetSubscriptionAsync();
        if (subscriptionInfoDto.IsActive)
        {
            subscriptionStartDate = subscriptionInfoDto.EndDate;
        }
        else
        {
            subscriptionStartDate = DateTime.UtcNow;
        }
        subscriptionEndDate = GetSubscriptionEndDate((PlanType)productConfig.PlanType, subscriptionStartDate);
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

        // Return paginated results ordered by most recent first
        return paymentHistories
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
                SubscriptionId = paymentDetails.SubscriptionId,
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

        _logger.LogInformation(
            "[UserBillingGrain][ValidateSubscriptionUpgradePath] Validating upgrade path: Current={CurrentPlan}, Target={TargetPlan}",
            currentSubscription.PlanType, productConfig.PlanType);

        switch (currentSubscription.PlanType)
        {
            case PlanType.Day:
                if (productConfig.PlanType == (int)PlanType.Day)
                {
                    _logger.LogWarning(
                        "[UserBillingGrain][ValidateSubscriptionUpgradePath] Invalid upgrade path: User with Day subscription trying to purchase Day subscription");
                    throw new InvalidOperationException(
                        "Daily subscription users can only upgrade to monthly or yearly subscriptions");
                }

                break;

            case PlanType.Month:
                if (productConfig.PlanType == (int)PlanType.Day || productConfig.PlanType == (int)PlanType.Month)
                {
                    _logger.LogWarning(
                        "[UserBillingGrain][ValidateSubscriptionUpgradePath] Invalid upgrade path: User with Month subscription trying to purchase Day/Month subscription");
                    throw new InvalidOperationException(
                        "Monthly subscription users can only upgrade to yearly subscriptions");
                }

                break;

            case PlanType.Year:
                _logger.LogWarning(
                    "[UserBillingGrain][ValidateSubscriptionUpgradePath] Invalid upgrade path: User with Year subscription trying to purchase Day/Month subscription");
                throw new InvalidOperationException("Yearly subscription users can only renew yearly subscriptions");
                break;
        }

        _logger.LogInformation(
            "[UserBillingGrain][ValidateSubscriptionUpgradePath] Valid upgrade path: {CurrentPlan} -> {NewPlan}",
            currentSubscription.PlanType, productConfig.PlanType);
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

    private DateTime GetSubscriptionEndDate(PlanType planType, DateTime startDate)
    {
        var endDate = startDate;
        switch (planType)
        {
            case PlanType.Day:
                return endDate.AddDays(1);
            case PlanType.Month:
                return endDate.AddMonths(1);
            case PlanType.Year:
                return endDate.AddYears(1).AddDays(30);
            default:
                throw new ArgumentException($"Invalid plan type: {planType}");
        }
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
}