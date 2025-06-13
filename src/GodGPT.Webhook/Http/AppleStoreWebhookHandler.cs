using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Webhook;
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
    
    private static readonly string AppleNotificationProcessorGrainId = "AppleNotificationProcessorGrainId_1";

    public AppleStoreWebhookHandler(
        IClusterClient clusterClient,
        ILogger<AppleStoreWebhookHandler> logger)
    {
        _clusterClient = clusterClient;
        _logger = logger;
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

            //TODO Test Notifications
            // 1. Use AppleEventProcessingGrain to parse notification and get userId
            var appleEventProcessingGrain = _clusterClient.GetGrain<IAppleEventProcessingGrain>(AppleNotificationProcessorGrainId);
            var userId = await appleEventProcessingGrain.ParseEventAndGetUserIdAsync(json);
            
            if (userId == default)
            {
                _logger.LogWarning("[AppleStoreWebhookHandler][webhook] Could not determine user ID from notification");
                // Return 200 success even if userId is not found, to prevent Apple from retrying
                return new { success = true, message = "Notification received but no associated user found" };
            }
            
            // 2. Use the found userId to request UserBillingGrain to process the notification
            var userBillingGrain = _clusterClient.GetGrain<IUserBillingGrain>(CommonHelper.GetUserBillingGAgentId(userId));
            var result = await userBillingGrain.HandleAppStoreNotificationAsync(userId, json);
            
            if (!result)
            {
                _logger.LogWarning("[AppleStoreWebhookHandler][Webhook] Failed to process notification for user {UserId}", userId);
                return new { success = false, message = "Failed to process notification" };
            }
            
            // Return success response
            _logger.LogInformation("[AppleStoreWebhookHandler][webhook] Successfully processed notification for user {UserId}", userId);
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