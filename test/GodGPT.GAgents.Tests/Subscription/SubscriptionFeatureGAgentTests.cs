using Aevatar.Application.Grains.Subscription;
using Aevatar.Application.Grains.Subscription.Dtos;
using Aevatar.Application.Grains.Subscription.Enums;
using Aevatar.GodGPT.Tests;
using Shouldly;
using Xunit.Abstractions;

namespace Aevatar.Application.Grains.Tests.Subscription;

/// <summary>
/// Test suite for SubscriptionFeatureGAgent functionality.
/// </summary>
public class SubscriptionFeatureGAgentTests : AevatarGodGPTTestsBase
{
    private readonly ITestOutputHelper _testOutputHelper;

    public SubscriptionFeatureGAgentTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    private ISubscriptionFeatureGAgent GetFeatureGAgent()
    {
        return Cluster.GrainFactory.GetGrain<ISubscriptionFeatureGAgent>(
            SubscriptionGAgentKeys.FeatureGAgentKey);
    }

    #region CreateFeatureAsync Tests

    [Fact]
    public async Task CreateFeatureAsync_Should_Create_Feature_Successfully()
    {
        // Arrange
        var featureGAgent = GetFeatureGAgent();
        var createDto = new CreateSubscriptionFeatureDto
        {
            NameKey = "feature_unlimited_chats",
            DescriptionKey = "feature_unlimited_chats_desc",
            Type = SubscriptionFeatureType.Core,
            DisplayOrder = 1,
            Usage = SubscriptionFeatureUsage.Comparison
        };

        // Act
        var result = await featureGAgent.CreateFeatureAsync(createDto);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldNotBe(Guid.Empty);
        result.NameKey.ShouldBe(createDto.NameKey);
        result.Type.ShouldBe(SubscriptionFeatureType.Core);
        result.DisplayOrder.ShouldBe(1);
        result.Usage.ShouldBe(SubscriptionFeatureUsage.Comparison);

        _testOutputHelper.WriteLine($"Created feature: Id={result.Id}, NameKey={result.NameKey}, Usage={result.Usage}");
    }

    [Fact]
    public async Task CreateFeatureAsync_Should_Create_ProductDisplay_Feature()
    {
        // Arrange
        var featureGAgent = GetFeatureGAgent();
        var createDto = new CreateSubscriptionFeatureDto
        {
            NameKey = "feature_premium_support",
            DescriptionKey = "feature_premium_support_desc",
            Type = SubscriptionFeatureType.None,
            DisplayOrder = 1,
            Usage = SubscriptionFeatureUsage.ProductDisplay
        };

        // Act
        var result = await featureGAgent.CreateFeatureAsync(createDto);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldNotBe(Guid.Empty);
        result.NameKey.ShouldBe(createDto.NameKey);
        result.Type.ShouldBe(SubscriptionFeatureType.None);
        result.Usage.ShouldBe(SubscriptionFeatureUsage.ProductDisplay);

        _testOutputHelper.WriteLine($"Created ProductDisplay feature: Id={result.Id}, NameKey={result.NameKey}, Usage={result.Usage}");
    }

    [Fact]
    public async Task CreateFeatureAsync_Should_Create_Multiple_Features()
    {
        // Arrange
        var featureGAgent = GetFeatureGAgent();
        
        var features = new List<CreateSubscriptionFeatureDto>
        {
            new() { NameKey = "feature_1", Type = SubscriptionFeatureType.Core, DisplayOrder = 1 },
            new() { NameKey = "feature_2", Type = SubscriptionFeatureType.Advanced, DisplayOrder = 2 },
            new() { NameKey = "feature_3", Type = SubscriptionFeatureType.Core, DisplayOrder = 3 }
        };

        // Act
        var results = new List<SubscriptionFeatureDto>();
        foreach (var dto in features)
        {
            var result = await featureGAgent.CreateFeatureAsync(dto);
            results.Add(result);
        }

        // Assert
        results.Count.ShouldBe(3);
        results.Select(r => r.NameKey).ShouldContain("feature_1");
        results.Select(r => r.NameKey).ShouldContain("feature_2");
        results.Select(r => r.NameKey).ShouldContain("feature_3");

        _testOutputHelper.WriteLine($"Created {results.Count} features successfully");
    }

