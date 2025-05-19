using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserBilling.Payment;
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
    
    private async Task<string> GetOrCreateStripeCustomerAsync(string userId = null)
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
            _logger.LogInformation("[UserBillingGrain][GetOrCreateStripeCustomerAsync] Created Stripe Customer for user {UserId}: {CustomerId}", 
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
            _logger.LogInformation("[UserBillingGrain][GetOrCreateStripeCustomerAsync] Created temporary Stripe Customer: {CustomerId}", 
                customer.Id);
            customerId = customer.Id;
        }

        State.CustomerId = customerId;
        await WriteStateAsync();
        return State.CustomerId;
    }

    public async Task<string> CreateCheckoutSessionAsync(CreateCheckoutSessionDto createCheckoutSessionDto)
    {
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
                { "quantity", createCheckoutSessionDto.Quantity.ToString() }
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
                        { "quantity", createCheckoutSessionDto.Quantity.ToString() }
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
                        { "quantity", createCheckoutSessionDto.Quantity.ToString() }
                    }
                }
                : null
        };

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
            _logger.LogInformation("[UserBillingGrain][CreateCheckoutSessionAsync] Using existing Customer: {CustomerId} for {Mode} mode", 
                options.Customer, createCheckoutSessionDto.Mode);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "[UserBillingGrain][CreateCheckoutSessionAsync] Failed to create or get Stripe Customer: {ErrorMessage}", ex.Message);
            throw;
        }

        var service = new SessionService(_client);
        try
        {
            var session = await service.CreateAsync(options);

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

        var paymentGrainId = Guid.NewGuid();
        var paymentGrain = GrainFactory.GetGrain<IPaymentGrain>(paymentGrainId);
        var grainResultDto = await paymentGrain.ProcessPaymentCallbackAsync(jsonPayload, stripeSignature);
        var detailsDto = grainResultDto.Data;
        if (!grainResultDto.Success || detailsDto == null)
        {
            return false;
        }

        var paymentSummary = await GetPaymentSummaryAsync(detailsDto.Id);
        if (paymentSummary == null)
        {
            // Add to payment history
            await AddPaymentRecordAsync(new PaymentSummary
            {
                PaymentGrainId = detailsDto.Id,
                OrderId = detailsDto.Id.ToString(),
                PlanType = PlanType.Day,
                Amount = detailsDto.Amount,
                Currency = detailsDto.Currency,
                CreatedAt = detailsDto.CreatedAt,
                CompletedAt = detailsDto.CompletedAt,
                Status = detailsDto.Status,
                PaymentType = detailsDto.PaymentType,
                Method = detailsDto.Method,
                Platform = detailsDto.Platform,
                IsSubscriptionRenewal = false,
                LastUpdated = detailsDto.LastUpdated
            });
        }
        else
        {
            await UpdatePaymentStatusAsync(paymentSummary, detailsDto.Status);
        }

        return true;
    }

    public async Task<Guid> AddPaymentRecordAsync(PaymentSummary paymentSummary)
    {
        _logger.LogInformation("[UserBillingGrain][AddPaymentRecordAsync] Adding payment record with status {Status} for amount {Amount} {Currency}",
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
        
        // Set last updated time
        paymentSummary.LastUpdated = DateTime.UtcNow;
        
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
        _logger.LogInformation("[UserBillingGrain][GetPaymentSummaryAsync] Getting payment summary for payment ID: {PaymentId}", paymentId);
        
        var payment = State.PaymentHistory.FirstOrDefault(p => p.PaymentGrainId == paymentId);
        if (payment == null)
        {
            _logger.LogWarning("[UserBillingGrain][GetPaymentSummaryAsync] Payment with ID {PaymentId} not found", paymentId);
            return null;
        }
        
        return payment;
    }

    public async Task<List<PaymentSummary>> GetPaymentHistoryAsync(int page = 1, int pageSize = 10)
    {
        _logger.LogInformation("[UserBillingGrain][GetPaymentHistoryAsync] Getting payment history page {Page} with size {PageSize}", 
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
        _logger.LogInformation("[UserBillingGrain][UpdatePaymentStatusAsync] Updating payment status for ID {PaymentId} to {NewStatus}", 
            payment.PaymentGrainId, newStatus);

        // Skip update if status is already the same
        if (payment.Status == newStatus)
        {
            _logger.LogInformation("[UserBillingGrain][UpdatePaymentStatusAsync] Payment {PaymentId} already has status {Status}", 
                payment.PaymentGrainId, newStatus);
            return true;
        }
        
        // Update status
        var oldStatus = payment.Status;
        payment.Status = newStatus;
        payment.LastUpdated = DateTime.UtcNow;
        
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
        
        _logger.LogInformation("[UserBillingGrain][UpdatePaymentStatusAsync] Payment status updated from {OldStatus} to {NewStatus} for ID {PaymentId}", 
            oldStatus, newStatus, payment.PaymentGrainId);
        
        return true;
    }
}