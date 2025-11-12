using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Lumen;
using Aevatar.Application.Grains.Lumen.Dtos;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Aevatar.Application.Grains.UserInfo.Dtos;
using Aevatar.Application.Grains.UserInfo.Enums;
using Aevatar.Application.Grains.UserInfo.SEvents;
using Aevatar.Application.Grains.UserInfo.Helpers;
using Microsoft.Extensions.Logging;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.UserInfo;

public interface IUserInfoCollectionGAgent : IGAgent
{
    /// <summary>
    /// Update user info collection with provided data
    /// </summary>
    Task<UserInfoCollectionResponseDto> UpdateUserInfoCollectionAsync(UpdateUserInfoCollectionDto updateDto);
    
    /// <summary>
    /// Get current user info collection data
    /// </summary>
    Task<UserInfoCollectionDto> GetUserInfoCollectionAsync();
    
    /// <summary>
    /// Get raw state data directly without any migration logic - used for migration checks to prevent circular dependency
    /// </summary>
    [ReadOnly]
    Task<UserInfoCollectionDto?> GetRawStateAsync();
    
    /// <summary>
    /// Get user info display data for confirmation
    /// </summary>
    [ReadOnly]
    Task<UserInfoDisplayDto> GetUserInfoDisplayAsync();
    
    /// <summary>
    /// Clear all user info collection data
    /// </summary>
    Task ClearAllAsync();
    
    /// <summary>
    /// Get user info options (seeking interests and source channels) based on language
    /// </summary>
    Task<UserInfoOptionsResponseDto> GetUserInfoOptionsAsync();
    
    /// <summary>
    /// Generate user info prompt template with user data
    /// </summary>
    Task<Tuple<string, string>> GenerateUserInfoPromptAsync(DateTime? userLocalTime = null);
}

[GAgent(nameof(UserInfoCollectionGAgent))]
[Reentrant]
public class UserInfoCollectionGAgent: GAgentBase<UserInfoCollectionGAgentState, UserInfoCollectionLogEvent>, IUserInfoCollectionGAgent
{
    private readonly ILogger<UserInfoCollectionGAgent> _logger;
    
