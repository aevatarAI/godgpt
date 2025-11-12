using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Core.Abstractions;
using Aevatar.GodGPT.Tests;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace GodGPT.GAgents.Tests.ChatManager;

/// <summary>
/// Tests for ChatManagerGAgent initialization and basic functionality
/// </summary>
public class ChatManagerInitializationTests : AevatarGodGPTTestsBase
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly IGAgentFactory _agentFactory;
    
    public ChatManagerInitializationTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        _agentFactory = GetRequiredService<IGAgentFactory>();
    }

    [Fact]
    public async Task GetGAgent_NewChatManager_ShouldActivateSuccessfully()
    {
        // Arrange & Act
        var chatManager = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();

        // Assert
        chatManager.ShouldNotBeNull();
        _testOutputHelper.WriteLine("ChatManagerGAgent activated successfully");
    }

    [Fact]
    public async Task GetUserProfileAsync_NewUser_ShouldReturnDefaultProfile()
    {
        // Arrange
        var chatManager = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();

        // Act
        var userProfile = await chatManager.GetUserProfileAsync();

        // Assert
        userProfile.ShouldNotBeNull();
        userProfile.Id.ShouldNotBe(Guid.Empty);
        _testOutputHelper.WriteLine($"New user profile - ID: {userProfile.Id}");
        _testOutputHelper.WriteLine($"Gender: {userProfile.Gender}");
        _testOutputHelper.WriteLine($"FullName: {userProfile.FullName}");
    }

    [Fact]
    public async Task GetSessionListAsync_NewUser_ShouldReturnEmptyList()
    {
        // Arrange
        var chatManager = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();

        // Act
        var sessionList = await chatManager.GetSessionListAsync();

        // Assert
        sessionList.ShouldNotBeNull();
        sessionList.ShouldBeEmpty();
        _testOutputHelper.WriteLine("New user has empty session list");
    }

    [Fact]
    public async Task SetUserProfileAsync_ValidProfile_ShouldUpdateProfile()
    {
        // Arrange
        var chatManager = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();
        var testGender = "Male";
        var testBirthDate = new DateTime(1990, 1, 1);
        var testBirthPlace = "New York";
        var testFullName = "John Doe";

        // Act
        var userId = await chatManager.SetUserProfileAsync(testGender, testBirthDate, testBirthPlace, testFullName);
        var userProfile = await chatManager.GetUserProfileAsync();

        // Assert
        userId.ShouldNotBe(Guid.Empty);
        userProfile.ShouldNotBeNull();
        userProfile.Gender.ShouldBe(testGender);
        userProfile.BirthDate.ShouldBe(testBirthDate);
        userProfile.BirthPlace.ShouldBe(testBirthPlace);
        userProfile.FullName.ShouldBe(testFullName);
        
        _testOutputHelper.WriteLine($"User profile updated - ID: {userId}");
        _testOutputHelper.WriteLine($"Gender: {userProfile.Gender}, Name: {userProfile.FullName}");
    }

    [Fact]
    public async Task GetUserProfileAsync_AfterMultipleCalls_ShouldReturnConsistentData()
    {
        // Arrange
        var chatManager = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();
        await chatManager.SetUserProfileAsync("Female", new DateTime(1995, 5, 15), "London", "Jane Smith");

        // Act
        var profile1 = await chatManager.GetUserProfileAsync();
        var profile2 = await chatManager.GetUserProfileAsync();

        // Assert
        profile1.ShouldNotBeNull();
        profile2.ShouldNotBeNull();
        profile1.Id.ShouldBe(profile2.Id);
        profile1.Gender.ShouldBe(profile2.Gender);
        profile1.FullName.ShouldBe(profile2.FullName);
        
        _testOutputHelper.WriteLine("User profile is consistent across multiple calls");
    }
}

