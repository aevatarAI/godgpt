using Aevatar.Application.Grains.Subscription;
using Aevatar.Application.Grains.Subscription.Dtos;
using Aevatar.GodGPT.Tests;
using Shouldly;
using Xunit.Abstractions;

namespace Aevatar.Application.Grains.Tests.Subscription;

/// <summary>
/// Test suite for SubscriptionLabelGAgent functionality.
/// </summary>
public class SubscriptionLabelGAgentTests : AevatarGodGPTTestsBase
{
    private readonly ITestOutputHelper _testOutputHelper;

    public SubscriptionLabelGAgentTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    private ISubscriptionLabelGAgent GetLabelGAgent()
    {
        return Cluster.GrainFactory.GetGrain<ISubscriptionLabelGAgent>(
            SubscriptionGAgentKeys.LabelGAgentKey);
    }

    #region CreateLabelAsync Tests

    [Fact]
    public async Task CreateLabelAsync_Should_Create_Label_Successfully()
    {
        // Arrange
        var labelGAgent = GetLabelGAgent();
        var createDto = new CreateSubscriptionLabelDto
        {
            NameKey = "most_popular"
        };

        // Act
        var result = await labelGAgent.CreateLabelAsync(createDto);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldNotBe(Guid.Empty);
        result.NameKey.ShouldBe("most_popular");
        result.CreatedAt.ShouldNotBe(default);

        _testOutputHelper.WriteLine($"Created label: Id={result.Id}, NameKey={result.NameKey}");
    }

    [Fact]
    public async Task CreateLabelAsync_Should_Create_Multiple_Labels()
    {
        // Arrange
        var labelGAgent = GetLabelGAgent();
        
        var labels = new List<CreateSubscriptionLabelDto>
        {
            new() { NameKey = "best_value" },
            new() { NameKey = "recommended" },
            new() { NameKey = "limited_offer" }
        };

        // Act
        var results = new List<SubscriptionLabelDto>();
        foreach (var dto in labels)
        {
            var result = await labelGAgent.CreateLabelAsync(dto);
            results.Add(result);
        }

        // Assert
        results.Count.ShouldBe(3);
        results.Select(r => r.NameKey).ShouldContain("best_value");
        results.Select(r => r.NameKey).ShouldContain("recommended");
        results.Select(r => r.NameKey).ShouldContain("limited_offer");

        _testOutputHelper.WriteLine($"Created {results.Count} labels successfully");
    }

    #endregion

    #region UpdateLabelAsync Tests

    [Fact]
    public async Task UpdateLabelAsync_Should_Update_Label_Successfully()
    {
        // Arrange
        var labelGAgent = GetLabelGAgent();
        var createDto = new CreateSubscriptionLabelDto
        {
            NameKey = "label_to_update"
        };
        var created = await labelGAgent.CreateLabelAsync(createDto);

        var updateDto = new UpdateSubscriptionLabelDto
        {
            NameKey = "label_updated"
        };

        // Act
        var result = await labelGAgent.UpdateLabelAsync(created.Id, updateDto);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(created.Id);
        result.NameKey.ShouldBe("label_updated");

        _testOutputHelper.WriteLine($"Updated label: Id={result.Id}, NameKey={result.NameKey}");
    }

    [Fact]
    public async Task UpdateLabelAsync_Should_Throw_When_Label_Not_Found()
    {
        // Arrange
        var labelGAgent = GetLabelGAgent();
        var nonExistentId = Guid.NewGuid();
        var updateDto = new UpdateSubscriptionLabelDto
        {
            NameKey = "should_not_exist"
        };

        // Act & Assert
        await Should.ThrowAsync<KeyNotFoundException>(
            async () => await labelGAgent.UpdateLabelAsync(nonExistentId, updateDto));

        _testOutputHelper.WriteLine($"Correctly threw KeyNotFoundException for non-existent label");
    }

    #endregion

    #region DeleteLabelAsync Tests

    [Fact]
    public async Task DeleteLabelAsync_Should_Delete_Label_Successfully()
    {
        // Arrange
        var labelGAgent = GetLabelGAgent();
        var createDto = new CreateSubscriptionLabelDto
        {
            NameKey = "label_to_delete"
        };
        var created = await labelGAgent.CreateLabelAsync(createDto);

        // Act
        await labelGAgent.DeleteLabelAsync(created.Id);

        // Assert
        var deletedLabel = await labelGAgent.GetLabelAsync(created.Id);
        deletedLabel.ShouldBeNull();

        _testOutputHelper.WriteLine($"Deleted label: Id={created.Id}");
    }

