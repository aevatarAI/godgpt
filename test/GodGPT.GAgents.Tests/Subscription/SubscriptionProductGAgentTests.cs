using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Subscription;
using Aevatar.Application.Grains.Subscription.Dtos;
using Aevatar.GodGPT.Tests;
using Shouldly;
using Xunit.Abstractions;

namespace Aevatar.Application.Grains.Tests.Subscription;

/// <summary>
/// Test suite for SubscriptionProductGAgent functionality.
/// </summary>
public class SubscriptionProductGAgentTests : AevatarGodGPTTestsBase
{
    private readonly ITestOutputHelper _testOutputHelper;

    public SubscriptionProductGAgentTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    private ISubscriptionProductGAgent GetProductGAgent()
    {
        return Cluster.GrainFactory.GetGrain<ISubscriptionProductGAgent>(
            SubscriptionGAgentKeys.ProductGAgentKey);
    }

    #region CreateProductAsync Tests

    [Fact]
    public async Task CreateProductAsync_Should_Create_Product_Successfully()
    {
        // Arrange
        var productGAgent = GetProductGAgent();
        var createDto = new CreateProductDto
        {
            NameKey = "pro_monthly",
            PlanType = PlanType.Month,
            DescriptionKey = "pro_monthly_desc",
            HighlightKey = "pro_monthly_highlight",
            IsUltimate = false,
            FeatureIds = new List<Guid>(),
            PlatformProductId = "prod_test_001",
            Platform = PaymentPlatform.Stripe
        };

        // Act
        var result = await productGAgent.CreateProductAsync(createDto);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldNotBe(Guid.Empty);
        result.NameKey.ShouldBe("pro_monthly");
        result.PlanType.ShouldBe(PlanType.Month);
        result.Platform.ShouldBe(PaymentPlatform.Stripe);

        _testOutputHelper.WriteLine($"Created product: Id={result.Id}, NameKey={result.NameKey}");
    }

    [Fact]
    public async Task CreateProductAsync_Should_Create_Product_With_Label()
    {
        // Arrange
        var productGAgent = GetProductGAgent();
        var labelGAgent = Cluster.GrainFactory.GetGrain<ISubscriptionLabelGAgent>(
            SubscriptionGAgentKeys.LabelGAgentKey);
        
        // Create a label first
        var label = await labelGAgent.CreateLabelAsync(new CreateSubscriptionLabelDto
        {
            NameKey = "product_label"
        });

        var createDto = new CreateProductDto
        {
            NameKey = "pro_with_label",
            LabelId = label.Id,
            PlanType = PlanType.Month,
            DescriptionKey = "pro_desc",
            PlatformProductId = "prod_test_002",
            Platform = PaymentPlatform.Stripe
        };

        // Act
        var result = await productGAgent.CreateProductAsync(createDto);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldNotBe(Guid.Empty);

        // Verify the product has the label
        var product = await productGAgent.GetProductAsync(result.Id);
        product.ShouldNotBeNull();
        product.LabelId.ShouldBe(label.Id);

        _testOutputHelper.WriteLine($"Created product with label: Id={result.Id}, LabelId={label.Id}");
    }

    [Fact]
    public async Task CreateProductAsync_Should_Create_Multiple_Products()
    {
        // Arrange
        var productGAgent = GetProductGAgent();
        
        var products = new List<CreateProductDto>
        {
            new()
            {
                NameKey = "basic_plan",
                PlanType = PlanType.None,
                DescriptionKey = "basic_desc",
                PlatformProductId = "prod_basic",
                Platform = PaymentPlatform.Stripe
            },
            new()
            {
                NameKey = "pro_plan",
                PlanType = PlanType.Month,
                DescriptionKey = "pro_desc",
                PlatformProductId = "prod_pro",
                Platform = PaymentPlatform.Stripe
            },
            new()
            {
                NameKey = "ultimate_plan",
                PlanType = PlanType.Month,
                DescriptionKey = "ultimate_desc",
                IsUltimate = true,
                PlatformProductId = "prod_ultimate",
                Platform = PaymentPlatform.Stripe
            }
        };

        // Act
        var results = new List<SubscriptionProductDto>();
        foreach (var dto in products)
        {
            var result = await productGAgent.CreateProductAsync(dto);
            results.Add(result);
        }

        // Assert
        results.Count.ShouldBe(3);
        results.Select(r => r.NameKey).ShouldContain("basic_plan");
        results.Select(r => r.NameKey).ShouldContain("pro_plan");
        results.Select(r => r.NameKey).ShouldContain("ultimate_plan");

        _testOutputHelper.WriteLine($"Created {results.Count} products successfully");
    }

