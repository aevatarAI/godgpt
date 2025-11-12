using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Core.Abstractions;
using Aevatar.GodGPT.Tests;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace GodGPT.GAgents.Tests.ChatManager;

/// <summary>
/// Tests for ChatManagerGAgent CreateSessionAsync functionality
/// </summary>
public class ChatManagerCreateSessionTests : AevatarGodGPTTestsBase
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly IGAgentFactory _agentFactory;
    
    public ChatManagerCreateSessionTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        _agentFactory = GetRequiredService<IGAgentFactory>();
    }

    [Fact]
    public async Task CreateSessionAsync_WithMinimalParameters_ShouldCreateSession()
    {
        // Arrange
        var chatManager = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();
        var systemLLM = "OpenAI";
        var prompt = "Test prompt";

        // Act
        var sessionId = await chatManager.CreateSessionAsync(systemLLM, prompt);

        // Assert
        sessionId.ShouldNotBe(Guid.Empty);
        _testOutputHelper.WriteLine($"Session created with ID: {sessionId}");
    }

    [Fact]
    public async Task CreateSessionAsync_WithUserProfile_ShouldCreateSessionAndSetProfile()
    {
        // Arrange
        var chatManager = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();
        var systemLLM = "OpenAI";
        var prompt = "Test prompt with profile";
        var userProfile = new UserProfileDto
        {
            Gender = "Male",
            BirthDate = new DateTime(1990, 6, 15),
            BirthPlace = "Tokyo",
            FullName = "Test User"
        };

        // Act
        var sessionId = await chatManager.CreateSessionAsync(systemLLM, prompt, userProfile);
        var retrievedProfile = await chatManager.GetUserProfileAsync();

        // Assert
        sessionId.ShouldNotBe(Guid.Empty);
        retrievedProfile.ShouldNotBeNull();
        retrievedProfile.Gender.ShouldBe(userProfile.Gender);
        retrievedProfile.BirthDate.ShouldBe(userProfile.BirthDate);
        retrievedProfile.BirthPlace.ShouldBe(userProfile.BirthPlace);
        retrievedProfile.FullName.ShouldBe(userProfile.FullName);
        
        _testOutputHelper.WriteLine($"Session created with profile - ID: {sessionId}");
        _testOutputHelper.WriteLine($"Profile: {retrievedProfile.FullName}, {retrievedProfile.Gender}");
    }

    [Fact]
    public async Task CreateSessionAsync_WithDailyGuider_ShouldCreateSessionWithGuider()
    {
        // Arrange
        var chatManager = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();
        var systemLLM = "OpenAI";
        var prompt = "Daily guidance prompt";
        var guider = SessionGuiderConstants.DailyGuide;

        // Act
        var sessionId = await chatManager.CreateSessionAsync(systemLLM, prompt, null, guider);
        var sessionInfo = await chatManager.GetSessionCreationInfoAsync(sessionId);

        // Assert
        sessionId.ShouldNotBe(Guid.Empty);
        sessionInfo.ShouldNotBeNull();
        sessionInfo.Guider.ShouldBe(guider);
        
        _testOutputHelper.WriteLine($"Session created with guider: {guider}, ID: {sessionId}");
    }

    [Fact]
    public async Task CreateSessionAsync_WithUserLocalTime_ShouldCreateSession()
    {
        // Arrange
        var chatManager = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();
        var systemLLM = "OpenAI";
        var prompt = "Test prompt with local time";
        var userLocalTime = DateTime.Now;

        // Act
        var sessionId = await chatManager.CreateSessionAsync(systemLLM, prompt, null, null, userLocalTime);

        // Assert
        sessionId.ShouldNotBe(Guid.Empty);
        _testOutputHelper.WriteLine($"Session created with local time: {userLocalTime}, ID: {sessionId}");
    }

    [Fact]
    public async Task CreateSessionAsync_MultipleSessionsCreation_ShouldAddToSessionList()
    {
        // Arrange
        var chatManager = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();
        var systemLLM = "OpenAI";

        // Act
        var sessionId1 = await chatManager.CreateSessionAsync(systemLLM, "First prompt");
        var sessionId2 = await chatManager.CreateSessionAsync(systemLLM, "Second prompt");
        var sessionId3 = await chatManager.CreateSessionAsync(systemLLM, "Third prompt");
        
        var sessionList = await chatManager.GetSessionListAsync();

        // Assert
        sessionId1.ShouldNotBe(Guid.Empty);
        sessionId2.ShouldNotBe(Guid.Empty);
        sessionId3.ShouldNotBe(Guid.Empty);
        sessionList.ShouldNotBeNull();
        sessionList.Count.ShouldBe(3);
        sessionList.Any(s => s.SessionId == sessionId1).ShouldBeTrue();
        sessionList.Any(s => s.SessionId == sessionId2).ShouldBeTrue();
        sessionList.Any(s => s.SessionId == sessionId3).ShouldBeTrue();
        
        _testOutputHelper.WriteLine($"Created {sessionList.Count} sessions");
        foreach (var session in sessionList)
        {
            _testOutputHelper.WriteLine($"  - Session ID: {session.SessionId}, Title: {session.Title}");
        }
    }

    [Fact]
    public async Task CreateSessionAsync_VerifySessionIsUserSession_ShouldReturnTrue()
    {
        // Arrange
        var chatManager = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();
        var systemLLM = "OpenAI";
        var prompt = "Test session ownership";

        // Act
        var sessionId = await chatManager.CreateSessionAsync(systemLLM, prompt);
        var isUserSession = await chatManager.IsUserSessionAsync(sessionId);

        // Assert
        sessionId.ShouldNotBe(Guid.Empty);
        isUserSession.ShouldBeTrue();
        
        _testOutputHelper.WriteLine($"Session {sessionId} belongs to user: {isUserSession}");
    }

    [Fact]
    public async Task CreateSessionAsync_GetSessionCreationInfo_ShouldReturnCreationInfo()
    {
        // Arrange
        var chatManager = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();
        var systemLLM = "OpenAI";
        var prompt = "Test creation info";
        var guider = "TestGuider";

        // Act
        var sessionId = await chatManager.CreateSessionAsync(systemLLM, prompt, null, guider);
        var creationInfo = await chatManager.GetSessionCreationInfoAsync(sessionId);

        // Assert
        sessionId.ShouldNotBe(Guid.Empty);
        creationInfo.ShouldNotBeNull();
        creationInfo.SessionId.ShouldBe(sessionId);
        creationInfo.Guider.ShouldBe(guider);
        creationInfo.CreateAt.ShouldNotBe(default(DateTime));
        
        _testOutputHelper.WriteLine($"Session creation info - ID: {creationInfo.SessionId}");
        _testOutputHelper.WriteLine($"Created at: {creationInfo.CreateAt}, Guider: {creationInfo.Guider}");
    }

    [Fact]
    public async Task CreateSessionAsync_WithAllParameters_ShouldCreateCompleteSession()
    {
        // Arrange
        var chatManager = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();
        var systemLLM = "OpenAI";
        var prompt = "Complete test prompt";
        var userProfile = new UserProfileDto
        {
            Gender = "Female",
            BirthDate = new DateTime(1985, 3, 20),
            BirthPlace = "Paris",
            FullName = "Complete Test User"
        };
        var guider = "CompleteGuider";
        var userLocalTime = DateTime.Now;

        // Act
        var sessionId = await chatManager.CreateSessionAsync(systemLLM, prompt, userProfile, guider, userLocalTime);
        var sessionList = await chatManager.GetSessionListAsync();
        var retrievedProfile = await chatManager.GetUserProfileAsync();
        var creationInfo = await chatManager.GetSessionCreationInfoAsync(sessionId);

        // Assert
        sessionId.ShouldNotBe(Guid.Empty);
        
        // Verify session in list
        sessionList.ShouldNotBeNull();
        sessionList.Count.ShouldBeGreaterThan(0);
        sessionList.Any(s => s.SessionId == sessionId).ShouldBeTrue();
        
        // Verify profile
        retrievedProfile.ShouldNotBeNull();
        retrievedProfile.FullName.ShouldBe(userProfile.FullName);
        
        // Verify creation info
        creationInfo.ShouldNotBeNull();
        creationInfo.Guider.ShouldBe(guider);
        
        _testOutputHelper.WriteLine($"Complete session created - ID: {sessionId}");
        _testOutputHelper.WriteLine($"Profile: {retrievedProfile.FullName}");
        _testOutputHelper.WriteLine($"Guider: {creationInfo.Guider}");
        _testOutputHelper.WriteLine($"Local time: {userLocalTime}");
    }

    [Fact]
    public async Task CreateSessionAsync_EmptyPrompt_ShouldStillCreateSession()
    {
        // Arrange
        var chatManager = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();
        var systemLLM = "OpenAI";
        var emptyPrompt = string.Empty;

        // Act
        var sessionId = await chatManager.CreateSessionAsync(systemLLM, emptyPrompt);
        var sessionList = await chatManager.GetSessionListAsync();

        // Assert
        sessionId.ShouldNotBe(Guid.Empty);
        sessionList.ShouldNotBeNull();
        sessionList.Any(s => s.SessionId == sessionId).ShouldBeTrue();
        
        _testOutputHelper.WriteLine($"Session created with empty prompt - ID: {sessionId}");
    }

    [Fact]
    public async Task IsUserSessionAsync_NonExistentSession_ShouldReturnFalse()
    {
        // Arrange
        var chatManager = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();
        var nonExistentSessionId = Guid.NewGuid();

        // Act
        var isUserSession = await chatManager.IsUserSessionAsync(nonExistentSessionId);

        // Assert
        isUserSession.ShouldBeFalse();
        
        _testOutputHelper.WriteLine($"Non-existent session {nonExistentSessionId} is user session: {isUserSession}");
    }
}