    #endregion

    #region UpdateFeatureAsync Tests

    [Fact]
    public async Task UpdateFeatureAsync_Should_Update_Feature_Successfully()
    {
        // Arrange
        var featureGAgent = GetFeatureGAgent();
        var createDto = new CreateSubscriptionFeatureDto
        {
            NameKey = "feature_to_update",
            DescriptionKey = "original_description",
            Type = SubscriptionFeatureType.Core,
            DisplayOrder = 10,
            Usage = SubscriptionFeatureUsage.Comparison
        };
        var created = await featureGAgent.CreateFeatureAsync(createDto);

        var updateDto = new UpdateSubscriptionFeatureDto
        {
            NameKey = "feature_updated",
            DescriptionKey = "updated_description",
            Type = SubscriptionFeatureType.Advanced,
            DisplayOrder = 20
        };

        // Act
        var result = await featureGAgent.UpdateFeatureAsync(created.Id, updateDto);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(created.Id);
        result.NameKey.ShouldBe("feature_updated");
        result.Type.ShouldBe(SubscriptionFeatureType.Advanced);
        result.DisplayOrder.ShouldBe(20);
        result.Usage.ShouldBe(SubscriptionFeatureUsage.Comparison); // Usage should remain unchanged

        _testOutputHelper.WriteLine($"Updated feature: Id={result.Id}, NameKey={result.NameKey}");
    }

    [Fact]
    public async Task UpdateFeatureAsync_Should_Update_Usage_Successfully()
    {
        // Arrange
        var featureGAgent = GetFeatureGAgent();
        var createDto = new CreateSubscriptionFeatureDto
        {
            NameKey = "feature_to_update_usage",
            Type = SubscriptionFeatureType.Core,
            DisplayOrder = 1,
            Usage = SubscriptionFeatureUsage.Comparison
        };
        var created = await featureGAgent.CreateFeatureAsync(createDto);

        var updateDto = new UpdateSubscriptionFeatureDto
        {
            Type = SubscriptionFeatureType.None,
            Usage = SubscriptionFeatureUsage.ProductDisplay
        };

        // Act
        var result = await featureGAgent.UpdateFeatureAsync(created.Id, updateDto);

        // Assert
        result.ShouldNotBeNull();
        result.Type.ShouldBe(SubscriptionFeatureType.None);
        result.Usage.ShouldBe(SubscriptionFeatureUsage.ProductDisplay);

        _testOutputHelper.WriteLine($"Updated feature usage: Id={result.Id}, Usage={result.Usage}");
    }

    [Fact]
    public async Task UpdateFeatureAsync_Should_Throw_When_Feature_Not_Found()
    {
        // Arrange
        var featureGAgent = GetFeatureGAgent();
        var nonExistentId = Guid.NewGuid();
        var updateDto = new UpdateSubscriptionFeatureDto
        {
            NameKey = "should_not_exist"
        };

        // Act & Assert
        await Should.ThrowAsync<KeyNotFoundException>(
            async () => await featureGAgent.UpdateFeatureAsync(nonExistentId, updateDto));

        _testOutputHelper.WriteLine($"Correctly threw KeyNotFoundException for non-existent feature");
    }

    #endregion

    #region DeleteFeatureAsync Tests

    [Fact]
    public async Task DeleteFeatureAsync_Should_Delete_Feature_Successfully()
    {
        // Arrange
        var featureGAgent = GetFeatureGAgent();
        var createDto = new CreateSubscriptionFeatureDto
        {
            NameKey = "feature_to_delete",
            Type = SubscriptionFeatureType.Core,
            DisplayOrder = 100
        };
        var created = await featureGAgent.CreateFeatureAsync(createDto);

        // Act
        await featureGAgent.DeleteFeatureAsync(created.Id);

        // Assert
        var deletedFeature = await featureGAgent.GetFeatureAsync(created.Id);
        deletedFeature.ShouldBeNull();

        _testOutputHelper.WriteLine($"Deleted feature: Id={created.Id}");
    }

    [Fact]
    public async Task DeleteFeatureAsync_Should_Throw_When_Feature_Not_Found()
    {
        // Arrange
        var featureGAgent = GetFeatureGAgent();
        var nonExistentId = Guid.NewGuid();

        // Act & Assert
        await Should.ThrowAsync<KeyNotFoundException>(
            async () => await featureGAgent.DeleteFeatureAsync(nonExistentId));

        _testOutputHelper.WriteLine($"Correctly threw KeyNotFoundException for non-existent feature");
    }