    #endregion

    #region UpdateProductAsync Tests

    [Fact]
    public async Task UpdateProductAsync_Should_Update_Product_Successfully()
    {
        // Arrange
        var productGAgent = GetProductGAgent();
        var createDto = new CreateProductDto
        {
            NameKey = "product_to_update",
            PlanType = PlanType.Month,
            DescriptionKey = "original_description",
            PlatformProductId = "prod_update_test",
            Platform = PaymentPlatform.Stripe
        };
        var created = await productGAgent.CreateProductAsync(createDto);

        var updateDto = new UpdateProductDto
        {
            NameKey = "product_updated",
            DescriptionKey = "updated_description",
            HighlightKey = "new_highlight",
            IsUltimate = true
        };

        // Act
        var result = await productGAgent.UpdateProductAsync(created.Id, updateDto);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(created.Id);
        result.NameKey.ShouldBe("product_updated");
        result.IsUltimate.ShouldBeTrue();

        _testOutputHelper.WriteLine($"Updated product: Id={result.Id}, NameKey={result.NameKey}");
    }

    [Fact]
    public async Task UpdateProductAsync_Should_Throw_When_Product_Not_Found()
    {
        // Arrange
        var productGAgent = GetProductGAgent();
        var nonExistentId = Guid.NewGuid();
        var updateDto = new UpdateProductDto
        {
            NameKey = "should_not_exist"
        };

        // Act & Assert
        await Should.ThrowAsync<KeyNotFoundException>(
            async () => await productGAgent.UpdateProductAsync(nonExistentId, updateDto));

        _testOutputHelper.WriteLine($"Correctly threw KeyNotFoundException for non-existent product");
    }

    #endregion

    #region DeleteProductAsync Tests

    [Fact]
    public async Task DeleteProductAsync_Should_Delete_Product_Successfully()
    {
        // Arrange
        var productGAgent = GetProductGAgent();
        var createDto = new CreateProductDto
        {
            NameKey = "product_to_delete",
            PlanType = PlanType.Month,
            DescriptionKey = "delete_test",
            PlatformProductId = "prod_delete_test",
            Platform = PaymentPlatform.Stripe
        };
        var created = await productGAgent.CreateProductAsync(createDto);

        // Act
        await productGAgent.DeleteProductAsync(created.Id);

        // Assert
        var deletedProduct = await productGAgent.GetProductAsync(created.Id);
        deletedProduct.ShouldBeNull();

        _testOutputHelper.WriteLine($"Deleted product: Id={created.Id}");
    }

    [Fact]
    public async Task DeleteProductAsync_Should_Throw_When_Product_Not_Found()
    {
        // Arrange
        var productGAgent = GetProductGAgent();
        var nonExistentId = Guid.NewGuid();

        // Act & Assert
        await Should.ThrowAsync<KeyNotFoundException>(
            async () => await productGAgent.DeleteProductAsync(nonExistentId));

        _testOutputHelper.WriteLine($"Correctly threw KeyNotFoundException for non-existent product");
    }

    #endregion

    #region SetProductListedAsync Tests

