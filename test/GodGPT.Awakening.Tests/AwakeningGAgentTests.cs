namespace GodGPT.Awakening.Tests;

/// <summary>
/// Unit tests for AwakeningGAgent
/// </summary>
public class AwakeningGAgentTests : AwakeningTestBase
{
    [Fact(Skip = "")]
    public async Task GetTodayAwakeningAsync_WithNoSessionContent_ShouldReturnCompleted()
    {
        // Arrange
        var awakeningAgent = await GetAwakeningGAgentAsync();
        var language = VoiceLanguageEnum.English;

        // Act
        var result = await awakeningAgent.GetTodayAwakeningAsync(language);

        // Assert
        // When no session content exists, should return completed result immediately
        Assert.NotNull(result);
        Assert.Equal(0, result.AwakeningLevel);
        Assert.Equal(string.Empty, result.AwakeningMessage);
        Assert.Equal(AwakeningStatus.Completed, result.Status);
    }

    [Fact(Skip = "")]
    public async Task GetLatestNonEmptySessionAsync_WithNoSessions_ShouldReturnNull()
    {
        // Arrange
        var awakeningAgent = await GetAwakeningGAgentAsync();

        // Act
        var result = await awakeningAgent.GetLatestNonEmptySessionAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact(Skip = "")]
    public async Task GenerateAwakeningContentAsync_WithValidSessionContent_ShouldReturnResult()
    {
        // Arrange
        var awakeningAgent = await GetAwakeningGAgentAsync();
        var sessionContent = CreateTestSessionContent();
        var language = VoiceLanguageEnum.English;

        // Act
        var result = await awakeningAgent.GenerateAwakeningContentAsync(sessionContent, language);

        // Assert
        Assert.NotNull(result);
        // Note: In test environment without LLM, this will likely fail
        // This is expected behavior and should be mocked in integration tests
    }

    [Fact(Skip = "")]
    public async Task GenerateAwakeningContentAsync_WithNullSessionContent_ShouldReturnFailure()
    {
        // Arrange
        var awakeningAgent = await GetAwakeningGAgentAsync();
        var language = VoiceLanguageEnum.English;

        // Act
        var result = await awakeningAgent.GenerateAwakeningContentAsync(null!, language);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccess);
        Assert.Equal("Session content is null", result.ErrorMessage);
    }

    [Fact(Skip = "")]
    // [Theory]
    // [InlineData(VoiceLanguageEnum.Chinese)]
    // [InlineData(VoiceLanguageEnum.English)]
    // [InlineData(VoiceLanguageEnum.Spanish)]
    public async Task GetTodayAwakeningAsync_WithDifferentLanguages_ShouldHandleAllLanguages(VoiceLanguageEnum language)
    {
        // Arrange
        var awakeningAgent = await GetAwakeningGAgentAsync();

        // Act
        var result = await awakeningAgent.GetTodayAwakeningAsync(language);

        // Assert
        // Without session content, should return completed result immediately
        Assert.NotNull(result);
        Assert.Equal(0, result.AwakeningLevel);
        Assert.Equal(string.Empty, result.AwakeningMessage);
        Assert.Equal(AwakeningStatus.Completed, result.Status);
    }

    [Fact(Skip = "")]
    public async Task GetTodayAwakeningAsync_WithNoSessionContent_ShouldReturnCompletedImmediately()
    {
        // Arrange - Use random userId that has no session history
        var userId = Guid.NewGuid();
        var awakeningAgent = await GetAwakeningGAgentAsync(userId);
        var language = VoiceLanguageEnum.English;

        // Act - Test user with no session history
        var result = await awakeningAgent.GetTodayAwakeningAsync(language);

        // Assert - Should return completed result immediately (no session content to process)
        Assert.NotNull(result);
        Assert.Equal(0, result.AwakeningLevel);
        Assert.Equal(string.Empty, result.AwakeningMessage);
        Assert.Equal(AwakeningStatus.Completed, result.Status);
    }

    [Fact(Skip = "")]
    public async Task GetTodayAwakeningAsync_WithValidSessionContent_ShouldTriggerAsyncGeneration()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var awakeningAgent = await GetAwakeningGAgentAsync(userId);
        var language = VoiceLanguageEnum.English;
        
        // Create test session data to simulate having conversation history
        await CreateTestSessionForUserAsync(userId);

        // Act - First call should trigger async generation
        var result1 = await awakeningAgent.GetTodayAwakeningAsync(language);
        
        // The first call might return null with Generating status, or completed with content
        // depending on whether the async generation completes quickly
        
        // Wait for async generation to potentially complete
        await Task.Delay(500);
        
        // Second call should return the same result as the system has been initialized
        var result2 = await awakeningAgent.GetTodayAwakeningAsync(language);

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        
        // Both calls should return the same result after the generation process
        Assert.Equal(result1.AwakeningLevel, result2.AwakeningLevel);
        Assert.Equal(result1.AwakeningMessage, result2.AwakeningMessage);
        Assert.Equal(result1.Status, result2.Status);
        
        // Final status should be Completed
        Assert.Equal(AwakeningStatus.Completed, result2.Status);
    }

    [Fact(Skip = "")]
    public async Task CreateTestSessionContent_ShouldCreateValidSessionContent()
    {
        // Arrange & Act
        var sessionContent = CreateTestSessionContent(5);

        // Assert
        Assert.NotNull(sessionContent);
        Assert.Equal(5, sessionContent.Messages.Count);
        Assert.NotEqual(Guid.Empty, sessionContent.SessionId);
        Assert.Equal("Test Session", sessionContent.Title);
        Assert.NotNull(sessionContent.ExtractedContent);
    }
}