    public UserInfoCollectionGAgent(ILogger<UserInfoCollectionGAgent> logger)
    {
        _logger = logger;
        _logger.LogDebug("[UserInfoCollectionGAgent] Activating agent for user {UserId}", this.GetPrimaryKey().ToString());
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"UserInfoCollectionGAgent for user {this.GetPrimaryKey().ToString()}, " +
                              $"IsInitialized: {State.IsInitialized}");
    }

    public async Task<UserInfoCollectionResponseDto> UpdateUserInfoCollectionAsync(UpdateUserInfoCollectionDto updateDto)
    {
        var userId = this.GetPrimaryKey();
        _logger.LogInformation("[UserInfoCollectionGAgent][UpdateUserInfoCollectionAsync] Updating user info collection userId:{userId}",userId);
        var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();

        // Validate required fields if they are being updated
        if (updateDto.NameInfo != null)
        {
            if ((updateDto.NameInfo.Gender != 1 &&  updateDto.NameInfo.Gender != 2) || 
                string.IsNullOrWhiteSpace(updateDto.NameInfo.FirstName) || 
                string.IsNullOrWhiteSpace(updateDto.NameInfo.LastName))
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = "Gender, FirstName, and LastName are required",
                    Data = ConvertStateToDto()
                };
            }
        }
        
        if (updateDto.LocationInfo != null)
        {
            if (string.IsNullOrWhiteSpace(updateDto.LocationInfo.Country) || 
                string.IsNullOrWhiteSpace(updateDto.LocationInfo.City))
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = "Country and City are required",
                    Data = ConvertStateToDto()
                };
            }
        }
        
        if (updateDto.BirthDateInfo != null)
        {
            if (!updateDto.BirthDateInfo.Day.HasValue || !updateDto.BirthDateInfo.Month.HasValue || !updateDto.BirthDateInfo.Year.HasValue)
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = "Day, Month, and Year are required",
                    Data = ConvertStateToDto()
                };
            }
            
            if (updateDto.BirthDateInfo.Day.Value <= 0 || updateDto.BirthDateInfo.Month.Value <= 0 || updateDto.BirthDateInfo.Year.Value <= 0)
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = "Valid Day, Month, and Year are required",
                    Data = ConvertStateToDto()
                };
            }
            
            // Validate date range
            if (updateDto.BirthDateInfo.Day.Value > 31 || updateDto.BirthDateInfo.Month.Value > 12 || 
                updateDto.BirthDateInfo.Year.Value < 1800 || updateDto.BirthDateInfo.Year.Value > DateTime.Now.Year)
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = "Invalid birthDate values",
                    Data = ConvertStateToDto()
                };
            }
        }
        
        if (updateDto.BirthTimeInfo != null)
        {
            if (updateDto.BirthTimeInfo.Hour.HasValue && (updateDto.BirthTimeInfo.Hour < 0 || updateDto.BirthTimeInfo.Hour > 23))
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = "Hour must be between 0 and 23",
                    Data = ConvertStateToDto()
                };
            }
            
            if (updateDto.BirthTimeInfo.Minute.HasValue && (updateDto.BirthTimeInfo.Minute < 0 || updateDto.BirthTimeInfo.Minute > 59))
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = "Minute must be between 0 and 59",
                    Data = ConvertStateToDto()
                };
            }
        }
        
        
        // Validate seeking interests - reject empty lists but allow null/not provided
        if (updateDto.SeekingInterests != null && updateDto.SeekingInterests.Count == 0)
        {
            return new UserInfoCollectionResponseDto
            {
                Success = false,
                Message = "At least one seeking interest is required",
                Data = ConvertStateToDto()
            };
        }
        
        // Validate source channels - reject empty lists but allow null/not provided
        if (updateDto.SourceChannels != null && updateDto.SourceChannels.Count == 0)
        {
            return new UserInfoCollectionResponseDto
            {
                Success = false,
                Message = "At least one source channel is required",
                Data = ConvertStateToDto()
            };
        }

        // Convert enums to localized text and codes
        // Validate enum values
        if (updateDto.SeekingInterests != null && updateDto.SeekingInterests.Count > 0)
        {
            var invalidSeekingInterests = updateDto.SeekingInterests.Where(x => !Enum.IsDefined(typeof(SeekingInterestEnum), x)).ToList();
            if (invalidSeekingInterests.Count > 0)
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = "Invalid seeking interests",
                    Data = ConvertStateToDto()
                };
            }
        }
        
        if (updateDto.SourceChannels != null && updateDto.SourceChannels.Count > 0)
        {
            var invalidSourceChannels = updateDto.SourceChannels.Where(x => !Enum.IsDefined(typeof(SourceChannelEnum), x)).ToList();
            if (invalidSourceChannels.Count > 0)
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = "Invalid source channels",
                    Data = ConvertStateToDto()
                };
            }
        }

        List<int> seekingInterestsCode = null;
        List<string> seekingInterests = null;
        
        if (updateDto.SeekingInterests != null && updateDto.SeekingInterests.Count > 0)
        {
            seekingInterestsCode = updateDto.SeekingInterests.Select(x => (int)x).Distinct().OrderBy(x => x).ToList();
            seekingInterests = updateDto.SeekingInterests
                .Select(interest => UserInfoLocalizationHelper.GetSeekingInterestText(interest, language))
                .ToList();
        }
        
        List<int> sourceChannelsCode = null;
        List<string> sourceChannels = null;
        
        if (updateDto.SourceChannels != null && updateDto.SourceChannels.Count > 0)
        {
            sourceChannelsCode = updateDto.SourceChannels.Select(x => (int)x).Distinct().OrderBy(x => x).ToList();
            sourceChannels = updateDto.SourceChannels
                .Select(channel => UserInfoLocalizationHelper.GetSourceChannelText(channel, language).Item1)
                .ToList();
        }
        
        var now = DateTime.UtcNow;
        
        RaiseEvent(new UpdateUserInfoCollectionLogEvent
        {
            UserId = userId,
            Gender = updateDto.NameInfo?.Gender,
            FirstName = updateDto.NameInfo?.FirstName,
            LastName = updateDto.NameInfo?.LastName,
            Country = updateDto.LocationInfo?.Country,
            City = updateDto.LocationInfo?.City,
            Day = updateDto.BirthDateInfo?.Day,
            Month = updateDto.BirthDateInfo?.Month,
            Year = updateDto.BirthDateInfo?.Year,
            Hour = updateDto.BirthTimeInfo?.Hour,
            Minute = updateDto.BirthTimeInfo?.Minute,
            SeekingInterests = seekingInterests,
            SourceChannels = sourceChannels,
            SeekingInterestsCode = seekingInterestsCode,
            SourceChannelsCode = sourceChannelsCode,
            UpdatedAt = now
        });
        
        await ConfirmEvents();
        
        _logger.LogInformation("[UserInfoCollectionGAgent][UpdateUserInfoCollectionAsync] Successfully updated user info collection");
        
        return new UserInfoCollectionResponseDto
        {
            Success = true,
            Message = "User info collection updated successfully",
            Data = ConvertStateToDto()
        };
    }

    public async Task<UserInfoCollectionDto> GetUserInfoCollectionAsync()
    {
        _logger.LogDebug("[UserInfoCollectionGAgent][GetUserInfoCollectionAsync] Getting user {UserId} info collection", this.GetPrimaryKey().ToString());
        
        if (!State.IsInitialized)
        {
            _logger.LogWarning("[UserInfoCollectionGAgent][GetUserInfoCollectionAsync] User info collection not initialized");
            // Use GetRawStateAsync to prevent circular dependency
            await TryToMigrateDataFromFortuneUserProfileAsync();
        }

        if (!State.IsInitialized)
        {
            return null;
        }

        var userInfoCollectionDto = ConvertStateToDto();
        _logger.LogDebug("[UserInfoCollectionGAgent][GetUserInfoCollectionAsync] result: userId {UserId}, initialized {Initialized}", 
            this.GetPrimaryKey().ToString(), userInfoCollectionDto.IsInitialized);
        
        return userInfoCollectionDto;
    }

    private async Task TryToMigrateDataFromFortuneUserProfileAsync()
    {
        var userGrainId = CommonHelper.StringToGuid(this.GetPrimaryKey().ToString());
        var fortuneUserProfileGAgent = GrainFactory.GetGrain<IFortuneUserProfileGAgent>(userGrainId);
        var profile = await fortuneUserProfileGAgent.GetRawStateAsync();
        if (profile != null && !profile.FullName.IsNullOrWhiteSpace())
        {
            _logger.LogInformation(
                "[UserInfoCollectionGAgent][GetUserInfoCollectionAsync] Migrating data from FortuneUserProfile to UserInfoCollection, userId {UserId}",
                this.GetPrimaryKey().ToString());

            var now = DateTime.UtcNow;

            // Prepare migration event - bypass validation for data migration scenario
            string firstName = null;
            string lastName = null;
            int? gender = null;

            // Migrate FullName (split into FirstName and LastName)
            var nameParts = profile.FullName.Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            firstName = nameParts.Length > 0 ? nameParts[0] : profile.FullName.Trim();
            lastName = nameParts.Length > 1 ? nameParts[1] : string.Empty;

            // Map Gender: GenderEnum.Male(0)->1, GenderEnum.Female(1)->2, skip Other
            if (profile.Gender == GenderEnum.Male)
            {
                gender = 1;
            }
            else if (profile.Gender == GenderEnum.Female)
            {
                gender = 2;
            }

            // Prepare birth date components
            int? day = null;
            int? month = null;
            int? year = null;
            if (profile.BirthDate != default)
            {
                day = profile.BirthDate.Day;
                month = profile.BirthDate.Month;
                year = profile.BirthDate.Year;
            }

            // Prepare birth time components (optional)
            int? hour = null;
            int? minute = null;
            if (profile.BirthTime.HasValue)
            {
                hour = profile.BirthTime.Value.Hour;
                minute = profile.BirthTime.Value.Minute;
            }

            // Prepare location - allow partial data (country without city or vice versa)
            string country = !string.IsNullOrWhiteSpace(profile.BirthCountry) ? profile.BirthCountry : null;
            string city = !string.IsNullOrWhiteSpace(profile.BirthCity) ? profile.BirthCity : null;

            // Check if we have any data to migrate
            bool hasDataToMigrate = !string.IsNullOrWhiteSpace(firstName) ||
                                    day.HasValue ||
                                    !string.IsNullOrWhiteSpace(country) ||
                                    !string.IsNullOrWhiteSpace(city);

            if (hasDataToMigrate)
            {
                _logger.LogInformation(
                    "[UserInfoCollectionGAgent][GetUserInfoCollectionAsync] Saving migrated data for userId {UserId}, " +
                    "firstName: {FirstName}, gender: {Gender}, birthDate: {Year}-{Month}-{Day}, country: {Country}, city: {City}",
                    this.GetPrimaryKey().ToString(), firstName, gender, year, month, day, country, city);

                // Directly raise event to bypass validation - migration is internal data sync
                RaiseEvent(new UpdateUserInfoCollectionLogEvent
                {
                    UserId = this.GetPrimaryKey(),
                    Gender = gender,
                    FirstName = firstName,
                    LastName = lastName,
                    Country = country,
                    City = city,
                    Day = day,
                    Month = month,
                    Year = year,
                    Hour = hour,
                    Minute = minute,
                    SeekingInterests = null,
                    SourceChannels = null,
                    SeekingInterestsCode = null,
                    SourceChannelsCode = null,
                    UpdatedAt = now
                });

                await ConfirmEvents();

                _logger.LogInformation(
                    "[UserInfoCollectionGAgent][GetUserInfoCollectionAsync] Successfully migrated data from FortuneUserProfile {UserId}",
                    this.GetPrimaryKey().ToString());
            }
            else
            {
                _logger.LogDebug(
                    "[UserInfoCollectionGAgent][GetUserInfoCollectionAsync] Fortune profile exists but no data available for migration");
            }
        }
    }

    /// <summary>
    /// Get raw state data directly without any migration logic - used for migration checks to prevent circular dependency
    /// </summary>
    public Task<UserInfoCollectionDto?> GetRawStateAsync()
    {
        try
        {
            _logger.LogDebug("[UserInfoCollectionGAgent][GetRawStateAsync] Getting raw state for user {UserId}", 
                this.GetPrimaryKey().ToString());

            // If not initialized, return null immediately without any migration logic
            if (!State.IsInitialized)
            {
                return Task.FromResult<UserInfoCollectionDto?>(null);
            }

            // Return raw state data without any processing or migration
            var dto = ConvertStateToDto();
            return Task.FromResult<UserInfoCollectionDto?>(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UserInfoCollectionGAgent][GetRawStateAsync] Error getting raw state");
            return Task.FromResult<UserInfoCollectionDto?>(null);
        }
    }

    public async Task<UserInfoDisplayDto> GetUserInfoDisplayAsync()
    {
        _logger.LogInformation("[UserInfoCollectionGAgent][GetUserInfoDisplayAsync] Getting user info display data");
        
        if (!State.IsInitialized)
        {
            _logger.LogWarning("[UserInfoCollectionGAgent][GetUserInfoDisplayAsync] User info collection not initialized");
            return null;
        }
        
        return new UserInfoDisplayDto
        {
            FirstName = State.FirstName,
            LastName = State.LastName,
            Gender = State.Gender,
            Day = State.Day,
            Month = State.Month,
            Year = State.Year,
            Hour = State.Hour,
            Minute = State.Minute,
            Country = State.Country,
            City = State.City,
            SeekingInterests = State.SeekingInterests ?? new List<string>(),
            SourceChannels = State.SourceChannels ?? new List<string>()
        };
    }

    public async Task ClearAllAsync()
    {
        _logger.LogInformation("[UserInfoCollectionGAgent][ClearAllAsync] Clearing all user info collection data");
        
        RaiseEvent(new ClearUserInfoCollectionLogEvent());
        await ConfirmEvents();
        
        _logger.LogInformation("[UserInfoCollectionGAgent][ClearAllAsync] Successfully cleared all user info collection data");
    }
    
    public async Task<UserInfoOptionsResponseDto> GetUserInfoOptionsAsync()
    {
        _logger.LogDebug("[UserInfoCollectionGAgent][GetUserInfoOptionsAsync] Getting user info options");
        var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();

        var seekingInterestOptions = UserInfoLocalizationHelper.GetSeekingInterestEnumOptions(language);
        var sourceChannelOptions = UserInfoLocalizationHelper.GetSourceChannelEnumOptions(language);
        
        return new UserInfoOptionsResponseDto
        {
            Success = true,
            Message = "Options retrieved successfully",
            SeekingInterestOptions = seekingInterestOptions,
            SourceChannelOptions = sourceChannelOptions
        };
    }

    public async Task<Tuple<string, string>> GenerateUserInfoPromptAsync(DateTime? userLocalTime = null)
    {
        _logger.LogInformation("[UserInfoCollectionGAgent][GenerateUserInfoPromptAsync] Generating user info prompt");

        if (!State.IsInitialized)
        {
            _logger.LogWarning("[UserInfoCollectionGAgent][GenerateUserInfoPromptAsync] User info collection not initialized");
            
            var userGrainId = CommonHelper.StringToGuid(this.GetPrimaryKey().ToString());
            var fortuneUserProfileGAgent = GrainFactory.GetGrain<IFortuneUserProfileGAgent>(userGrainId);
            var profileResult = await fortuneUserProfileGAgent.GetUserProfileAsync(userGrainId);

            if (!profileResult.Success || profileResult.UserProfile == null || profileResult.UserProfile.UserId.IsNullOrWhiteSpace())
            {
                _logger.LogDebug("[UserInfoCollectionGAgent][GenerateUserInfoPromptAsync] query fortune user profile, userId {UserId} not exist.", 
                    this.GetPrimaryKey().ToString());
                return new Tuple<string, string>(string.Empty, string.Empty);
            }

            return await GenerateUserInfoPromptWithFortuneUserProfileAsync(userLocalTime, profileResult.UserProfile);
        }

        return await GenerateUserInfoPromptWithUserProfileAsync(userLocalTime);
    }

    private async Task<Tuple<string, string>> GenerateUserInfoPromptWithUserProfileAsync(DateTime? userLocalTime)
    {
        var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
        var currentTime = userLocalTime ?? DateTime.UtcNow;

        // Generate full name
        var fullName = $"{State.FirstName} {State.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(fullName))
        {
            fullName = "Unknown";
        }

        // Generate location
        var location = $"{State.City}, {State.Country}".Trim(' ', ',');
        if (string.IsNullOrWhiteSpace(location))
        {
            location = "Unknown";
        }

        // Generate gender text
        var genderText = State.Gender switch
        {
            1 => "Male",
            2 => "Female",
            _ => "Unknown"
        };

        // Calculate age from birth date
        var age = "Unknown";
        if (State.Year > 0 && State.Month > 0 && State.Day > 0)
        {
            try
            {
                var birthDate = new DateTime(State.Year, State.Month, State.Day);
                var calculatedAge = currentTime.Year - birthDate.Year;
                if (currentTime < birthDate.AddYears(calculatedAge))
                {
                    calculatedAge--;
                }

                age = calculatedAge.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[UserInfoCollectionGAgent][GenerateUserInfoPromptAsync] Invalid birth date: {Year}-{Month}-{Day}",
                    State.Year, State.Month, State.Day);
                age = "Unknown";
            }
        }

        // Generate language text
        var languageText = language switch
        {
            GodGPTLanguage.English => "English",
            GodGPTLanguage.TraditionalChinese => "Traditional Chinese",
            GodGPTLanguage.Spanish => "Spanish",
            GodGPTLanguage.CN => "Chinese",
            _ => "English"
        };

        // Format current time
        var timeText = currentTime.ToString("yyyy-MM-dd HH:mm:ss");

        // Generate the prompt template
        var prompt =
            $@"Generate a personalized ""Today's Dos and Don'ts"" for the user based on their information and cosmological theories.
