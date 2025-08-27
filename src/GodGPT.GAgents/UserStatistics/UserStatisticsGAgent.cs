using Aevatar.Application.Grains.Agents.UserStatistics.SEvents;
using Aevatar.Application.Grains.UserStatistics.Dtos;
using Aevatar.Application.Grains.UserStatistics.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.UserStatistics;

public interface IUserStatisticsGAgent : IGAgent
{
    Task<AppRatingRecordDto> RecordAppRatingAsync(string platform, string deviceId);
    [ReadOnly]
    Task<UserStatisticsDto> GetUserStatisticsAsync();
    [ReadOnly]
    Task<List<AppRatingRecordDto>> GetAppRatingRecordsAsync(string? deviceId = null);
    [ReadOnly]
    Task<bool> CanUserRateAppAsync(string deviceId);
}

/// <summary>
/// User Statistics GAgent - manages user behavior statistics including app ratings
/// </summary>
[GAgent(nameof(UserStatisticsGAgent))]
[Reentrant]
public class UserStatisticsGAgent : GAgentBase<UserStatisticsState, UserStatisticsEventLog>, IUserStatisticsGAgent
{
    private readonly ILogger<UserStatisticsGAgent> _logger;
    private readonly IOptionsMonitor<UserStatisticsOptions> _userStatisticsOptions;

    public UserStatisticsGAgent(ILogger<UserStatisticsGAgent> logger, IOptionsMonitor<UserStatisticsOptions> userStatisticsOptions)
    {
        _logger = logger;
        _userStatisticsOptions = userStatisticsOptions;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("manages user behavior statistics including app ratings");
    }

    public async Task<AppRatingRecordDto> RecordAppRatingAsync(string platform, string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            _logger.LogWarning("[UserStatisticsGAgent][RecordAppRatingAsync] DeviceId cannot be empty for user: {UserId}", this.GetPrimaryKey().ToString());
            return new AppRatingRecordDto();
        }

