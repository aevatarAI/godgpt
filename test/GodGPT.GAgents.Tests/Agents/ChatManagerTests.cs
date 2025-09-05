using Xunit;
using Aevatar.Application.Grains.Agents.ChatManager;

namespace Aevatar.Application.Grains.Tests.Agents
{
    // public class ChatManagerTests : GodGPTTestBase
    // {
    //     [Fact]
    //     public async Task Should_Create_Chat_Session()
    //     {
    //         // Get ChatManager Grain
    //         var grain = GrainFactory.GetGrain<IChatManagerGAgent>(Guid.NewGuid());
    //         
    //         // Create session
    //         var session = await grain.CreateSessionAsync("DefaultLLM", "Test prompt");
    //         
    //         // Verify session created successfully
    //         Assert.NotEqual(Guid.Empty, session);
    //     }
    //
    //     [Fact]
    //     public async Task Should_Process_Message()
    //     {
    //         // Get ChatManager Grain
    //         var grain = GrainFactory.GetGrain<IChatManagerGAgent>(Guid.NewGuid());
    //         
    //         // Create session
    //         var sessionId = await grain.CreateSessionAsync("DefaultLLM", "Test prompt");
    //         
    //         // Process message
    //         var response = await grain.ChatWithSessionAsync(sessionId, "DefaultLLM", "Test message");
    //         
    //         // Verify response
    //         Assert.NotNull(response);
    //     }
    //
    //     [Fact]
    //     public async Task Should_Maintain_Session_State()
    //     {
    //         // Get ChatManager Grain
    //         var grain = GrainFactory.GetGrain<IChatManagerGAgent>(Guid.NewGuid());
    //         
    //         // Create session
    //         var sessionId = await grain.CreateSessionAsync("DefaultLLM", "Test prompt");
    //         
    //         // Verify session state
    //         Assert.True(await grain.IsUserSessionAsync(sessionId));
    //         
    //         // Process message
    //         var response = await grain.ChatWithSessionAsync(sessionId, "DefaultLLM", "Test message");
    //         Assert.NotNull(response);
    //         
    //         // Verify session state again
    //         Assert.True(await grain.IsUserSessionAsync(sessionId));
    //     }
    // }
} 