User Name: {fullName}
User Location: {location}
User Message Time: {timeText}
User Gender: {genderText}
User Age: {age}
User Language: {languageText}";

        _logger.LogDebug("[UserInfoCollectionGAgent][GenerateUserInfoPromptAsync] Generated prompt for user {UserId}",
            State.UserId);

        return new Tuple<string, string>(fullName, prompt);
    }
    
    
    private async Task<Tuple<string, string>> GenerateUserInfoPromptWithFortuneUserProfileAsync(DateTime? userLocalTime,
        FortuneUserProfileDto userProfile)
    {
        var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
        var currentTime = userLocalTime ?? DateTime.UtcNow;

        // Generate full name
        
        var fullName = userProfile.FullName.Trim();
        if (string.IsNullOrWhiteSpace(fullName))
        {
            fullName = "Unknown";
        }

        // Generate location
        var location = $"{userProfile.BirthCity}, {userProfile.BirthCountry}".Trim(' ', ',');
        if (string.IsNullOrWhiteSpace(location))
        {
            location = "Unknown";
        }

        // Generate gender text
        var genderText = userProfile.Gender switch
        {
            GenderEnum.Male => "Male",
            GenderEnum.Female => "Female",
            _ => "Other"
        };

        // Calculate age from birth date
        var age = "Unknown";
        if (userProfile.BirthDate != default)
        {
            try
            {
                var birthDate = userProfile.BirthDate.ToDateTime(userProfile.BirthTime ?? default);;
                var calculatedAge = currentTime.Year - userProfile.BirthDate.Year;
                if (currentTime < birthDate.AddYears(calculatedAge))
                {
                    calculatedAge--;
                }

                age = calculatedAge.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[UserInfoCollectionGAgent][GenerateUserInfoPromptWithFortuneUserProfileAsync] Invalid birth date: {Year}-{Month}-{Day}",
                    State.Year, State.Month, State.Day);
                age = "Unknown";
            }
        }

        // Generate language text
        var languageText = language switch
        {
            GodGPTLanguage.English => "English",
            GodGPTLanguage.TraditionalChinese => "Traditional Chinese",
            GodGPTLanguage.Spanish => "Spanish",
            GodGPTLanguage.CN => "Chinese",
            _ => "English"
        };

        // Format current time
        var timeText = currentTime.ToString("yyyy-MM-dd HH:mm:ss");

        // Generate the prompt template
        var prompt =
            $@"Generate a personalized ""Today's Dos and Don'ts"" for the user based on their information and cosmological theories.
