using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.Options;
using Moq;
using Shouldly;
using Xunit.Abstractions;

namespace Aevatar.Application.Grains.Tests.ChatManager.UserBilling;

/// <summary>
/// Test cases for GetAppleProductsAsync method in UserBillingGrain
/// </summary>
public partial class UserBillingGrainTests  : AevatarOrleansTestBase<AevatarGodGPTTestsMoudle>
{
    [Fact]
    public async Task GetAppleProductsAsync_NoProducts_Test()
    {
        try
        {
            // Arrange
            var userId = Guid.NewGuid().ToString();
            _testOutputHelper.WriteLine($"Testing GetAppleProductsAsync_NoProducts with UserId: {userId}");
            
            // Get UserBillingGrain
            var userBillingGrain = Cluster.GrainFactory.GetGrain<IUserBillingGrain>(userId);
            
            // Act
            var result = await userBillingGrain.GetAppleProductsAsync();
            
            // Assert
            result.ShouldNotBeNull();
            result.Count.ShouldBe(1);
            _testOutputHelper.WriteLine("Test passed: Empty products list returned when no products configured");
        }
        catch (Exception ex)
        {
            _testOutputHelper.WriteLine($"Exception during GetAppleProductsAsync_NoProducts test: {ex.Message}");
            _testOutputHelper.WriteLine($"Stack trace: {ex.StackTrace}");
            throw;
        }
    }
} 