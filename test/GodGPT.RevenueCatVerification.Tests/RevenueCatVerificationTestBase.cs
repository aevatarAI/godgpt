using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.UserBilling;
using Microsoft.Extensions.Options;
using System.Net.Http;

namespace GodGPT.RevenueCatVerification.Tests;

/// <summary>
/// Base class for RevenueCat verification tests
/// </summary>
public class RevenueCatVerificationTestBase : AevatarOrleansTestBase<RevenueCatVerificationTestModule>
{
    protected ILogger Logger => GetService<ILogger<RevenueCatVerificationTestBase>>() ?? throw new InvalidOperationException("Logger service not found");

    /// <summary>
    /// Get UserBillingGAgent instance for testing
    /// </summary>
    /// <param name="userId">User ID for the grain, uses default if not provided</param>
    /// <returns>UserBillingGAgent instance</returns>
    protected async Task<IUserBillingGAgent> GetUserBillingGAgentAsync(Guid? userId = null)
    {
        var id = userId ?? Guid.NewGuid();
        var grain = Cluster.GrainFactory.GetGrain<IUserBillingGAgent>(id);
        
        // Ensure grain is activated
        await Task.Delay(10); // Small delay to ensure activation
        
        return grain;
    }

    /// <summary>
    /// Create a test RevenueCat JSON response based on the provided sample
    /// </summary>
    /// <param name="transactionId">Transaction ID to use in the response</param>
    /// <param name="productId">Product ID (subscription key) to use</param>
    /// <param name="userId">User ID for the subscriber</param>
    /// <returns>JSON string representing RevenueCat response</returns>
    protected string CreateTestRevenueCatResponse(string transactionId = "GPA.3327-7042-0698-86706", 
                                                string productId = "premium_weekly_test1", 
                                                string userId = "ebb5d7d5-3ae7-39dd-21c1-3a1a4f097144")
    {
        var response = new
        {
            request_date = "2025-08-19T02:05:37Z",
            request_date_ms = 1755569137650L,
            subscriber = new
            {
                entitlements = new { },
                first_seen = "2025-08-11T13:43:12Z",
                last_seen = "2025-08-18T03:38:39Z",
                management_url = "https://play.google.com/store/account/subscriptions",
                non_subscriptions = new { },
                original_app_user_id = "$RCAnonymousID:520ed1732528410d837118eb2f661158",
                original_application_version = (string?)null,
                original_purchase_date = (string?)null,
                other_purchases = new { },
                subscriptions = new Dictionary<string, object>
                {
                    [productId] = new
                    {
                        auto_resume_date = (string?)null,
                        billing_issues_detected_at = (string?)null,
                        display_name = (string?)null,
                        expires_date = "2025-08-19T02:07:00Z",
                        grace_period_expires_date = (string?)null,
                        is_sandbox = true,
                        management_url = "https://play.google.com/store/account/subscriptions",
                        original_purchase_date = "2025-08-19T02:02:00Z",
                        period_type = "normal",
                        price = new
                        {
                            amount = 48.0,
                            currency = "HKD"
                        },
                        product_plan_identifier = "test1-week-1",
                        purchase_date = "2025-08-19T02:02:00Z",
                        refunded_at = (string?)null,
                        store = "play_store",
                        store_transaction_id = transactionId,
                        unsubscribe_detected_at = (string?)null
                    }
                }
            }
        };

        return JsonConvert.SerializeObject(response);
    }

    /// <summary>
    /// Create a test GooglePlay transaction verification request
    /// </summary>
    /// <param name="userId">User ID for verification</param>
    /// <param name="transactionId">Transaction ID to verify</param>
    /// <returns>GooglePlayTransactionVerificationDto for testing</returns>
    protected GooglePlayTransactionVerificationDto CreateTestVerificationRequest(string userId = "test-user-123", 
                                                                               string transactionId = "GPA.3327-7042-0698-86706")
    {
        return new GooglePlayTransactionVerificationDto
        {
            UserId = userId,
            TransactionIdentifier = transactionId
        };
    }

    /// <summary>
    /// Create a mock HttpClient for RevenueCat API testing
    /// </summary>
    /// <param name="responseJson">JSON response to return from the mock API</param>
    /// <param name="statusCode">HTTP status code to return</param>
    /// <returns>Mock HttpClient with predefined response</returns>
    protected HttpClient CreateMockHttpClient(string responseJson, System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", 
                ItExpr.IsAny<HttpRequestMessage>(), 
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            });

        return new HttpClient(mockHandler.Object);
    }

    /// <summary>
    /// Get GooglePayOptions for testing
    /// </summary>
    /// <returns>Configured GooglePayOptions instance</returns>
    protected GooglePayOptions GetTestGooglePayOptions()
    {
        var options = GetService<IOptions<GooglePayOptions>>();
        return options?.Value ?? new GooglePayOptions
        {
            PackageName = "com.aevatar.godgpt.test",
            RevenueCatApiKey = "goog_test_api_key_for_testing",
            RevenueCatBaseUrl = "https://api.revenuecat.com/v1",
            Products = new List<GooglePayProduct>
            {
                new GooglePayProduct
                {
                    ProductId = "premium_weekly_test1",
                    PlanType = 1,
                    Amount = 48.0m,
                    Currency = "HKD",
                    IsSubscription = true,
                    IsUltimate = false
                }
            }
        };
    }
}
