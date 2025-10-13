using Aevatar.Application.Grains.UserInfo;
using Aevatar.Application.Grains.UserInfo.Dtos;
using Aevatar.Application.Grains.UserInfo.Enums;
using Shouldly;
using Xunit.Abstractions;

namespace Aevatar.Application.Grains.Tests.UserInfoCollection;

/// <summary>
/// Test suite for UserInfoCollectionGAgent functionality
/// </summary>
public class UserInfoCollectionTest : AevatarOrleansTestBase<AevatarGodGPTTestsMoudle>
{
    private readonly ITestOutputHelper _testOutputHelper;

    public UserInfoCollectionTest(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    private async Task<IUserInfoCollectionGAgent> CreateTestUserInfoCollectionGAgentAsync()
    {
        var userId = Guid.NewGuid();
        var userInfoCollectionGAgent = Cluster.GrainFactory.GetGrain<IUserInfoCollectionGAgent>(userId);
        
        _testOutputHelper.WriteLine($"Created test UserInfoCollectionGAgent with UserId: {userId}");
        return userInfoCollectionGAgent;
    }

    #region Full Data Collection Tests

    [Fact]
    public async Task UpdateUserInfoCollectionAsync_Should_Save_All_Data_Successfully()
    {
        RequestContext.Set("GodGPTLanguage","English");
        // Arrange
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 1,
                FirstName = "John",
                LastName = "Doe"
            },
            LocationInfo = new UserLocationInfoDto
            {
                Country = "United States",
                City = "New York"
            },
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 15,
                Month = 6,
                Year = 1990
            },
            BirthTimeInfo = new UserBirthTimeInfoDto
            {
                Hour = 14,
                Minute = 30
            },
            SeekingInterests = new List<SeekingInterestEnum> { SeekingInterestEnum.Companionship, SeekingInterestEnum.SelfDiscovery },
            SourceChannels = new List<SourceChannelEnum> { SourceChannelEnum.AppStorePlayStore, SourceChannelEnum.SocialMedia }
        };

        // Act
        var result = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Message.ShouldBe("User info collection updated successfully");
        result.Data.ShouldNotBeNull();
        result.Data.NameInfo.ShouldNotBeNull();
        result.Data.NameInfo.Gender.ShouldBe(1);
        result.Data.NameInfo.FirstName.ShouldBe("John");
        result.Data.NameInfo.LastName.ShouldBe("Doe");
        result.Data.LocationInfo.ShouldNotBeNull();
        result.Data.LocationInfo.Country.ShouldBe("United States");
        result.Data.LocationInfo.City.ShouldBe("New York");
        result.Data.BirthDateInfo.ShouldNotBeNull();
        result.Data.BirthDateInfo.Day.ShouldBe(15);
        result.Data.BirthDateInfo.Month.ShouldBe(6);
        result.Data.BirthDateInfo.Year.ShouldBe(1990);
        result.Data.BirthTimeInfo.ShouldNotBeNull();
        result.Data.BirthTimeInfo.Hour.ShouldBe(14);
        result.Data.BirthTimeInfo.Minute.ShouldBe(30);
        result.Data.SeekingInterests.ShouldNotBeNull();
        result.Data.SeekingInterests.Count.ShouldBe(2);
        result.Data.SeekingInterests.ShouldContain("Companionship");
        result.Data.SeekingInterests.ShouldContain("Self-discovery");
        result.Data.SourceChannels.ShouldNotBeNull();
        result.Data.SourceChannels.Count.ShouldBe(2);
        result.Data.SourceChannels.ShouldContain("App Store / Play Store");
        result.Data.SourceChannels.ShouldContain("Social media");
        result.Data.IsInitialized.ShouldBeTrue();
        result.Data.IsCompleted.ShouldBeTrue();
        
        // Verify code fields are populated correctly
        result.Data.SeekingInterestsCode.ShouldNotBeNull();
        result.Data.SeekingInterestsCode.Count.ShouldBe(2);
        result.Data.SeekingInterestsCode.ShouldContain(0); // Companionship
        result.Data.SeekingInterestsCode.ShouldContain(1); // Self-discovery
        result.Data.SourceChannelsCode.ShouldNotBeNull();
        result.Data.SourceChannelsCode.Count.ShouldBe(2);
        result.Data.SourceChannelsCode.ShouldContain(0); // App Store / Play Store
        result.Data.SourceChannelsCode.ShouldContain(1); // Social media
    }

    [Fact]
    public async Task GetUserInfoCollectionAsync_Should_Return_Complete_Data_After_Full_Update()
    {
        RequestContext.Set("GodGPTLanguage","English");

        // Arrange
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 2,
                FirstName = "Jane",
                LastName = "Smith"
            },
            LocationInfo = new UserLocationInfoDto
            {
                Country = "Canada",
                City = "Toronto"
            },
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 22,
                Month = 12,
                Year = 1985
            },
            BirthTimeInfo = new UserBirthTimeInfoDto
            {
                Hour = 9,
                Minute = 15
            },
            SeekingInterests = new List<SeekingInterestEnum> { SeekingInterestEnum.SpiritualGrowth, SeekingInterestEnum.CareerGuidance },
            SourceChannels = new List<SourceChannelEnum> { SourceChannelEnum.SearchEngine, SourceChannelEnum.FriendReferral }
        };

        // Act
        await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);
        var result = await userInfoCollectionGAgent.GetUserInfoCollectionAsync();

        // Assert
        result.ShouldNotBeNull();
        result.NameInfo.ShouldNotBeNull();
        result.NameInfo.Gender.ShouldBe(2);
        result.NameInfo.FirstName.ShouldBe("Jane");
        result.NameInfo.LastName.ShouldBe("Smith");
        result.LocationInfo.ShouldNotBeNull();
        result.LocationInfo.Country.ShouldBe("Canada");
        result.LocationInfo.City.ShouldBe("Toronto");
        result.BirthDateInfo.ShouldNotBeNull();
        result.BirthDateInfo.Day.ShouldBe(22);
        result.BirthDateInfo.Month.ShouldBe(12);
        result.BirthDateInfo.Year.ShouldBe(1985);
        result.BirthTimeInfo.ShouldNotBeNull();
        result.BirthTimeInfo.Hour.ShouldBe(9);
        result.BirthTimeInfo.Minute.ShouldBe(15);
        result.SeekingInterests.ShouldNotBeNull();
        result.SeekingInterests.Count.ShouldBe(2);
        result.SeekingInterests.ShouldContain("Spiritual growth");
        result.SeekingInterests.ShouldContain("Career guidance");
        result.SourceChannels.ShouldNotBeNull();
        result.SourceChannels.Count.ShouldBe(2);
        result.SourceChannels.ShouldContain("Search engine");
        result.SourceChannels.ShouldContain("Friend referral");
        result.IsInitialized.ShouldBeTrue();
        result.IsCompleted.ShouldBeTrue();
        
        // Verify code fields
        result.SeekingInterestsCode.ShouldNotBeNull();
        result.SeekingInterestsCode.Count.ShouldBe(2);
        result.SeekingInterestsCode.ShouldContain(2); // Spiritual growth
        result.SeekingInterestsCode.ShouldContain(5); // Career guidance
        result.SourceChannelsCode.ShouldNotBeNull();
        result.SourceChannelsCode.Count.ShouldBe(2);
        result.SourceChannelsCode.ShouldContain(2); // Search engine
        result.SourceChannelsCode.ShouldContain(3); // Friend referral
    }

    [Fact]
    public async Task GetUserInfoDisplayAsync_Should_Format_Data_Correctly_For_Display()
    {
        RequestContext.Set("GodGPTLanguage","English");
        // Arrange
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 1,
                FirstName = "John",
                LastName = "Doe"
            },
            LocationInfo = new UserLocationInfoDto
            {
                Country = "United States",
                City = "New York"
            },
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 15,
                Month = 6,
                Year = 1990
            },
            BirthTimeInfo = new UserBirthTimeInfoDto
            {
                Hour = 14,
                Minute = 30
            },
            SeekingInterests = new List<SeekingInterestEnum> { SeekingInterestEnum.Companionship },
            SourceChannels = new List<SourceChannelEnum> { SourceChannelEnum.AppStorePlayStore }
        };

        // Act
        await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);
        var result = await userInfoCollectionGAgent.GetUserInfoDisplayAsync();

        // Assert
        result.ShouldNotBeNull();
        result.FirstName.ShouldBe("John");
        result.LastName.ShouldBe("Doe");
        result.Gender.ShouldBe(1);
        result.Day.ShouldBe(15);
        result.Month.ShouldBe(6);
        result.Year.ShouldBe(1990);
        result.Hour.ShouldBe(14);
        result.Minute.ShouldBe(30);
        result.Country.ShouldBe("United States");
        result.City.ShouldBe("New York");
        result.SeekingInterests.ShouldNotBeNull();
        result.SeekingInterests.Count.ShouldBe(1);
        result.SeekingInterests.ShouldContain("Companionship");
        result.SourceChannels.ShouldNotBeNull();
        result.SourceChannels.Count.ShouldBe(1);
        result.SourceChannels.ShouldContain("App Store / Play Store");
    }

    #endregion

    #region Partial Data Collection Tests

    [Fact]
    public async Task UpdateUserInfoCollectionAsync_Should_Save_Partial_Data_Successfully()
    {
        RequestContext.Set("GodGPTLanguage","English");
        // Arrange
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 2,
                FirstName = "Alice",
                LastName = "Johnson"
            }
        };

        // Act
        var result = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.NameInfo.ShouldNotBeNull();
        result.Data.NameInfo.Gender.ShouldBe(2);
        result.Data.NameInfo.FirstName.ShouldBe("Alice");
        result.Data.NameInfo.LastName.ShouldBe("Johnson");
        result.Data.LocationInfo.ShouldBeNull();
        result.Data.BirthDateInfo.ShouldBeNull();
        result.Data.BirthTimeInfo.ShouldBeNull();
        result.Data.SeekingInterests.ShouldNotBeNull();
        result.Data.SeekingInterests.Count.ShouldBe(0);
        result.Data.SourceChannels.ShouldNotBeNull();
        result.Data.SourceChannels.Count.ShouldBe(0);
        result.Data.IsInitialized.ShouldBeTrue();
        result.Data.IsCompleted.ShouldBeFalse();
        
        // Verify code fields are empty for partial data
        result.Data.SeekingInterestsCode.ShouldNotBeNull();
        result.Data.SeekingInterestsCode.Count.ShouldBe(0);
        result.Data.SourceChannelsCode.ShouldNotBeNull();
        result.Data.SourceChannelsCode.Count.ShouldBe(0);
    }

    [Fact]
    public async Task UpdateUserInfoCollectionAsync_Should_Handle_Birth_Time_Without_Minute()
    {
        RequestContext.Set("GodGPTLanguage","English");
        // Arrange
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 1,
                FirstName = "Bob",
                LastName = "Wilson"
            },
            LocationInfo = new UserLocationInfoDto
            {
                Country = "United Kingdom",
                City = "London"
            },
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 10,
                Month = 3,
                Year = 1995
            },
            BirthTimeInfo = new UserBirthTimeInfoDto
            {
                Hour = 16
                // Minute is null
            },
            SeekingInterests = new List<SeekingInterestEnum> { SeekingInterestEnum.LoveAndRelationships },
            SourceChannels = new List<SourceChannelEnum> { SourceChannelEnum.EventConference }
        };

        // Act
        var result = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.BirthTimeInfo.ShouldNotBeNull();
        result.Data.BirthTimeInfo.Hour.ShouldBe(16);
        result.Data.BirthTimeInfo.Minute.ShouldBeNull();
        result.Data.IsCompleted.ShouldBeTrue();
        
        // Verify code fields
        result.Data.SeekingInterestsCode.ShouldNotBeNull();
        result.Data.SeekingInterestsCode.Count.ShouldBe(1);
        result.Data.SeekingInterestsCode.ShouldContain(3); // Love & relationships
        result.Data.SourceChannelsCode.ShouldNotBeNull();
        result.Data.SourceChannelsCode.Count.ShouldBe(1);
        result.Data.SourceChannelsCode.ShouldContain(4); // Event / conference
    }

    [Fact]
    public async Task UpdateUserInfoCollectionAsync_Should_Update_Sequentially()
    {
        RequestContext.Set("GodGPTLanguage","English");
        // Arrange
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();

        // First update - name only
        var nameUpdateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 2,
                FirstName = "Sarah",
                LastName = "Davis"
            }
        };

        // Second update - location
        var locationUpdateDto = new UpdateUserInfoCollectionDto
        {
            LocationInfo = new UserLocationInfoDto
            {
                Country = "Australia",
                City = "Sydney"
            }
        };

        // Third update - birth date and interests
        var birthDateUpdateDto = new UpdateUserInfoCollectionDto
        {
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 5,
                Month = 8,
                Year = 1992
            },
            SeekingInterests = new List<SeekingInterestEnum> { SeekingInterestEnum.DailyFortuneTelling, SeekingInterestEnum.CareerGuidance },
            SourceChannels = new List<SourceChannelEnum> { SourceChannelEnum.Advertisement, SourceChannelEnum.Other }
        };

        // Act
        var result1 = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(nameUpdateDto);
        var result2 = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(locationUpdateDto);
        var result3 = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(birthDateUpdateDto);

        // Assert
        result1.Success.ShouldBeTrue();
        result2.Success.ShouldBeTrue();
        result3.Success.ShouldBeTrue();

        var finalResult = await userInfoCollectionGAgent.GetUserInfoCollectionAsync();
        finalResult.ShouldNotBeNull();
        finalResult.NameInfo.ShouldNotBeNull();
        finalResult.NameInfo.Gender.ShouldBe(2);
        finalResult.NameInfo.FirstName.ShouldBe("Sarah");
        finalResult.NameInfo.LastName.ShouldBe("Davis");
        finalResult.LocationInfo.ShouldNotBeNull();
        finalResult.LocationInfo.Country.ShouldBe("Australia");
        finalResult.LocationInfo.City.ShouldBe("Sydney");
        finalResult.BirthDateInfo.ShouldNotBeNull();
        finalResult.BirthDateInfo.Day.ShouldBe(5);
        finalResult.BirthDateInfo.Month.ShouldBe(8);
        finalResult.BirthDateInfo.Year.ShouldBe(1992);
        finalResult.SeekingInterests.ShouldNotBeNull();
        finalResult.SeekingInterests.Count.ShouldBe(2);
        finalResult.SeekingInterests.ShouldContain("Daily fortune telling");
        finalResult.SeekingInterests.ShouldContain("Career guidance");
        finalResult.SourceChannels.ShouldNotBeNull();
        finalResult.SourceChannels.Count.ShouldBe(2);
        finalResult.SourceChannels.ShouldContain("Advertisement");
        finalResult.SourceChannels.ShouldContain("Other");
        finalResult.IsCompleted.ShouldBeTrue();
        
        // Verify code fields
        finalResult.SeekingInterestsCode.ShouldNotBeNull();
        finalResult.SeekingInterestsCode.Count.ShouldBe(2);
        finalResult.SeekingInterestsCode.ShouldContain(4); // Daily fortune telling
        finalResult.SeekingInterestsCode.ShouldContain(5); // Career guidance
        finalResult.SourceChannelsCode.ShouldNotBeNull();
        finalResult.SourceChannelsCode.Count.ShouldBe(2);
        finalResult.SourceChannelsCode.ShouldContain(5); // Advertisement
        finalResult.SourceChannelsCode.ShouldContain(6); // Other
    }

    [Fact]
    public async Task UpdateUserInfoCollectionAsync_Should_Update_Existing_Field_Successfully()
    {
        RequestContext.Set("GodGPTLanguage","English");
        // Arrange
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        
        // First, save complete data
        var initialUpdateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 1,
                FirstName = "Mike",
                LastName = "Brown"
            },
            LocationInfo = new UserLocationInfoDto
            {
                Country = "Germany",
                City = "Berlin"
            },
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 20,
                Month = 4,
                Year = 1988
            },
            SeekingInterests = new List<SeekingInterestEnum> { SeekingInterestEnum.SelfDiscovery },
            SourceChannels = new List<SourceChannelEnum> { SourceChannelEnum.SocialMedia }
        };

        await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(initialUpdateDto);

        // Then, update only the name
        var nameUpdateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 1,
                FirstName = "Michael",
                LastName = "Brown"
            }
        };

        // Act
        var result = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(nameUpdateDto);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        
        var finalResult = await userInfoCollectionGAgent.GetUserInfoCollectionAsync();
        finalResult.ShouldNotBeNull();
        finalResult.NameInfo.ShouldNotBeNull();
        finalResult.NameInfo.FirstName.ShouldBe("Michael"); // Updated
        finalResult.NameInfo.LastName.ShouldBe("Brown");
        finalResult.LocationInfo.ShouldNotBeNull();
        finalResult.LocationInfo.Country.ShouldBe("Germany"); // Should remain unchanged
        finalResult.LocationInfo.City.ShouldBe("Berlin");
        finalResult.BirthDateInfo.ShouldNotBeNull();
        finalResult.BirthDateInfo.Day.ShouldBe(20); // Should remain unchanged
        finalResult.SeekingInterests.ShouldNotBeNull();
        finalResult.SeekingInterests.Count.ShouldBe(1);
        finalResult.SeekingInterests.ShouldContain("Self-discovery"); // Should remain unchanged
        finalResult.SourceChannels.ShouldNotBeNull();
        finalResult.SourceChannels.Count.ShouldBe(1);
        finalResult.SourceChannels.ShouldContain("Social media"); // Should remain unchanged
        finalResult.IsCompleted.ShouldBeTrue();
        
        // Verify code fields remain unchanged
        finalResult.SeekingInterestsCode.ShouldNotBeNull();
        finalResult.SeekingInterestsCode.Count.ShouldBe(1);
        finalResult.SeekingInterestsCode.ShouldContain(1); // Self-discovery
        finalResult.SourceChannelsCode.ShouldNotBeNull();
        finalResult.SourceChannelsCode.Count.ShouldBe(1);
        finalResult.SourceChannelsCode.ShouldContain(1); // Social media
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task UpdateUserInfoCollectionAsync_Should_Reject_Invalid_Data()
    {
        RequestContext.Set("GodGPTLanguage","English");
        // Arrange
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();

        // Test 1: Empty name fields
        var invalidNameDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 0, // Empty
                FirstName = "John",
                LastName = "Doe"
            }
        };

        // Test 2: Invalid birth date
        var invalidBirthDateDto = new UpdateUserInfoCollectionDto
        {
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 32, // Invalid day
                Month = 6,
                Year = 1990
            }
        };

        // Test 3: Invalid birth time
        var invalidBirthTimeDto = new UpdateUserInfoCollectionDto
        {
            BirthTimeInfo = new UserBirthTimeInfoDto
            {
                Hour = 25, // Invalid hour
                Minute = 30
            }
        };

        // Test 4: Empty seeking interests
        var invalidSeekingInterestsDto = new UpdateUserInfoCollectionDto
        {
            SeekingInterests = new List<SeekingInterestEnum>() // Empty list
        };

        // Test 5: Empty source channels
        var invalidSourceChannelsDto = new UpdateUserInfoCollectionDto
        {
            SeekingInterests = new List<SeekingInterestEnum> { SeekingInterestEnum.Companionship },
            SourceChannels = new List<SourceChannelEnum>() // Empty list
        };

        // Act & Assert
        var result1 = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(invalidNameDto);
        result1.Success.ShouldBeFalse();
        result1.Message.ShouldContain("Gender, FirstName, and LastName are required");

        var result2 = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(invalidBirthDateDto);
        result2.Success.ShouldBeFalse();
        result2.Message.ShouldContain("Invalid birthDate values");

        var result3 = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(invalidBirthTimeDto);
        result3.Success.ShouldBeFalse();
        result3.Message.ShouldContain("Hour must be between 0 and 23");

        var result4 = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(invalidSeekingInterestsDto);
        result4.Success.ShouldBeFalse();
        result4.Message.ShouldContain("At least one seeking interest is required");

        var result5 = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(invalidSourceChannelsDto);
        result5.Success.ShouldBeFalse();
        result5.Message.ShouldContain("At least one source channel is required");
    }

    #endregion

    #region Clear Data Tests

    [Fact]
    public async Task ClearAllAsync_Should_Reset_All_Data_Successfully()
    {
        RequestContext.Set("GodGPTLanguage","English");
        // Arrange
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 2,
                FirstName = "Emma",
                LastName = "Taylor"
            },
            LocationInfo = new UserLocationInfoDto
            {
                Country = "France",
                City = "Paris"
            },
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 12,
                Month = 9,
                Year = 1993
            },
            BirthTimeInfo = new UserBirthTimeInfoDto
            {
                Hour = 11,
                Minute = 45
            },
            SeekingInterests = new List<SeekingInterestEnum> { SeekingInterestEnum.SpiritualGrowth, SeekingInterestEnum.LoveAndRelationships },
            SourceChannels = new List<SourceChannelEnum> { SourceChannelEnum.SearchEngine, SourceChannelEnum.FriendReferral }
        };

        // First, save data
        await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);
        var beforeClear = await userInfoCollectionGAgent.GetUserInfoCollectionAsync();
        beforeClear.ShouldNotBeNull();
        beforeClear.IsInitialized.ShouldBeTrue();

        // Act
        await userInfoCollectionGAgent.ClearAllAsync();

        // Assert
        var afterClear = await userInfoCollectionGAgent.GetUserInfoCollectionAsync();
        afterClear.ShouldBeNull(); // Should return null when not initialized

        // Try to get display data - should also return null
        var displayData = await userInfoCollectionGAgent.GetUserInfoDisplayAsync();
        displayData.ShouldBeNull();
    }

    #endregion

    #region Multi-language Tests

    [Fact]
    public async Task UpdateUserInfoCollectionAsync_Should_Work_With_English_Language()
    {
        RequestContext.Set("GodGPTLanguage","English");
        // Arrange
        RequestContext.Set("GodGPTLanguage", "English");
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 1,
                FirstName = "John",
                LastName = "Doe"
            },
            SeekingInterests = new List<SeekingInterestEnum> { SeekingInterestEnum.Companionship, SeekingInterestEnum.SelfDiscovery, SeekingInterestEnum.SpiritualGrowth },
            SourceChannels = new List<SourceChannelEnum> { SourceChannelEnum.AppStorePlayStore, SourceChannelEnum.SocialMedia, SourceChannelEnum.SearchEngine }
        };

        // Act
        var result = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Data.SeekingInterests.ShouldNotBeNull();
        result.Data.SeekingInterests.Count.ShouldBe(3);
        result.Data.SeekingInterests.ShouldContain("Companionship");
        result.Data.SeekingInterests.ShouldContain("Self-discovery");
        result.Data.SeekingInterests.ShouldContain("Spiritual growth");
        result.Data.SourceChannels.ShouldNotBeNull();
        result.Data.SourceChannels.Count.ShouldBe(3);
        result.Data.SourceChannels.ShouldContain("App Store / Play Store");
        result.Data.SourceChannels.ShouldContain("Social media");
        result.Data.SourceChannels.ShouldContain("Search engine");
        
        // Verify code fields
        result.Data.SeekingInterestsCode.ShouldNotBeNull();
        result.Data.SeekingInterestsCode.Count.ShouldBe(3);
        result.Data.SeekingInterestsCode.ShouldContain(0); // Companionship
        result.Data.SeekingInterestsCode.ShouldContain(1); // Self-discovery
        result.Data.SeekingInterestsCode.ShouldContain(2); // Spiritual growth
        result.Data.SourceChannelsCode.ShouldNotBeNull();
        result.Data.SourceChannelsCode.Count.ShouldBe(3);
        result.Data.SourceChannelsCode.ShouldContain(0); // App Store / Play Store
        result.Data.SourceChannelsCode.ShouldContain(1); // Social media
        result.Data.SourceChannelsCode.ShouldContain(2); // Search engine
    }

    [Fact]
    public async Task UpdateUserInfoCollectionAsync_Should_Work_With_Traditional_Chinese_Language()
    {
        // Arrange
        RequestContext.Set("GodGPTLanguage", "TraditionalChinese");
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 2,
                FirstName = "Xiaomei",
                LastName = "Wang"
            },
            SeekingInterests = new List<SeekingInterestEnum> { SeekingInterestEnum.Companionship, SeekingInterestEnum.SelfDiscovery, SeekingInterestEnum.SpiritualGrowth },
            SourceChannels = new List<SourceChannelEnum> { SourceChannelEnum.AppStorePlayStore, SourceChannelEnum.SocialMedia, SourceChannelEnum.SearchEngine }
        };

        // Act
        var result = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Data.SeekingInterests.ShouldNotBeNull();
        result.Data.SeekingInterests.Count.ShouldBe(3);
        result.Data.SeekingInterests.ShouldContain("夥伴關係");
        result.Data.SeekingInterests.ShouldContain("自我探索");
        result.Data.SeekingInterests.ShouldContain("靈性成長");
        result.Data.SourceChannels.ShouldNotBeNull();
        result.Data.SourceChannels.Count.ShouldBe(3);
        result.Data.SourceChannels.ShouldContain("App Store／Play 商店");
        result.Data.SourceChannels.ShouldContain("社群媒體");
        result.Data.SourceChannels.ShouldContain("搜尋引擎");
        
        // Verify code fields (should be same as English)
        result.Data.SeekingInterestsCode.ShouldNotBeNull();
        result.Data.SeekingInterestsCode.Count.ShouldBe(3);
        result.Data.SeekingInterestsCode.ShouldContain(0);
        result.Data.SeekingInterestsCode.ShouldContain(1);
        result.Data.SeekingInterestsCode.ShouldContain(2);
        result.Data.SourceChannelsCode.ShouldNotBeNull();
        result.Data.SourceChannelsCode.Count.ShouldBe(3);
        result.Data.SourceChannelsCode.ShouldContain(0);
        result.Data.SourceChannelsCode.ShouldContain(1);
        result.Data.SourceChannelsCode.ShouldContain(2);
    }

    [Fact]
    public async Task UpdateUserInfoCollectionAsync_Should_Work_With_Spanish_Language()
    {
        // Arrange
        RequestContext.Set("GodGPTLanguage", "Spanish");
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 1,
                FirstName = "Carlos",
                LastName = "Rodriguez"
            },
            SeekingInterests = new List<SeekingInterestEnum> { SeekingInterestEnum.Companionship, SeekingInterestEnum.SelfDiscovery, SeekingInterestEnum.SpiritualGrowth },
            SourceChannels = new List<SourceChannelEnum> { SourceChannelEnum.AppStorePlayStore, SourceChannelEnum.SocialMedia, SourceChannelEnum.SearchEngine }
        };

        // Act
        var result = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Data.SeekingInterests.ShouldNotBeNull();
        result.Data.SeekingInterests.Count.ShouldBe(3);
        result.Data.SeekingInterests.ShouldContain("Compañía");
        result.Data.SeekingInterests.ShouldContain("Autodescubrimiento");
        result.Data.SeekingInterests.ShouldContain("Crecimiento espiritual");
        result.Data.SourceChannels.ShouldNotBeNull();
        result.Data.SourceChannels.Count.ShouldBe(3);
        result.Data.SourceChannels.ShouldContain("Tienda de Aplicaciones / Tienda Play");
        result.Data.SourceChannels.ShouldContain("Redes sociales");
        result.Data.SourceChannels.ShouldContain("Motor de búsqueda");
        
        // Verify code fields (should be same as English)
        result.Data.SeekingInterestsCode.ShouldNotBeNull();
        result.Data.SeekingInterestsCode.Count.ShouldBe(3);
        result.Data.SeekingInterestsCode.ShouldContain(0); // Compañía
        result.Data.SeekingInterestsCode.ShouldContain(1); // Autodescubrimiento
        result.Data.SeekingInterestsCode.ShouldContain(2); // Crecimiento espiritual
        result.Data.SourceChannelsCode.ShouldNotBeNull();
        result.Data.SourceChannelsCode.Count.ShouldBe(3);
        result.Data.SourceChannelsCode.ShouldContain(0); // Tiendas de aplicaciones
        result.Data.SourceChannelsCode.ShouldContain(1); // Redes sociales
        result.Data.SourceChannelsCode.ShouldContain(2); // Motor de búsqueda
    }

    [Fact]
    public async Task UpdateUserInfoCollectionAsync_Should_Reject_Invalid_Language_Options()
    {
        // Arrange
        RequestContext.Set("GodGPTLanguage", "English");
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 1,
                FirstName = "John",
                LastName = "Doe"
            },
            SeekingInterests = new List<SeekingInterestEnum> { (SeekingInterestEnum)999 }, // Invalid option
            SourceChannels = new List<SourceChannelEnum> { SourceChannelEnum.AppStorePlayStore }
        };

        // Act
        var result = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("Invalid seeking interests");
    }

    #endregion

    #region Generate User Info Prompt Tests

    [Fact]
    public async Task GenerateUserInfoPromptAsync_Should_Generate_Complete_Prompt_With_All_Data()
    {
        // Arrange
        RequestContext.Set("GodGPTLanguage", "English");
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 1,
                FirstName = "John",
                LastName = "Doe"
            },
            LocationInfo = new UserLocationInfoDto
            {
                Country = "United States",
                City = "New York"
            },
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 15,
                Month = 6,
                Year = 1990
            }
        };

        await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);

        // Act
        var result = await userInfoCollectionGAgent.GenerateUserInfoPromptAsync();

        // Assert
        result.ShouldNotBeNull();
        result.ShouldNotBeEmpty();
        result.ShouldContain("User Name: John Doe");
        result.ShouldContain("User Location: New York, United States");
        result.ShouldContain("Message Time:");
        result.ShouldContain("User Gender: Male");
        result.ShouldContain("User Age:");
        result.ShouldContain("User Language: English");
        
        _testOutputHelper.WriteLine($"Generated prompt: {result}");
    }

    [Fact]
    public async Task GenerateUserInfoPromptAsync_Should_Generate_Prompt_With_Female_Gender()
    {
        // Arrange
        RequestContext.Set("GodGPTLanguage", "TraditionalChinese");
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 2,
                FirstName = "Jane",
                LastName = "Smith"
            },
            LocationInfo = new UserLocationInfoDto
            {
                Country = "Canada",
                City = "Toronto"
            },
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 22,
                Month = 12,
                Year = 1985
            }
        };

        await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);

        // Act
        var result = await userInfoCollectionGAgent.GenerateUserInfoPromptAsync();

        // Assert
        result.ShouldNotBeNull();
        result.ShouldNotBeEmpty();
        result.ShouldContain("User Name: Jane Smith");
        result.ShouldContain("User Location: Toronto, Canada");
        result.ShouldContain("User Gender: Female");
        result.ShouldContain("User Language: Traditional Chinese");
        
        _testOutputHelper.WriteLine($"Generated prompt: {result}");
    }

    [Fact]
    public async Task GenerateUserInfoPromptAsync_Should_Handle_Partial_Data_With_Unknown_Values()
    {
        // Arrange
        RequestContext.Set("GodGPTLanguage", "Spanish");
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 1,
                FirstName = "Carlos",
                LastName = "Rodriguez"
            }
            // No location or birth date provided
        };

        await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);

        // Act
        var result = await userInfoCollectionGAgent.GenerateUserInfoPromptAsync();

        // Assert
        result.ShouldNotBeNull();
        result.ShouldNotBeEmpty();
        result.ShouldContain("User Name: Carlos Rodriguez");
        result.ShouldContain("User Location: Unknown");
        result.ShouldContain("User Gender: Male");
        result.ShouldContain("User Age: Unknown");
        result.ShouldContain("User Language: Spanish");
        
        _testOutputHelper.WriteLine($"Generated prompt: {result}");
    }

    [Fact]
    public async Task GenerateUserInfoPromptAsync_Should_Return_Empty_String_When_Not_Initialized()
    {
        // Arrange
        RequestContext.Set("GodGPTLanguage", "English");
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        // Don't update any data - agent remains uninitialized

        // Act
        var result = await userInfoCollectionGAgent.GenerateUserInfoPromptAsync();

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
        
        _testOutputHelper.WriteLine($"Generated prompt for uninitialized agent: '{result}'");
    }

    [Fact]
    public async Task GenerateUserInfoPromptAsync_Should_Calculate_Age_Correctly()
    {
        // Arrange
        RequestContext.Set("GodGPTLanguage", "English");
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var currentYear = DateTime.UtcNow.Year;
        var birthYear = currentYear - 30; // 30 years old
        
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 2,
                FirstName = "Alice",
                LastName = "Johnson"
            },
            LocationInfo = new UserLocationInfoDto
            {
                Country = "Australia",
                City = "Sydney"
            },
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 1,
                Month = 1,
                Year = birthYear
            }
        };

        await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);

        // Act
        var result = await userInfoCollectionGAgent.GenerateUserInfoPromptAsync();

        // Assert
        result.ShouldNotBeNull();
        result.ShouldNotBeEmpty();
        result.ShouldContain("User Name: Alice Johnson");
        result.ShouldContain("User Location: Sydney, Australia");
        result.ShouldContain("User Gender: Female");
        result.ShouldContain("User Age: 30");
        result.ShouldContain("User Language: English");
        
        _testOutputHelper.WriteLine($"Generated prompt: {result}");
    }

    [Fact]
    public async Task GenerateUserInfoPromptAsync_Should_Handle_Invalid_Birth_Date_Gracefully()
    {
        // Arrange
        RequestContext.Set("GodGPTLanguage", "English");
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 1,
                FirstName = "Bob",
                LastName = "Wilson"
            },
            LocationInfo = new UserLocationInfoDto
            {
                Country = "United Kingdom",
                City = "London"
            },
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 31,
                Month = 2, // Invalid: February 31st
                Year = 1990
            }
        };

        await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);

        // Act
        var result = await userInfoCollectionGAgent.GenerateUserInfoPromptAsync();

        // Assert
        result.ShouldNotBeNull();
        result.ShouldNotBeEmpty();
        result.ShouldContain("User Name: Bob Wilson");
        result.ShouldContain("User Location: London, United Kingdom");
        result.ShouldContain("User Gender: Male");
        result.ShouldContain("User Age: Unknown"); // Should handle invalid date gracefully
        result.ShouldContain("User Language: English");
        
        _testOutputHelper.WriteLine($"Generated prompt: {result}");
    }

    [Fact]
    public async Task GenerateUserInfoPromptAsync_Should_Handle_Different_Languages()
    {
        // Test CN language
        RequestContext.Set("GodGPTLanguage", "CN");
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 1,
                FirstName = "Li",
                LastName = "Ming"
            }
        };

        await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);

        // Act
        var result = await userInfoCollectionGAgent.GenerateUserInfoPromptAsync();

        // Assert
        result.ShouldNotBeNull();
        result.ShouldNotBeEmpty();
        result.ShouldContain("User Name: Li Ming");
        result.ShouldContain("User Language: Chinese");
        
        _testOutputHelper.WriteLine($"Generated prompt for CN language: {result}");
    }

    #endregion
}
