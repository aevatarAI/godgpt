using GodGPT.Webhook.Http;
using Microsoft.AspNetCore.Http;
using System.Text;
using Xunit;
using System;
using System.Threading.Tasks;
using Aevatar.Application.Grains.Common.Constants;

namespace GodGPT.GooglePay.Tests;

/// <summary>
/// Tests for GooglePayWebhookHandler
/// </summary>
public class GooglePayWebhookHandlerTests : GooglePayTestBase
{
    [Fact]
    public void RelativePath_ReturnsCorrectPath()
    {
        // Arrange
        var handler = GetService<GooglePayWebhookHandler>();

        // Act
        var path = handler.RelativePath;

        // Assert
        Assert.Equal("api/webhooks/godgpt-googleplay-payment", path);
    }

    [Fact]
    public void HttpMethod_ReturnsPost()
    {
        // Arrange
        var handler = GetService<GooglePayWebhookHandler>();

        // Act
        var method = handler.HttpMethod;

        // Assert
        Assert.Equal("POST", method);
    }

    [Fact]
    public async Task HandleAsync_ValidNotificationWithMappedUser_ReturnsSuccess()
    {
        // Arrange
        var handler = GetService<GooglePayWebhookHandler>();
        var testUserId = Guid.NewGuid();
        var testPurchaseToken = "test_purchase_token_webhook";
        
        // Set up purchase token mapping
        var mappingGrain = await GetUserPurchaseTokenMappingGrainAsync(testPurchaseToken);
        await mappingGrain.SetUserIdAsync(testUserId);
        
        var notification = CreateTestRTDNSubscriptionNotification(GooglePlayNotificationType.SUBSCRIPTION_PURCHASED, "product_id", testPurchaseToken);
        var notificationJson = CreateTestRTDN(notification);
        var request = CreateMockRequest(notificationJson);

        // Act
        var result = await handler.HandleAsync(request);

        // Assert
        Assert.NotNull(result);
        var response = result as dynamic;
        Assert.True(response.success);
    }

    [Fact]
    public async Task HandleAsync_NotificationWithoutMappedUser_ReturnsSuccessWithMessage()
    {
        // Arrange
        var handler = GetService<GooglePayWebhookHandler>();
        var unmappedToken = "unmapped_purchase_token";
        var notification = CreateTestRTDNSubscriptionNotification(GooglePlayNotificationType.SUBSCRIPTION_PURCHASED, "product_id", unmappedToken);
        var notificationJson = CreateTestRTDN(notification);
        var request = CreateMockRequest(notificationJson);

        // Act
        var result = await handler.HandleAsync(request);

        // Assert
        Assert.NotNull(result);
        var response = result as dynamic;
        Assert.NotNull(response);
        Assert.True(response.success);
        Assert.Contains("no associated user found", response.message?.ToString());
    }

    [Fact]
    public async Task HandleAsync_FilteredNotificationType_ReturnsSuccessWithFilterMessage()
    {
        // Arrange
        var handler = GetService<GooglePayWebhookHandler>();
        var testUserId = Guid.NewGuid();
        var testPurchaseToken = "test_purchase_token_filtered";
        
        // Set up purchase token mapping
        var mappingGrain = await GetUserPurchaseTokenMappingGrainAsync(testPurchaseToken);
        await mappingGrain.SetUserIdAsync(testUserId);
        
        // Use a notification type that will be filtered (e.g., type 999 which is not a key business event)
        var notification = CreateTestRTDNSubscriptionNotification((GooglePlayNotificationType)999, "product_id", testPurchaseToken);
        var notificationJson = CreateTestRTDN(notification);
        var request = CreateMockRequest(notificationJson);

        // Act
        var result = await handler.HandleAsync(request);

        // Assert
        Assert.NotNull(result);
        var response = result as dynamic;
        Assert.NotNull(response);
        Assert.True(response.success);
        Assert.Contains("filtered by type", response.message?.ToString());
    }

    [Fact]
    public async Task HandleAsync_EmptyRequestBody_HandlesGracefully()
    {
        // Arrange
        var handler = GetService<GooglePayWebhookHandler>();
        var request = CreateMockRequest("");

        // Act
        var result = await handler.HandleAsync(request);

        // Assert
        Assert.NotNull(result);
        var response = result as dynamic;
        Assert.NotNull(response);
        Assert.True(response.success);
        Assert.Contains("no associated user found", response.message?.ToString());
    }

    [Fact]
    public async Task HandleAsync_InvalidJson_HandlesGracefully()
    {
        // Arrange
        var handler = GetService<GooglePayWebhookHandler>();
        var invalidJson = "{ invalid json }";
        var request = CreateMockRequest(invalidJson);

        // Act
        var result = await handler.HandleAsync(request);

        // Assert
        Assert.NotNull(result);
        var response = result as dynamic;
        Assert.NotNull(response);
        // Should handle gracefully and return appropriate response
    }

    /// <summary>
    /// Create a mock HTTP request with the given JSON body
    /// </summary>
    private HttpRequest CreateMockRequest(string jsonBody)
    {
        var context = new DefaultHttpContext();
        var request = context.Request;
        
        request.Method = "POST";
        request.Path = "/api/webhooks/godgpt-googleplay-payment";
        request.ContentType = "application/json";
        
        var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
        request.Body = new MemoryStream(bodyBytes);
        
        return request;
    }
}