    [Fact]
    public async Task DeleteLabelAsync_Should_Throw_When_Label_Not_Found()
    {
        // Arrange
        var labelGAgent = GetLabelGAgent();
        var nonExistentId = Guid.NewGuid();

        // Act & Assert
        await Should.ThrowAsync<KeyNotFoundException>(
            async () => await labelGAgent.DeleteLabelAsync(nonExistentId));

        _testOutputHelper.WriteLine($"Correctly threw KeyNotFoundException for non-existent label");
    }

    #endregion

    #region GetLabelAsync Tests

    [Fact]
    public async Task GetLabelAsync_Should_Return_Label_When_Exists()
    {
        // Arrange
        var labelGAgent = GetLabelGAgent();
        var createDto = new CreateSubscriptionLabelDto
        {
            NameKey = "label_to_get"
        };
        var created = await labelGAgent.CreateLabelAsync(createDto);

        // Act
        var result = await labelGAgent.GetLabelAsync(created.Id);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(created.Id);
        result.NameKey.ShouldBe("label_to_get");

        _testOutputHelper.WriteLine($"Retrieved label: Id={result.Id}, NameKey={result.NameKey}");
    }

    [Fact]
    public async Task GetLabelAsync_Should_Return_Null_When_Not_Exists()
    {
        // Arrange
        var labelGAgent = GetLabelGAgent();
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await labelGAgent.GetLabelAsync(nonExistentId);

        // Assert
        result.ShouldBeNull();

        _testOutputHelper.WriteLine($"Correctly returned null for non-existent label");
    }

    #endregion

    #region GetAllLabelsAsync Tests

    [Fact]
    public async Task GetAllLabelsAsync_Should_Return_All_Labels()
    {
        // Arrange
        var labelGAgent = GetLabelGAgent();
        
        // Create some labels
        await labelGAgent.CreateLabelAsync(new CreateSubscriptionLabelDto { NameKey = "all_label_1" });
        await labelGAgent.CreateLabelAsync(new CreateSubscriptionLabelDto { NameKey = "all_label_2" });

        // Act
        var result = await labelGAgent.GetAllLabelsAsync();

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBeGreaterThanOrEqualTo(2);

        _testOutputHelper.WriteLine($"Retrieved {result.Count} labels");
    }

    [Fact]
    public async Task GetAllLabelsAsync_Should_Return_Empty_List_When_No_Labels()
    {
        // Arrange - Use a different grain key to ensure isolation
        var labelGAgent = Cluster.GrainFactory.GetGrain<ISubscriptionLabelGAgent>(Guid.NewGuid());

        // Act
        var result = await labelGAgent.GetAllLabelsAsync();

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeEmpty();

        _testOutputHelper.WriteLine($"Correctly returned empty list");
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task Integration_Create_Update_Get_Delete_Label_Flow()
    {
        // Arrange
        var labelGAgent = GetLabelGAgent();

        // Create
        var createDto = new CreateSubscriptionLabelDto { NameKey = "integration_test_label" };
        var created = await labelGAgent.CreateLabelAsync(createDto);
        created.ShouldNotBeNull();
        created.NameKey.ShouldBe("integration_test_label");

        // Update
        var updateDto = new UpdateSubscriptionLabelDto { NameKey = "integration_test_label_updated" };
        var updated = await labelGAgent.UpdateLabelAsync(created.Id, updateDto);
        updated.NameKey.ShouldBe("integration_test_label_updated");

        // Get
        var retrieved = await labelGAgent.GetLabelAsync(created.Id);
        retrieved.ShouldNotBeNull();
        retrieved.NameKey.ShouldBe("integration_test_label_updated");

        // Delete
        await labelGAgent.DeleteLabelAsync(created.Id);
        var deleted = await labelGAgent.GetLabelAsync(created.Id);
        deleted.ShouldBeNull();

        _testOutputHelper.WriteLine("Integration test completed: Create -> Update -> Get -> Delete");
    }

    #endregion
}
