using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.UserInfo;
using Aevatar.Application.Grains.UserInfo.Enums;
using Shouldly;
using Xunit.Abstractions;

namespace Aevatar.Application.Grains.Tests.UserInfoCollection;

public class GetUserInfoOptionsAsyncTest : AevatarOrleansTestBase<AevatarGodGPTTestsMoudle>
{
    private readonly ITestOutputHelper _testOutputHelper;

    public GetUserInfoOptionsAsyncTest(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task GetUserInfoOptionsAsync_Should_Return_English_Options()
    {
        // Arrange
        var userInfoCollectionGAgent = Cluster.GrainFactory.GetGrain<IUserInfoCollectionGAgent>(Guid.NewGuid());
        var language = GodGPTLanguage.English;

        // Act
        var result = await userInfoCollectionGAgent.GetUserInfoOptionsAsync(language);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Message.ShouldBe("Options retrieved successfully");
        result.SeekingInterestOptions.ShouldNotBeNull();
        result.SeekingInterestOptions.Count.ShouldBe(6);
        result.SourceChannelOptions.ShouldNotBeNull();
        result.SourceChannelOptions.Count.ShouldBe(7);

        // Verify English text content
        var companionshipOption = result.SeekingInterestOptions.FirstOrDefault(x => x.Code == (int)SeekingInterestEnum.Companionship);
        companionshipOption.ShouldNotBeNull();
        companionshipOption.Text.ShouldBe("Companionship");

        var appStoreOption = result.SourceChannelOptions.FirstOrDefault(x => x.Code == (int)SourceChannelEnum.AppStorePlayStore);
        appStoreOption.ShouldNotBeNull();
        appStoreOption.Text.ShouldBe("App Store / Play Store");
    }

    [Fact]
    public async Task GetUserInfoOptionsAsync_Should_Return_TraditionalChinese_Options()
    {
        // Arrange
        var userInfoCollectionGAgent = Cluster.GrainFactory.GetGrain<IUserInfoCollectionGAgent>(Guid.NewGuid());
        var language = GodGPTLanguage.TraditionalChinese;

        // Act
        var result = await userInfoCollectionGAgent.GetUserInfoOptionsAsync(language);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Message.ShouldBe("Options retrieved successfully");
        result.SeekingInterestOptions.ShouldNotBeNull();
        result.SeekingInterestOptions.Count.ShouldBe(6);
        result.SourceChannelOptions.ShouldNotBeNull();
        result.SourceChannelOptions.Count.ShouldBe(7);

        // Verify Traditional Chinese text content
        var companionshipOption = result.SeekingInterestOptions.FirstOrDefault(x => x.Code == (int)SeekingInterestEnum.Companionship);
        companionshipOption.ShouldNotBeNull();
        companionshipOption.Text.ShouldBe("夥伴關係");

        var appStoreOption = result.SourceChannelOptions.FirstOrDefault(x => x.Code == (int)SourceChannelEnum.AppStorePlayStore);
        appStoreOption.ShouldNotBeNull();
        appStoreOption.Text.ShouldBe("App Store／Play 商店");
    }

    [Fact]
    public async Task GetUserInfoOptionsAsync_Should_Return_Spanish_Options()
    {
        // Arrange
        var userInfoCollectionGAgent = Cluster.GrainFactory.GetGrain<IUserInfoCollectionGAgent>(Guid.NewGuid());
        var language = GodGPTLanguage.Spanish;

        // Act
        var result = await userInfoCollectionGAgent.GetUserInfoOptionsAsync(language);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Message.ShouldBe("Options retrieved successfully");
        result.SeekingInterestOptions.ShouldNotBeNull();
        result.SeekingInterestOptions.Count.ShouldBe(6);
        result.SourceChannelOptions.ShouldNotBeNull();
        result.SourceChannelOptions.Count.ShouldBe(7);

        // Verify Spanish text content
        var companionshipOption = result.SeekingInterestOptions.FirstOrDefault(x => x.Code == (int)SeekingInterestEnum.Companionship);
        companionshipOption.ShouldNotBeNull();
        companionshipOption.Text.ShouldBe("Compañía");

        var appStoreOption = result.SourceChannelOptions.FirstOrDefault(x => x.Code == (int)SourceChannelEnum.AppStorePlayStore);
        appStoreOption.ShouldNotBeNull();
        appStoreOption.Text.ShouldBe("Tienda de Aplicaciones / Tienda Play");
    }

    [Fact]
    public async Task GetUserInfoOptionsAsync_Should_Return_All_Options_With_Codes()
    {
        // Arrange
        var userInfoCollectionGAgent = Cluster.GrainFactory.GetGrain<IUserInfoCollectionGAgent>(Guid.NewGuid());
        var language = GodGPTLanguage.English;

        // Act
        var result = await userInfoCollectionGAgent.GetUserInfoOptionsAsync(language);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();

        // Verify all seeking interest options are present
        var expectedSeekingInterests = Enum.GetValues<SeekingInterestEnum>();
        result.SeekingInterestOptions.Count.ShouldBe(expectedSeekingInterests.Length);

        foreach (SeekingInterestEnum interest in expectedSeekingInterests)
        {
            var option = result.SeekingInterestOptions.FirstOrDefault(x => x.Code == (int)interest);
            option.ShouldNotBeNull($"SeekingInterest {interest} should be present in options");
            option.Text.ShouldNotBeNullOrWhiteSpace($"SeekingInterest {interest} should have text");
        }

        // Verify all source channel options are present
        var expectedSourceChannels = Enum.GetValues<SourceChannelEnum>();
        result.SourceChannelOptions.Count.ShouldBe(expectedSourceChannels.Length);

        foreach (SourceChannelEnum channel in expectedSourceChannels)
        {
            var option = result.SourceChannelOptions.FirstOrDefault(x => x.Code == (int)channel);
            option.ShouldNotBeNull($"SourceChannel {channel} should be present in options");
            option.Text.ShouldNotBeNullOrWhiteSpace($"SourceChannel {channel} should have text");
        }
    }

    [Fact]
    public async Task GetUserInfoOptionsAsync_Should_Return_Default_English_For_Unsupported_Language()
    {
        // Arrange
        var userInfoCollectionGAgent = Cluster.GrainFactory.GetGrain<IUserInfoCollectionGAgent>(Guid.NewGuid());
        var language = (GodGPTLanguage)999; // Invalid/unsupported language

        // Act
        var result = await userInfoCollectionGAgent.GetUserInfoOptionsAsync(language);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Message.ShouldBe("Options retrieved successfully");

        // Should default to English text
        var companionshipOption = result.SeekingInterestOptions.FirstOrDefault(x => x.Code == (int)SeekingInterestEnum.Companionship);
        companionshipOption.ShouldNotBeNull();
        companionshipOption.Text.ShouldBe("Companionship"); // Default English text
    }
}
