using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Core.Abstractions;
using Aevatar.GodGPT.Tests;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace GodGPT.GAgents.Tests.ChatManager;

/// <summary>
/// Tests for optimized first access detection functionality in ChatManagerGAgent
/// </summary>
public class ChatManagerFirstAccessTests : AevatarGodGPTTestsBase
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly IGAgentFactory _agentFactory;
    
    public ChatManagerFirstAccessTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        _agentFactory = GetRequiredService<IGAgentFactory>();
    }

    [Fact]
    public async Task OnActivate_NewUser_ShouldSetIsFirstConversationToTrue()
    {
        try
        {
            // Arrange
            var chatManager = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();

            // Act - Activate the grain (this should trigger OnAIGAgentActivateAsync)
            var userProfile = await chatManager.GetUserProfileAsync();

            // Assert
            userProfile.ShouldNotBeNull();
            userProfile.IsFirstConversation.ShouldNotBeNull("IsFirstConversation should be set for new users");
            userProfile.IsFirstConversation.Value.ShouldBeTrue("New user should be marked as first access");

            _testOutputHelper.WriteLine($"New user IsFirstConversation: {userProfile.IsFirstConversation}");
            _testOutputHelper.WriteLine($"New user ID: {userProfile.Id}");
        }
        catch (Exception ex)
        {
            _testOutputHelper.WriteLine($"Exception during new user test: {ex.Message}");
            throw;
        }
    }

    [Fact]
    public async Task OnActivate_ExistingUserWithActivity_ShouldSetIsFirstConversationToFalse()
    {
        try
        {
            // Arrange
            var chatManager = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();

            // Create a session to simulate existing user activity (creates events, Version > 0)
            await chatManager.CreateSessionAsync("OpenAI", string.Empty, new Aevatar.Application.Grains.Agents.ChatManager.UserProfileDto
            {
                Gender = "Male",
                BirthDate = DateTime.UtcNow.AddYears(-25),
                BirthPlace = "New York",
                FullName = "John Doe"
            });

            // Act - Get user profile which will trigger first access check if not already done
            var userProfile = await chatManager.GetUserProfileAsync();

            // Assert
            userProfile.ShouldNotBeNull();
            userProfile.IsFirstConversation.ShouldNotBeNull("IsFirstConversation should be set for existing users");
            userProfile.IsFirstConversation.Value.ShouldBeFalse("User with existing activity should be marked as not first access");

            _testOutputHelper.WriteLine($"Existing user IsFirstConversation: {userProfile.IsFirstConversation}");
        }
        catch (Exception ex)
        {
            _testOutputHelper.WriteLine($"Exception during existing user test: {ex.Message}");
            throw;
        }
    }
} 