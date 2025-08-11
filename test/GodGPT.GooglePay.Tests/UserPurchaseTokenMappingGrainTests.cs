namespace GodGPT.GooglePay.Tests;

/// <summary>
/// Tests for UserPurchaseTokenMappingGrain
/// </summary>
public class UserPurchaseTokenMappingGrainTests : GooglePayTestBase
{
    [Fact]
    public async Task SetUserIdAsync_ValidUserId_StoresMapping()
    {
        // Arrange
        var purchaseToken = "test_purchase_token_mapping_123";
        var testUserId = Guid.NewGuid();
        var grain = await GetUserPurchaseTokenMappingGrainAsync(purchaseToken);

        // Act
        await grain.SetUserIdAsync(testUserId);

        // Assert
        var retrievedUserId = await grain.GetUserIdAsync();
        Assert.Equal(testUserId, retrievedUserId);
    }

    [Fact]
    public async Task GetUserIdAsync_NoMapping_ReturnsEmptyGuid()
    {
        // Arrange
        var purchaseToken = "unmapped_purchase_token_456";
        var grain = await GetUserPurchaseTokenMappingGrainAsync(purchaseToken);

        // Act
        var retrievedUserId = await grain.GetUserIdAsync();

        // Assert
        Assert.Equal(Guid.Empty, retrievedUserId);
    }

    [Fact]
    public async Task SetUserIdAsync_UpdateExistingMapping_UpdatesMapping()
    {
        // Arrange
        var purchaseToken = "test_purchase_token_update_789";
        var originalUserId = Guid.NewGuid();
        var newUserId = Guid.NewGuid();
        var grain = await GetUserPurchaseTokenMappingGrainAsync(purchaseToken);

        // Act
        await grain.SetUserIdAsync(originalUserId);
        var firstRetrieved = await grain.GetUserIdAsync();
        
        await grain.SetUserIdAsync(newUserId);
        var secondRetrieved = await grain.GetUserIdAsync();

        // Assert
        Assert.Equal(originalUserId, firstRetrieved);
        Assert.Equal(newUserId, secondRetrieved);
    }

    [Fact]
    public async Task SetUserIdAsync_MultipleTokens_MaintainsIndependentMappings()
    {
        // Arrange
        var token1 = "test_token_1";
        var token2 = "test_token_2";
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();
        
        var grain1 = await GetUserPurchaseTokenMappingGrainAsync(token1);
        var grain2 = await GetUserPurchaseTokenMappingGrainAsync(token2);

        // Act
        await grain1.SetUserIdAsync(userId1);
        await grain2.SetUserIdAsync(userId2);

        // Assert
        var retrievedUserId1 = await grain1.GetUserIdAsync();
        var retrievedUserId2 = await grain2.GetUserIdAsync();
        
        Assert.Equal(userId1, retrievedUserId1);
        Assert.Equal(userId2, retrievedUserId2);
        Assert.NotEqual(retrievedUserId1, retrievedUserId2);
    }
}