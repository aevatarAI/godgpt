using Aevatar.Application.Grains.ChatManager.UserBilling.Payment;
using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;
using Stripe;
using Stripe.Checkout;
using System;
using System.Collections.Generic;
using Aevatar.Application.Grains.Common.Constants;

namespace Aevatar.Application.Grains.Webhook;

public interface IStripeEventProcessingGrain : IGrainWithStringKey
{
    Task<string> ParseEventAndGetUserIdAsync([Immutable] string json);
    
    Task<(string userId, StripeEnvironment environment)> ParseEventAndGetUserInfoAsync([Immutable] string json);
}

[StatelessWorker]
[Reentrant]
public class StripeEventProcessingGrain : Grain, IStripeEventProcessingGrain
{
    private readonly ILogger<StripeEventProcessingGrain> _logger;
    private readonly IOptionsMonitor<StripeOptions> _stripeOptions;
    private readonly StripeClient _stripeClient;

    public StripeEventProcessingGrain(
        ILogger<StripeEventProcessingGrain> logger,
        IOptionsMonitor<StripeOptions> stripeOptions)
    {
        _logger = logger;
        _stripeOptions = stripeOptions;
        _stripeClient = new StripeClient(stripeOptions.CurrentValue.SecretKey);
    }

    [ReadOnly]
    public async Task<string> ParseEventAndGetUserIdAsync([Immutable] string json)
    {
        var result = await ParseEventAndGetUserInfoAsync(json);
        if (result.environment != StripeEnvironment.None && result.environment != _stripeOptions.CurrentValue.Environment)
        {
            _logger.LogDebug("[StripeEventProcessingGrain][ParseEventAndGetUserIdAsync] Filter messages from {env}", result.environment.ToString());
            return string.Empty;
        }
        return result.userId;
    }
    
    [ReadOnly]
    public async Task<(string userId, StripeEnvironment environment)> ParseEventAndGetUserInfoAsync([Immutable] string json)
    {
        var stripeEvent = EventUtility.ParseEvent(json);
        _logger.LogInformation("[StripeEventProcessingGrain][ParseEventAndGetUserInfoAsync] Type: {0}", stripeEvent.Type);

        // Extract metadata based on event type
        var metadata = GetEventMetadata(stripeEvent);
        
        // Determine environment from metadata
        var environment = GetEnvironmentFromMetadata(metadata);
        _logger.LogDebug("[StripeEventProcessingGrain] Detected environment: {Environment}", environment);
        
        // Extract user ID
        string userId = string.Empty;
        if (stripeEvent.Type == "checkout.session.completed")
        {
            var session = stripeEvent.Data.Object as global::Stripe.Checkout.Session;
            if (TryGetUserIdFromMetadata(session.Metadata, out userId))
            {
                _logger.LogDebug("[StripeEventProcessingGrain][ParseEventAndGetUserInfoAsync] Type={0}, UserId={1}, Environment={2}",
                    stripeEvent.Type, userId, environment);
                return (userId, environment);
            }

            _logger.LogWarning("[StripeEventProcessingGrain][ParseEventAndGetUserInfoAsync] Type={0}, not found userid",
                stripeEvent.Type);
        }
        else if (stripeEvent.Type is "invoice.paid" or "invoice.payment_failed")
        {
            var invoice = stripeEvent.Data.Object as global::Stripe.Invoice;
            if (TryGetUserIdFromMetadata(invoice?.Parent?.SubscriptionDetails?.Metadata, out userId))
            {
                _logger.LogDebug("[StripeEventProcessingGrain][ParseEventAndGetUserInfoAsync] Type={0}, UserId={1}, Environment={2}",
                    stripeEvent.Type, userId, environment);
                return (userId, environment);
            }

            _logger.LogWarning("[StripeEventProcessingGrain][ParseEventAndGetUserInfoAsync] Type={0}, not found userid",
                stripeEvent.Type);
        }
        else if (stripeEvent.Type is "customer.subscription.deleted" or "customer.subscription.updated")
        {
            var subscription = stripeEvent.Data.Object as global::Stripe.Subscription;
            if (TryGetUserIdFromMetadata(subscription.Metadata, out userId))
            {
                _logger.LogDebug("[StripeEventProcessingGrain][ParseEventAndGetUserInfoAsync] Type={0}, UserId={1}, Environment={2}",
                    stripeEvent.Type, userId, environment);
                return (userId, environment);
            }
        }
        else if (stripeEvent.Type == "charge.refunded")
        {
            var charge = stripeEvent.Data.Object as Stripe.Charge;
            var paymentIntentService = new PaymentIntentService(_stripeClient);
            var paymentIntent = paymentIntentService.Get(charge.PaymentIntentId);
            if (TryGetUserIdFromMetadata(paymentIntent.Metadata, out userId))
            {
                _logger.LogDebug("[StripeEventProcessingGrain][ParseEventAndGetUserInfoAsync] Type={0}, UserId={1}, Environment={2}",
                    stripeEvent.Type, userId, environment);
                return (userId, environment);
            }
        }

        return (string.Empty, environment);
    }
    
    private IDictionary<string, string> GetEventMetadata(Event stripeEvent)
    {
        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                return (stripeEvent.Data.Object as global::Stripe.Checkout.Session)?.Metadata;
            case "invoice.paid":
            case "invoice.payment_failed":
                return (stripeEvent.Data.Object as Invoice)?.Parent?.SubscriptionDetails?.Metadata;
            case "customer.subscription.deleted":
            case "customer.subscription.updated":
                return (stripeEvent.Data.Object as Subscription)?.Metadata;
            case "charge.refunded":
                var charge = stripeEvent.Data.Object as Charge;
                if (charge?.PaymentIntentId != null)
                {
                    var paymentIntentService = new PaymentIntentService(_stripeClient);
                    var paymentIntent = paymentIntentService.Get(charge.PaymentIntentId);
                    return paymentIntent?.Metadata;
                }
                return null;
            default:
                return null;
        }
    }

    private bool TryGetUserIdFromMetadata(IDictionary<string, string> metadata, out string userId)
    {
        userId = null;
        if (metadata != null && metadata.TryGetValue("internal_user_id", out var id) && !string.IsNullOrEmpty(id))
        {
            userId = id;
            return true;
        }

        return false;
    }
    
    private StripeEnvironment GetEnvironmentFromMetadata(IDictionary<string, string> metadata)
    {
        if (metadata != null && 
            metadata.TryGetValue("env", out var envValue) && 
            Enum.TryParse<StripeEnvironment>(envValue, true, out var environment))
        {
            _logger.LogDebug("Found environment in metadata: {Environment}", environment);
            return environment;
        }
        
        _logger.LogDebug("Environment not found in metadata");
        return StripeEnvironment.None;
    }
}