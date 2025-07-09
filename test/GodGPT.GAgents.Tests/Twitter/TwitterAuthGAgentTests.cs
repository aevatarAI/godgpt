using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Twitter;
using Shouldly;
using Xunit.Abstractions;

namespace Aevatar.Application.Grains.Tests.Twitter;

public class TwitterAuthGAgentTests : AevatarOrleansTestBase<AevatarGodGPTTestsMoudle>
{
    private readonly ITestOutputHelper _testOutputHelper;

    public TwitterAuthGAgentTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task VerifyAuthCode_Success_ShouldBindTwitterAccount()
    {
        // Arrange
        var code = "dmFkcVA4YUdmSjl1d1F6bU9yY0pVVmFpc0RRenRQaV9fWnV4azN5RlJ0NHdiOjE3NTEzNTY2NzQ2NzQ6MToxOmFjOjE";

        var userId = Guid.NewGuid();
        var twitterAuthGAgent = Cluster.GrainFactory.GetGrain<ITwitterAuthGAgent>(userId);
        await twitterAuthGAgent.GeneratePkcePlainAsync();
        // Act
        var result = await twitterAuthGAgent.VerifyAuthCodeAsync("web", code, "");
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();

        var bindStatus = await twitterAuthGAgent.GetBindStatusAsync();
        bindStatus.IsBound.ShouldBeTrue();


        var bindingGAgent = Cluster.GrainFactory.GetGrain<ITwitterIdentityBindingGAgent>(CommonHelper.StringToGuid(result.TwitterId));
        var bindingUserId = await bindingGAgent.GetUserIdAsync();
        bindingUserId.ShouldBe(userId);
    }

    [Fact]
    public async Task GetAuthParamsAsyncTest()
    {
        var userId = Guid.NewGuid();
        var twitterAuthGAgent = Cluster.GrainFactory.GetGrain<ITwitterAuthGAgent>(userId);
        var twitterAuthParamsDto = await twitterAuthGAgent.GetAuthParamsAsync();
        twitterAuthParamsDto.ShouldNotBeNull();
    }
}