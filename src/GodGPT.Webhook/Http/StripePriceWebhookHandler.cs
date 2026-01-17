using Aevatar.Application.Grains.Subscription;
using Aevatar.Webhook.SDK.Handler;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GodGPT.Webhook.Http;

/// <summary>
/// Webhook handler for Stripe price events (price.created, price.updated, price.deleted).
/// These events are triggered when prices are managed in Stripe Dashboard.
/// 
/// Note: Signature validation and event processing is handled in StripePriceGAgent.
/// </summary>
public class StripePriceWebhookHandler : IWebhookHandler
{
    private readonly ILogger<StripePriceWebhookHandler> _logger;
    private readonly IClusterClient _clusterClient;

    public StripePriceWebhookHandler(
        IClusterClient clusterClient,
        ILogger<StripePriceWebhookHandler> logger)
    {
        _clusterClient = clusterClient;
        _logger = logger;
    }

    public string RelativePath => "api/webhooks/godgpt-stripe-price";

    public string HttpMethod => "POST";

    public async Task<object> HandleAsync(HttpRequest request)
    {
        try
        {
            _logger.LogDebug(
                "[StripePriceWebhookHandler] Received request: Method={Method}, Path={Path}",
                request.Method, request.Path);

            var signature = request.Headers["Stripe-Signature"].ToString();
            if (string.IsNullOrEmpty(signature))
            {
                _logger.LogWarning("[StripePriceWebhookHandler] Missing Stripe-Signature header");
                return new { success = false, error = "Missing signature" };
            }

            var json = await new StreamReader(request.Body).ReadToEndAsync();
            _logger.LogDebug("[StripePriceWebhookHandler] Received payload length: {Length}", json.Length);

            // Forward to StripePriceGAgent for signature validation and event processing
            var stripeGAgent = _clusterClient.GetGrain<IStripePriceGAgent>(
                SubscriptionGAgentKeys.StripePriceGAgentKey);
            
            var result = await stripeGAgent.HandleWebhookAsync(json, signature);
            
            _logger.LogInformation(
                "[StripePriceWebhookHandler] Webhook processing result: {Result}",
                result);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StripePriceWebhookHandler] Error processing webhook request");
            throw;
        }
    }
}