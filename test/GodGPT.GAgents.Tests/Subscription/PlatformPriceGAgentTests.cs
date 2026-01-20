using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Subscription;
using Aevatar.Application.Grains.Subscription.Dtos;
using Aevatar.Application.Grains.Subscription.Providers;
using Aevatar.GodGPT.Tests;
using Shouldly;
using Xunit.Abstractions;

namespace Aevatar.Application.Grains.Tests.Subscription;

/// <summary>
/// Test suite for PlatformPriceGAgent functionality.
/// </summary>
public class PlatformPriceGAgentTests : AevatarGodGPTTestsBase
{
    private readonly ITestOutputHelper _testOutputHelper;

    public PlatformPriceGAgentTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    private IPlatformPriceGAgent GetPriceGAgent()
    {
        return Cluster.GrainFactory.GetGrain<IPlatformPriceGAgent>(
            SubscriptionGAgentKeys.PriceGAgentKey);
    }

    private ISubscriptionProductGAgent GetProductGAgent()
    {
        return Cluster.GrainFactory.GetGrain<ISubscriptionProductGAgent>(
            SubscriptionGAgentKeys.ProductGAgentKey);
    }

    private async Task<Guid> CreateTestProductAsync()
    {
        var productGAgent = GetProductGAgent();
        var product = await productGAgent.CreateProductAsync(new CreateProductDto
        {
            NameKey = $"price_test_product_{Guid.NewGuid():N}",
            PlanType = PlanType.Month,
            DescriptionKey = "price_test_desc",
            PlatformProductId = $"prod_price_test_{Guid.NewGuid():N}",
            Platform = PaymentPlatform.Stripe
        });
        return product.Id;
    }

    #region SetPriceAsync Tests

    [Fact]
    public async Task SetPriceAsync_Should_Set_Price_Successfully()
    {
        // Arrange
        var priceGAgent = GetPriceGAgent();
        var productId = await CreateTestProductAsync();
        
        var setPriceDto = new SetPriceDto
        {
            Platform = PaymentPlatform.Stripe,
            PlatformPriceId = "price_test_001",
            Price = 9.99m,
            Currency = "USD"
        };

        // Act
        var result = await priceGAgent.SetPriceAsync(productId, setPriceDto);

        // Assert
        result.ShouldNotBeNull();
        result.Price.ShouldBe(9.99m);
        result.Currency.ShouldBe("USD");
        result.PlatformPriceId.ShouldBe("price_test_001");

        _testOutputHelper.WriteLine($"Set price for product {productId}: {result.Price} {result.Currency}");
    }

    [Fact]
    public async Task SetPriceAsync_Should_Set_Multiple_Currencies()
    {
        // Arrange
        var priceGAgent = GetPriceGAgent();
        var productId = await CreateTestProductAsync();

        // Act - Set USD price
        await priceGAgent.SetPriceAsync(productId, new SetPriceDto
        {
            Platform = PaymentPlatform.Stripe,
            PlatformPriceId = "price_usd_001",
            Price = 9.99m,
            Currency = "USD"
        });

        // Set EUR price
        await priceGAgent.SetPriceAsync(productId, new SetPriceDto
        {
            Platform = PaymentPlatform.Stripe,
            PlatformPriceId = "price_eur_001",
            Price = 8.99m,
            Currency = "EUR"
        });

        // Assert
        var prices = await priceGAgent.GetPricesByProductIdAsync(productId);
        prices.ShouldNotBeNull();
        prices.Count.ShouldBeGreaterThanOrEqualTo(2);
        prices.Any(p => p.Currency == "USD").ShouldBeTrue();
        prices.Any(p => p.Currency == "EUR").ShouldBeTrue();

        _testOutputHelper.WriteLine($"Set {prices.Count} prices for product {productId}");
    }

    [Fact]
    public async Task SetPriceAsync_Should_Update_Existing_Price()
    {
        // Arrange
        var priceGAgent = GetPriceGAgent();
        var productId = await CreateTestProductAsync();

        // Set initial price
        await priceGAgent.SetPriceAsync(productId, new SetPriceDto
        {
            Platform = PaymentPlatform.Stripe,
            PlatformPriceId = "price_update_001",
            Price = 9.99m,
            Currency = "USD"
        });

        // Act - Update price
        var updated = await priceGAgent.SetPriceAsync(productId, new SetPriceDto
        {
            Platform = PaymentPlatform.Stripe,
            PlatformPriceId = "price_update_002",
            Price = 14.99m,
            Currency = "USD"
        });

        // Assert
        updated.Price.ShouldBe(14.99m);

        var prices = await priceGAgent.GetPricesByProductIdAsync(productId);
        var usdPrice = prices.FirstOrDefault(p => p.Currency == "USD" && p.Platform == PaymentPlatform.Stripe);
        usdPrice.ShouldNotBeNull();
        usdPrice.Price.ShouldBe(9.99m);

        _testOutputHelper.WriteLine($"Updated price for product {productId}: {usdPrice.Price} USD");
    }

