using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Aevatar.Application.Grains.UserInfo.Dtos;
using Aevatar.Application.Grains.UserInfo.Enums;
using Aevatar.Application.Grains.UserInfo.SEvents;
using Aevatar.Application.Grains.UserInfo.Helpers;
using Microsoft.Extensions.Logging;

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
    /// Get user info display data for confirmation
    /// </summary>
    Task<UserInfoDisplayDto> GetUserInfoDisplayAsync();
    
    /// <summary>
    /// Clear all user info collection data
    /// </summary>
    Task ClearAllAsync();
    
    /// <summary>
    /// Get user info options (seeking interests and source channels) based on language
    /// </summary>
    Task<UserInfoOptionsResponseDto> GetUserInfoOptionsAsync();
}

[GAgent(nameof(UserInfoCollectionGAgent))]
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
        _logger.LogInformation("[UserInfoCollectionGAgent][UpdateUserInfoCollectionAsync] Updating user info collection");
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
                updateDto.BirthDateInfo.Year.Value < 1900 || updateDto.BirthDateInfo.Year.Value > DateTime.Now.Year)
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
                .Select(channel => UserInfoLocalizationHelper.GetSourceChannelText(channel, language))
                .ToList();
        }
        
        var now = DateTime.UtcNow;
        
        RaiseEvent(new UpdateUserInfoCollectionLogEvent
        {
            UserId = this.GetPrimaryKey(),
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
        _logger.LogInformation("[UserInfoCollectionGAgent][GetUserInfoCollectionAsync] Getting user info collection");
        
        if (!State.IsInitialized)
        {
            _logger.LogWarning("[UserInfoCollectionGAgent][GetUserInfoCollectionAsync] User info collection not initialized");
            return null;
        }
        
        return ConvertStateToDto();
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
        _logger.LogInformation("[UserInfoCollectionGAgent][GetUserInfoOptionsAsync] Getting user info options");
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
                
                _logger.LogDebug("[UserInfoCollectionGAgent][GAgentTransitionState] Updated user info collection, isFirstUpdate: {IsFirstUpdate}", isFirstUpdate);
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
        }
    }
}