    #endregion

    #region GetFeatureAsync Tests

    [Fact]
    public async Task GetFeatureAsync_Should_Return_Feature_When_Exists()
    {
        // Arrange
        var featureGAgent = GetFeatureGAgent();
        var createDto = new CreateSubscriptionFeatureDto
        {
            NameKey = "feature_to_get",
            DescriptionKey = "test_description",
            Type = SubscriptionFeatureType.Advanced,
            DisplayOrder = 5
        };
        var created = await featureGAgent.CreateFeatureAsync(createDto);

        // Act
        var result = await featureGAgent.GetFeatureAsync(created.Id);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(created.Id);
        result.NameKey.ShouldBe("feature_to_get");
        result.Type.ShouldBe(SubscriptionFeatureType.Advanced);

        _testOutputHelper.WriteLine($"Retrieved feature: Id={result.Id}, NameKey={result.NameKey}");
    }

    [Fact]
    public async Task GetFeatureAsync_Should_Return_Null_When_Not_Exists()
    {
        // Arrange
        var featureGAgent = GetFeatureGAgent();
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await featureGAgent.GetFeatureAsync(nonExistentId);

        // Assert
        result.ShouldBeNull();

        _testOutputHelper.WriteLine($"Correctly returned null for non-existent feature");
    }

    #endregion

    #region GetAllFeaturesAsync Tests

    [Fact]
    public async Task GetAllFeaturesAsync_Should_Return_All_Features()
    {
        // Arrange
        var featureGAgent = GetFeatureGAgent();
        
        // Create some features
        await featureGAgent.CreateFeatureAsync(new CreateSubscriptionFeatureDto
        {
            NameKey = "all_feature_1",
            Type = SubscriptionFeatureType.Core,
            DisplayOrder = 1
        });
        await featureGAgent.CreateFeatureAsync(new CreateSubscriptionFeatureDto
        {
            NameKey = "all_feature_2",
            Type = SubscriptionFeatureType.Advanced,
            DisplayOrder = 2
        });

        // Act
        var result = await featureGAgent.GetAllFeaturesAsync();

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBeGreaterThanOrEqualTo(2);

        _testOutputHelper.WriteLine($"Retrieved {result.Count} features");
    }

    #endregion

    #region GetFeaturesByTypeAsync Tests

    [Fact]
    public async Task GetFeaturesByTypeAsync_Should_Return_Features_Of_Type()
    {
        // Arrange
        var featureGAgent = GetFeatureGAgent();
        
        await featureGAgent.CreateFeatureAsync(new CreateSubscriptionFeatureDto
        {
            NameKey = "type_core_feature",
            Type = SubscriptionFeatureType.Core,
            DisplayOrder = 1
        });
        await featureGAgent.CreateFeatureAsync(new CreateSubscriptionFeatureDto
        {
            NameKey = "type_advanced_feature",
            Type = SubscriptionFeatureType.Advanced,
            DisplayOrder = 2
        });

        // Act
        var coreFeatures = await featureGAgent.GetFeaturesByTypeAsync(SubscriptionFeatureType.Core);
        var advancedFeatures = await featureGAgent.GetFeaturesByTypeAsync(SubscriptionFeatureType.Advanced);

        // Assert
        coreFeatures.ShouldNotBeNull();
        advancedFeatures.ShouldNotBeNull();
        coreFeatures.All(f => f.Type == SubscriptionFeatureType.Core).ShouldBeTrue();
        advancedFeatures.All(f => f.Type == SubscriptionFeatureType.Advanced).ShouldBeTrue();

        _testOutputHelper.WriteLine($"Core features: {coreFeatures.Count}, Advanced features: {advancedFeatures.Count}");
    }

    #endregion

    #region ReorderFeaturesAsync Tests

