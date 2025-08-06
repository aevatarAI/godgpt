using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Core.Abstractions;
using Aevatar.GodGPT.Tests;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace GodGPT.GAgents.Tests.ChatManager;

/// <summary>
/// Tests for first-time login detection functionality in ChatManagerGAgent
/// </summary>
public class ChatManagerFirstLoginTests : AevatarGodGPTTestsBase
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly IGAgentFactory _agentFactory;
    
    public ChatManagerFirstLoginTests(ITestOutputHelper testOutputHelper)
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

            // Act - Get user profile (this should trigger OnAIGAgentActivateAsync)
            var userProfile = await chatManager.GetUserProfileAsync();

            // Assert
            userProfile.ShouldNotBeNull();
            userProfile.IsFirstConversation.ShouldNotBeNull("IsFirstConversation should be set");
            userProfile.IsFirstConversation.Value.ShouldBeTrue("New user should be marked as first conversation");

            _testOutputHelper.WriteLine($"New user IsFirstConversation: {userProfile.IsFirstConversation}");
        }
        catch (Exception ex)
        {
            _testOutputHelper.WriteLine($"Exception during OnActivate_NewUser test: {ex.Message}");
            throw;
        }
    }

    [Fact]
    public async Task OnActivate_ExistingUserWithProfile_ShouldSetIsFirstConversationToFalse()
    {
        try
        {
            // Arrange
            var chatManager = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();

            // Create a session first to simulate existing user
            await chatManager.CreateSessionAsync("OpenAI", string.Empty, new Aevatar.Application.Grains.Agents.ChatManager.UserProfileDto
            {
                Gender = "Male",
                BirthDate = DateTime.UtcNow.AddYears(-25),
                BirthPlace = "New York",
                FullName = "John Doe"
            });

            // Act
            var userProfile = await chatManager.GetUserProfileAsync();

            // Assert
            userProfile.ShouldNotBeNull();
            userProfile.IsFirstConversation.ShouldNotBeNull("IsFirstConversation should be set");
            userProfile.IsFirstConversation.Value.ShouldBeFalse("User with existing profile should not be marked as first conversation");
            
            _testOutputHelper.WriteLine($"Existing user IsFirstConversation: {userProfile.IsFirstConversation}");
        }
        catch (Exception ex)
        {
            _testOutputHelper.WriteLine($"Exception during OnActivate_ExistingUserWithProfile test: {ex.Message}");
            throw;
        }
    }
} 