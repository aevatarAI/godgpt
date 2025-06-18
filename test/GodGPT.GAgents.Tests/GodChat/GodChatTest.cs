using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.AIGAgent.Dtos;
using Aevatar.Application.Grains.Tests.Core;
using Newtonsoft.Json;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Aevatar.Application.Grains.Tests.GodChat
{
    // /// <summary>
    // /// Test GodGPT functionality
    // /// Migrated from Aevatar.GodGPT.Tests
    // /// </summary>
    // public class GodChatTest : GodGPTTestBase
    // {
    //     private readonly ITestOutputHelper _testOutputHelper;
    //     
    //     public GodChatTest(ITestOutputHelper testOutputHelper)
    //     {
    //         _testOutputHelper = testOutputHelper;
    //     }
    //     
    //     [Fact]
    //     public async Task ChatAsync_Test()
    //     {
    //         // Get chat manager
    //         var grainId = Guid.NewGuid();
    //         _testOutputHelper.WriteLine($"Chat Manager GrainId: {grainId}");
    //         
    //         var chatManagerGrain = GrainFactory.GetGrain<IChatManagerGAgent>(grainId);
    //         
    //         // Create a session
    //         var userProfile = new UserProfileDto
    //         {
    //             Gender = "Male",
    //             BirthDate = DateTime.UtcNow,
    //             BirthPlace = "BeiJing",
    //             FullName = "Test001"
    //         };
    //         
    //         var godGAgentId = await chatManagerGrain.CreateSessionAsync("OpenAI", string.Empty, userProfile);
    //         _testOutputHelper.WriteLine($"God GAgent GrainId: {godGAgentId}");
    //
    //         // Create chat ID
    //         var chatId = Guid.NewGuid();
    //         _testOutputHelper.WriteLine($"ChatId: {chatId}");
    //         
    //         // Perform chat
    //         var godChat = GrainFactory.GetGrain<IGodChat>(godGAgentId);
    //         await godChat.GodStreamChatAsync(grainId, "OpenAI", true, "Who are you",
    //             chatId.ToString(), null);
    //             
    //         // Wait for chat to complete
    //         await Task.Delay(TimeSpan.FromSeconds(10));
    //         
    //         // Get and verify chat messages
    //         var chatMessage = await godChat.GetChatMessageAsync();
    //         _testOutputHelper.WriteLine($"chatMessage: {JsonConvert.SerializeObject(chatMessage)}");
    //         
    //         // Assert
    //         chatMessage.ShouldNotBeEmpty();
    //         chatMessage.Count.ShouldBe(2);
    //     }
    // }
} 