    [Fact]
    public async Task ReorderFeaturesAsync_Should_Reorder_Features()
    {
        // Arrange
        var featureGAgent = GetFeatureGAgent();
        
        var feature1 = await featureGAgent.CreateFeatureAsync(new CreateSubscriptionFeatureDto
        {
            NameKey = "reorder_feature_1",
            Type = SubscriptionFeatureType.Core,
            DisplayOrder = 1
        });
        var feature2 = await featureGAgent.CreateFeatureAsync(new CreateSubscriptionFeatureDto
        {
            NameKey = "reorder_feature_2",
            Type = SubscriptionFeatureType.Core,
            DisplayOrder = 2
        });

        var newOrders = new List<SubscriptionFeatureOrderItemDto>
        {
            new() { FeatureId = feature1.Id, DisplayOrder = 10 },
            new() { FeatureId = feature2.Id, DisplayOrder = 5 }
        };

        // Act
        await featureGAgent.ReorderFeaturesAsync(newOrders);

        // Assert
        var updatedFeature1 = await featureGAgent.GetFeatureAsync(feature1.Id);
        var updatedFeature2 = await featureGAgent.GetFeatureAsync(feature2.Id);

        updatedFeature1.ShouldNotBeNull();
        updatedFeature2.ShouldNotBeNull();
        updatedFeature1.DisplayOrder.ShouldBe(10);
        updatedFeature2.DisplayOrder.ShouldBe(5);

        _testOutputHelper.WriteLine($"Reordered features: {feature1.Id}={updatedFeature1.DisplayOrder}, {feature2.Id}={updatedFeature2.DisplayOrder}");
    }

    #endregion

    #region GetFeaturesByUsageAsync Tests

    [Fact]
    public async Task GetFeaturesByUsageAsync_Should_Return_Features_By_Usage()
    {
        // Arrange
        var featureGAgent = GetFeatureGAgent();
        
        await featureGAgent.CreateFeatureAsync(new CreateSubscriptionFeatureDto
        {
            NameKey = "usage_comparison_feature",
            Type = SubscriptionFeatureType.Core,
            DisplayOrder = 1,
            Usage = SubscriptionFeatureUsage.Comparison
        });
        await featureGAgent.CreateFeatureAsync(new CreateSubscriptionFeatureDto
        {
            NameKey = "usage_product_display_feature",
            Type = SubscriptionFeatureType.None,
            DisplayOrder = 2,
            Usage = SubscriptionFeatureUsage.ProductDisplay
        });

        // Act
        var comparisonFeatures = await featureGAgent.GetFeaturesByUsageAsync(SubscriptionFeatureUsage.Comparison);
        var productDisplayFeatures = await featureGAgent.GetFeaturesByUsageAsync(SubscriptionFeatureUsage.ProductDisplay);

        // Assert
        comparisonFeatures.ShouldNotBeNull();
        productDisplayFeatures.ShouldNotBeNull();
        comparisonFeatures.All(f => f.Usage == SubscriptionFeatureUsage.Comparison).ShouldBeTrue();
        productDisplayFeatures.All(f => f.Usage == SubscriptionFeatureUsage.ProductDisplay).ShouldBeTrue();

        _testOutputHelper.WriteLine($"Comparison features: {comparisonFeatures.Count}, ProductDisplay features: {productDisplayFeatures.Count}");
    }

    #endregion

    #region GetFeaturesByIdsAsync Tests

    [Fact]
    public async Task GetFeaturesByIdsAsync_Should_Return_Features_By_Ids()
    {
        // Arrange
        var featureGAgent = GetFeatureGAgent();
        
        var feature1 = await featureGAgent.CreateFeatureAsync(new CreateSubscriptionFeatureDto
        {
            NameKey = "byids_feature_1",
            Type = SubscriptionFeatureType.Core,
            DisplayOrder = 1
        });
        var feature2 = await featureGAgent.CreateFeatureAsync(new CreateSubscriptionFeatureDto
        {
            NameKey = "byids_feature_2",
            Type = SubscriptionFeatureType.Advanced,
            DisplayOrder = 2
        });

        var idsToGet = new List<Guid> { feature1.Id, feature2.Id };

        // Act
        var result = await featureGAgent.GetFeaturesByIdsAsync(idsToGet);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(2);
        result.Select(f => f.Id).ShouldContain(feature1.Id);
        result.Select(f => f.Id).ShouldContain(feature2.Id);

        _testOutputHelper.WriteLine($"Retrieved {result.Count} features by IDs");
    }

    #endregion
}
