using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Timers;
using Aevatar.Application.Grains.TwitterInteraction.Dtos;
using Microsoft.Extensions.Options;
using Aevatar.Application.Grains.Common.Options;
using System.Linq;

namespace Aevatar.Application.Grains.TwitterInteraction;

/// <summary>
/// Twitter System Manager Grain Implementation
/// Provides centralized management for Twitter reward system components
/// </summary>
public class TwitterSystemManagerGrain : Grain, ITwitterSystemManagerGrain
{
    private readonly ILogger<TwitterSystemManagerGrain> _logger;
    private readonly IOptionsMonitor<TwitterRewardOptions> _options;
    private readonly Dictionary<string, TaskInfo> _taskStates = new();
    private TwitterSystemConfigDto _currentConfig = new();
    
    // Component references
    private ITweetMonitorGrain? _tweetMonitorGrain;
    private ITwitterRewardGrain? _twitterRewardGrain;
    private ITwitterInteractionGrain? _twitterInteractionGrain;
    
    // Task names
    private const string TWEET_MONITOR_TASK = "TweetMonitor";
    private const string REWARD_CALCULATION_TASK = "RewardCalculation";
    private const string INTERACTION_TASK = "TwitterInteraction";

    public TwitterSystemManagerGrain(
        ILogger<TwitterSystemManagerGrain> logger,
        IOptionsMonitor<TwitterRewardOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        InitializeDefaultConfig();
        _logger.LogInformation("TwitterSystemManagerGrain已激活");
        
        // Initialize component references
        _tweetMonitorGrain = GrainFactory.GetGrain<ITweetMonitorGrain>(_options.CurrentValue.PullTaskTargetId);
        _twitterRewardGrain = GrainFactory.GetGrain<ITwitterRewardGrain>(_options.CurrentValue.RewardTaskTargetId);
        _twitterInteractionGrain = GrainFactory.GetGrain<ITwitterInteractionGrain>("main");
        
        await base.OnActivateAsync(cancellationToken);
    }

    #region Task Control
    
