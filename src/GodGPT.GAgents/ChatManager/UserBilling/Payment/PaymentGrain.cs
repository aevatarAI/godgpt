using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using PaymentMethod = Aevatar.Application.Grains.Common.Constants.PaymentMethod;

namespace Aevatar.Application.Grains.ChatManager.UserBilling.Payment;

public interface IPaymentGrain : IGrainWithGuidKey
{
    Task<GrainResultDto<PaymentDetailsDto>> ProcessPaymentCallbackAsync(string jsonPayload, string stripeSignature);
    Task<PaymentDetailsDto> GetPaymentDetailsAsync();
    Task<bool> UpdatePaymentStatusAsync(PaymentStatus newStatus);
    
}

public class PaymentGrain : Grain<PaymentState>, IPaymentGrain
{
    private readonly ILogger<UserBillingGrain> _logger;
    private readonly IOptionsMonitor<StripeOptions> _stripeOptions;

    public PaymentGrain(ILogger<UserBillingGrain> logger, IOptionsMonitor<StripeOptions> stripeOptions)
    {
        _logger = logger;
        _stripeOptions = stripeOptions;
    }

    public async Task<GrainResultDto<PaymentDetailsDto>> ProcessPaymentCallbackAsync(string jsonPayload, string stripeSignature)
    {
        _logger.LogInformation("[PaymentGAgent][ProcessPaymentCallbackAsync] Processing Stripe webhook event");
        
        if (string.IsNullOrEmpty(jsonPayload) || string.IsNullOrEmpty(stripeSignature))
        {
            _logger.LogError("[PaymentGAgent][ProcessPaymentCallbackAsync] Invalid webhook parameters");
            return new GrainResultDto<PaymentDetailsDto>
            {
                Success = false,
                Message = "Invalid webhook parameters"
            };
        }
        
        try
        {
            // Verify the event
            var webhookSecret = _stripeOptions.CurrentValue.WebhookSecret;
            var stripeEvent = EventUtility.ConstructEvent(
                jsonPayload,
                stripeSignature,
                webhookSecret
            );
            
            _logger.LogInformation("[PaymentGAgent][ProcessPaymentCallbackAsync] Received event type: {EventType}", stripeEvent.Type);

            GrainResultDto<PaymentDetailsDto> resultDto =  null;
            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                    resultDto = await ProcessCheckoutSessionCompletedAsync(stripeEvent);
                    break;
                    
                case EventTypes.PaymentIntentSucceeded: //"payment_intent.succeeded"
                    resultDto = await ProcessPaymentIntentSucceededAsync(stripeEvent);
                    break;
                    
                // case "invoice.paid":
                //     await ProcessInvoicePaidAsync(stripeEvent);
                //     break;
                //     
                // case "customer.subscription.created":
                // case "customer.subscription.updated":
                //     await ProcessSubscriptionEventAsync(stripeEvent);
                //     break;
                //     
                // case "payment_intent.payment_failed":
                //     await ProcessPaymentFailedAsync(stripeEvent);
                //     break;
                    
                case "charge.refunded":
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
        
        string userId = null;
        if (session.Metadata.TryGetValue("internal_user_id", out var id) && !string.IsNullOrEmpty(id))
        {
            userId = id;
        }
        
        _logger.LogInformation("[PaymentGAgent][ProcessCheckoutSessionCompletedAsync] Processing completed session {SessionId} for user {UserId}", 
            session.Id, userId);
        
        // Create payment record
        var paymentSummary = new PaymentDetailsDto
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Status = PaymentStatus.Completed,
            PaymentType = session.Mode == PaymentMode.SUBSCRIPTION 
                ? PaymentType.Subscription 
                : PaymentType.OneTime,
            Method = PaymentMethod.Card, // Default to card, could be updated based on session data
            Platform = PaymentPlatform.Stripe,
            Amount = session.AmountTotal.HasValue ? (decimal)session.AmountTotal.Value / 100 : 0, // Stripe amounts are in cents
            Currency = session.Currency?.ToUpper() ?? "USD",
            Description = $"Checkout session {session.Id}",
            LastUpdated = DateTime.UtcNow
        };
        _logger.LogInformation("[PaymentGAgent][ProcessCheckoutSessionCompletedAsync] Recorded payment {PaymentId} for session {SessionId}", 
            paymentSummary.Id, session.Id);
        return new GrainResultDto<PaymentDetailsDto>
        {
            Data = paymentSummary
        };
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
        
        // Check if we've already processed this payment intent
        // if (State.PaymentHistory.Any(p => p.Description.Contains(paymentIntent.Id)))
        // {
        //     _logger.LogInformation("[PaymentGAgent][ProcessPaymentIntentSucceededAsync] Payment for {PaymentIntentId} already processed", 
        //         paymentIntent.Id);
        //     return;
        // }
        
        // Create payment record
        var paymentSummary = new PaymentDetailsDto
        {
            Id = this.GetPrimaryKey(),
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Status = PaymentStatus.Completed,
            PaymentType = PaymentType.Subscription,
            Method = PaymentMethod.Card, // Default to card
            Platform = PaymentPlatform.Stripe,
            Amount = paymentIntent.Amount / 100m, // Stripe amounts are in cents
            Currency = paymentIntent.Currency?.ToUpper() ?? "USD",
            Description = $"Payment {paymentIntent.Id}",
            LastUpdated = DateTime.UtcNow
        };

        _logger.LogInformation("[PaymentGAgent][ProcessPaymentIntentSucceededAsync] Recorded payment {PaymentId} for payment intent {PaymentIntentId}", 
            paymentSummary.Id, paymentIntent.Id);

        return new GrainResultDto<PaymentDetailsDto>
        {
            Data = paymentSummary
        };
    }

