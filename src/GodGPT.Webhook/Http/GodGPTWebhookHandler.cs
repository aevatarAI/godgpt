using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.UserBilling;
using Aevatar.Application.Grains.Webhook;
using Aevatar.Webhook.SDK.Handler;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Stripe;

namespace GodGPT.Webhook.Http;

public class GodGPTWebhookHandler : IWebhookHandler
{
    private readonly ILogger<GodGPTWebhookHandler> _logger;
    private readonly IClusterClient _clusterClient;

    // Default Agent ID for backward compatibility
    private static readonly Guid DefaultAgentId = Guid.Empty;
    
    private static readonly string StripeEventProcessingGrainId = "StripeEventProcessingGrainId_1";
    
    private readonly IOptionsMonitor<StripeOptions> _stripeOptions;
    private readonly StripeClient _stripeClient;

    public GodGPTWebhookHandler(
        IClusterClient clusterClient,
        ILogger<GodGPTWebhookHandler> logger, IOptionsMonitor<StripeOptions> stripeOptions)
    {
        _clusterClient = clusterClient;
        _logger = logger;
        _stripeOptions = stripeOptions;
        
        _stripeClient = new StripeClient(_stripeOptions.CurrentValue.SecretKey);
    }


    public string RelativePath => "api/webhooks/godgpt-stripe-payment";

    public string HttpMethod => "POST";

    // Modified to match the actual URL path
    public async Task<object> HandleAsync(HttpRequest request)
    {
        try
        {
            _logger.LogDebug(
                "GodGPTWebhookHandler Received request: Method={method}, Path={path}, QueryString={query}",
                request.Method, request.Path, request.QueryString);

            var headers = request.Headers;
            var signature = headers["Stripe-Signature"].ToString();
            _logger.LogDebug("[GodGPTPaymentController][webhook] Signature={A}", signature);
            var json = await new StreamReader(request.Body).ReadToEndAsync();
            _logger.LogDebug("[GodGPTPaymentController][webhook] Json: {0}", json);

            string internalUserId = null;
            try
            {
                var stripeEventGrain = _clusterClient.GetGrain<IStripeEventProcessingGrain>(StripeEventProcessingGrainId);
                internalUserId = await stripeEventGrain.ParseEventAndGetUserIdAsync(json);
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
                    await HandleStripeWebhookEventAsync(userId, json, signature);
                _logger.LogInformation("[GodGPTPaymentController][Webhook] result={0}", result);
                if (!result)
                {
                    return Task.CompletedTask;
                }

                return Task.CompletedTask;
            }
            _logger.LogWarning("[GodGPTPaymentController][Webhook] User not found {0}", internalUserId);
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
            _clusterClient.GetGrain<IUserBillingGAgent>(internalUserId);
        return await userBillingGrain.HandleStripeWebhookEventAsync(json, stripeSignature);
    }
}