    public async Task<TwitterApiResultDto<bool>> StartTaskAsync(string taskName, string targetId)
    {
        try
        {
            _logger.LogInformation("Starting task: {TaskName} with target: {TargetId}", taskName, targetId);
            
            if (string.IsNullOrWhiteSpace(taskName))
            {
                return new TwitterApiResultDto<bool> 
                { 
                    IsSuccess = false, 
                    ErrorMessage = "Task name cannot be empty" 
                };
            }
            
            if (string.IsNullOrWhiteSpace(targetId))
            {
                return new TwitterApiResultDto<bool> 
                { 
                    IsSuccess = false, 
                    ErrorMessage = "Target ID cannot be empty" 
                };
            }

            var result = taskName.ToLower() switch
            {
                "tweetmonitor" => await StartTweetMonitorTaskAsync(targetId),
                "rewardcalculation" => await StartRewardCalculationTaskAsync(targetId),
                _ => false
            };

            if (result)
            {
                _logger.LogInformation("Task {TaskName} started successfully", taskName);
                return new TwitterApiResultDto<bool> 
                { 
                    IsSuccess = true, 
                    Data = true 
                };
            }
            else
            {
                _logger.LogWarning("Failed to start task: {TaskName}", taskName);
                return new TwitterApiResultDto<bool> 
                { 
                    IsSuccess = false, 
                    ErrorMessage = $"Failed to start task: {taskName}" 
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting task: {TaskName}", taskName);
            return new TwitterApiResultDto<bool> 
            { 
                IsSuccess = false, 
                ErrorMessage = $"Error starting task: {ex.Message}" 
            };
        }
    }

    public async Task<TwitterApiResultDto<bool>> StopTaskAsync(string taskName)
    {
        try
        {
            _logger.LogInformation("Stopping task: {TaskName}", taskName);
            
            if (string.IsNullOrWhiteSpace(taskName))
            {
                return new TwitterApiResultDto<bool> 
                { 
                    IsSuccess = false, 
                    ErrorMessage = "Task name cannot be empty" 
                };
            }

            var result = taskName.ToLower() switch
            {
                "tweetmonitor" => await StopTweetMonitorTaskAsync(),
                "rewardcalculation" => await StopRewardCalculationTaskAsync(),
                _ => false
            };

            if (result)
            {
                _logger.LogInformation("Task {TaskName} stopped successfully", taskName);
                return new TwitterApiResultDto<bool> 
                { 
                    IsSuccess = true, 
                    Data = true 
                };
            }
            else
            {
                _logger.LogWarning("Failed to stop task: {TaskName}", taskName);
                return new TwitterApiResultDto<bool> 
                { 
                    IsSuccess = false, 
                    ErrorMessage = $"Failed to stop task: {taskName}" 
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping task: {TaskName}", taskName);
            return new TwitterApiResultDto<bool> 
            { 
                IsSuccess = false, 
                ErrorMessage = $"Error stopping task: {ex.Message}" 
            };
        }
    }

    public async Task<TwitterApiResultDto<TaskExecutionStatusDto>> GetTaskStatusAsync(string taskName)
    {
        try
        {
            _logger.LogDebug("Getting status for task: {TaskName}", taskName);
            
            var taskStatuses = await GetAllTaskStatusAsync();
            if (!taskStatuses.IsSuccess)
            {
                return new TwitterApiResultDto<TaskExecutionStatusDto>
                {
                    IsSuccess = false,
                    ErrorMessage = taskStatuses.ErrorMessage
                };
            }

            var taskStatus = taskStatuses.Data.FirstOrDefault(t => 
                string.Equals(t.TaskName, taskName, StringComparison.OrdinalIgnoreCase));

            if (taskStatus == null)
            {
                return new TwitterApiResultDto<TaskExecutionStatusDto>
                {
                    IsSuccess = false,
                    ErrorMessage = $"Task not found: {taskName}"
                };
            }

            return new TwitterApiResultDto<TaskExecutionStatusDto>
            {
                IsSuccess = true,
                Data = taskStatus
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting task status: {TaskName}", taskName);
            return new TwitterApiResultDto<TaskExecutionStatusDto>
            {
                IsSuccess = false,
                ErrorMessage = $"Error getting task status: {ex.Message}"
            };
        }
    }

    #endregion

    #region Monitoring

    public async Task<TwitterApiResultDto<List<TaskExecutionStatusDto>>> GetAllTaskStatusAsync()
    {
        try
        {
            _logger.LogDebug("Getting status for all Twitter tasks");
            
            var taskStatuses = new List<TaskExecutionStatusDto>();

            // Get TweetMonitor status
            if (_tweetMonitorGrain != null)
            {
                try
                {
                    var monitorStatus = await _tweetMonitorGrain.GetMonitoringStatusAsync();
                    if (monitorStatus.IsSuccess)
                    {
                        var tweetMonitorStatus = monitorStatus.Data;
                        var taskStatus = new TaskExecutionStatusDto
                        {
                            TaskName = TWEET_MONITOR_TASK,
                            IsEnabled = true, // Default enabled if status query succeeds
                            IsRunning = tweetMonitorStatus.IsRunning,
                            LastExecutionTime = tweetMonitorStatus.LastFetchTime,
                            LastExecutionTimestamp = tweetMonitorStatus.LastFetchTimeUtc,
                            NextScheduledTime = tweetMonitorStatus.NextScheduledFetch,
                            NextScheduledTimestamp = tweetMonitorStatus.NextScheduledFetchUtc,
                            LastError = tweetMonitorStatus.LastError ?? string.Empty
                        };
                        taskStatuses.Add(taskStatus);
                    }
                    else
                    {
                        taskStatuses.Add(new TaskExecutionStatusDto
                        {
                            TaskName = TWEET_MONITOR_TASK,
                            IsEnabled = false,
                            IsRunning = false,
                            LastError = monitorStatus.ErrorMessage
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get TweetMonitor status");
                    taskStatuses.Add(new TaskExecutionStatusDto
                    {
                        TaskName = TWEET_MONITOR_TASK,
                        IsEnabled = false,
                        IsRunning = false,
                        LastError = $"Status check failed: {ex.Message}"
                    });
                }
            }

            // Get TwitterReward status  
            if (_twitterRewardGrain != null)
            {
                try
                {
                    var rewardStatus = await _twitterRewardGrain.GetRewardCalculationStatusAsync();
                    if (rewardStatus.IsSuccess)
                    {
                        var twitterRewardStatus = rewardStatus.Data;
                        var taskStatus = new TaskExecutionStatusDto
                        {
                            TaskName = REWARD_CALCULATION_TASK,
                            IsEnabled = true, // Default enabled if status query succeeds
                            IsRunning = twitterRewardStatus.IsRunning,
                            LastExecutionTime = twitterRewardStatus.LastCalculationTime,
                            LastExecutionTimestamp = twitterRewardStatus.LastCalculationTimeUtc,
                            NextScheduledTime = twitterRewardStatus.NextScheduledCalculation,
                            NextScheduledTimestamp = twitterRewardStatus.NextScheduledCalculationUtc,
                            LastError = twitterRewardStatus.LastError ?? string.Empty
                        };
                        taskStatuses.Add(taskStatus);
                    }
                    else
                    {
                        taskStatuses.Add(new TaskExecutionStatusDto
                        {
                            TaskName = REWARD_CALCULATION_TASK,
                            IsEnabled = false,
                            IsRunning = false,
                            LastError = rewardStatus.ErrorMessage
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get TwitterReward status");
                    taskStatuses.Add(new TaskExecutionStatusDto
                    {
                        TaskName = REWARD_CALCULATION_TASK,
                        IsEnabled = false,
                        IsRunning = false,
                        LastError = $"Status check failed: {ex.Message}"
                    });
                }
            }

            _logger.LogInformation("Retrieved status for {TaskCount} tasks", taskStatuses.Count);
            return new TwitterApiResultDto<List<TaskExecutionStatusDto>> 
            { 
                IsSuccess = true, 
                Data = taskStatuses 
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting task statuses");
            return new TwitterApiResultDto<List<TaskExecutionStatusDto>> 
            { 
                IsSuccess = false, 
                ErrorMessage = $"Error getting task statuses: {ex.Message}" 
            };
        }
    }

    public async Task<TwitterApiResultDto<SystemHealthDto>> GetSystemHealthAsync()
    {
        try
        {
            _logger.LogDebug("Getting system health status");
            
            var allTasks = await GetAllTaskStatusAsync();
            if (!allTasks.IsSuccess)
            {
                return new TwitterApiResultDto<SystemHealthDto>
                {
                    IsSuccess = false,
                    ErrorMessage = allTasks.ErrorMessage
                };
            }

            var tasks = allTasks.Data;
            var health = new SystemHealthDto
            {
                IsHealthy = tasks.All(t => t.IsEnabled && string.IsNullOrEmpty(t.LastError)),
                LastUpdateTime = DateTime.UtcNow,
                LastUpdateTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ActiveTasks = tasks.Count(t => t.IsEnabled),
                TaskStatuses = tasks.Select(t => new TaskHealthStatusDto
                {
                    TaskName = t.TaskName,
                    IsHealthy = t.IsEnabled && string.IsNullOrEmpty(t.LastError),
                    LastSuccessTime = t.LastSuccessTime,
                    LastSuccessTimestamp = t.LastSuccessTimestamp,
                    Status = t.IsRunning ? "Running" : (t.IsEnabled ? "Enabled" : "Disabled"),
                    Issues = string.IsNullOrEmpty(t.LastError) ? new List<string>() : new List<string> { t.LastError }
                }).ToList()
            };

            return new TwitterApiResultDto<SystemHealthDto>
            {
                IsSuccess = true,
                Data = health
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system health");
            return new TwitterApiResultDto<SystemHealthDto>
            {
                IsSuccess = false,
                ErrorMessage = $"Error getting system health: {ex.Message}"
            };
        }
    }

    #endregion

    #region Private Helper Methods

    private async Task<bool> StartTweetMonitorTaskAsync(string targetId)
    {
        if (_tweetMonitorGrain == null)
        {
            _logger.LogError("TweetMonitorGrain is not initialized");
            return false;
        }

        var result = await _tweetMonitorGrain.StartMonitoringAsync();
        return result.IsSuccess && result.Data;
    }

    private async Task<bool> StopTweetMonitorTaskAsync()
    {
        if (_tweetMonitorGrain == null)
        {
            _logger.LogError("TweetMonitorGrain is not initialized");
            return false;
        }

        var result = await _tweetMonitorGrain.StopMonitoringAsync();
        return result.IsSuccess && result.Data;
    }

    private async Task<bool> StartRewardCalculationTaskAsync(string targetId)
    {
        if (_twitterRewardGrain == null)
        {
            _logger.LogError("TwitterRewardGrain is not initialized");
            return false;
        }

        var result = await _twitterRewardGrain.StartRewardCalculationAsync();
        return result.IsSuccess && result.Data;
    }

    private async Task<bool> StopRewardCalculationTaskAsync()
    {
        if (_twitterRewardGrain == null)
        {
            _logger.LogError("TwitterRewardGrain is not initialized");
            return false;
        }

        var result = await _twitterRewardGrain.StopRewardCalculationAsync();
        return result.IsSuccess && result.Data;
    }

    #endregion

    #region Configuration and Management

    public async Task<TwitterSystemConfigDto> GetCurrentConfigAsync()
    {
        return _currentConfig;
    }

    public async Task SetConfigAsync(TwitterSystemConfigDto config)
    {
        _currentConfig = config;
        _logger.LogInformation("TwitterSystemManager配置已更新");
    }

    public async Task UpdateTimeConfigAsync(TimeSpan monitorInterval, TimeSpan rewardInterval)
    {
        _currentConfig.MonitorInterval = monitorInterval;
        _currentConfig.RewardInterval = rewardInterval;
        _logger.LogInformation("时间配置已更新: Monitor={MonitorInterval}, Reward={RewardInterval}", 
            monitorInterval, rewardInterval);
    }

    public async Task ManualPullTweetsAsync()
    {
        try
        {
            var tweetMonitor = GrainFactory.GetGrain<ITweetMonitorGrain>(Guid.NewGuid().ToString());
            await tweetMonitor.StartMonitoringAsync();
            _logger.LogInformation("手动拉取推文任务已启动");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "手动拉取推文失败");
            throw;
        }
    }

    public async Task ManualCalculateRewardsAsync()
    {
        try
        {
            var rewardGrain = GrainFactory.GetGrain<ITwitterRewardGrain>(Guid.NewGuid().ToString());
            await rewardGrain.TriggerRewardCalculationAsync(DateTime.UtcNow);
            _logger.LogInformation("手动计算奖励任务已启动");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "手动计算奖励失败");
            throw;
        }
    }

    public async Task<List<ProcessingHistoryDto>> GetProcessingHistoryAsync()
    {
        // 这里应该从持久化存储中获取历史记录
        // 现在返回模拟数据
        return new List<ProcessingHistoryDto>
        {
            new ProcessingHistoryDto
            {
                Timestamp = DateTime.UtcNow.AddHours(-1),
                Operation = "TweetMonitoring",
                Status = "Success",
                Details = "监听到新推文",
                ProcessedCount = 5,
                Duration = TimeSpan.FromMinutes(2)
            }
        };
    }

    public async Task<SystemMetricsDto> GetSystemMetricsAsync()
    {
        return new SystemMetricsDto
        {
            GeneratedAt = DateTime.UtcNow,
            GeneratedAtTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            TotalTweetsStored = 100, // 这里应该从实际数据获取
            TweetsProcessedToday = 50,
            TotalUsersRewarded = 25,
            CreditsDistributedToday = 1000,
            TotalCreditsDistributed = 5000,
            AverageProcessingTime = 1.5,
            ApiCallsToday = 200,
            ApiSuccessRate = 0.95,
            TweetsByType = new Dictionary<string, int>
            {
                ["Original"] = 80,
                ["Retweet"] = 20
            },
            PerformanceMetrics = new Dictionary<string, double>
            {
                ["AverageResponseTime"] = 0.5,
                ["MemoryUsage"] = 128.5
            }
        };
    }

    #endregion

    #region ITwitterSystemManagerGrain Simple Interface Implementation

    public async Task StartTweetMonitorAsync()
    {
        var result = await StartTaskAsync("TweetMonitor", _options.CurrentValue.PullTaskTargetId);
        if (!result.IsSuccess)
        {
            throw new Exception(result.ErrorMessage ?? "Failed to start tweet monitor");
        }
    }

    public async Task StopTweetMonitorAsync()
    {
        var result = await StopTaskAsync("TweetMonitor");
        if (!result.IsSuccess)
        {
            throw new Exception(result.ErrorMessage ?? "Failed to stop tweet monitor");
        }
    }

    public async Task StartRewardCalculationAsync()
    {
        var result = await StartTaskAsync("RewardCalculation", _options.CurrentValue.RewardTaskTargetId);
        if (!result.IsSuccess)
        {
            throw new Exception(result.ErrorMessage ?? "Failed to start reward calculation");
        }
    }

    public async Task StopRewardCalculationAsync()
    {
        var result = await StopTaskAsync("RewardCalculation");
        if (!result.IsSuccess)
        {
            throw new Exception(result.ErrorMessage ?? "Failed to stop reward calculation");
        }
    }

    public async Task<TaskExecutionStatusDto> GetTaskStatusAsync()
    {
        var result = await GetAllTaskStatusAsync();
        if (!result.IsSuccess)
        {
            throw new Exception(result.ErrorMessage ?? "Failed to get task status");
        }

        return new TaskExecutionStatusDto
        {
            TaskName = "SystemOverview",
            IsEnabled = true,
            IsRunning = result.Data?.Any(t => t.IsRunning) ?? false,
            LastExecutionTime = result.Data?.OrderByDescending(t => t.LastExecutionTime).FirstOrDefault()?.LastExecutionTime,
            LastExecutionTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            LastSuccessTime = result.Data?.OrderByDescending(t => t.LastSuccessTime).FirstOrDefault()?.LastSuccessTime,
            LastSuccessTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            NextScheduledTime = DateTime.UtcNow.AddMinutes(30),
            NextScheduledTimestamp = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds(),
            RetryCount = 0,
            LastError = string.Empty,
            ReminderTargetId = "SystemManager",
            TaskMetrics = new Dictionary<string, object>
            {
                ["TweetMonitorRunning"] = result.Data?.Any(t => t.TaskName == "TweetMonitor" && t.IsRunning) ?? false,
                ["RewardCalculationRunning"] = result.Data?.Any(t => t.TaskName == "RewardCalculation" && t.IsRunning) ?? false,
                ["ActiveTasks"] = result.Data?.Count(t => t.IsRunning) ?? 0,
                ["TotalTasks"] = result.Data?.Count ?? 0
            }
        };
    }



    #endregion

    #region 私有方法

    private void InitializeDefaultConfig()
    {
        _currentConfig = new TwitterSystemConfigDto
        {
            MonitorInterval = TimeSpan.FromMinutes(5),
            RewardInterval = TimeSpan.FromHours(1),
            MaxTweetsPerRequest = 100,
            AutoStartMonitoring = true,
            EnableRewardCalculation = true
        };
    }

    #endregion
} 