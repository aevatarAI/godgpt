using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Webhook.SDK.Handler;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Aevatar.Application.Grains.Http;

public class AppleStoreWebhookHandler : IWebhookHandler
{
    private readonly ILogger<AppleStoreWebhookHandler> _logger;
    private readonly IClusterClient _clusterClient;
    private readonly IOptionsMonitor<ApplePayOptions> _appleOptions;

    public AppleStoreWebhookHandler(
        IClusterClient clusterClient,
        ILogger<AppleStoreWebhookHandler> logger,
        IOptionsMonitor<ApplePayOptions> appleOptions)
    {
        _clusterClient = clusterClient;
        _logger = logger;
        _appleOptions = appleOptions;
    }

    public string RelativePath => "api/webhooks/godgpt-appstore-payment";

    public string HttpMethod => "POST";

    public async Task<object> HandleAsync(HttpRequest request)
    {
        try
        {
            _logger.LogInformation(
                "AppleStoreWebhookHandler Received request: Method={method}, Path={path}, QueryString={query}",
                request.Method, request.Path, request.QueryString);

            var fullUrl = $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}{request.QueryString}";
            _logger.LogInformation("AppleStoreWebhookHandler Raw URL: {rawUrl}", fullUrl);

            var headers = request.Headers;
            var notificationToken = string.Empty;
            if (headers.TryGetValue("X-Apple-Notification-Token", out StringValues tokenValues))
            {
                notificationToken = tokenValues.ToString();
                _logger.LogInformation("AppleStoreWebhookHandler token={A}", notificationToken);
            }
            else
            {
                _logger.LogWarning("AppleStoreWebhookHandler missing notification token");
            }
            
            var json = await new StreamReader(request.Body).ReadToEndAsync();
            _logger.LogInformation("[AppleStoreWebhookHandler][webhook] json: {0}", json);

            // Find user ID (During notification processing, we extract the original transaction ID from the notification and find the associated user ID)
            // Use a default UserBillingGrain to process the notification
            var defaultUserBillingGrainId = "AppStoreNotificationProcessingGrainId";
            var userBillingGrain = _clusterClient.GetGrain<IUserBillingGrain>(defaultUserBillingGrainId);

            var userId = Guid.NewGuid();
            // Process the notification
            var result = await userBillingGrain.HandleAppStoreNotificationAsync(userId, json, notificationToken);
            if (!result)
            {
                _logger.LogWarning("[AppleStoreWebhookHandler][Webhook] Failed to process notification");
                return new { success = false, message = "Failed to process notification" };
            }

            // Return successful response (Apple requires an HTTP 200 status code)
            return new { success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AppleStoreWebhookHandler Error processing webhook request");
            // Return 200 status code even on error to prevent Apple from retrying (can be adjusted based on business requirements)
            return new { success = false, error = "Internal server error" };
        }
    }
} 