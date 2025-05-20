using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserBilling.Payment;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Core;
using Stripe;
using Stripe.Checkout;
using PaymentMethod = Aevatar.Application.Grains.Common.Constants.PaymentMethod;

namespace Aevatar.Application.Grains.ChatManager.UserBilling;

public interface IUserBillingGrain : IGrainWithStringKey
{
    Task<List<StripeProductDto>> GetStripeProductsAsync();
    Task<string> GetOrCreateStripeCustomerAsync(string userId = null);
    Task<string> CreateCheckoutSessionAsync(CreateCheckoutSessionDto createCheckoutSessionDto);
    Task<Guid> AddPaymentRecordAsync(PaymentSummary paymentSummary);
    Task<PaymentSummary> GetPaymentSummaryAsync(Guid paymentId);
    Task<List<PaymentSummary>> GetPaymentHistoryAsync(int page = 1, int pageSize = 10);
    Task<bool> UpdatePaymentStatusAsync(PaymentSummary payment, PaymentStatus newStatus);
    Task<bool> HandleStripeWebhookEventAsync(string jsonPayload, string stripeSignature);
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
            await CreateOrUpdatePaymentSummaryAsync(paymentDetails, session, true);
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
            _logger.LogError("[UserBillingGrain][HandleStripeWebhookEventAsync] error. {0}", grainResultDto.Message);
            return false;
        }

        var userId = detailsDto.UserId;
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
            subscriptionStartDate = DateTime.UtcNow.Date;
        }
        var productConfig = await GetProductConfigAsync(detailsDto.PriceId);
        subscriptionEndDate = GetSubscriptionEndDate((PlanType) productConfig.PlanType, subscriptionStartDate);
        var paymentSummary = await CreateOrUpdatePaymentSummaryAsync(detailsDto, null, true, subscriptionStartDate, subscriptionEndDate);
        _logger.LogDebug("[UserBillingGrain][HandleStripeWebhookEventAsync] payment status{0}, {1}", paymentSummary.Status, userId);
        
        if (paymentSummary.Status == PaymentStatus.Completed)
        {
            _logger.LogDebug("[UserBillingGrain][HandleStripeWebhookEventAsync] Update User subscription {0}", userId);
            
            if (subscriptionInfoDto.IsActive)
            {
                subscriptionInfoDto.PlanType = (PlanType) productConfig.PlanType;
                subscriptionInfoDto.EndDate =
                    GetSubscriptionEndDate(subscriptionInfoDto.PlanType, subscriptionInfoDto.EndDate);
                subscriptionInfoDto.Status = PaymentStatus.Completed;
            }
            else
            {
                subscriptionInfoDto.IsActive = true;
                subscriptionInfoDto.PlanType = (PlanType) productConfig.PlanType;
                subscriptionInfoDto.StartDate = DateTime.UtcNow.Date;
                subscriptionInfoDto.EndDate =
                    GetSubscriptionEndDate(subscriptionInfoDto.PlanType, subscriptionInfoDto.StartDate);
                subscriptionInfoDto.Status = PaymentStatus.Completed;
                
            }
            await userQuotaGrain.UpdateSubscriptionAsync(subscriptionInfoDto);
        }
        return true;
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

        // Return paginated results ordered by most recent first
        return State.PaymentHistory
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
    
    private async Task<PaymentSummary> CreateOrUpdatePaymentSummaryAsync(
        PaymentDetailsDto paymentDetails,
        Session session = null,
        bool? isSubscriptionRenewal = null,
        DateTime? subscriptionStartDate = null,
        DateTime? subscriptionEndDate = null
    )
    {
        var existingPaymentSummary = State.PaymentHistory
            .FirstOrDefault(p => p.PaymentGrainId == paymentDetails.Id ||
                                 (!string.IsNullOrEmpty(paymentDetails.OrderId) &&
                                  p.OrderId == paymentDetails.OrderId));

        var productConfig = await GetProductConfigAsync(paymentDetails.PriceId);

        if (existingPaymentSummary != null)
        {
            _logger.LogInformation(
                "[UserBillingGrain][CreateOrUpdatePaymentSummaryAsync] Updating existing payment record with ID: {PaymentId}",
                existingPaymentSummary.PaymentGrainId);

            existingPaymentSummary.PaymentGrainId = paymentDetails.Id;
            existingPaymentSummary.OrderId = paymentDetails.OrderId;
            existingPaymentSummary.UserId = paymentDetails.UserId;
            existingPaymentSummary.PlanType = (PlanType)productConfig.PlanType;
            existingPaymentSummary.Amount = productConfig.Amount;
            existingPaymentSummary.Currency = productConfig.Currency;
            existingPaymentSummary.Status = paymentDetails.Status;
            existingPaymentSummary.PaymentType = paymentDetails.PaymentType;
            existingPaymentSummary.Method = paymentDetails.Method;
            existingPaymentSummary.Platform = paymentDetails.Platform;
            if (isSubscriptionRenewal != null)
            {
                existingPaymentSummary.IsSubscriptionRenewal = (bool)isSubscriptionRenewal;
            }

            existingPaymentSummary.SubscriptionId = paymentDetails.SubscriptionId;
            if (subscriptionStartDate != null)
            {
                existingPaymentSummary.SubscriptionStartDate = (DateTime)subscriptionStartDate;
            }

            if (subscriptionEndDate != null)
            {
                existingPaymentSummary.SubscriptionEndDate = (DateTime)subscriptionEndDate;
            }

            if (paymentDetails.Status == PaymentStatus.Completed && !existingPaymentSummary.CompletedAt.HasValue)
            {
                existingPaymentSummary.CompletedAt = paymentDetails.CompletedAt ?? DateTime.UtcNow;
            }

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
                PlanType = (PlanType)productConfig.PlanType,
                Amount = productConfig.Amount,
                Currency = productConfig.Currency,
                CreatedAt = paymentDetails.CreatedAt,
                Status = paymentDetails.Status,
                PaymentType = paymentDetails.PaymentType,
                Method = paymentDetails.Method,
                Platform = paymentDetails.Platform,
                SubscriptionId = paymentDetails.SubscriptionId,
                SessionId = session?.Id
            };
            if (isSubscriptionRenewal != null)
            {
                newPaymentSummary.IsSubscriptionRenewal = (bool)isSubscriptionRenewal;
            }

            if (subscriptionStartDate != null)
            {
                newPaymentSummary.SubscriptionStartDate = (DateTime)subscriptionStartDate;
            }

            if (subscriptionEndDate != null)
            {
                newPaymentSummary.SubscriptionEndDate = (DateTime)subscriptionEndDate;
            }

            if (paymentDetails.Status == PaymentStatus.Completed)
            {
                newPaymentSummary.CompletedAt = paymentDetails.CompletedAt ?? DateTime.UtcNow;
            }

            await AddPaymentRecordAsync(newPaymentSummary);

            _logger.LogInformation(
                "[UserBillingGrain][CreateOrUpdatePaymentSummaryAsync] Created new payment record with ID: {PaymentId}",
                newPaymentSummary.PaymentGrainId);

            return newPaymentSummary;
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
            {
                var invoice = stripeEvent.Data.Object as Stripe.Invoice;
                userId = TryGetFromMetadata(invoice?.Parent?.SubscriptionDetails?.Metadata, "internal_user_id");
                orderId = TryGetFromMetadata(invoice?.Parent?.SubscriptionDetails?.Metadata, "order_id");
                priceId = TryGetFromMetadata(invoice?.Parent?.SubscriptionDetails?.Metadata, "price_id");
                break;
            }
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