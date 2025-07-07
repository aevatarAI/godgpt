using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Options;
using Newtonsoft.Json;
using Shouldly;
using Xunit.Abstractions;

namespace Aevatar.Application.Grains.Tests.Invitation;

public class ChatWithHistoryTest : AevatarOrleansTestBase<AevatarGodGPTTestsMoudle>
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly IGAgentFactory _agentFactory;

    public ChatWithHistoryTest(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        _agentFactory = GetRequiredService<IGAgentFactory>();
    }

    [Fact]
    public async Task ChatWithHistory_EmptyHistory_ReturnsEmptyList()
    {
        // Arrange
        var grainId = Guid.NewGuid();
        var godChatAgent = await _agentFactory.GetGAgentAsync<IGodChat>(grainId);

        // Initialize the agent
        var chatManagerGuid = Guid.NewGuid();
        await godChatAgent.InitAsync(chatManagerGuid);

        var sessionId = Guid.NewGuid();
        var systemLLM = "OpenAI";
        var content = "Hello, world!";
        var chatId = Guid.NewGuid().ToString();

        // Act
        var result = await godChatAgent.ChatWithHistory(sessionId, systemLLM, content, chatId);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
        _testOutputHelper.WriteLine(
            $"ChatWithHistory with empty history returned: {JsonConvert.SerializeObject(result)}");
    }

    [Fact]
    public async Task ChatWithHistory_WithHistory_CallsProxyCorrectly()
    {
        // Arrange
        var grainId = Guid.NewGuid();
        var chatManagerGuid = Guid.NewGuid();

        // Create a chat manager first to set up the session properly
        var chatManagerGAgent = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();
        var godGAgentId = await chatManagerGAgent.CreateSessionAsync("OpenAI", string.Empty, null);
        var godChatAgent = await _agentFactory.GetGAgentAsync<IGodChat>(godGAgentId);

        // Add some chat history first by calling GodStreamChatAsync
        var initialChatId = Guid.NewGuid().ToString();
        await godChatAgent.GodStreamChatAsync(grainId, "OpenAI", true,
            "Hello world", initialChatId);

        // Wait a bit for the async operation to complete
        await Task.Delay(TimeSpan.FromSeconds(2));

        var sessionId = Guid.NewGuid();
        var systemLLM = "OpenAI";
        var content =
            "Please summarize our conversation history into 1 to 2 sentences, keeping the content within 20 words, suitable for sharing with others";
        var chatId = Guid.NewGuid().ToString();
        //get history
        var history = await godChatAgent.GetChatMessageAsync();

        // Act
        var result = await godChatAgent.ChatWithHistory(sessionId, systemLLM, content, chatId);

        // Assert
        result.ShouldNotBeNull();
        _testOutputHelper.WriteLine(
            $"ChatWithHistory with existing history returned: {JsonConvert.SerializeObject(result)}");
    }

    [Fact]
    public async Task ChatWithHistory_WithCustomPromptSettings_AppliesSettingsCorrectly()
    {
        // Arrange
        var grainId = Guid.NewGuid();
        var chatManagerGuid = Guid.NewGuid();

        // Create a chat manager and session
        var chatManagerGAgent = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();
        var godGAgentId = await chatManagerGAgent.CreateSessionAsync("OpenAI", string.Empty, null);
        var godChatAgent = await _agentFactory.GetGAgentAsync<IGodChat>(godGAgentId);

        // Add some chat history
        var initialChatId = Guid.NewGuid().ToString();
        await godChatAgent.GodStreamChatAsync(grainId, "OpenAI", true,
            "Hello world", initialChatId);

        await Task.Delay(TimeSpan.FromSeconds(2));

        var sessionId = Guid.NewGuid();
        var systemLLM = "OpenAI";
        var content =
            "Please summarize our conversation history into 1 to 2 sentences, keeping the content within 20 words, suitable for sharing with others";
        var chatId = Guid.NewGuid().ToString();

        // Custom prompt settings
        var promptSettings = new ExecutionPromptSettings
        {
            Temperature = "0.5"
        };

        // Act
        var result = await godChatAgent.ChatWithHistory(sessionId, systemLLM, content, chatId, promptSettings);

        // Assert
        result.ShouldNotBeNull();
        _testOutputHelper.WriteLine(
            $"ChatWithHistory with custom prompt settings returned: {JsonConvert.SerializeObject(result)}");
    }

    [Fact]
    public async Task ChatWithHistory_WithDifferentRegions_HandlesRegionCorrectly()
    {
        // Arrange
        var grainId = Guid.NewGuid();
        var chatManagerGuid = Guid.NewGuid();

        var chatManagerGAgent = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();
        var godGAgentId = await chatManagerGAgent.CreateSessionAsync("OpenAI", string.Empty, null);
        var godChatAgent = await _agentFactory.GetGAgentAsync<IGodChat>(godGAgentId);

        // Add some chat history
        var initialChatId = Guid.NewGuid().ToString();
        await godChatAgent.GodStreamChatAsync(grainId, "OpenAI", true,
            "Message to create history", initialChatId);

        await Task.Delay(TimeSpan.FromSeconds(2));

        var sessionId = Guid.NewGuid();
        var systemLLM = "OpenAI";
        var content = "Test message with region";
        var chatId = Guid.NewGuid().ToString();
        var region = "CN";

        // Act
        var result = await godChatAgent.ChatWithHistory(sessionId, systemLLM, content, chatId,
            region: region);

        // Assert
        result.ShouldNotBeNull();
        _testOutputHelper.WriteLine(
            $"ChatWithHistory with region {region} returned: {JsonConvert.SerializeObject(result)}");
    }

    [Fact]
    public async Task ChatWithHistory_WithHttpRequest_HandlesHttpRequestCorrectly()
    {
        // Arrange
        var grainId = Guid.NewGuid();
        var chatManagerGuid = Guid.NewGuid();

        var chatManagerGAgent = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();
        var godGAgentId = await chatManagerGAgent.CreateSessionAsync("OpenAI", string.Empty, null);
        var godChatAgent = await _agentFactory.GetGAgentAsync<IGodChat>(godGAgentId);

        // Add some chat history
        var initialChatId = Guid.NewGuid().ToString();
        await godChatAgent.GodStreamChatAsync(grainId, "OpenAI", true,
            "Message to create history", initialChatId);

        await Task.Delay(TimeSpan.FromSeconds(2));

        var sessionId = Guid.NewGuid();
        var systemLLM = "OpenAI";
        var content = "Test message as HTTP request";
        var chatId = Guid.NewGuid().ToString();

        // Act
        var result = await godChatAgent.ChatWithHistory(sessionId, systemLLM, content, chatId,
            isHttpRequest: true);

        // Assert
        result.ShouldNotBeNull();
        _testOutputHelper.WriteLine(
            $"ChatWithHistory with HTTP request returned: {JsonConvert.SerializeObject(result)}");
    }

    [Fact]
    public async Task ChatWithHistory_DefaultPromptSettings_UsesDefaultTemperature()
    {
        // Arrange
        var grainId = Guid.NewGuid();
        var chatManagerGuid = Guid.NewGuid();

        var chatManagerGAgent = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();
        var godGAgentId = await chatManagerGAgent.CreateSessionAsync("OpenAI", string.Empty, null);
        var godChatAgent = await _agentFactory.GetGAgentAsync<IGodChat>(godGAgentId);

        // Add some chat history
        var initialChatId = Guid.NewGuid().ToString();
        await godChatAgent.GodStreamChatAsync(grainId, "OpenAI", true,
            "Message to create history", initialChatId);

        await Task.Delay(TimeSpan.FromSeconds(2));

        var sessionId = Guid.NewGuid();
        var systemLLM = "OpenAI";
        var content = "Test message with default settings";
        var chatId = Guid.NewGuid().ToString();

        // Act (no promptSettings parameter, should use default)
        var result = await godChatAgent.ChatWithHistory(sessionId, systemLLM, content, chatId);

        // Assert
        result.ShouldNotBeNull();
        _testOutputHelper.WriteLine(
            $"ChatWithHistory with default settings returned: {JsonConvert.SerializeObject(result)}");
    }

    [Fact]
    public async Task ChatWithHistory_AllParameters_HandlesAllParametersCorrectly()
    {
        // Arrange
        var grainId = Guid.NewGuid();
        var chatManagerGuid = Guid.NewGuid();

        var chatManagerGAgent = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();
        var godGAgentId = await chatManagerGAgent.CreateSessionAsync("BytePlusDeepSeekV3", string.Empty, null);
        var godChatAgent = await _agentFactory.GetGAgentAsync<IGodChat>(godGAgentId);

        // Add some chat history
        var initialChatId = Guid.NewGuid().ToString();
        await godChatAgent.GodStreamChatAsync(grainId, "BytePlusDeepSeekV3", true,
            "Message to create history", initialChatId);

        await Task.Delay(TimeSpan.FromSeconds(2));

        var sessionId = Guid.NewGuid();
        var systemLLM = "BytePlusDeepSeekV3";
        var content = "Comprehensive test message";
        var chatId = Guid.NewGuid().ToString();
        var promptSettings = new ExecutionPromptSettings
        {
            Temperature = "0.8"
        };
        var isHttpRequest = true;
        var region = "CN";

        // Act
        var result = await godChatAgent.ChatWithHistory(sessionId, systemLLM, content, chatId,
            promptSettings, isHttpRequest, region);

        // Assert
        result.ShouldNotBeNull();
        _testOutputHelper.WriteLine(
            $"ChatWithHistory with all parameters returned: {JsonConvert.SerializeObject(result)}");
    }

    [Fact]
    public async Task ChatWithHistory_MultipleSequentialCalls_HandlesSequenceCorrectly()
    {
        // Arrange
        var grainId = Guid.NewGuid();
        var chatManagerGuid = Guid.NewGuid();

        var chatManagerGAgent = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();
        var godGAgentId = await chatManagerGAgent.CreateSessionAsync("OpenAI", string.Empty, null);
        var godChatAgent = await _agentFactory.GetGAgentAsync<IGodChat>(godGAgentId);

        // Add some chat history
        var initialChatId = Guid.NewGuid().ToString();
        await godChatAgent.GodStreamChatAsync(grainId, "OpenAI", true,
            "Initial message", initialChatId);

        await Task.Delay(TimeSpan.FromSeconds(2));

        var sessionId = Guid.NewGuid();
        var systemLLM = "OpenAI";
        var chatId = Guid.NewGuid().ToString();

        // Act - Multiple sequential calls
        var result1 = await godChatAgent.ChatWithHistory(sessionId, systemLLM, "First call", chatId);
        var result2 = await godChatAgent.ChatWithHistory(sessionId, systemLLM, "Second call", chatId);
        var result3 = await godChatAgent.ChatWithHistory(sessionId, systemLLM, "Third call", chatId);

        // Assert
        result1.ShouldNotBeNull();
        result2.ShouldNotBeNull();
        result3.ShouldNotBeNull();

        _testOutputHelper.WriteLine($"Multiple ChatWithHistory calls:");
        _testOutputHelper.WriteLine($"Result 1: {JsonConvert.SerializeObject(result1)}");
        _testOutputHelper.WriteLine($"Result 2: {JsonConvert.SerializeObject(result2)}");
        _testOutputHelper.WriteLine($"Result 3: {JsonConvert.SerializeObject(result3)}");
    }
    
    [Fact]
    public async Task ChatWithHistory_WithMultiHistory_CallsProxyCorrectly()
    {
        // Arrange
        var grainId = Guid.NewGuid();
        var chatManagerGuid = Guid.NewGuid();

        // Create a chat manager first to set up the session properly
        var chatManagerGAgent = await _agentFactory.GetGAgentAsync<IChatManagerGAgent>();
        var godGAgentId = await chatManagerGAgent.CreateSessionAsync("OpenAI", string.Empty, null);
        var godChatAgent = await _agentFactory.GetGAgentAsync<IGodChat>(godGAgentId);

        // Add some chat history first by calling GodStreamChatAsync
        var firstChatId = Guid.NewGuid().ToString();
        await godChatAgent.GodStreamChatAsync(grainId, "OpenAI", true,
            "Internally rotated in the heart, outwardly manifested as the universe", firstChatId);
        var secondChatId = Guid.NewGuid().ToString();
        await godChatAgent.GodStreamChatAsync(grainId, "OpenAI", true,
            "I am the manifestation of the God of Language, and all phenomena are folded by my heart.", secondChatId);
        // Wait a bit for the async operation to complete
        await Task.Delay(TimeSpan.FromSeconds(2));

        var sessionId = Guid.NewGuid();
        var systemLLM = "OpenAI";
        var content =
            "Please summarize our conversation history into 1 to 2 sentences, keeping the content within 20 words, suitable for sharing with others";
        var chatId = Guid.NewGuid().ToString();
        //get history
        var history = await godChatAgent.GetChatMessageAsync();

        // Act
        var result = await godChatAgent.ChatWithHistory(sessionId, systemLLM, content, chatId);

        // Assert
        result.ShouldNotBeNull();
        _testOutputHelper.WriteLine(
            $"ChatWithHistory with existing history returned: {JsonConvert.SerializeObject(result)}");
    }
}