        try
        {
            var isFirstRating = State.AppRatings == null || !State.AppRatings.ContainsKey(deviceId);
            var ratingTime = DateTime.UtcNow;

            _logger.LogDebug("[UserStatisticsGAgent][RecordAppRatingAsync] Recording app rating for user: {UserId}, platform: {Platform}, device: {DeviceID}, isFirstRating: {IsFirstRating}", 
                this.GetPrimaryKey().ToString(), platform, deviceId, isFirstRating);

            RaiseEvent(new RecordAppRatingEventLog
            {
                Platform = platform,
                DeviceId = deviceId,
                RatingTime = ratingTime,
                RatingCount = isFirstRating ? 1 : State.AppRatings[deviceId].RatingCount + 1
            });

            _logger.LogDebug("[UserStatisticsGAgent][RecordAppRatingAsync] App rating recorded successfully for user: {UserId}, platform: {Platform}, device: {DeviceID}", 
                this.GetPrimaryKey().ToString(), platform, deviceId);

            return new AppRatingRecordDto
            {
                Platform = platform,
                DeviceId = deviceId,
                FirstRatingTime = isFirstRating ? ratingTime : State.AppRatings[deviceId].FirstRatingTime,
                LastRatingTime = ratingTime,
                RatingCount = isFirstRating ? 1 : State.AppRatings[deviceId].RatingCount + 1
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UserStatisticsGAgent][RecordAppRatingAsync] Failed to record app rating for user: {UserId}, platform: {Platform}", 
                this.GetPrimaryKey().ToString(), platform);
            throw;
        }
    }

    /// <summary>
    /// Get user statistics information
    /// </summary>
    [ReadOnly]
    public Task<UserStatisticsDto> GetUserStatisticsAsync()
    {
        var result = new UserStatisticsDto
        {
            UserId = State.UserId,
            AppRatings = State.AppRatings.Values.Select(rating => new AppRatingRecordDto
            {
                Platform = rating.Platform,
                DeviceId = rating.DeviceId,
                FirstRatingTime = rating.FirstRatingTime,
                LastRatingTime = rating.LastRatingTime,
                RatingCount = rating.RatingCount
            }).ToList(),
        };
        return Task.FromResult(result);
    }
    
    /// <summary>
    /// Get app rating records for specific platform or all platforms
    /// </summary>
    [ReadOnly]
    public Task<List<AppRatingRecordDto>> GetAppRatingRecordsAsync(string? deviceId = null)
    {
        if (State.AppRatings == null)
        {
            return Task.FromResult(new List<AppRatingRecordDto>());
        }

        var query = State.AppRatings.Values.AsEnumerable();
        
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            query = query.Where(r => r.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
        }

        var result = query.Select(rating => new AppRatingRecordDto
        {
            Platform = rating.Platform,
            DeviceId = rating.DeviceId,
            FirstRatingTime = rating.FirstRatingTime,
            LastRatingTime = rating.LastRatingTime,
            RatingCount = rating.RatingCount
        }).ToList();

        return Task.FromResult(result);
    }
    
    [ReadOnly]
    public Task<bool> CanUserRateAppAsync(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            _logger.LogDebug("[UserStatisticsGAgent][CanUserRateAppAsync] DeviceId is null or empty for user: {UserId}", this.GetPrimaryKey().ToString());
            return Task.FromResult(false);
        }

        if (State.AppRatings == null)
        {
            _logger.LogDebug("[UserStatisticsGAgent][CanUserRateAppAsync] No rating records found, user can rate for user: {UserId}, device: {DeviceId}", this.GetPrimaryKey().ToString(), deviceId);
            return Task.FromResult(true);
        }

        if (!State.AppRatings.TryGetValue(deviceId, out var ratingInfo))
        {
            _logger.LogDebug("[UserStatisticsGAgent][CanUserRateAppAsync] No rating record for device, user can rate for user: {UserId}, device: {DeviceId}", this.GetPrimaryKey().ToString(), deviceId);
            return Task.FromResult(true);
        }

        var ratingIntervalMinutes = _userStatisticsOptions.CurrentValue.RatingIntervalMinutes;
        var minutesSinceLastRating = (DateTime.UtcNow - ratingInfo.LastRatingTime).TotalMinutes;
        var canRate = minutesSinceLastRating >= ratingIntervalMinutes;
        
        _logger.LogDebug("[UserStatisticsGAgent][CanUserRateAppAsync] User: {UserId}, device: {DeviceId}, minutes since last rating: {MinutesSinceLastRating}, interval required: {IntervalMinutes}, can rate: {CanRate}", 
            this.GetPrimaryKey().ToString(), deviceId, minutesSinceLastRating, ratingIntervalMinutes, canRate);

        return Task.FromResult(canRate);
    }

    private async Task<bool> InitializeUserStatisticsAsync()
    {
        if (State.IsInitialized)
        {
            _logger.LogDebug("[UserStatisticsGAgent][InitializeUserStatisticsAsync] User statistics already initialized for user: {UserId}", this.GetPrimaryKey().ToString());
            return true;
        }

        try
        {
            RaiseEvent(new InitializeUserStatsEventLog
            {
                UserId = this.GetPrimaryKey()
            });

            _logger.LogDebug("[UserStatisticsGAgent][InitializeUserStatisticsAsync] User statistics initialized for user: {UserId}", this.GetPrimaryKey().ToString());
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UserStatisticsGAgent][InitializeUserStatisticsAsync] Failed to initialize user statistics for user: {UserId}", this.GetPrimaryKey().ToString());
            return false;
        }
    }
    
    protected override async Task OnGAgentActivateAsync(CancellationToken cancellationToken)
    {
        await InitializeUserStatisticsAsync();
    }

    /// <summary>
    /// Handle state transitions for events
    /// </summary>
    protected override void GAgentTransitionState(UserStatisticsState state, StateLogEventBase<UserStatisticsEventLog> @event)
    {
        switch (@event)
        {
            case InitializeUserStatsEventLog initEvent:
                state.UserId = initEvent.UserId;
                state.IsInitialized = true;
                break;

            case RecordAppRatingEventLog ratingEvent:
                var key = ratingEvent.DeviceId;
                
                if (state.AppRatings == null)
                {
                    state.AppRatings = new Dictionary<string, AppRatingInfo>();
                }
                
                if (state.AppRatings.ContainsKey(key))
                {
                    var existing = state.AppRatings[key];
                    existing.LastRatingTime = ratingEvent.RatingTime;
                    existing.RatingCount = ratingEvent.RatingCount;
                    existing.DeviceId = ratingEvent.DeviceId;
                }
                else
                {
                    state.AppRatings[key] = new AppRatingInfo
                    {
                        Platform = ratingEvent.Platform,
                        DeviceId = ratingEvent.DeviceId,
                        FirstRatingTime = ratingEvent.RatingTime,
                        LastRatingTime = ratingEvent.RatingTime,
                        RatingCount = ratingEvent.RatingCount
                    };
                }
                break;
        }
    }
}