    #endregion

    #region DeletePriceAsync Tests

    [Fact]
    public async Task DeletePriceAsync_Should_Delete_Price_Successfully()
    {
        // Arrange
        var priceGAgent = GetPriceGAgent();
        var productId = await CreateTestProductAsync();

        await priceGAgent.SetPriceAsync(productId, new SetPriceDto
        {
            Platform = PaymentPlatform.Stripe,
            PlatformPriceId = "price_to_delete",
            Price = 9.99m,
            Currency = "USD"
        });

        // Verify price exists
        var pricesBefore = await priceGAgent.GetPricesByProductIdAsync(productId);
        pricesBefore.Any(p => p.Currency == "USD" && p.Platform == PaymentPlatform.Stripe).ShouldBeTrue();

        // Act
        await priceGAgent.DeletePriceAsync(productId, PaymentPlatform.Stripe, "USD");

        // Assert
        var pricesAfter = await priceGAgent.GetPricesByProductIdAsync(productId);
        pricesAfter.Any(p => p.Currency == "USD" && p.Platform == PaymentPlatform.Stripe).ShouldBeFalse();

        _testOutputHelper.WriteLine($"Deleted USD price for product {productId}");
    }

    #endregion

    #region GetPricesByProductIdAsync Tests

    [Fact]
    public async Task GetPricesByProductIdAsync_Should_Return_All_Prices()
    {
        // Arrange
        var priceGAgent = GetPriceGAgent();
        var productId = await CreateTestProductAsync();

        await priceGAgent.SetPriceAsync(productId, new SetPriceDto
        {
            Platform = PaymentPlatform.Stripe,
            PlatformPriceId = "price_get_all_1",
            Price = 9.99m,
            Currency = "USD"
        });

        await priceGAgent.SetPriceAsync(productId, new SetPriceDto
        {
            Platform = PaymentPlatform.AppStore,
            PlatformPriceId = "price_get_all_2",
            Price = 8.99m,
            Currency = "EUR"
        });

        // Act
        var result = await priceGAgent.GetPricesByProductIdAsync(productId);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBeGreaterThanOrEqualTo(2);

        _testOutputHelper.WriteLine($"Retrieved {result.Count} prices for product {productId}");
    }

    [Fact]
    public async Task GetPricesByProductIdAsync_Should_Return_Empty_When_No_Prices()
    {
        // Arrange
        var priceGAgent = GetPriceGAgent();
        var productId = Guid.NewGuid(); // Non-existent product

        // Act
        var result = await priceGAgent.GetPricesByProductIdAsync(productId);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeEmpty();

        _testOutputHelper.WriteLine($"Correctly returned empty list for product without prices");
    }

    #endregion

    #region GetPricesByProductIdAndPlatformAsync Tests

    [Fact]
    public async Task GetPricesByProductIdAndPlatformAsync_Should_Filter_By_Platform()
    {
        // Arrange
        var priceGAgent = GetPriceGAgent();
        var productId = await CreateTestProductAsync();

        await priceGAgent.SetPriceAsync(productId, new SetPriceDto
        {
            Platform = PaymentPlatform.Stripe,
            PlatformPriceId = "price_stripe_filter",
            Price = 9.99m,
            Currency = "USD"
        });

        var otherProductId = await CreateTestProductAsync();
        await priceGAgent.SetPriceAsync(otherProductId, new SetPriceDto
        {
            Platform = PaymentPlatform.AppStore,
            PlatformPriceId = "price_appstore_filter",
            Price = 8.99m,
            Currency = "EUR"
        });

        // Act
        var stripePrices = await priceGAgent.GetPricesByProductIdAsync(productId);
        var appStorePrices = await priceGAgent.GetPricesByProductIdAsync(otherProductId);

        // Assert
        stripePrices.All(p => p.Platform == PaymentPlatform.Stripe).ShouldBeTrue();
        appStorePrices.All(p => p.Platform == PaymentPlatform.AppStore).ShouldBeTrue();

        _testOutputHelper.WriteLine($"Stripe prices: {stripePrices.Count}, AppStore prices: {appStorePrices.Count}");
    }

    #endregion

    #region GetPriceByPlatformPriceIdAsync Tests

