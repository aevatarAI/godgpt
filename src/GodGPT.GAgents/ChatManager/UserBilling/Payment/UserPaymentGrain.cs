using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using PaymentMethod = Aevatar.Application.Grains.Common.Constants.PaymentMethod;

namespace Aevatar.Application.Grains.ChatManager.UserBilling.Payment;

public interface IUserPaymentGrain : IGrainWithGuidKey
{
    Task<GrainResultDto<PaymentDetailsDto>> ProcessPaymentCallbackAsync(string jsonPayload, string stripeSignature);
    Task<PaymentDetailsDto> GetPaymentDetailsAsync();
    Task<bool> UpdatePaymentStatusAsync(PaymentStatus newStatus);
    Task<GrainResultDto<PaymentDetailsDto>> InitializePaymentAsync(UserPaymentState paymentState);
}

public class UserPaymentGrain : Grain<UserPaymentState>, IUserPaymentGrain
{
    private readonly ILogger<UserBillingGrain> _logger;
    private readonly IOptionsMonitor<StripeOptions> _stripeOptions;

    public UserPaymentGrain(ILogger<UserBillingGrain> logger, IOptionsMonitor<StripeOptions> stripeOptions)
    {
        _logger = logger;
        _stripeOptions = stripeOptions;
    }

    public async Task<GrainResultDto<PaymentDetailsDto>> ProcessPaymentCallbackAsync(string jsonPayload, string stripeSignature)
    {
        try
        {
            var stripeEvent = EventUtility.ConstructEvent(
                jsonPayload,
                stripeSignature,
                _stripeOptions.CurrentValue.WebhookSecret
            );
            
            _logger.LogDebug("[PaymentGAgent][ProcessPaymentCallbackAsync] Processing Stripe webhook event, {0}, {1}", stripeEvent.Type, stripeEvent.Id);
            
            GrainResultDto<PaymentDetailsDto> resultDto =  null;
            switch (stripeEvent.Type)
            {
                //checkout.session.completed
                case EventTypes.CheckoutSessionCompleted: 
                    resultDto = await ProcessCheckoutSessionCompletedAsync(stripeEvent);
                    break;
                case "invoice.paid":
                    resultDto = await ProcessInvoicePaidAsync(stripeEvent);
                    break;
                //invoice.payment_failed
                case EventTypes.InvoicePaymentFailed:
                    resultDto = await ProcessInvoicePaymentFailedAsync(stripeEvent);
                    break;
                // case "customer.subscription.created":
                //customer.subscription.updated
                case EventTypes.CustomerSubscriptionUpdated:
                //customer.subscription.deleted
                case EventTypes.CustomerSubscriptionDeleted:   
                    resultDto = await ProcessSubscriptionEventAsync(stripeEvent);
                    break;
                //payment_intent.succeeded
                // case EventTypes.PaymentIntentSucceeded: 
                //     resultDto = await ProcessPaymentIntentSucceededAsync(stripeEvent);
                //     break;
                //charge.refunded
                case EventTypes.ChargeRefunded:
                    resultDto = await ProcessChargeRefundedAsync(stripeEvent);
                    break;
                    
                default:
                    _logger.LogInformation("[PaymentGAgent][ProcessPaymentCallbackAsync] Unhandled event type: {EventType}", 
                            stripeEvent.Type);
                    break;
            }

            return resultDto ?? new GrainResultDto<PaymentDetailsDto>
            {
                Success = false,
                Message = "Unexpected error processing webhook",
            };
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "[PaymentGAgent][ProcessPaymentCallbackAsync] Error validating webhook: {Message}", ex.Message);
            return new GrainResultDto<PaymentDetailsDto>
            {
                Success = false,
                Message = $"Error validating webhook: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PaymentGAgent][ProcessPaymentCallbackAsync] Unexpected error processing webhook: {Message}", ex.Message);
            return new GrainResultDto<PaymentDetailsDto>
            {
                Success = false,
                Message = $"Unexpected error processing webhook: {ex.Message}"
            };
        }
    }
    
    private async Task<GrainResultDto<PaymentDetailsDto>> ProcessCheckoutSessionCompletedAsync(Event stripeEvent)
    {
        var session = stripeEvent.Data.Object as Session;
        if (session == null)
        {
            _logger.LogError("[PaymentGAgent][ProcessCheckoutSessionCompletedAsync] Failed to cast event data to Session");
            return new GrainResultDto<PaymentDetailsDto>
            {
                Success = false,
                Message = "Failed to cast event data to Session"
            };
        }
        
        var userId = TryGetFromMetadata(session.Metadata, "internal_user_id");
        var orderId = TryGetFromMetadata(session.Metadata, "order_id");
        var priceId = TryGetFromMetadata(session.Metadata, "price_id");

        _logger.LogInformation("[PaymentGAgent][ProcessCheckoutSessionCompletedAsync] Processing completed session {SessionId} for user {UserId}", 
            session.Id, userId);
        
        bool canUpdateStatus = true;
        if (State.Status >= PaymentStatus.Completed)
        {
            canUpdateStatus = false;
            _logger.LogWarning("[PaymentGAgent][ProcessCheckoutSessionCompletedAsync] Cannot update status from {CurrentStatus} to Processing as current status is finalized", 
                State.Status);
        }

        State.Id = this.GetPrimaryKey();
        State.PriceId = priceId;
        State.Amount = session.AmountTotal.HasValue ? (decimal)session.AmountTotal.Value / 100 : 0;
        State.Currency = session.Currency?.ToUpper() ?? "USD";
        State.PaymentType = session.Mode == PaymentMode.SUBSCRIPTION 
            ? PaymentType.Subscription 
            : PaymentType.OneTime;
        if (canUpdateStatus)
        {
            State.Status = PaymentStatus.Completed;
            State.CompletedAt = DateTime.UtcNow;
        }
        State.Method = session.PaymentMethodTypes.MapToPaymentMethod();
        State.Mode = session.Mode;
        State.Platform = PaymentPlatform.Stripe;
        if (State.CreatedAt == default)
        {
            State.CreatedAt = DateTime.UtcNow;
        }
        State.SubscriptionId = session.SubscriptionId;
        State.InvoiceId = session.InvoiceId;
        State.SessionId = session.Id;
        State.OrderId = orderId;
        State.LastUpdated = DateTime.UtcNow;
        
        await WriteStateAsync();

        _logger.LogInformation("[PaymentGAgent][ProcessCheckoutSessionCompletedAsync] Recorded payment {PaymentId} for session {SessionId}", 
            State.Id, session.Id);
        
        return new GrainResultDto<PaymentDetailsDto>
        {
            Data = State.ToDto()
        };
    }
    
    private string TryGetFromMetadata(IDictionary<string, string>? metadata, string key)
    {
        if (metadata != null && metadata.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
        {
            return value;
        }

        return string.Empty;
    }

    private async Task<GrainResultDto<PaymentDetailsDto>> ProcessPaymentIntentSucceededAsync(Event stripeEvent)
    {
        var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
        if (paymentIntent == null)
        {
            _logger.LogError("[PaymentGAgent][ProcessPaymentIntentSucceededAsync] Failed to cast event data to PaymentIntent");
            return new GrainResultDto<PaymentDetailsDto>
            {
                Success = false,
                Message = "Failed to cast event data to PaymentIntent"
            };
        }
        
        string userId = null;
        if (paymentIntent.Metadata.TryGetValue("internal_user_id", out var id) && !string.IsNullOrEmpty(id))
        {
            userId = id;
        }
        
        _logger.LogInformation("[PaymentGAgent][ProcessPaymentIntentSucceededAsync] Processing successful payment {PaymentIntentId} for user {UserId}", 
            paymentIntent.Id, userId);

        bool canUpdateStatus = true;
        if (State.Status > PaymentStatus.Processing)
        {
            canUpdateStatus = false;
            _logger.LogWarning("[PaymentGAgent][ProcessCheckoutSessionCompletedAsync] Cannot update status from {CurrentStatus} to Processing as current status is finalized", 
                State.Status);
        }
        
        State.Id = this.GetPrimaryKey();
        State.UserId = Guid.Parse(userId);
        if (canUpdateStatus)
        {
            State.Status = PaymentStatus.Completed;
            State.CompletedAt = DateTime.UtcNow;
        }
        State.PaymentType = PaymentType.Subscription;
        State.Platform = PaymentPlatform.Stripe;
        State.Amount = paymentIntent.Amount / 100m;
        State.Currency = paymentIntent.Currency?.ToUpper() ?? "USD";
        State.Description = $"Payment {paymentIntent.Id}";
        State.LastUpdated = DateTime.UtcNow;
        
        await WriteStateAsync();

        _logger.LogInformation("[PaymentGAgent][ProcessPaymentIntentSucceededAsync] Recorded payment {PaymentId} for payment intent {PaymentIntentId}", 
            State.Id, paymentIntent.Id);

        return new GrainResultDto<PaymentDetailsDto>
        {
            Data = State.ToDto()
        };
    }

    private async Task<GrainResultDto<PaymentDetailsDto>> ProcessInvoicePaidAsync(Event stripeEvent)
    {
        var invoice = stripeEvent.Data.Object as Invoice;
        if (invoice == null)
        {
            _logger.LogError("[PaymentGAgent][ProcessInvoicePaidAsync] Failed to cast event data to Invoice");
            return new GrainResultDto<PaymentDetailsDto>
            {
                Success = false,
                Message = "Failed to cast event data to Invoice"
            };
        }
        
        var userId = TryGetFromMetadata(invoice?.Parent?.SubscriptionDetails?.Metadata, "internal_user_id");
        var orderId = TryGetFromMetadata(invoice?.Parent?.SubscriptionDetails?.Metadata, "order_id");
        var priceId = TryGetFromMetadata(invoice?.Parent?.SubscriptionDetails?.Metadata, "price_id");
        _logger.LogInformation("[PaymentGAgent][ProcessInvoicePaidAsync] Processing paid invoice {InvoiceId}", invoice.Id);

        bool canUpdateStatus = true;
        if (State.Status >= PaymentStatus.Completed)
        {
            canUpdateStatus = false;
            _logger.LogWarning("[PaymentGAgent][ProcessCheckoutSessionCompletedAsync] Cannot update status from {CurrentStatus} to Processing as current status is finalized", 
                State.Status);
        }

        State.Id = this.GetPrimaryKey();
        State.UserId = Guid.Parse(userId);
        State.PriceId = priceId;
        if (canUpdateStatus)
        {
            State.Status = PaymentStatus.Completed;
            State.CompletedAt = DateTime.UtcNow;
        }

        if (State.CreatedAt == default)
        {
            State.CreatedAt = DateTime.UtcNow;
        }
        State.InvoiceId = invoice.Id;
        State.SubscriptionId = invoice?.Parent?.SubscriptionDetails?.SubscriptionId;
        State.OrderId = orderId;
        State.LastUpdated = DateTime.UtcNow;
        
        await WriteStateAsync();
        
        _logger.LogInformation("[UserBillingGrain][ProcessInvoicePaidAsync] Recorded payment {PaymentId} for invoice {InvoiceId}", 
            State.Id, invoice.Id);
            
        return new GrainResultDto<PaymentDetailsDto>
        {
            Success = true,
            Data = State.ToDto()
        };
    }
    
    private async Task<GrainResultDto<PaymentDetailsDto>> ProcessInvoicePaymentFailedAsync(Event stripeEvent)
    {
        var invoice = stripeEvent.Data.Object as Invoice;
        if (invoice == null)
        {
            _logger.LogError("[PaymentGAgent][ProcessInvoicePaymentFailedAsync] Failed to cast event data to PaymentIntent");
            return new GrainResultDto<PaymentDetailsDto>
            {
                Success = false,
                Message = "Failed to cast event data to PaymentIntent"
            };
        }

        _logger.LogDebug($"[PaymentGAgent][ProcessInvoicePaymentFailedAsync] Processing failed payment {0}", 
           invoice.Parent?.SubscriptionDetails?.SubscriptionId);
        
        State.Status = PaymentStatus.Failed;
        await WriteStateAsync();
        
        _logger.LogDebug($"[PaymentGAgent][ProcessInvoicePaymentFailedAsync] Processing failed payment {0}, {1}", 
            invoice.Parent?.SubscriptionDetails?.SubscriptionId, State.Status);
        
        return new GrainResultDto<PaymentDetailsDto>
        {
            Success = true,
            Data = State.ToDto()
        };
    }

    private async Task<GrainResultDto<PaymentDetailsDto>> ProcessSubscriptionEventAsync(Event stripeEvent)
    {
        var subscription = stripeEvent.Data.Object as Subscription;
        if (subscription == null)
        {
            _logger.LogError("[PaymentGAgent][ProcessSubscriptionEventAsync] Failed to cast event data to Subscription");
            return new GrainResultDto<PaymentDetailsDto>
            {
                Success = false,
                Message = "Failed to cast event data to Subscription"
            };
        }
        
        _logger.LogInformation("[PaymentGAgent][ProcessSubscriptionEventAsync] Processing subscription event {EventType} for {SubscriptionId}", 
            stripeEvent.Type, subscription.Id);

        if (subscription.Status == "canceled" || IsAutoRenewalCancelled(stripeEvent))
        {
            if (State.Status != PaymentStatus.Failed && State.Status != PaymentStatus.Refunded_In_Processing && State.Status != PaymentStatus.Refunded)
            {
                State.Status = PaymentStatus.Cancelled;
            }
            State.LastUpdated = DateTime.UtcNow;
            State.SubscriptionId = subscription.Id;
            await WriteStateAsync();
            
            _logger.LogInformation("[PaymentGAgent][ProcessSubscriptionEventAsync] Subscription canceled {SubscriptionId} status: {Status}", 
                subscription.Id, subscription.Status);
        }
        return new GrainResultDto<PaymentDetailsDto>
        {
            Success = true,
            Data = State.ToDto()
        };
    }
    
    public bool IsAutoRenewalCancelled(Event stripeEvent)
    {
        var subscription = stripeEvent.Data.Object as Subscription;
        var previousAttributes = stripeEvent.Data.PreviousAttributes as Dictionary<string, object>;

        if (subscription != null && previousAttributes != null)
        {
            if (previousAttributes.TryGetValue("cancel_at_period_end", out object previousValue) &&
                (bool)previousValue == false &&
                subscription.CancelAtPeriodEnd == true)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<GrainResultDto<PaymentDetailsDto>> ProcessChargeRefundedAsync(Event stripeEvent)
    {
        var charge = stripeEvent.Data.Object as Charge;
        if (charge == null)
        {
            _logger.LogError("[PaymentGAgent][ProcessChargeRefundedAsync] Failed to cast event data to Charge");
            return new GrainResultDto<PaymentDetailsDto>
            {
                Success = false,
                Message = "Failed to cast event data to Charge"
            };
        }
        
        _logger.LogInformation("[PaymentGAgent][ProcessChargeRefundedAsync] Processing refunded charge {ChargeId}", charge.Id);

        State.Status = PaymentStatus.Refunded;
        State.LastUpdated = DateTime.UtcNow;

        await WriteStateAsync();

        _logger.LogInformation("[PaymentGAgent][ProcessChargeRefundedAsync] Created new refund record {PaymentId} for charge {ChargeId}", 
            State.Id, charge.Id);

        return new GrainResultDto<PaymentDetailsDto>
        {
            Data = State.ToDto()
        };
    }

    public Task<PaymentDetailsDto> GetPaymentDetailsAsync()
    {
        _logger.LogInformation("[PaymentGAgent][GetPaymentDetailsAsync] Getting payment details for ID {PaymentId}", this.GetPrimaryKey());
        return Task.FromResult(State.ToDto());
    }

    public async Task<bool> UpdatePaymentStatusAsync(PaymentStatus newStatus)
    {
        _logger.LogInformation("[PaymentGAgent][UpdatePaymentStatusAsync] Updating payment {PaymentId} status to {NewStatus}", 
            this.GetPrimaryKey(), newStatus);
        
        // 判断是否可以更新状态
        bool canUpdateStatus = true;
        if (State.Status > PaymentStatus.Processing && State.Status != newStatus)
        {
            // 特殊允许的状态转换
            bool isSpecialAllowedTransition = 
                (State.Status == PaymentStatus.Completed && newStatus == PaymentStatus.Refunded) ||
                (State.Status == PaymentStatus.Completed && newStatus == PaymentStatus.Disputed);
            
            if (!isSpecialAllowedTransition)
            {
                // 如果当前状态已经大于Processing且不是特殊允许的转换，不允许更新
                canUpdateStatus = false;
                _logger.LogWarning("[PaymentGAgent][UpdatePaymentStatusAsync] Cannot update status from {CurrentStatus} to {NewStatus} as current status is finalized", 
                    State.Status, newStatus);
                return false;
            }
        }
        
        // 只有在允许更新状态的情况下才更新
        if (canUpdateStatus)
        {
            State.Status = newStatus;
            State.LastUpdated = DateTime.UtcNow;
            
            if (newStatus == PaymentStatus.Completed && State.CompletedAt == null)
            {
                State.CompletedAt = DateTime.UtcNow;
            }
            
            await WriteStateAsync();
            
            _logger.LogInformation("[PaymentGAgent][UpdatePaymentStatusAsync] Successfully updated status to {NewStatus}", newStatus);
            return true;
        }
        
        return false;
    }

    public async Task<GrainResultDto<PaymentDetailsDto>> InitializePaymentAsync(UserPaymentState paymentState)
    {
        _logger.LogInformation("[PaymentGAgent][InitializePaymentAsync] Initializing payment for order {OrderId}", paymentState.OrderId);
        try
        {
            State = paymentState;
            if (State.Id == Guid.Empty)
            {
                State.Id = this.GetPrimaryKey();
            }
            if (State.CreatedAt == default)
            {
                State.CreatedAt = DateTime.UtcNow;
            }
            State.LastUpdated = DateTime.UtcNow;
            
            await WriteStateAsync();
            
            _logger.LogInformation("[PaymentGAgent][InitializePaymentAsync] Payment initialized with ID: {PaymentId}", 
                State.Id);
            
            return new GrainResultDto<PaymentDetailsDto>
            {
                Success = true,
                Data = State.ToDto()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PaymentGAgent][InitializePaymentAsync] Failed to initialize payment: {ErrorMessage}", ex.Message);
            return new GrainResultDto<PaymentDetailsDto>
            {
                Success = false,
                Message = $"Failed to initialize payment: {ex.Message}"
            };
        }
    }
}