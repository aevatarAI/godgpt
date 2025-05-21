using System;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Webhook.SDK.Handler;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Stripe;

namespace GodGPT.Webhook.Http;

public class GodGPTWebhookHandler : IWebhookHandler
{
    private readonly ILogger<GodGPTWebhookHandler> _logger;
    private readonly IClusterClient _clusterClient;

    // Default Agent ID for backward compatibility
    private static readonly Guid DefaultAgentId = Guid.Empty;

    public GodGPTWebhookHandler(
        IClusterClient clusterClient,
        ILogger<GodGPTWebhookHandler> logger)
    {
        _clusterClient = clusterClient;
        _logger = logger;
    }


    public string RelativePath => "api/webhooks/godgpt";

    public string HttpMethod => "POST";

    // Modified to match the actual URL path
    public async Task<object> HandleAsync(HttpRequest request)
    {
        try
        {
            _logger.LogInformation(
                "GodGPTWebhookHandler Received request: Method={method}, Path={path}, QueryString={query}",
                request.Method, request.Path, request.QueryString);

            var fullUrl = $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}{request.QueryString}";
            _logger.LogInformation("GodGPTWebhookHandler Raw URL: {rawUrl}", fullUrl);

            var headers = request.Headers;
            var token = headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
            _logger.LogInformation("GodGPTWebhookHandler token={A}", token);
            var json = await new StreamReader(request.Body).ReadToEndAsync();
            _logger.LogInformation("[GodGPTPaymentController][webhook] josn: {0}", json);

            string internalUserId = null;
            try
            {
                internalUserId = await ParseEventAndGetUserIdAsync(json);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[GodGPTPaymentController][Webhook] Error validating webhook: {Message}",
                    e.Message);
                return Task.CompletedTask;
            }

            if (!internalUserId.IsNullOrWhiteSpace() && Guid.TryParse(internalUserId, out var userId))
            {
                var result =
                    await HandleStripeWebhookEventAsync(userId, json,
                        request.Headers["Stripe-Signature"]);
                if (!result)
                {
                    return Task.CompletedTask;
                }

                return Task.CompletedTask;
            }

            _logger.LogWarning("[GodGPTPaymentController][Webhook] ");

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GodGPTWebhookHandler Error processing webhook request");
            throw;
        }
    }

    public async Task<bool> HandleStripeWebhookEventAsync(Guid internalUserId, string json,
        StringValues stripeSignature)
    {
        var userBillingGrain =
            _clusterClient.GetGrain<IUserBillingGrain>(CommonHelper.GetUserBillingGAgentId(internalUserId));
        return await userBillingGrain.HandleStripeWebhookEventAsync(json, stripeSignature);
    }

    private async Task<string> ParseEventAndGetUserIdAsync(string json)
    {
        var stripeEvent = EventUtility.ParseEvent(json);
        _logger.LogInformation("[GodGPTPaymentController][webhook] Type: {0}", stripeEvent.Type);
        if (stripeEvent.Type == "checkout.session.completed")
        {
            var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
            var b = session.Metadata;
            if (TryGetUserIdFromMetadata(session.Metadata, out var userId))
            {
                _logger.LogDebug("[GodGPTService][ParseEventAndGetUserIdAsync] Type={0}, UserId={1}", stripeEvent.Type,
                    userId);
                return userId;
            }

            _logger.LogWarning("[GodGPTService][ParseEventAndGetUserIdAsync] Type={0}, not found uerid",
                stripeEvent.Type);
        }
        // else if (stripeEvent.Type == "invoice.payment_succeeded")
        // {
        //     var invoice = stripeEvent.Data.Object as Stripe.Invoice;
        //     if (TryGetUserIdFromMetadata(invoice?.Parent?.SubscriptionDetails?.Metadata, out  var userId))
        //     {
        //         return userId;
        //     }
        // } 
        else if (stripeEvent.Type == "invoice.paid")
        {
            var invoice = stripeEvent.Data.Object as Stripe.Invoice;
            if (TryGetUserIdFromMetadata(invoice?.Parent?.SubscriptionDetails?.Metadata, out var userId))
            {
                _logger.LogDebug("[GodGPTService][ParseEventAndGetUserIdAsync] Type={0}, UserId={1}", stripeEvent.Type,
                    userId);
                return userId;
            }

            _logger.LogWarning("[GodGPTService][ParseEventAndGetUserIdAsync] Type={0}, not found uerid",
                stripeEvent.Type);
        }
        // else if (stripeEvent.Type == "invoice.payment_failed")
        // {
        //     var invoice = stripeEvent.Data.Object as Stripe.Invoice;
        //     if (TryGetUserIdFromMetadata(invoice.Metadata, out  var userId))
        //     {
        //         return userId;
        //     }
        // }
        // else if (stripeEvent.Type == "payment_intent.succeeded")
        // {
        //     var paymentIntent = stripeEvent.Data.Object as Stripe.PaymentIntent;
        //     if (TryGetUserIdFromMetadata(paymentIntent.Metadata, out  var userId))
        //     {
        //         return userId;
        //     }
        // }
        // else if (stripeEvent.Type == "customer.subscription.created")
        // {
        //     var subscription = stripeEvent.Data.Object as Stripe.Subscription;
        //     if (TryGetUserIdFromMetadata(subscription.Metadata, out  var userId))
        //     {
        //         return userId;
        //     }
        // }
        // else if (stripeEvent.Type == "charge.refunded")
        // {
        //     var charge = stripeEvent.Data.Object as Stripe.Charge;
        //     if (TryGetUserIdFromMetadata(charge.Metadata, out  var userId))
        //     {
        //         return userId;
        //     }
        // }

        return string.Empty;
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
}