User Name: {fullName}
User Location: {location}
User Message Time: {timeText}
User Gender: {genderText}
User Age: {age}
User Language: {languageText}";

        _logger.LogDebug("[UserInfoCollectionGAgent][GenerateUserInfoPromptWithFortuneUserProfileAsync] Generated prompt for user {UserId}",
            this.GetPrimaryKey().ToString());

        return new Tuple<string, string>(fullName, prompt);
    }

    /// <summary>
    /// Convert current state to DTO
    /// </summary>
    private UserInfoCollectionDto ConvertStateToDto()
    {
        return new UserInfoCollectionDto
        {
            UserId = State.UserId,
            NameInfo = !string.IsNullOrWhiteSpace(State.FirstName) ? new UserNameInfoDto
            {
                Gender = State.Gender,
                FirstName = State.FirstName,
                LastName = State.LastName
            } : null,
            LocationInfo = !string.IsNullOrWhiteSpace(State.Country) ? new UserLocationInfoDto
            {
                Country = State.Country,
                City = State.City
            } : null,
            BirthDateInfo = State.Day > 0 && State.Month > 0 && State.Year > 0 ? new UserBirthDateInfoDto
            {
                Day = State.Day,
                Month = State.Month,
                Year = State.Year
            } : null,
            BirthTimeInfo = State.Hour.HasValue || State.Minute.HasValue ? new UserBirthTimeInfoDto
            {
                Hour = State.Hour,
                Minute = State.Minute
            } : null,
            SeekingInterests = State.SeekingInterests ?? new List<string>(),
            SourceChannels = State.SourceChannels ?? new List<string>(),
            CreatedAt = State.CreatedAt,
            UpdatedAt = State.LastUpdated,
            IsInitialized = State.IsInitialized,
            SeekingInterestsCode = State.SeekingInterestsCode ?? new List<int>(),
            SourceChannelsCode = State.SourceChannelsCode ?? new List<int>(),
            IsCompleted = IsCollectionCompleted()
        };
    }
    
    /// <summary>
    /// Check if all required information is collected
    /// </summary>
    private bool IsCollectionCompleted()
    {
        return State.Gender !=0 &&
               !string.IsNullOrWhiteSpace(State.FirstName) &&
               !string.IsNullOrWhiteSpace(State.LastName) &&
               !string.IsNullOrWhiteSpace(State.Country) &&
               !string.IsNullOrWhiteSpace(State.City) &&
               State.Day > 0 && State.Month > 0 && State.Year > 0 &&
               (State.SeekingInterests?.Count > 0) &&
               (State.SourceChannels?.Count > 0);
    }
    
    /// <summary>
    /// Handle state transitions based on log events
    /// </summary>
    protected override void GAgentTransitionState(UserInfoCollectionGAgentState state, StateLogEventBase<UserInfoCollectionLogEvent> @event)
    {
        switch (@event)
        {
            case InitializeUserInfoCollectionLogEvent initializeEvent:
                state.UserId = initializeEvent.UserId;
                state.IsInitialized = true;
                state.CreatedAt = initializeEvent.CreatedAt;
                state.LastUpdated = initializeEvent.CreatedAt;
                _logger.LogDebug("[UserInfoCollectionGAgent][GAgentTransitionState] Initialized user info collection for user {UserId}", initializeEvent.UserId);
                break;
                
            case UpdateUserInfoCollectionLogEvent updateEvent:
                var isFirstUpdate = !state.IsInitialized;
                
                // Update basic state fields
                if (isFirstUpdate)
                {
                    state.IsInitialized = true;
                    state.UserId = updateEvent.UserId;
                    state.CreatedAt = updateEvent.UpdatedAt;
                }

                if (state.UserId == null || state.UserId == Guid.Empty)
                {
                    state.UserId = updateEvent.UserId;
                }

                state.LastUpdated = updateEvent.UpdatedAt;
                
                // Update name information if provided and not empty
                if (updateEvent.Gender.HasValue && updateEvent.Gender !=0 ) state.Gender = updateEvent.Gender.Value;
                if (!string.IsNullOrWhiteSpace(updateEvent.FirstName)) state.FirstName = updateEvent.FirstName;
                if (!string.IsNullOrWhiteSpace(updateEvent.LastName)) state.LastName = updateEvent.LastName;
                
                // Update location information if provided and not empty
                if (!string.IsNullOrWhiteSpace(updateEvent.Country)) state.Country = updateEvent.Country;
                if (!string.IsNullOrWhiteSpace(updateEvent.City)) state.City = updateEvent.City;
                
                // Update birth date information if provided and valid
                if (updateEvent.Day.HasValue && updateEvent.Day.Value > 0) state.Day = updateEvent.Day.Value;
                if (updateEvent.Month.HasValue && updateEvent.Month.Value > 0) state.Month = updateEvent.Month.Value;
                if (updateEvent.Year.HasValue && updateEvent.Year.Value > 0) state.Year = updateEvent.Year.Value;
                
                // Update birth time information if provided and valid
                if (updateEvent.Hour.HasValue && updateEvent.Hour.Value >= 0 && updateEvent.Hour.Value <= 23) 
                    state.Hour = updateEvent.Hour;
                if (updateEvent.Minute.HasValue && updateEvent.Minute.Value >= 0 && updateEvent.Minute.Value <= 59) 
                    state.Minute = updateEvent.Minute;
                
                // Update seeking interests if provided and not empty
                if (updateEvent.SeekingInterests != null && updateEvent.SeekingInterests.Count > 0) 
                    state.SeekingInterests = updateEvent.SeekingInterests;
                
                // Update source channels if provided and not empty
                if (updateEvent.SourceChannels != null && updateEvent.SourceChannels.Count > 0) 
                    state.SourceChannels = updateEvent.SourceChannels;
                
                // Update seeking interests codes if provided
                if (updateEvent.SeekingInterestsCode != null && updateEvent.SeekingInterestsCode.Count > 0)
                    state.SeekingInterestsCode = updateEvent.SeekingInterestsCode;
                
                // Update source channels codes if provided
                if (updateEvent.SourceChannelsCode != null && updateEvent.SourceChannelsCode.Count > 0)
                    state.SourceChannelsCode = updateEvent.SourceChannelsCode;
                
                _logger.LogDebug("[UserInfoCollectionGAgent][GAgentTransitionState] Updated user info collection,userId:{userId} isFirstUpdate: {IsFirstUpdate}", updateEvent.UserId,isFirstUpdate);
                break;
                
            case ClearUserInfoCollectionLogEvent clearEvent:
                state.UserId = Guid.Empty;
                state.IsInitialized = false;
                state.CreatedAt = default;
                state.LastUpdated = default;
                state.Gender = 0;
                state.FirstName = null;
                state.LastName = null;
                state.Country = null;
                state.City = null;
                state.Day = 0;  // Reset to 0 for int fields
                state.Month = 0;  // Reset to 0 for int fields
                state.Year = 0;  // Reset to 0 for int fields
                state.Hour = null;
                state.Minute = null;
                state.SeekingInterests = new List<string>();
                state.SourceChannels = new List<string>();
                state.SeekingInterestsCode = new List<int>();
                state.SourceChannelsCode = new List<int>();
                _logger.LogDebug("[UserInfoCollectionGAgent][GAgentTransitionState] Cleared all user info collection data");
                break;
            case UpdateFixStateLogEvent updateFixState:
                state.FixState = updateFixState.FixState;
                break;
        }
    }
    
    // Check and initialize first access status if needed
    protected override async Task OnGAgentActivateAsync(CancellationToken cancellationToken)
    {
        // Check and initialize first access status if needed
        if (State.FixState == 0)
        {
            _logger.LogDebug("[UserInfoCollectionGAgent][GAgentTransitionState] Modify data sync status {0}", this.GetPrimaryKey().ToString());
            RaiseEvent(new UpdateFixStateLogEvent
            {
                FixState = 1
            });
        }
    }
}
