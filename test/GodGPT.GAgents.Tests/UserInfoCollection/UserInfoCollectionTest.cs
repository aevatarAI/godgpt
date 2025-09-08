using Aevatar.Application.Grains.UserInfo;
using Aevatar.Application.Grains.UserInfo.Dtos;
using Aevatar.Application.Grains.Tests;
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
        // Arrange
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = "Male",
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
            SeekingInterests = new List<string> { "陪伴", "自我发现", "每日占星" },
            SourceChannels = new List<string> { "App Store", "Social media" }
        };

        // Act
        var result = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Message.ShouldBe("User info collection updated successfully");
        result.Data.ShouldNotBeNull();
        result.Data.NameInfo.ShouldNotBeNull();
        result.Data.NameInfo.Gender.ShouldBe("Male");
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
        result.Data.SeekingInterests.Count.ShouldBe(3);
        result.Data.SourceChannels.ShouldNotBeNull();
        result.Data.SourceChannels.Count.ShouldBe(2);
        result.Data.IsCompleted.ShouldBeTrue();

        _testOutputHelper.WriteLine("Full data collection test passed successfully");
    }

    [Fact]
    public async Task GetUserInfoCollectionAsync_Should_Return_Complete_Data_After_Full_Update()
    {
        // Arrange
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = "Female",
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
            SeekingInterests = new List<string> { "精神成长", "爱情与关系" },
            SourceChannels = new List<string> { "Friend referral", "Event/conference" }
        };

        // Act
        await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);
        var result = await userInfoCollectionGAgent.GetUserInfoCollectionAsync();

        // Assert
        result.ShouldNotBeNull();
        result.NameInfo.ShouldNotBeNull();
        result.NameInfo.Gender.ShouldBe("Female");
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
        result.SeekingInterests.Count.ShouldBe(2);
        result.SourceChannels.Count.ShouldBe(2);
        result.IsCompleted.ShouldBeTrue();

        _testOutputHelper.WriteLine("Get complete data test passed successfully");
    }

    [Fact]
    public async Task GetUserInfoDisplayAsync_Should_Format_Data_Correctly_For_Display()
    {
        // Arrange
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = "Male",
                FirstName = "Alex",
                LastName = "Johnson"
            },
            LocationInfo = new UserLocationInfoDto
            {
                Country = "United Kingdom",
                City = "London"
            },
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 3,
                Month = 8,
                Year = 1992
            },
            BirthTimeInfo = new UserBirthTimeInfoDto
            {
                Hour = 16,
                Minute = 45
            },
            SeekingInterests = new List<string> { "职业指导", "每日占星" },
            SourceChannels = new List<string> { "Search engine", "Advertisement" }
        };

        // Act
        await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);
        var result = await userInfoCollectionGAgent.GetUserInfoDisplayAsync();

        // Assert
        result.ShouldNotBeNull();
        result.FirstName.ShouldBe("Alex");
        result.LastName.ShouldBe("Johnson");
        result.Gender.ShouldBe("Male");
        result.Day.ShouldBe(3);
        result.Month.ShouldBe(8);
        result.Year.ShouldBe(1992);
        result.Hour.ShouldBe(16);
        result.Minute.ShouldBe(45);
        result.Country.ShouldBe("United Kingdom");
        result.City.ShouldBe("London");
        result.SeekingInterests.Count.ShouldBe(2);
        result.SourceChannels.Count.ShouldBe(2);

        _testOutputHelper.WriteLine("Display formatting test passed successfully");
    }

    #endregion

    #region Partial Data Collection Tests

    [Fact]
    public async Task UpdateUserInfoCollectionAsync_Should_Save_Partial_Data_Successfully()
    {
        // Arrange
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = "Female",
                FirstName = "Sarah",
                LastName = "Wilson"
            },
            LocationInfo = new UserLocationInfoDto
            {
                Country = "Australia",
                City = "Sydney"
            },
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 10,
                Month = 3,
                Year = 1988
            }
            // Note: BirthTimeInfo, SeekingInterests, and SourceChannels are not provided
        };

        // Act
        var result = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.NameInfo.ShouldNotBeNull();
        result.Data.NameInfo.Gender.ShouldBe("Female");
        result.Data.NameInfo.FirstName.ShouldBe("Sarah");
        result.Data.NameInfo.LastName.ShouldBe("Wilson");
        result.Data.LocationInfo.ShouldNotBeNull();
        result.Data.LocationInfo.Country.ShouldBe("Australia");
        result.Data.LocationInfo.City.ShouldBe("Sydney");
        result.Data.BirthDateInfo.ShouldNotBeNull();
        result.Data.BirthDateInfo.Day.ShouldBe(10);
        result.Data.BirthDateInfo.Month.ShouldBe(3);
        result.Data.BirthDateInfo.Year.ShouldBe(1988);
        result.Data.BirthTimeInfo.ShouldBeNull(); // Not provided
        result.Data.SeekingInterests.ShouldNotBeNull();
        result.Data.SeekingInterests.Count.ShouldBe(0); // Empty list
        result.Data.SourceChannels.ShouldNotBeNull();
        result.Data.SourceChannels.Count.ShouldBe(0); // Empty list
        result.Data.IsCompleted.ShouldBeFalse(); // Not all required fields provided

        _testOutputHelper.WriteLine("Partial data collection test passed successfully");
    }

    [Fact]
    public async Task UpdateUserInfoCollectionAsync_Should_Handle_Birth_Time_Without_Minute()
    {
        // Arrange
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = "Male",
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
                Day = 25,
                Month = 11,
                Year = 1995
            },
            BirthTimeInfo = new UserBirthTimeInfoDto
            {
                Hour = 20
                // Minute is not provided
            },
            SeekingInterests = new List<string> { "陪伴" },
            SourceChannels = new List<string> { "Other" }
        };

        // Act
        var result = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Data.BirthTimeInfo.ShouldNotBeNull();
        result.Data.BirthTimeInfo.Hour.ShouldBe(20);
        result.Data.BirthTimeInfo.Minute.ShouldBeNull();

        // Test display formatting - should show N/A when only hour is provided
        var displayResult = await userInfoCollectionGAgent.GetUserInfoDisplayAsync();
        displayResult.ShouldNotBeNull();
        displayResult.Hour.ShouldBe(20);
        displayResult.Minute.ShouldBeNull();

        _testOutputHelper.WriteLine("Birth time without minute test passed successfully");
    }

    [Fact]
    public async Task UpdateUserInfoCollectionAsync_Should_Handle_Sequential_Updates()
    {
        // Arrange
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();

        // First update: Name and Location only
        var firstUpdate = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = "Female",
                FirstName = "Emma",
                LastName = "Davis"
            },
            LocationInfo = new UserLocationInfoDto
            {
                Country = "France",
                City = "Paris"
            }
        };

        // Second update: Birth date and time
        var secondUpdate = new UpdateUserInfoCollectionDto
        {
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 7,
                Month = 4,
                Year = 1993
            },
            BirthTimeInfo = new UserBirthTimeInfoDto
            {
                Hour = 11,
                Minute = 20
            }
        };

        // Third update: Interests and sources
        var thirdUpdate = new UpdateUserInfoCollectionDto
        {
            SeekingInterests = new List<string> { "自我发现", "精神成长", "爱情与关系" },
            SourceChannels = new List<string> { "App Store", "Social media", "Friend referral" }
        };

        // Act
        var firstResult = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(firstUpdate);
        var secondResult = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(secondUpdate);
        var thirdResult = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(thirdUpdate);
        var finalResult = await userInfoCollectionGAgent.GetUserInfoCollectionAsync();

        // Assert
        firstResult.Success.ShouldBeTrue();
        secondResult.Success.ShouldBeTrue();
        thirdResult.Success.ShouldBeTrue();
        
        finalResult.ShouldNotBeNull();
        finalResult.NameInfo.ShouldNotBeNull();
        finalResult.NameInfo.FirstName.ShouldBe("Emma");
        finalResult.NameInfo.LastName.ShouldBe("Davis");
        finalResult.LocationInfo.ShouldNotBeNull();
        finalResult.LocationInfo.Country.ShouldBe("France");
        finalResult.LocationInfo.City.ShouldBe("Paris");
        finalResult.BirthDateInfo.ShouldNotBeNull();
        finalResult.BirthDateInfo.Day.ShouldBe(7);
        finalResult.BirthDateInfo.Month.ShouldBe(4);
        finalResult.BirthDateInfo.Year.ShouldBe(1993);
        finalResult.BirthTimeInfo.ShouldNotBeNull();
        finalResult.BirthTimeInfo.Hour.ShouldBe(11);
        finalResult.BirthTimeInfo.Minute.ShouldBe(20);
        finalResult.SeekingInterests.Count.ShouldBe(3);
        finalResult.SourceChannels.Count.ShouldBe(3);
        finalResult.IsCompleted.ShouldBeTrue();

        _testOutputHelper.WriteLine("Sequential updates test passed successfully");
    }

    #endregion

    #region Clear Data Tests

    [Fact]
    public async Task ClearAllAsync_Should_Reset_All_Data_Successfully()
    {
        // Arrange
        var userInfoCollectionGAgent = await CreateTestUserInfoCollectionGAgentAsync();
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = "Male",
                FirstName = "Test",
                LastName = "User"
            },
            LocationInfo = new UserLocationInfoDto
            {
                Country = "Test Country",
                City = "Test City"
            },
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 1,
                Month = 1,
                Year = 2000
            },
            BirthTimeInfo = new UserBirthTimeInfoDto
            {
                Hour = 12,
                Minute = 0
            },
            SeekingInterests = new List<string> { "陪伴" },
            SourceChannels = new List<string> { "App Store" }
        };

        // Act
        await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);
        var beforeClear = await userInfoCollectionGAgent.GetUserInfoCollectionAsync();
        beforeClear.ShouldNotBeNull();
        beforeClear.IsInitialized.ShouldBeTrue();

        await userInfoCollectionGAgent.ClearAllAsync();
        var afterClear = await userInfoCollectionGAgent.GetUserInfoCollectionAsync();

        // Assert
        afterClear.ShouldBeNull(); // Should return null when not initialized

        _testOutputHelper.WriteLine("Clear all data test passed successfully");
    }

    #endregion
}