    [Fact]
    public async Task GetPriceByPlatformPriceIdAsync_Should_Return_Price()
    {
        // Arrange
        var priceGAgent = GetPriceGAgent();
        var productId = await CreateTestProductAsync();
        var platformPriceId = $"price_lookup_{Guid.NewGuid():N}";

        await priceGAgent.SetPriceAsync(productId, new SetPriceDto
        {
            Platform = PaymentPlatform.Stripe,
            PlatformPriceId = platformPriceId,
            Price = 19.99m,
            Currency = "USD"
        });

        // Act
        var result = await priceGAgent.GetPriceByPlatformPriceIdAsync(platformPriceId);

        // Assert
        result.ShouldNotBeNull();
        result.PlatformPriceId.ShouldBe(platformPriceId);
        result.Price.ShouldBe(19.99m);

        _testOutputHelper.WriteLine($"Retrieved price by platform ID: {platformPriceId}");
    }

    [Fact]
    public async Task GetPriceByPlatformPriceIdAsync_Should_Return_Null_When_Not_Found()
    {
        // Arrange
        var priceGAgent = GetPriceGAgent();

        // Act
        var result = await priceGAgent.GetPriceByPlatformPriceIdAsync("non_existent_price_id");

        // Assert
        result.ShouldBeNull();

        _testOutputHelper.WriteLine($"Correctly returned null for non-existent platform price ID");
    }

    #endregion

    #region GetLastPlatformPriceSyncTimeAsync Tests

    [Fact]
    public async Task GetLastPlatformPriceSyncTimeAsync_Should_Return_Sync_Time()
    {
        // Arrange
        var priceGAgent = GetPriceGAgent();

        // Act
        var result = await priceGAgent.GetLastPlatformPriceSyncTimeAsync();

        // Assert - Initially should be default value
        result.ShouldBeOfType<DateTime>();

        _testOutputHelper.WriteLine($"Last Stripe sync time: {result}");
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task Integration_Set_Get_Delete_Price_Flow()
    {
        // Arrange
        var priceGAgent = GetPriceGAgent();
        var productId = await CreateTestProductAsync();

        // Set price
        var setResult = await priceGAgent.SetPriceAsync(productId, new SetPriceDto
        {
            Platform = PaymentPlatform.Stripe,
            PlatformPriceId = "price_integration_test",
            Price = 29.99m,
            Currency = "USD"
        });
        setResult.Price.ShouldBe(29.99m);

        // Get prices
        var prices = await priceGAgent.GetPricesByProductIdAsync(productId);
        prices.Count.ShouldBeGreaterThan(0);

        // Delete price
        await priceGAgent.DeletePriceAsync(productId, PaymentPlatform.Stripe, "USD");

        // Verify deletion
        var pricesAfter = await priceGAgent.GetPricesByProductIdAsync(productId);
        pricesAfter.Any(p => p.Currency == "USD" && p.Platform == PaymentPlatform.Stripe).ShouldBeFalse();

        _testOutputHelper.WriteLine("Integration test completed: Set -> Get -> Delete");
    }

    [Fact]
    public async Task Integration_Multi_Platform_Pricing()
    {
        // Arrange
        var priceGAgent = GetPriceGAgent();
        var productId = await CreateTestProductAsync();

        // Set Stripe prices
        await priceGAgent.SetPriceAsync(productId, new SetPriceDto
        {
            Platform = PaymentPlatform.Stripe,
            PlatformPriceId = "price_stripe_multi",
            Price = 9.99m,
            Currency = "USD"
        });

        // Set AppStore prices
        await priceGAgent.SetPriceAsync(productId, new SetPriceDto
        {
            Platform = PaymentPlatform.AppStore,
            PlatformPriceId = "com.app.pro.monthly",
            Price = 9.99m,
            Currency = "USD"
        });

        // Set Google Play prices
        await priceGAgent.SetPriceAsync(productId, new SetPriceDto
        {
            Platform = PaymentPlatform.GooglePlay,
            PlatformPriceId = "pro_monthly",
            Price = 9.99m,
            Currency = "USD"
        });

        // Assert
        var allPrices = await priceGAgent.GetPricesByProductIdAsync(productId);
        allPrices.Count.ShouldBeGreaterThanOrEqualTo(3);

        var platforms = allPrices.Select(p => p.Platform).Distinct().ToList();
        platforms.ShouldContain(PaymentPlatform.Stripe);
        platforms.ShouldContain(PaymentPlatform.AppStore);
        platforms.ShouldContain(PaymentPlatform.GooglePlay);

        _testOutputHelper.WriteLine($"Set prices for {platforms.Count} platforms");
    }

    #endregion
}