    private async Task ProcessInvoicePaidAsync(Event stripeEvent)
    {
        var invoice = stripeEvent.Data.Object as Invoice;
        if (invoice == null)
        {
            _logger.LogError("[UserBillingGrain][ProcessInvoicePaidAsync] Failed to cast event data to Invoice");
            return;
        }
        
        _logger.LogInformation("[UserBillingGrain][ProcessInvoicePaidAsync] Processing paid invoice {InvoiceId}", invoice.Id);
        
        // Check if we've already processed this invoice
        // if (State.PaymentHistory.Any(p => p.Description.Contains(invoice.Id)))
        // {
        //     _logger.LogInformation("[UserBillingGrain][ProcessInvoicePaidAsync] Payment for invoice {InvoiceId} already processed", invoice.Id);
        //     return;
        // }
        
        // 使用反射安全地获取SubscriptionId属性
        string subscriptionId = "unknown";
        try
        {
            var prop = invoice.GetType().GetProperty("SubscriptionId");
            if (prop != null)
            {
                var value = prop.GetValue(invoice);
                if (value != null)
                {
                    subscriptionId = value.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[UserBillingGrain][ProcessInvoicePaidAsync] Could not get SubscriptionId: {Error}", ex.Message);
        }
        
        // Create payment record for subscription payment
        var paymentSummary = new 
        {
            PaymentGrainId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Status = PaymentStatus.Completed,
            PaymentType = PaymentType.Subscription,
            Method = PaymentMethod.Card, // Default to card
            Platform = PaymentPlatform.Stripe,
            Amount = invoice.AmountPaid / 100m, // Stripe amounts are in cents
            Currency = invoice.Currency?.ToUpper() ?? "USD",
            Description = $"Invoice {invoice.Id} for subscription {subscriptionId}",
            IsSubscriptionRenewal = invoice.BillingReason == "subscription_cycle",
            LastUpdated = DateTime.UtcNow
        };
        
        // Add to payment history
        //await AddPaymentRecordAsync(paymentSummary);
        
        _logger.LogInformation("[UserBillingGrain][ProcessInvoicePaidAsync] Recorded payment {PaymentId} for invoice {InvoiceId}", 
            paymentSummary.PaymentGrainId, invoice.Id);
    }

    private async Task ProcessSubscriptionEventAsync(Event stripeEvent)
    {
        var subscription = stripeEvent.Data.Object as Subscription;
        if (subscription == null)
        {
            _logger.LogError("[UserBillingGrain][ProcessSubscriptionEventAsync] Failed to cast event data to Subscription");
            return;
        }
        
        _logger.LogInformation("[UserBillingGrain][ProcessSubscriptionEventAsync] Processing subscription event {EventType} for {SubscriptionId}", 
            stripeEvent.Type, subscription.Id);
        
        // For subscription events, we might need to update an existing payment record
        // or simply log the event without creating a new payment record
        // This depends on your business logic
        
        // 使用反射安全地获取属性值
        string periodEnd = "Unknown";
        try
        {
            var prop = subscription.GetType().GetProperty("CurrentPeriodEnd");
            if (prop != null)
            {
                var value = prop.GetValue(subscription);
                if (value != null)
                {
                    periodEnd = value.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[UserBillingGrain][ProcessSubscriptionEventAsync] Could not get CurrentPeriodEnd: {Error}", ex.Message);
        }
        
        // For this example, we'll just log the event details
        _logger.LogInformation("[UserBillingGrain][ProcessSubscriptionEventAsync] Subscription {SubscriptionId} status: {Status}, current period end: {PeriodEnd}", 
            subscription.Id, subscription.Status, periodEnd);
    }

    private async Task ProcessPaymentFailedAsync(Event stripeEvent)
    {
        var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
        if (paymentIntent == null)
        {
            _logger.LogError("[UserBillingGrain][ProcessPaymentFailedAsync] Failed to cast event data to PaymentIntent");
            return;
        }
        
        string userId = null;
        if (paymentIntent.Metadata.TryGetValue("internal_user_id", out var id) && !string.IsNullOrEmpty(id))
        {
            userId = id;
        }
        
        _logger.LogInformation("[UserBillingGrain][ProcessPaymentFailedAsync] Processing failed payment {PaymentIntentId} for user {UserId}", 
            paymentIntent.Id, userId);
        
        // Create payment record for failed payment
        var paymentSummary = new PaymentDetailsDto
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            Status = PaymentStatus.Failed,
            PaymentType = PaymentType.OneTime, // Default for payment_intent
            Method = PaymentMethod.Card, // Default to card
            Platform = PaymentPlatform.Stripe,
            Amount = paymentIntent.Amount / 100m, // Stripe amounts are in cents
            Currency = paymentIntent.Currency?.ToUpper() ?? "USD",
            Description = $"Failed payment {paymentIntent.Id}: {paymentIntent.LastPaymentError?.Message ?? "Unknown error"}",
            LastUpdated = DateTime.UtcNow
        };
        
        // Add to payment history
        //await AddPaymentRecordAsync(paymentSummary);
        
        _logger.LogInformation("[UserBillingGrain][ProcessPaymentFailedAsync] Recorded failed payment {PaymentId} for payment intent {PaymentIntentId}", 
            paymentSummary.Id, paymentIntent.Id);
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
        
        // Try to find a related payment by payment intent ID
        // var relatedPayment = State.PaymentHistory.FirstOrDefault(p => 
        //     p.Description.Contains(charge.PaymentIntentId) && 
        //     p.Status == PaymentStatus.Completed);
        //
        // if (relatedPayment != null)
        // {
        //     // Update existing payment to refunded
        //     await UpdatePaymentStatusAsync(relatedPayment.PaymentGrainId, PaymentStatus.Refunded);
        //     
        //     _logger.LogInformation("[UserBillingGrain][ProcessChargeRefundedAsync] Updated payment {PaymentId} to refunded status", 
        //         relatedPayment.PaymentGrainId);
        // }

        // Create new refund record if we can't find the original payment
        var paymentSummary = new PaymentDetailsDto
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Status = PaymentStatus.Refunded,
            PaymentType = PaymentType.Refund,
            Method = PaymentMethod.Card, // Default to card
            Platform = PaymentPlatform.Stripe,
            Amount = charge.Amount / 100m, // Stripe amounts are in cents
            Currency = charge.Currency?.ToUpper() ?? "USD",
            Description = $"Refund for charge {charge.Id}, payment intent {charge.PaymentIntentId}",
            LastUpdated = DateTime.UtcNow
        };

        _logger.LogInformation("[PaymentGAgent][ProcessChargeRefundedAsync] Created new refund record {PaymentId} for charge {ChargeId}", 
            paymentSummary.Id, charge.Id);

        return new GrainResultDto<PaymentDetailsDto>
        {
            Data = paymentSummary
        };

    }

    public Task<PaymentDetailsDto> GetPaymentDetailsAsync()
    {
        throw new NotImplementedException();
    }

    public Task<bool> UpdatePaymentStatusAsync(PaymentStatus newStatus)
    {
        throw new NotImplementedException();
    }
}