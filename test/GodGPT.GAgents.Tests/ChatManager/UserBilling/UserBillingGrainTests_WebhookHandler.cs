using System.Text;
using System.Text.Json;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.Http;
using Aevatar.Application.Grains.UserBilling;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using Shouldly;
using Xunit.Abstractions;

namespace Aevatar.Application.Grains.Tests.ChatManager.UserBilling;

public class UserBillingGrainTests_WebhookHandler
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly Mock<IClusterClient> _mockClusterClient;
    private readonly Mock<IUserBillingGAgent> _mockUserBillingGAgent;
    private readonly Mock<ILogger<AppleStoreWebhookHandler>> _mockLogger;
    
    public UserBillingGrainTests_WebhookHandler(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        
        // Initialize Mock objects
        _mockClusterClient = new Mock<IClusterClient>();
        _mockUserBillingGAgent = new Mock<IUserBillingGAgent>();
        _mockLogger = new Mock<ILogger<AppleStoreWebhookHandler>>();
        
        // Set up Mock behavior
        _mockClusterClient
            .Setup(c => c.GetGrain<IUserBillingGAgent>(It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(_mockUserBillingGAgent.Object);
    }
    
    [Fact]
    public async Task AppleStoreWebhookHandler_HandleAsync_Success_Test()
    {
        try
        {
            // Prepare test data
            var notificationJson = GenerateInitialBuyNotification();
            var notificationToken = "test_notification_token";
            
            // Mock request
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("example.com");
            httpContext.Request.Path = new PathString("/api/webhooks/godgpt-appstore-payment");
            httpContext.Request.Headers.Add("X-Apple-Notification-Token", new StringValues(notificationToken));
            
            // Set request body
            var requestBody = Encoding.UTF8.GetBytes(notificationJson);
            var memoryStream = new MemoryStream(requestBody);
            httpContext.Request.Body = memoryStream;
            httpContext.Request.ContentLength = requestBody.Length;
            
            // Mock UserBillingGrain.HandleAppStoreNotificationAsync return value
            _mockUserBillingGAgent
                .Setup(g => g.HandleAppStoreNotificationAsync(It.IsAny<Guid>(),It.IsAny<string>()))
                .ReturnsAsync(true);
            
            // Create AppleStoreWebhookHandler instance
            var handler = new AppleStoreWebhookHandler(
                _mockClusterClient.Object,
                _mockLogger.Object// Skip AppleOptions, as we don't need to verify notification token in tests
            );
            
            // Execute test method
            var result = await handler.HandleAsync(httpContext.Request);
            
            // Verify results
            result.ShouldNotBeNull();
            _testOutputHelper.WriteLine($"AppleStoreWebhookHandler.HandleAsync result: {JsonSerializer.Serialize(result)}");
            
            // Verify method calls
            _mockUserBillingGAgent.Verify(
                g => g.HandleAppStoreNotificationAsync(It.IsAny<Guid>(),
                    It.Is<string>(json => json == notificationJson)
                ),
                Times.Once
            );
        }
        catch (Exception ex)
        {
            _testOutputHelper.WriteLine($"Exception during AppleStoreWebhookHandler_HandleAsync_Success_Test: {ex.Message}");
            _testOutputHelper.WriteLine($"Stack trace: {ex.StackTrace}");
            throw;
        }
    }
    
    [Fact]
    public async Task AppleStoreWebhookHandler_HandleAsync_Error_Test()
    {
        try
        {
            // Prepare test data
            var notificationJson = GenerateInitialBuyNotification();
            var notificationToken = "test_notification_token";
            
            // Mock request
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("example.com");
            httpContext.Request.Path = new PathString("/api/webhooks/godgpt-appstore-payment");
            httpContext.Request.Headers.Add("X-Apple-Notification-Token", new StringValues(notificationToken));
            
            // Set request body
            var requestBody = Encoding.UTF8.GetBytes(notificationJson);
            var memoryStream = new MemoryStream(requestBody);
            httpContext.Request.Body = memoryStream;
            httpContext.Request.ContentLength = requestBody.Length;
            
            // Mock UserBillingGrain.HandleAppStoreNotificationAsync to throw exception
            _mockUserBillingGAgent
                .Setup(g => g.HandleAppStoreNotificationAsync(It.IsAny<Guid>(),It.IsAny<string>()))
                .ThrowsAsync(new Exception("Test exception"));
            
            // Create AppleStoreWebhookHandler instance
            var handler = new AppleStoreWebhookHandler(
                _mockClusterClient.Object,
                _mockLogger.Object // Skip AppleOptions, as we don't need to verify notification token in tests
            );
            
            // Execute test method
            var result = await handler.HandleAsync(httpContext.Request);
            
            // Verify results
            result.ShouldNotBeNull();
            _testOutputHelper.WriteLine($"AppleStoreWebhookHandler.HandleAsync result: {JsonSerializer.Serialize(result)}");
            
            // Even with exceptions, should return a result (should not throw exception)
            // This ensures webhook endpoint always returns HTTP 200 response to prevent Apple from retrying
            var resultObject = result as object;
            resultObject.ShouldNotBeNull();
            
            // Verify method calls
            _mockUserBillingGAgent.Verify(
                g => g.HandleAppStoreNotificationAsync(It.IsAny<Guid>(),
                    It.Is<string>(json => json == notificationJson)
                ),
                Times.Once
            );
        }
        catch (Exception ex)
        {
            _testOutputHelper.WriteLine($"Exception during AppleStoreWebhookHandler_HandleAsync_Error_Test: {ex.Message}");
            _testOutputHelper.WriteLine($"Stack trace: {ex.StackTrace}");
            throw;
        }
    }
    
    [Fact]
    public async Task AppleStoreWebhookHandler_HandleAsync_MissingToken_Test()
    {
        try
        {
            // Prepare test data
            var notificationJson = GenerateInitialBuyNotification();
            
            // Mock request (without notification token)
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("example.com");
            httpContext.Request.Path = new PathString("/api/webhooks/godgpt-appstore-payment");
            
            // Set request body
            var requestBody = Encoding.UTF8.GetBytes(notificationJson);
            var memoryStream = new MemoryStream(requestBody);
            httpContext.Request.Body = memoryStream;
            httpContext.Request.ContentLength = requestBody.Length;
            
            // Mock UserBillingGrain.HandleAppStoreNotificationAsync return value
            _mockUserBillingGAgent
                .Setup(g => g.HandleAppStoreNotificationAsync(It.IsAny<Guid>(),It.IsAny<string>()))
                .ReturnsAsync(false); // Expect validation to fail when token is missing
            
            // Create AppleStoreWebhookHandler instance
            var handler = new AppleStoreWebhookHandler(
                _mockClusterClient.Object,
                _mockLogger.Object // Skip AppleOptions, as we don't need to verify notification token in tests
            );
            
            // Execute test method
            var result = await handler.HandleAsync(httpContext.Request);
            
            // Verify results
            result.ShouldNotBeNull();
            _testOutputHelper.WriteLine($"AppleStoreWebhookHandler.HandleAsync result: {JsonSerializer.Serialize(result)}");
            
            // Verify method calls
            _mockUserBillingGAgent.Verify(
                g => g.HandleAppStoreNotificationAsync(
                    It.IsAny<Guid>(),It.IsAny<string>())
                ,
                Times.Once
            );
        }
        catch (Exception ex)
        {
            _testOutputHelper.WriteLine($"Exception during AppleStoreWebhookHandler_HandleAsync_MissingToken_Test: {ex.Message}");
            _testOutputHelper.WriteLine($"Stack trace: {ex.StackTrace}");
            throw;
        }
    }
    
    // Helper method to generate test data
    private string GenerateInitialBuyNotification()
    {
        return JsonSerializer.Serialize(new
        {
            notification_type = "INITIAL_BUY",
            environment = "Sandbox",
            auto_renew_status = true,
            unified_receipt = new
            {
                latest_receipt = "base64encodedreceipt",
                latest_receipt_info = new[]
                {
                    new
                    {
                        transaction_id = "1000000000000001",
                        original_transaction_id = "1000000000000001",
                        product_id = "com.example.subscription.monthly",
                        purchase_date_ms = "1633046400000", // 2021-10-01
                        expires_date_ms = "1635724800000",  // 2021-11-01
                        is_trial_period = "false"
                    }
                },
                pending_renewal_info = new[]
                {
                    new
                    {
                        auto_renew_status = "1",
                        original_transaction_id = "1000000000000001"
                    }
                }
            }
        });
    }
} 