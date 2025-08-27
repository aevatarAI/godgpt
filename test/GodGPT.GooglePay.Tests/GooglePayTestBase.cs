using System;
using System.Text;
using System.Threading.Tasks;
using Aevatar;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Dtos;
using Aevatar.Application.Grains.UserBilling;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Aevatar.Application.Grains.Webhook;

namespace GodGPT.GooglePay.Tests
{
    /// <summary>
    /// Base class for Google Pay integration tests
    /// </summary>
    public class GooglePayTestBase : AevatarOrleansTestBase<GooglePayTestModule>
    {
        protected ILogger Logger => GetService<ILogger<GooglePayTestBase>>();
        
        /// <summary>
        /// Get UserBillingGAgent instance for testing
        /// </summary>
        protected async Task<IUserBillingGAgent> GetUserBillingGAgentAsync(Guid? userId = null)
        {
            var id = userId ?? Guid.NewGuid();
            var grain = Cluster.GrainFactory.GetGrain<IUserBillingGAgent>(id);
            await Task.Delay(10); // Ensure activation
            return grain;
        }

        /// <summary>
        /// Create a test Google Play purchase verification request DTO
        /// </summary>
        protected GooglePlayVerificationDto CreateTestGooglePlayVerificationDto(string userId, string productId = "com.aevatar.godgpt.monthly.ultimate", string purchaseToken = null)
        {
            return new GooglePlayVerificationDto
            {
                UserId = userId,
                ProductId = productId,
                PurchaseToken = purchaseToken ?? $"test_token_{Guid.NewGuid()}",
                PackageName = "com.aevatar.godgpt.test"
            };
        }

        /// <summary>
        /// Creates a complete RTDN payload JSON string from a developer notification object.
        /// </summary>
        protected string CreateTestRTDN(object notificationPayload)
        {
            var developerNotification = new DeveloperNotification
            {
                Version = "1.0",
                PackageName = "com.aevatar.godgpt.test",
                EventTimeMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            if (notificationPayload is SubscriptionNotification subNotification)
            {
                developerNotification.SubscriptionNotification = subNotification;
            }
            else if (notificationPayload is VoidedPurchaseNotification voidedNotification)
            {
                developerNotification.VoidedPurchaseNotification = voidedNotification;
            }

            var jsonPayload = JsonConvert.SerializeObject(developerNotification);
            var base64Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonPayload));

            var rtdn = new GooglePlayRTDNDto
            {
                Message = new GooglePlayMessage
                {
                    Data = base64Payload,
                    MessageId = Guid.NewGuid().ToString(),
                    PublishTime = DateTime.UtcNow
                },
                Subscription = "projects/your-project-id/subscriptions/your-subscription-id"
            };
            return JsonConvert.SerializeObject(rtdn);
        }

        /// <summary>
        /// Creates a SubscriptionNotification object for testing.
        /// </summary>
        protected SubscriptionNotification CreateTestRTDNSubscriptionNotification(GooglePlayNotificationType type, string productId, string purchaseToken)
        {
            return new SubscriptionNotification
            {
                Version = "1.0",
                NotificationType = (int)type,
                PurchaseToken = purchaseToken,
                SubscriptionId = productId
            };
        }

        /// <summary>
        /// Creates a VoidedPurchaseNotification object for testing.
        /// </summary>
        protected VoidedPurchaseNotification CreateTestRTDNVoidedPurchaseNotification(string purchaseToken, string orderId)
        {
            return new VoidedPurchaseNotification
            {
                PurchaseToken = purchaseToken,
                OrderId = orderId
            };
        }
        
        protected Task<IUserPurchaseTokenMappingGrain> GetUserPurchaseTokenMappingGrainAsync(string purchaseToken)
        {
            var grain = Cluster.GrainFactory.GetGrain<IUserPurchaseTokenMappingGrain>(purchaseToken);
            return Task.FromResult(grain);
        }

        protected string CreateTestRTDNNotification(string packageName, SubscriptionNotification subscriptionNotification)
        {
            var rtdn = new GooglePlayRTDNDto
            {
            };
            return JsonConvert.SerializeObject(rtdn);
        }

        protected Task<IGooglePlayEventProcessingGrain> GetGooglePlayEventProcessingGrainAsync()
        {
            var grain = Cluster.GrainFactory.GetGrain<IGooglePlayEventProcessingGrain>("GooglePlayEventProcessingGrain");
            return Task.FromResult(grain);
        }
    }
}