    [Fact]
    public async Task SetProductListedAsync_Should_Set_Product_Listed()
    {
        // Arrange
        var productGAgent = GetProductGAgent();
        var createDto = new CreateProductDto
        {
            NameKey = "product_to_list",
            PlanType = PlanType.Month,
            DescriptionKey = "list_test",
            PlatformProductId = "prod_list_test",
            Platform = PaymentPlatform.Stripe
        };
        var created = await productGAgent.CreateProductAsync(createDto);

        // Act - Set as listed
        var listedResult = await productGAgent.SetProductListedAsync(created.Id, true);

        // Assert
        listedResult.ShouldNotBeNull();
        
        var product = await productGAgent.GetProductAsync(created.Id);
        product.ShouldNotBeNull();
        product.IsListed!.Value.ShouldBeTrue();

        // Act - Set as unlisted
        await productGAgent.SetProductListedAsync(created.Id, false);
        
        product = await productGAgent.GetProductAsync(created.Id);
        product.ShouldNotBeNull();
        product.IsListed!.Value.ShouldBeFalse();

        _testOutputHelper.WriteLine($"Set product listed/unlisted: Id={created.Id}");
    }

    [Fact]
    public async Task SetProductListedAsync_Should_Throw_When_Product_Not_Found()
    {
        // Arrange
        var productGAgent = GetProductGAgent();
        var nonExistentId = Guid.NewGuid();

        // Act & Assert
        await Should.ThrowAsync<KeyNotFoundException>(
            async () => await productGAgent.SetProductListedAsync(nonExistentId, true));

        _testOutputHelper.WriteLine($"Correctly threw KeyNotFoundException for non-existent product");
    }

    #endregion

    #region GetProductAsync Tests

    [Fact]
    public async Task GetProductAsync_Should_Return_Product_When_Exists()
    {
        // Arrange
        var productGAgent = GetProductGAgent();
        var createDto = new CreateProductDto
        {
            NameKey = "product_to_get",
            PlanType = PlanType.Month,
            DescriptionKey = "get_test",
            PlatformProductId = "prod_get_test",
            Platform = PaymentPlatform.Stripe
        };
        var created = await productGAgent.CreateProductAsync(createDto);

        // Act
        var result = await productGAgent.GetProductAsync(created.Id);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(created.Id);
        result.NameKey.ShouldBe("product_to_get");
        result.Platform.ShouldBe(PaymentPlatform.Stripe);

        _testOutputHelper.WriteLine($"Retrieved product: Id={result.Id}, NameKey={result.NameKey}");
    }

    [Fact]
    public async Task GetProductAsync_Should_Return_Null_When_Not_Exists()
    {
        // Arrange
        var productGAgent = GetProductGAgent();
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await productGAgent.GetProductAsync(nonExistentId);

        // Assert
        result.ShouldBeNull();

        _testOutputHelper.WriteLine($"Correctly returned null for non-existent product");
    }

    #endregion

    #region GetAllProductsAsync Tests

    [Fact]
    public async Task GetAllProductsAsync_Should_Return_All_Products()
    {
        // Arrange
        var productGAgent = GetProductGAgent();
        
        await productGAgent.CreateProductAsync(new CreateProductDto
        {
            NameKey = "all_product_1",
            PlanType = PlanType.Month,
            DescriptionKey = "desc_1",
            PlatformProductId = "prod_all_1",
            Platform = PaymentPlatform.Stripe
        });
        await productGAgent.CreateProductAsync(new CreateProductDto
        {
            NameKey = "all_product_2",
            PlanType = PlanType.None,
            DescriptionKey = "desc_2",
            PlatformProductId = "prod_all_2",
            Platform = PaymentPlatform.AppStore
        });

        // Act
        var result = await productGAgent.GetAllProductsAsync();

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBeGreaterThanOrEqualTo(2);

        _testOutputHelper.WriteLine($"Retrieved {result.Count} products");
    }

    #endregion

    #region GetListedProductsByPlatformAsync Tests

