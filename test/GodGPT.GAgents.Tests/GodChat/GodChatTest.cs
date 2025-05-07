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
    // /// 测试GodGPT功能
    // /// 从Aevatar.GodGPT.Tests迁移而来
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
    //         // 获取聊天管理器
    //         var grainId = Guid.NewGuid();
    //         _testOutputHelper.WriteLine($"Chat Manager GrainId: {grainId}");
    //         
    //         var chatManagerGrain = GrainFactory.GetGrain<IChatManagerGAgent>(grainId);
    //         
    //         // 创建一个会话
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
    //         // 创建聊天ID
    //         var chatId = Guid.NewGuid();
    //         _testOutputHelper.WriteLine($"ChatId: {chatId}");
    //         
    //         // 进行聊天
    //         var godChat = GrainFactory.GetGrain<IGodChat>(godGAgentId);
    //         await godChat.GodStreamChatAsync(grainId, "OpenAI", true, "Who are you",
    //             chatId.ToString(), null);
    //             
    //         // 等待聊天完成
    //         await Task.Delay(TimeSpan.FromSeconds(10));
    //         
    //         // 获取并验证聊天消息
    //         var chatMessage = await godChat.GetChatMessageAsync();
    //         _testOutputHelper.WriteLine($"chatMessage: {JsonConvert.SerializeObject(chatMessage)}");
    //         
    //         // 断言
    //         chatMessage.ShouldNotBeEmpty();
    //         chatMessage.Count.ShouldBe(2);
    //     }
    // }
} 