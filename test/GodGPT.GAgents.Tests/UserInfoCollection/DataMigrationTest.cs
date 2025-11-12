using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Lumen;
using Aevatar.Application.Grains.Lumen.Dtos;
using Aevatar.Application.Grains.UserInfo;
using Aevatar.Application.Grains.UserInfo.Dtos;
using Shouldly;
using Xunit.Abstractions;

namespace Aevatar.Application.Grains.Tests.UserInfoCollection;

/// <summary>
/// Test suite for data migration between FortuneUserProfile and UserInfoCollection
/// </summary>
public class DataMigrationTest : AevatarOrleansTestBase<AevatarGodGPTTestsMoudle>
{
    private readonly ITestOutputHelper _testOutputHelper;

    public DataMigrationTest(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    /// <summary>
    /// Helper method to get FortuneUserProfileGAgent with correct Grain ID mapping
    /// FortuneUserProfile uses StringToGuid(userId.ToString()) as Grain ID
    /// </summary>
    private IFortuneUserProfileGAgent GetFortuneUserProfileGAgent(Guid userId)
    {
        var fortuneGrainId = CommonHelper.StringToGuid(userId.ToString());
        return Cluster.GrainFactory.GetGrain<IFortuneUserProfileGAgent>(fortuneGrainId);
    }

    #region Migration from FortuneUserProfile to UserInfoCollection

    [Fact]
    public async Task GetUserInfoCollectionAsync_Should_Migrate_From_FortuneUserProfile_When_UserInfoCollection_Not_Initialized()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _testOutputHelper.WriteLine($"Testing migration from FortuneUserProfile to UserInfoCollection for UserId: {userId}");

        // First, create and populate FortuneUserProfile
        var fortuneUserProfileGAgent = GetFortuneUserProfileGAgent(userId);
        var updateProfileRequest = new UpdateUserProfileRequest
        {
            UserId = userId.ToString(),
            FullName = "John Doe Smith",
            Gender = GenderEnum.Male,
            BirthDate = new DateOnly(1990, 6, 15),
            BirthTime = new TimeOnly(14, 30),
            BirthCountry = "United States",
            BirthCity = "New York"
        };

        var updateResult = await fortuneUserProfileGAgent.UpdateUserProfileAsync(updateProfileRequest);
        updateResult.Success.ShouldBeTrue();
        _testOutputHelper.WriteLine("FortuneUserProfile created successfully");

        // Act - Query UserInfoCollection (should trigger migration)
        var userInfoCollectionGAgent = Cluster.GrainFactory.GetGrain<IUserInfoCollectionGAgent>(userId);
        var userInfoResult = await userInfoCollectionGAgent.GetUserInfoCollectionAsync();

        // Assert
        userInfoResult.ShouldNotBeNull();
        userInfoResult.IsInitialized.ShouldBeTrue();
        
        // Verify name migration (FullName split into FirstName and LastName)
        userInfoResult.NameInfo.ShouldNotBeNull();
        userInfoResult.NameInfo.FirstName.ShouldBe("John");
        userInfoResult.NameInfo.LastName.ShouldBe("Doe Smith");
        userInfoResult.NameInfo.Gender.ShouldBe(1); // Male -> 1
        
        // Verify birth date migration
        userInfoResult.BirthDateInfo.ShouldNotBeNull();
        userInfoResult.BirthDateInfo.Day.ShouldBe(15);
        userInfoResult.BirthDateInfo.Month.ShouldBe(6);
        userInfoResult.BirthDateInfo.Year.ShouldBe(1990);
        
        // Verify birth time migration
        userInfoResult.BirthTimeInfo.ShouldNotBeNull();
        userInfoResult.BirthTimeInfo.Hour.ShouldBe(14);
        userInfoResult.BirthTimeInfo.Minute.ShouldBe(30);
        
        // Verify location migration
        userInfoResult.LocationInfo.ShouldNotBeNull();
        userInfoResult.LocationInfo.Country.ShouldBe("United States");
        userInfoResult.LocationInfo.City.ShouldBe("New York");

        _testOutputHelper.WriteLine("Migration from FortuneUserProfile to UserInfoCollection completed successfully");
    }

    [Fact]
    public async Task GetUserInfoCollectionAsync_Should_Migrate_Female_Gender_Correctly()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _testOutputHelper.WriteLine($"Testing female gender migration for UserId: {userId}");

        // Create FortuneUserProfile with Female gender
        var fortuneUserProfileGAgent = GetFortuneUserProfileGAgent(userId);
        var updateProfileRequest = new UpdateUserProfileRequest
        {
            UserId = userId.ToString(),
            FullName = "Jane Smith",
            Gender = GenderEnum.Female,
            BirthDate = new DateOnly(1995, 3, 20),
            BirthCountry = "Canada",
            BirthCity = "Toronto"
        };