    [Fact]
    public async Task GetListedProductsByPlatformAsync_Should_Return_Listed_Products_For_Platform()
    {
        // Arrange
        var productGAgent = GetProductGAgent();
        
        // Create and list a Stripe product
        var stripeProduct = await productGAgent.CreateProductAsync(new CreateProductDto
        {
            NameKey = "stripe_listed_product",
            PlanType = PlanType.Month,
            DescriptionKey = "stripe_desc",
            PlatformProductId = "prod_stripe_listed",
            Platform = PaymentPlatform.Stripe
        });
        await productGAgent.SetProductListedAsync(stripeProduct.Id, true);

        // Create and list an AppStore product
        var appStoreProduct = await productGAgent.CreateProductAsync(new CreateProductDto
        {
            NameKey = "appstore_listed_product",
            PlanType = PlanType.Month,
            DescriptionKey = "appstore_desc",
            PlatformProductId = "prod_appstore_listed",
            Platform = PaymentPlatform.AppStore
        });
        await productGAgent.SetProductListedAsync(appStoreProduct.Id, true);

        // Act
        var stripeProducts = await productGAgent.GetListedProductsByPlatformAsync(PaymentPlatform.Stripe);
        var appStoreProducts = await productGAgent.GetListedProductsByPlatformAsync(PaymentPlatform.AppStore);

        // Assert
        stripeProducts.ShouldNotBeNull();
        appStoreProducts.ShouldNotBeNull();
        stripeProducts.All(p => p.Platform == PaymentPlatform.Stripe && p.IsListed!.Value).ShouldBeTrue();
        appStoreProducts.All(p => p.Platform == PaymentPlatform.AppStore && p.IsListed!.Value).ShouldBeTrue();

        _testOutputHelper.WriteLine($"Stripe listed: {stripeProducts.Count}, AppStore listed: {appStoreProducts.Count}");
    }

    #endregion

    #region GetProductByPlatformProductIdAsync Tests

    [Fact]
    public async Task GetProductByPlatformProductIdAsync_Should_Return_Product()
    {
        // Arrange
        var productGAgent = GetProductGAgent();
        var platformProductId = $"prod_platform_{Guid.NewGuid():N}";
        
        await productGAgent.CreateProductAsync(new CreateProductDto
        {
            NameKey = "platform_product_test",
            PlanType = PlanType.Month,
            DescriptionKey = "platform_desc",
            PlatformProductId = platformProductId,
            Platform = PaymentPlatform.Stripe
        });

        // Act
        var result = await productGAgent.GetProductByPlatformProductIdAsync(
            platformProductId, PaymentPlatform.Stripe);

        // Assert
        result.ShouldNotBeNull();
        result.PlatformProductId.ShouldBe(platformProductId);
        result.Platform.ShouldBe(PaymentPlatform.Stripe);

        _testOutputHelper.WriteLine($"Retrieved product by platform ID: {platformProductId}");
    }

    [Fact]
    public async Task GetProductByPlatformProductIdAsync_Should_Return_Null_When_Not_Found()
    {
        // Arrange
        var productGAgent = GetProductGAgent();

        // Act
        var result = await productGAgent.GetProductByPlatformProductIdAsync(
            "non_existent_platform_id", PaymentPlatform.Stripe);

        // Assert
        result.ShouldBeNull();

        _testOutputHelper.WriteLine($"Correctly returned null for non-existent platform product ID");
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task Integration_Create_Update_List_Delete_Product_Flow()
    {
        // Arrange
        var productGAgent = GetProductGAgent();

        // Create
        var createDto = new CreateProductDto
        {
            NameKey = "integration_product",
            PlanType = PlanType.Month,
            DescriptionKey = "integration_desc",
            PlatformProductId = $"prod_integration_{Guid.NewGuid():N}",
            Platform = PaymentPlatform.Stripe
        };
        var created = await productGAgent.CreateProductAsync(createDto);
        created.ShouldNotBeNull();

        // Update
        var updateDto = new UpdateProductDto
        {
            NameKey = "integration_product_updated",
            IsUltimate = true
        };
        var updated = await productGAgent.UpdateProductAsync(created.Id, updateDto);
        updated.NameKey.ShouldBe("integration_product_updated");
        updated.IsUltimate.ShouldBeTrue();

        // Set Listed
        await productGAgent.SetProductListedAsync(created.Id, true);
        var listed = await productGAgent.GetProductAsync(created.Id);
        listed.ShouldNotBeNull();
        listed.IsListed!.Value.ShouldBeTrue();

        // Delete
        await productGAgent.DeleteProductAsync(created.Id);
        var deleted = await productGAgent.GetProductAsync(created.Id);
        deleted.ShouldBeNull();

        _testOutputHelper.WriteLine("Integration test completed: Create -> Update -> List -> Delete");
    }

    #endregion
}
