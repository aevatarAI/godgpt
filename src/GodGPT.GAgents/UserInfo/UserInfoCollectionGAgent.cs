using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Aevatar.Application.Grains.UserInfo.Dtos;
using Aevatar.Application.Grains.UserInfo.SEvents;
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
}

[GAgent(nameof(UserInfoCollectionGAgent))]
public class UserInfoCollectionGAgent: GAgentBase<UserInfoCollectionGAgentState, UserInfoCollectionLogEvent>, IUserInfoCollectionGAgent
{
    private readonly ILogger<UserInfoCollectionGAgent> _logger;
    // Constants for seeking interests options
    private static readonly List<string> SeekingInterestOptions = new List<string>
    {
        "陪伴", "自我发现", "每日占星", "精神成长", "爱情与关系", "职业指导"
    };
    
    // Constants for source channel options
    private static readonly List<string> SourceChannelOptions = new List<string>
    {
        "App Store", "Social media", "Search engine", "Friend referral", 
        "Event/conference", "Advertisement", "Other"
    };
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
        
        // Validate required fields if they are being updated
        if (updateDto.NameInfo != null)
        {
            if (string.IsNullOrWhiteSpace(updateDto.NameInfo.Gender) || 
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
        
        if (updateDto.SeekingInterests != null)
        {
            if (updateDto.SeekingInterests.Count == 0)
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = "At least one seeking interest is required",
                    Data = ConvertStateToDto()
                };
            }
            // Validate that all interests are from the allowed options
            var invalidInterests = updateDto.SeekingInterests.Where(interest => !SeekingInterestOptions.Contains(interest)).ToList();
            if (invalidInterests.Any())
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = $"Invalid seeking interests: {string.Join(", ", invalidInterests)}",
                    Data = ConvertStateToDto()
                };
            }
        }
        
        if (updateDto.SourceChannels != null)
        {
            if (updateDto.SourceChannels.Count == 0)
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = "At least one source channel is required",
                    Data = ConvertStateToDto()
                };
            }
            // Validate that all channels are from the allowed options
            var invalidChannels = updateDto.SourceChannels.Where(channel => !SourceChannelOptions.Contains(channel)).ToList();
            if (invalidChannels.Any())
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = $"Invalid source channels: {string.Join(", ", invalidChannels)}",
                    Data = ConvertStateToDto()
                };
            }
        }
        
        var now = DateTime.UtcNow;
        
        RaiseEvent(new UpdateUserInfoCollectionLogEvent
        {
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
            SeekingInterests = updateDto.SeekingInterests,
            SourceChannels = updateDto.SourceChannels,
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
        
        // Format birth time display
        string birthTimeDisplay = "N/A";
        if (State.Hour.HasValue && State.Minute.HasValue)
        {
            birthTimeDisplay = $"{State.Hour:D2}:{State.Minute:D2}";
        }
        else if (State.Hour.HasValue || State.Minute.HasValue)
        {
            birthTimeDisplay = "N/A"; // If only one is provided, show N/A
        }
        
        return new UserInfoDisplayDto
        {
            FirstName = State.FirstName,
            LastName = State.LastName,
            Gender = State.Gender ?? "N/A",
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
            IsCompleted = IsCollectionCompleted()
        };
    }
    
    /// <summary>
    /// Check if all required information is collected
    /// </summary>
    private bool IsCollectionCompleted()
    {
        return !string.IsNullOrWhiteSpace(State.Gender) &&
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
                    state.CreatedAt = updateEvent.UpdatedAt;
                }
                state.LastUpdated = updateEvent.UpdatedAt;
                
                // Update name information if provided and not empty
                if (!string.IsNullOrWhiteSpace(updateEvent.Gender)) state.Gender = updateEvent.Gender;
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
                
                _logger.LogDebug("[UserInfoCollectionGAgent][GAgentTransitionState] Updated user info collection, isFirstUpdate: {IsFirstUpdate}", isFirstUpdate);
                break;
                
            case ClearUserInfoCollectionLogEvent clearEvent:
                state.IsInitialized = false;
                state.CreatedAt = default;
                state.LastUpdated = default;
                state.Gender = null;
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
                _logger.LogDebug("[UserInfoCollectionGAgent][GAgentTransitionState] Cleared all user info collection data");
                break;
        }
    }
}
