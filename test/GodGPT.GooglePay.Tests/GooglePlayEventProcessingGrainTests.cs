using System;
using System.Threading.Tasks;
using Aevatar.Application.Grains.Common.Constants;
using Xunit;

namespace GodGPT.GooglePay.Tests;

/// <summary>
/// Tests for GooglePlayEventProcessingGrain
/// </summary>
public class GooglePlayEventProcessingGrainTests : GooglePayTestBase
{
    [Fact]
    public async Task ParseEventAndGetUserIdAsync_ValidNotification_ReturnsCorrectData()
    {
        // Arrange
        var grain = await GetGooglePlayEventProcessingGrainAsync();
        var testUserId = Guid.NewGuid();
        var testPurchaseToken = "test_purchase_token_12345";
        
        // Set up purchase token mapping
        var mappingGrain = await GetUserPurchaseTokenMappingGrainAsync(testPurchaseToken);
        await mappingGrain.SetUserIdAsync(testUserId);
        
        var notification = CreateTestRTDNSubscriptionNotification(GooglePlayNotificationType.SUBSCRIPTION_PURCHASED, "product_id", testPurchaseToken);
        var notificationJson = CreateTestRTDN(notification);

        // Act
        var (userId, notificationType, purchaseToken) = await grain.ParseEventAndGetUserIdAsync(notificationJson);

        // Assert
        Assert.Equal(testUserId, userId);
        Assert.Equal("SUBSCRIPTION_PURCHASED", notificationType);
        Assert.Equal(testPurchaseToken, purchaseToken);
    }

    [Fact]
    public async Task ParseEventAndGetUserIdAsync_EmptyJson_ReturnsEmptyResult()
    {
        // Arrange
        var grain = await GetGooglePlayEventProcessingGrainAsync();

        // Act
        var (userId, notificationType, purchaseToken) = await grain.ParseEventAndGetUserIdAsync("");

        // Assert
        Assert.Equal(Guid.Empty, userId);
        Assert.Equal(string.Empty, notificationType);
        Assert.Equal(string.Empty, purchaseToken);
    }

    [Fact]
    public async Task ParseEventAndGetUserIdAsync_InvalidJson_ReturnsEmptyResult()
    {
        // Arrange
        var grain = await GetGooglePlayEventProcessingGrainAsync();
        var invalidJson = "{ invalid json }";

        // Act
        var (userId, notificationType, purchaseToken) = await grain.ParseEventAndGetUserIdAsync(invalidJson);

        // Assert
        Assert.Equal(Guid.Empty, userId);
        Assert.Equal(string.Empty, notificationType);
        Assert.Equal(string.Empty, purchaseToken);
    }

    [Fact]
    public async Task ParseEventAndGetUserIdAsync_UnmappedPurchaseToken_ReturnsEmptyUserId()
    {
        // Arrange
        var grain = await GetGooglePlayEventProcessingGrainAsync();
        var unmappedToken = "unmapped_purchase_token";
        var notification = CreateTestRTDNSubscriptionNotification(GooglePlayNotificationType.SUBSCRIPTION_PURCHASED, "product_id", unmappedToken);
        var notificationJson = CreateTestRTDN(notification);

        // Act
        var (userId, notificationType, purchaseToken) = await grain.ParseEventAndGetUserIdAsync(notificationJson);

        // Assert
        Assert.Equal(Guid.Empty, userId);
        Assert.Equal("SUBSCRIPTION_PURCHASED", notificationType);
        Assert.Equal(unmappedToken, purchaseToken);
    }

    [Theory]
    [InlineData(1, "SUBSCRIPTION_RECOVERED")]
    [InlineData(2, "SUBSCRIPTION_RENEWED")]
    [InlineData(3, "SUBSCRIPTION_CANCELED")]
    [InlineData(4, "SUBSCRIPTION_PURCHASED")]
    [InlineData(13, "SUBSCRIPTION_EXPIRED")]
    [InlineData(999, "UNKNOWN_999")]
    public async Task ParseEventAndGetUserIdAsync_DifferentNotificationTypes_ReturnsCorrectType(int notificationTypeValue, string expectedTypeName)
    {
        // Arrange
        var grain = await GetGooglePlayEventProcessingGrainAsync();
        var testUserId = Guid.NewGuid();
        var testPurchaseToken = "test_purchase_token_12345";
        
        // Set up purchase token mapping
        var mappingGrain = await GetUserPurchaseTokenMappingGrainAsync(testPurchaseToken);
        await mappingGrain.SetUserIdAsync(testUserId);
        
        var notification = CreateTestRTDNSubscriptionNotification((GooglePlayNotificationType)notificationTypeValue, "product_id", testPurchaseToken);
        var notificationJson = CreateTestRTDN(notification);

        // Act
        var (userId, notificationType, purchaseToken) = await grain.ParseEventAndGetUserIdAsync(notificationJson);

        // Assert
        Assert.Equal(testUserId, userId);
        Assert.Equal(expectedTypeName, notificationType);
        Assert.Equal(testPurchaseToken, purchaseToken);
    }
}