        await fortuneUserProfileGAgent.UpdateUserProfileAsync(updateProfileRequest);

        // Act
        var userInfoCollectionGAgent = Cluster.GrainFactory.GetGrain<IUserInfoCollectionGAgent>(userId);
        var userInfoResult = await userInfoCollectionGAgent.GetUserInfoCollectionAsync();

        // Assert
        userInfoResult.ShouldNotBeNull();
        userInfoResult.NameInfo.ShouldNotBeNull();
        userInfoResult.NameInfo.Gender.ShouldBe(2); // Female -> 2
        userInfoResult.NameInfo.FirstName.ShouldBe("Jane");
        userInfoResult.NameInfo.LastName.ShouldBe("Smith");

        _testOutputHelper.WriteLine("Female gender migration completed successfully");
    }

    [Fact]
    public async Task GetUserInfoCollectionAsync_Should_Handle_Single_Name_Migration()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _testOutputHelper.WriteLine($"Testing single name migration for UserId: {userId}");

        // Create FortuneUserProfile with single name
        var fortuneUserProfileGAgent = GetFortuneUserProfileGAgent(userId);
        var updateProfileRequest = new UpdateUserProfileRequest
        {
            UserId = userId.ToString(),
            FullName = "Madonna",
            Gender = GenderEnum.Female,
            BirthDate = new DateOnly(1958, 8, 16),
            BirthCountry = "United States",
            BirthCity = "Bay City"
        };

        await fortuneUserProfileGAgent.UpdateUserProfileAsync(updateProfileRequest);

        // Act
        var userInfoCollectionGAgent = Cluster.GrainFactory.GetGrain<IUserInfoCollectionGAgent>(userId);
        var userInfoResult = await userInfoCollectionGAgent.GetUserInfoCollectionAsync();

        // Assert
        userInfoResult.ShouldNotBeNull();
        userInfoResult.NameInfo.ShouldNotBeNull();
        userInfoResult.NameInfo.FirstName.ShouldBe("Madonna");
        userInfoResult.NameInfo.LastName.ShouldBeNull(); // Empty last name

        _testOutputHelper.WriteLine("Single name migration completed successfully");
    }

    [Fact]
    public async Task GetUserInfoCollectionAsync_Should_Not_Migrate_When_Both_Not_Initialized()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _testOutputHelper.WriteLine($"Testing no migration when both not initialized for UserId: {userId}");

        // Act - Query UserInfoCollection without initializing FortuneUserProfile
        var userInfoCollectionGAgent = Cluster.GrainFactory.GetGrain<IUserInfoCollectionGAgent>(userId);
        var userInfoResult = await userInfoCollectionGAgent.GetUserInfoCollectionAsync();

        // Assert - Should return null when neither is initialized
        userInfoResult.ShouldBeNull();

        _testOutputHelper.WriteLine("Correctly returned null when both not initialized");
    }

    #endregion

    #region Migration from UserInfoCollection to FortuneUserProfile

    [Fact]
    public async Task GetUserProfileAsync_Should_Migrate_From_UserInfoCollection_When_FortuneUserProfile_Not_Initialized()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _testOutputHelper.WriteLine($"Testing migration from UserInfoCollection to FortuneUserProfile for UserId: {userId}");

        // First, create and populate UserInfoCollection
        var userInfoCollectionGAgent = Cluster.GrainFactory.GetGrain<IUserInfoCollectionGAgent>(userId);
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 1, // Male
                FirstName = "Robert",
                LastName = "Johnson"
            },
            LocationInfo = new UserLocationInfoDto
            {
                Country = "United Kingdom",
                City = "London"
            },
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 25,
                Month = 12,
                Year = 1985
            },
            BirthTimeInfo = new UserBirthTimeInfoDto
            {
                Hour = 10,
                Minute = 45
            }
        };

        var updateResult = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);
        updateResult.Success.ShouldBeTrue();
        _testOutputHelper.WriteLine("UserInfoCollection created successfully");

        // Act - Query FortuneUserProfile (should trigger migration)
        var fortuneUserProfileGAgent = GetFortuneUserProfileGAgent(userId);
        var profileResult = await fortuneUserProfileGAgent.GetUserProfileAsync(userId);

        // Assert
        profileResult.ShouldNotBeNull();
        profileResult.Success.ShouldBeTrue();
        profileResult.UserProfile.ShouldNotBeNull();
        
        // Verify name migration (FirstName + LastName -> FullName)
        profileResult.UserProfile.FullName.ShouldBe("Robert Johnson");
        profileResult.UserProfile.Gender.ShouldBe(GenderEnum.Male); // 1 -> Male
        
        // Verify birth date migration
        profileResult.UserProfile.BirthDate.Day.ShouldBe(25);
        profileResult.UserProfile.BirthDate.Month.ShouldBe(12);
        profileResult.UserProfile.BirthDate.Year.ShouldBe(1985);
        
        // Verify birth time migration
        profileResult.UserProfile.BirthTime.ShouldNotBeNull();
        profileResult.UserProfile.BirthTime.Value.Hour.ShouldBe(10);
        profileResult.UserProfile.BirthTime.Value.Minute.ShouldBe(45);
        
        // Verify location migration
        profileResult.UserProfile.BirthCountry.ShouldBe("United Kingdom");
        profileResult.UserProfile.BirthCity.ShouldBe("London");

        _testOutputHelper.WriteLine("Migration from UserInfoCollection to FortuneUserProfile completed successfully");
    }

    [Fact]
    public async Task GetUserProfileAsync_Should_Migrate_Female_Gender_Correctly()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _testOutputHelper.WriteLine($"Testing female gender migration for UserId: {userId}");

        // Create UserInfoCollection with Female gender
        var userInfoCollectionGAgent = Cluster.GrainFactory.GetGrain<IUserInfoCollectionGAgent>(userId);
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 2, // Female
                FirstName = "Emily",
                LastName = "Watson"
            },
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 10,
                Month = 5,
                Year = 1992
            }
        };

        await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);

        // Act
        var fortuneUserProfileGAgent = GetFortuneUserProfileGAgent(userId);
        var profileResult = await fortuneUserProfileGAgent.GetUserProfileAsync(userId);

        // Assert
        profileResult.ShouldNotBeNull();
        profileResult.Success.ShouldBeTrue();
        profileResult.UserProfile.FullName.ShouldBe("Emily Watson");
        profileResult.UserProfile.Gender.ShouldBe(GenderEnum.Female); // 2 -> Female

        _testOutputHelper.WriteLine("Female gender migration completed successfully");
    }

    [Fact]
    public async Task GetUserProfileAsync_Should_Concatenate_Names_Correctly()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _testOutputHelper.WriteLine($"Testing name concatenation for UserId: {userId}");

        // Create UserInfoCollection with FirstName and LastName
        var userInfoCollectionGAgent = Cluster.GrainFactory.GetGrain<IUserInfoCollectionGAgent>(userId);
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 1,
                FirstName = "William",
                LastName = "Shakespeare"
            },
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 23,
                Month = 4,
                Year = 1801
            }
        };

        var updateUserInfoCollectionResponseDto = await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);

        // Act
        var fortuneUserProfileGAgent = GetFortuneUserProfileGAgent(userId);
        var profileResult = await fortuneUserProfileGAgent.GetUserProfileAsync(userId);

        // Assert
        profileResult.ShouldNotBeNull();
        profileResult.Success.ShouldBeTrue();
        profileResult.UserProfile.FullName.ShouldBe("William Shakespeare");

        _testOutputHelper.WriteLine("Name concatenation completed successfully");
    }

    [Fact]
    public async Task GetUserProfileAsync_Should_Not_Migrate_When_Both_Not_Initialized()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _testOutputHelper.WriteLine($"Testing no migration when both not initialized for UserId: {userId}");

        // Act - Query FortuneUserProfile without initializing UserInfoCollection
        var fortuneUserProfileGAgent = GetFortuneUserProfileGAgent(userId);
        var profileResult = await fortuneUserProfileGAgent.GetUserProfileAsync(userId);

        // Assert - Should return failure when not initialized
        profileResult.ShouldNotBeNull();
        profileResult.Success.ShouldBeFalse();
        profileResult.Message.ShouldBe("User profile not found");

        _testOutputHelper.WriteLine("Correctly returned failure when both not initialized");
    }

    #endregion

    #region Circular Dependency Prevention Tests

    [Fact]
    public async Task GetRawStateAsync_Should_Not_Trigger_Migration_From_FortuneUserProfile()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _testOutputHelper.WriteLine($"Testing GetRawStateAsync does not trigger migration for UserId: {userId}");

        // Create FortuneUserProfile
        var fortuneUserProfileGAgent = GetFortuneUserProfileGAgent(userId);
        var updateProfileRequest = new UpdateUserProfileRequest
        {
            UserId = userId.ToString(),
            FullName = "Test User",
            Gender = GenderEnum.Male,
            BirthDate = new DateOnly(1990, 1, 1),
            BirthCountry = "Test Country",
            BirthCity = "Test City"
        };

        await fortuneUserProfileGAgent.UpdateUserProfileAsync(updateProfileRequest);

        // Act - Call GetRawStateAsync on UserInfoCollection (should not trigger migration)
        var userInfoCollectionGAgent = Cluster.GrainFactory.GetGrain<IUserInfoCollectionGAgent>(userId);
        var rawState = await userInfoCollectionGAgent.GetRawStateAsync();

        // Assert - Should return null because UserInfoCollection is not initialized
        rawState.ShouldBeNull();

        _testOutputHelper.WriteLine("GetRawStateAsync correctly returned null without triggering migration");
    }

    [Fact]
    public async Task GetRawStateAsync_Should_Not_Trigger_Migration_From_UserInfoCollection()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _testOutputHelper.WriteLine($"Testing GetRawStateAsync does not trigger migration for UserId: {userId}");

        // Create UserInfoCollection
        var userInfoCollectionGAgent = Cluster.GrainFactory.GetGrain<IUserInfoCollectionGAgent>(userId);
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 1,
                FirstName = "Test",
                LastName = "User"
            },
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 1,
                Month = 1,
                Year = 1990
            }
        };

        await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);

        // Act - Call GetRawStateAsync on FortuneUserProfile (should not trigger migration)
        var fortuneUserProfileGAgent = GetFortuneUserProfileGAgent(userId);
        var rawState = await fortuneUserProfileGAgent.GetRawStateAsync();

        // Assert - Should return null because FortuneUserProfile is not initialized
        rawState.ShouldBeNull();

        _testOutputHelper.WriteLine("GetRawStateAsync correctly returned null without triggering migration");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task Migration_Should_Handle_Partial_Data_From_FortuneUserProfile()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _testOutputHelper.WriteLine($"Testing partial data migration from FortuneUserProfile for UserId: {userId}");

        // Create FortuneUserProfile with minimal data (no birth time)
        var fortuneUserProfileGAgent = GetFortuneUserProfileGAgent(userId);
        var updateProfileRequest = new UpdateUserProfileRequest
        {
            UserId = userId.ToString(),
            FullName = "Minimal Data",
            Gender = GenderEnum.Male,
            BirthDate = new DateOnly(1990, 1, 1),
            // No BirthTime, BirthCountry, BirthCity
        };

        await fortuneUserProfileGAgent.UpdateUserProfileAsync(updateProfileRequest);

        // Act
        var userInfoCollectionGAgent = Cluster.GrainFactory.GetGrain<IUserInfoCollectionGAgent>(userId);
        var userInfoResult = await userInfoCollectionGAgent.GetUserInfoCollectionAsync();

        // Assert
        userInfoResult.ShouldNotBeNull();
        userInfoResult.IsInitialized.ShouldBeTrue();
        userInfoResult.NameInfo.ShouldNotBeNull();
        userInfoResult.BirthDateInfo.ShouldNotBeNull();
        userInfoResult.BirthTimeInfo.ShouldBeNull(); // Should be null
        userInfoResult.LocationInfo.ShouldBeNull(); // Should be null

        _testOutputHelper.WriteLine("Partial data migration completed successfully");
    }

    [Fact]
    public async Task Migration_Should_Handle_Partial_Data_From_UserInfoCollection()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _testOutputHelper.WriteLine($"Testing partial data migration from UserInfoCollection for UserId: {userId}");

        // Create UserInfoCollection with minimal data (no birth time, no location)
        var userInfoCollectionGAgent = Cluster.GrainFactory.GetGrain<IUserInfoCollectionGAgent>(userId);
        var updateDto = new UpdateUserInfoCollectionDto
        {
            NameInfo = new UserNameInfoDto
            {
                Gender = 1,
                FirstName = "Minimal",
                LastName = "Data"
            },
            BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = 1,
                Month = 1,
                Year = 1990
            }
            // No BirthTimeInfo, no LocationInfo
        };

        await userInfoCollectionGAgent.UpdateUserInfoCollectionAsync(updateDto);

        // Act
        var fortuneUserProfileGAgent = GetFortuneUserProfileGAgent(userId);
        var profileResult = await fortuneUserProfileGAgent.GetUserProfileAsync(userId);

        // Assert
        profileResult.ShouldNotBeNull();
        profileResult.Success.ShouldBeTrue();
        profileResult.UserProfile.FullName.ShouldBe("Minimal Data");
        profileResult.UserProfile.BirthTime.ShouldBeNull(); // Should be null
        profileResult.UserProfile.BirthCountry.ShouldBeNull(); // Should be null
        profileResult.UserProfile.BirthCity.ShouldBeNull(); // Should be null

        _testOutputHelper.WriteLine("Partial data migration completed successfully");
    }

    #endregion
}

