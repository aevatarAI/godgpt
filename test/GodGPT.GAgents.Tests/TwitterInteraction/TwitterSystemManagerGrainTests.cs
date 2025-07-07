using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Application.Grains.TwitterInteraction;
using Aevatar.Application.Grains.TwitterInteraction.Dtos;
using Aevatar.GodGPT.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Aevatar.Application.Grains.Tests.TwitterInteraction;

/// <summary>
/// Unit tests for TwitterSystemManagerGrain
/// Tests system management functionality including task control, configuration management, and monitoring
/// </summary>
public class TwitterSystemManagerGrainTests : AevatarGodGPTTestsBase
{
    private readonly ILogger<TwitterSystemManagerGrainTests> _logger;

    public TwitterSystemManagerGrainTests() : base()
    {
        _logger = GetRequiredService<ILogger<TwitterSystemManagerGrainTests>>();
    }

    #region Task Management Tests

    [Fact]
    public async Task StartTaskAsync_WithValidTweetMonitorTask_ShouldReturnSuccess()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-manager");

        // Act
        var result = await grain.StartTaskAsync("TweetMonitor", "test-target-id");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Data);
    }

    [Fact]
    public async Task StartTaskAsync_WithEmptyTaskName_ShouldReturnFailure()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-manager");

        // Act
        var result = await grain.StartTaskAsync("", "test-target-id");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Task name cannot be empty", result.ErrorMessage);
    }

    [Fact]
    public async Task StartTaskAsync_WithEmptyTargetId_ShouldReturnFailure()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-manager");

        // Act
        var result = await grain.StartTaskAsync("TweetMonitor", "");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Target ID cannot be empty", result.ErrorMessage);
    }

    [Fact]
    public async Task StartTaskAsync_WithInvalidTaskName_ShouldReturnFailure()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-manager");

        // Act
        var result = await grain.StartTaskAsync("InvalidTask", "test-target-id");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Unknown task type", result.ErrorMessage);
    }

    [Fact]
    public async Task StopTaskAsync_WithValidTweetMonitorTask_ShouldReturnSuccess()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-manager");

        // Act
        var result = await grain.StopTaskAsync("TweetMonitor");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Data);
    }

    [Fact]
    public async Task StopTaskAsync_WithEmptyTaskName_ShouldReturnFailure()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-manager");

        // Act
        var result = await grain.StopTaskAsync("");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Task name cannot be empty", result.ErrorMessage);
    }

    [Fact]
    public async Task GetAllTaskStatusAsync_ShouldReturnTaskStatuses()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-manager");

        // Act
        var result = await grain.GetAllTaskStatusAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.IsType<List<TaskExecutionStatusDto>>(result.Data);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public async Task GetCurrentConfigAsync_ShouldReturnConfiguration()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-manager");

        // Act
        var result = await grain.GetCurrentConfigAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsType<TwitterSystemConfigDto>(result);
        Assert.True(result.MonitorInterval > TimeSpan.Zero);
        Assert.True(result.RewardInterval > TimeSpan.Zero);
    }

    [Fact]
    public async Task SetConfigAsync_WithValidConfig_ShouldComplete()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-manager");
        var config = new TwitterSystemConfigDto
        {
            MonitorInterval = TimeSpan.FromMinutes(10),
            RewardInterval = TimeSpan.FromHours(2),
            MaxTweetsPerRequest = 200,
            AutoStartMonitoring = true,
            EnableRewardCalculation = true
        };

        // Act & Assert - Should not throw
        await grain.SetConfigAsync(config);
    }

    [Fact]
    public async Task UpdateTimeConfigAsync_ShouldComplete()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-manager");

        // Act & Assert - Should not throw
        await grain.UpdateTimeConfigAsync(TimeSpan.FromMinutes(15), TimeSpan.FromHours(3));
    }

    #endregion

    #region Manual Execution Tests

    [Fact]
    public async Task ManualPullTweetsAsync_ShouldComplete()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-manager");

        // Act & Assert - Should not throw
        await grain.ManualPullTweetsAsync();
    }

    [Fact]
    public async Task ManualCalculateRewardsAsync_ShouldComplete()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-manager");

        // Act & Assert - Should not throw
        await grain.ManualCalculateRewardsAsync();
    }

    #endregion

    #region Monitoring Tests

    [Fact]
    public async Task GetSystemHealthAsync_ShouldReturnHealthStatus()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-manager");

        // Act
        var result = await grain.GetSystemHealthAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.IsType<SystemHealthDto>(result.Data);
        Assert.NotEmpty(result.Data.HealthMetrics);
    }

    [Fact]
    public async Task GetProcessingHistoryAsync_ShouldReturnHistory()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-manager");

        // Act
        var result = await grain.GetProcessingHistoryAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsType<List<ProcessingHistoryDto>>(result);
    }

    [Fact]
    public async Task GetSystemMetricsAsync_ShouldReturnMetrics()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-manager");

        // Act
        var result = await grain.GetSystemMetricsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsType<SystemMetricsDto>(result);
        Assert.True(result.GeneratedAt <= DateTime.UtcNow);
        Assert.True(result.GeneratedAtTimestamp > 0);
        Assert.NotEmpty(result.TweetsByType);
        Assert.NotEmpty(result.PerformanceMetrics);
    }

    #endregion

    #region Simple Interface Tests

    [Fact]
    public async Task StartTweetMonitorAsync_ShouldComplete()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-manager");

        // Act & Assert - Should not throw
        await grain.StartTweetMonitorAsync();
    }

    [Fact]
    public async Task StopTweetMonitorAsync_ShouldComplete()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-manager");

        // Act & Assert - Should not throw
        await grain.StopTweetMonitorAsync();
    }

    [Fact]
    public async Task StartRewardCalculationAsync_ShouldComplete()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-manager");

        // Act & Assert - Should not throw
        await grain.StartRewardCalculationAsync();
    }

    [Fact]
    public async Task StopRewardCalculationAsync_ShouldComplete()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-manager");

        // Act & Assert - Should not throw
        await grain.StopRewardCalculationAsync();
    }

    [Fact]
    public async Task GetTaskStatusAsync_ShouldReturnStatus()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-manager");

        // Act
        var result = await grain.GetTaskStatusAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsType<TaskExecutionStatusDto>(result);
        Assert.Equal("SystemOverview", result.TaskName);
        Assert.NotEmpty(result.TaskMetrics);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task SystemManagerWorkflow_StartStopTasks_ShouldWorkCorrectly()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-workflow");

        // Act & Assert
        await grain.StartTweetMonitorAsync();
        await grain.StartRewardCalculationAsync();

        var status = await grain.GetTaskStatusAsync();
        Assert.NotNull(status);

        await grain.StopTweetMonitorAsync();
        await grain.StopRewardCalculationAsync();
    }

    [Fact]
    public async Task ConfigurationWorkflow_GetUpdateSet_ShouldWorkCorrectly()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-config");

        // Act
        var originalConfig = await grain.GetCurrentConfigAsync();
        Assert.NotNull(originalConfig);

        await grain.UpdateTimeConfigAsync(TimeSpan.FromMinutes(20), TimeSpan.FromHours(4));

        var newConfig = new TwitterSystemConfigDto
        {
            MonitorInterval = TimeSpan.FromMinutes(25),
            RewardInterval = TimeSpan.FromHours(5),
            MaxTweetsPerRequest = 150,
            AutoStartMonitoring = false,
            EnableRewardCalculation = false
        };

        await grain.SetConfigAsync(newConfig);

        var updatedConfig = await grain.GetCurrentConfigAsync();

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(25), updatedConfig.MonitorInterval);
        Assert.Equal(TimeSpan.FromHours(5), updatedConfig.RewardInterval);
        Assert.Equal(150, updatedConfig.MaxTweetsPerRequest);
    }

    [Fact]
    public async Task MonitoringWorkflow_HealthMetricsHistory_ShouldWorkCorrectly()
    {
        // Arrange
        var grain = Cluster.GrainFactory.GetGrain<ITwitterSystemManagerGrain>("test-monitoring");

        // Act
        var healthResult = await grain.GetSystemHealthAsync();
        var metricsResult = await grain.GetSystemMetricsAsync();
        var historyResult = await grain.GetProcessingHistoryAsync();

        // Assert
        Assert.True(healthResult.IsSuccess);
        Assert.NotNull(healthResult.Data);

        Assert.NotNull(metricsResult);
        Assert.True(metricsResult.GeneratedAt <= DateTime.UtcNow);

        Assert.NotNull(historyResult);
        Assert.IsType<List<ProcessingHistoryDto>>(historyResult);
    }

    #endregion
} 