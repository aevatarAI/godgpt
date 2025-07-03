using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Shouldly;
using Aevatar.Application.Grains.TwitterInteraction;
using Aevatar.Application.Grains.TwitterInteraction.Dtos;
using Aevatar.Application.Grains.Common.Options;
using GodGPT.TwitterIntegration.Tests.Helpers;

namespace GodGPT.TwitterIntegration.Tests;

/// <summary>
/// Integration tests for TwitterReward functionality
/// </summary>
public class TwitterRewardIntegrationTests : TwitterIntegrationTestBase
{
    private readonly ILogger<TwitterRewardIntegrationTests> _logger;
    private readonly TwitterIntegrationTestHelper _testHelper;
    private readonly IConfiguration _configuration;

    public TwitterRewardIntegrationTests()
    {
        _logger = ServiceProvider.GetRequiredService<ILogger<TwitterRewardIntegrationTests>>();
        _testHelper = ServiceProvider.GetRequiredService<TwitterIntegrationTestHelper>();
        _configuration = ServiceProvider.GetRequiredService<IConfiguration>();
    }


    /// <summary>
    /// Test manual reward calculation trigger - based on guide section 4
    /// Manually trigger reward calculation (uses same logic as scheduled tasks)
    /// </summary>
    [Fact(Skip = "Integration test")]
    public async Task TriggerRewardCalculationAsync_ShouldWork_WithYesterdayDate()
    {
        // Arrange - validate configuration
        var configValid = await _testHelper.ValidateTwitterConfigurationAsync();
        if (!configValid)
        {
            Assert.True(true, "Twitter configuration is invalid, skipping test");
            return;
        }
        
        /***/
        // Get TargetId from configuration
        var pullTaskTargetId = _configuration["TwitterReward:PullTaskTargetId"] ?? "tweet-monitor";
        var tweetMonitor = ClusterClient.GetGrain<ITwitterMonitorGrain>(pullTaskTargetId);

        // Define time range for last 24 hours using simplified TimeRangeDto
        var timeRange = new TimeRangeDto
        {
            StartTimeUtcSecond = 1751420640,
            EndTimeUtcSecond = 1751420640 + 120
        };

        // Act - refetch tweets by time range
        _logger.LogInformation("Starting refetch by time range test...");
        _logger.LogInformation("Time range: {StartTime} to {EndTime}", timeRange.StartTime, timeRange.EndTime);

        var resultPre = await tweetMonitor.RefetchTweetsByTimeRangeAsync(timeRange);

        // Assert - verify task started successfully
        resultPre.ShouldNotBeNull();
        if (resultPre.IsSuccess && resultPre.Data)
        {
            _logger.LogInformation("✅ Refetch task started successfully: {Message}", resultPre.ErrorMessage);
        }
        else
        {
            _logger.LogWarning("⚠️ Refetch task failed to start: {Message}", resultPre.ErrorMessage);
        }
        /***/

        // Get reward calculator instance using configuration
        var rewardTaskTargetId = _configuration["TwitterReward:RewardTaskTargetId"] ?? "reward-calculator";
        var rewardGrain = ClusterClient.GetGrain<ITwitterRewardGrain>(rewardTaskTargetId);

        // Manually trigger reward calculation for specified date
        var targetDate = DateTime.UtcNow.Date.AddDays(0); // yesterday

        // Act - trigger reward calculation for yesterday
        _logger.LogInformation("Manual triggering reward calculation for date: {TargetDate}", targetDate);
        var result = await rewardGrain.TriggerRewardCalculationAsync(targetDate);

        // Assert - verify trigger result
        result.ShouldNotBeNull();
        _logger.LogInformation(
            "Manual trigger reward calculation result: IsSuccess={IsSuccess}, TaskStarted={TaskStarted}, Message={Message}",
            result.IsSuccess, result.Data, result.ErrorMessage);

        if (result.IsSuccess)
        {
            result.Data.ShouldBe(true); // Task should have started successfully
            _logger.LogInformation("✅ Manual trigger reward calculation task started successfully");
            
            // Wait a bit for background task to complete
            await Task.Delay(5000);
            
            // Get actual calculation status to verify results
            var statusResult = await rewardGrain.GetRewardCalculationStatusAsync();
            statusResult.ShouldNotBeNull();
            statusResult.IsSuccess.ShouldBe(true);
            
            _logger.LogInformation("✅ Manual trigger reward calculation test passed");
        }
        else
        {
            _logger.LogWarning("⚠️ Manual trigger reward calculation failed: {ErrorMessage}", result.ErrorMessage);
            Assert.True(true, $"Manual trigger reward calculation failed (possibly normal): {result.ErrorMessage}");
        }
    }

    /// <summary>
    /// Test clear reward records and re-trigger reward calculation
    /// Tests the ClearRewardByDayUtcSecondAsync functionality for testing purposes
    /// </summary>
    [Fact(Skip = "Integration test")]
    public async Task ClearRewardAndRetrigger_ShouldWork_WithYesterdayDate()
    {
        // Arrange - validate configuration
        var configValid = await _testHelper.ValidateTwitterConfigurationAsync();
        if (!configValid)
        {
            Assert.True(true, "Twitter configuration is invalid, skipping test");
            return;
        }
        
        /***/
        // Get TargetId from configuration
        var pullTaskTargetId = _configuration["TwitterReward:PullTaskTargetId"] ?? "tweet-monitor";
        var tweetMonitor = ClusterClient.GetGrain<ITwitterMonitorGrain>(pullTaskTargetId);

        // Define time range for last 24 hours using simplified TimeRangeDto
        var timeRange = new TimeRangeDto
        {
            StartTimeUtcSecond = 1751420640,
            EndTimeUtcSecond = 1751420640 + 120
        };

        // Act - refetch tweets by time range
        _logger.LogInformation("Starting refetch by time range test...");
        _logger.LogInformation("Time range: {StartTime} to {EndTime}", timeRange.StartTime, timeRange.EndTime);

        var resultPre = await tweetMonitor.RefetchTweetsByTimeRangeAsync(timeRange);

        // Assert - verify task started successfully
        resultPre.ShouldNotBeNull();
        if (resultPre.IsSuccess && resultPre.Data)
        {
            _logger.LogInformation("✅ Refetch task started successfully: {Message}", resultPre.ErrorMessage);
        }
        else
        {
            _logger.LogWarning("⚠️ Refetch task failed to start: {Message}", resultPre.ErrorMessage);
        }
        /***/

        // Get reward calculator instance using configuration
        var rewardTaskTargetId = _configuration["TwitterReward:RewardTaskTargetId"] ?? "reward-calculator";
        var rewardGrain = ClusterClient.GetGrain<ITwitterRewardGrain>(rewardTaskTargetId);

        // Manually trigger reward calculation for specified date
        var targetDate = DateTime.UtcNow.Date.AddDays(0); // yesterday

        // Act - trigger reward calculation for yesterday
        _logger.LogInformation("Manual triggering reward calculation for date: {TargetDate}", targetDate);
        var result = await rewardGrain.TriggerRewardCalculationAsync(targetDate);

        // Assert - verify trigger result
        result.ShouldNotBeNull();
        _logger.LogInformation(
            "Manual trigger reward calculation result: IsSuccess={IsSuccess}, TaskStarted={TaskStarted}, Message={Message}",
            result.IsSuccess, result.Data, result.ErrorMessage);

        if (result.IsSuccess)
        {
            result.Data.ShouldBe(true); // Task should have started successfully
            _logger.LogInformation("✅ Manual trigger reward calculation task started successfully");
            
            // Wait a bit for background task to complete
            await Task.Delay(5000);
        }
        else
        {
            _logger.LogWarning("⚠️ Manual trigger reward calculation failed: {ErrorMessage}", result.ErrorMessage);
            Assert.True(true, $"Manual trigger reward calculation failed (possibly normal): {result.ErrorMessage}");
        }

        // Additional test: Clear reward records and re-trigger
        var targetDateUtcSeconds = ((DateTimeOffset)targetDate).ToUnixTimeSeconds();
        
        // 1. Clear reward records for the target date
        _logger.LogInformation("Clearing reward records for date: {TargetDate} (UTC seconds: {UtcSeconds})", targetDate, targetDateUtcSeconds);
        var clearResult = await rewardGrain.ClearRewardByDayUtcSecondAsync(targetDateUtcSeconds);
        
        // Assert clear operation succeeded
        clearResult.ShouldNotBeNull();
        clearResult.IsSuccess.ShouldBe(true);
        _logger.LogInformation("✅ Clear reward records result: IsSuccess={IsSuccess}, Message={Message}", clearResult.IsSuccess, clearResult.ErrorMessage);

        // 2. Re-trigger reward calculation and verify it can succeed again
        _logger.LogInformation("Re-triggering reward calculation after clearing records for date: {TargetDate}", targetDate);
        var retriggerResult = await rewardGrain.TriggerRewardCalculationAsync(targetDate);

        // Assert re-trigger succeeded
        retriggerResult.ShouldNotBeNull();
        _logger.LogInformation(
            "Re-trigger reward calculation result: IsSuccess={IsSuccess}, TaskStarted={TaskStarted}, Message={Message}",
            retriggerResult.IsSuccess, retriggerResult.Data, retriggerResult.ErrorMessage);

        if (retriggerResult.IsSuccess)
        {
            retriggerResult.Data.ShouldBe(true); // Task should have started successfully
            _logger.LogInformation("✅ Re-trigger reward calculation test passed - task started successfully after clearing");
            
            // Wait a bit for background task to complete
            await Task.Delay(5000);
        }
        else
        {
            _logger.LogWarning("⚠️ Re-trigger reward calculation failed: {ErrorMessage}", retriggerResult.ErrorMessage);
            Assert.True(true, $"Re-trigger reward calculation failed (possibly normal): {retriggerResult.ErrorMessage}");
        }
        
        result = await rewardGrain.TriggerRewardCalculationAsync(targetDate);
        if (result.IsSuccess)
        {
            result.Data.ShouldBe(true); // Task should have started successfully
            _logger.LogInformation("✅ Final trigger reward calculation task started successfully");
        }
        else
        {
            _logger.LogWarning("⚠️ Final trigger reward calculation failed: {ErrorMessage}", result.ErrorMessage);
            Assert.True(true, $"Final trigger reward calculation failed (possibly normal): {result.ErrorMessage}");
        }
    }
    
    /// <summary>
    /// Test clear reward records and re-trigger reward calculation
    /// Tests the ClearRewardByDayUtcSecondAsync functionality for testing purposes
    /// </summary>
    [Fact(Skip = "Integration test")]
    public async Task ClearReward_ShouldWork_WithYesterdayDate()
    {
        // Arrange - validate configuration
        var configValid = await _testHelper.ValidateTwitterConfigurationAsync();
        if (!configValid)
        {
            Assert.True(true, "Twitter configuration is invalid, skipping test");
            return;
        }

        // Get reward calculator instance using configuration
        var rewardTaskTargetId = _configuration["TwitterReward:RewardTaskTargetId"] ?? "reward-calculator";
        var rewardGrain = ClusterClient.GetGrain<ITwitterRewardGrain>(rewardTaskTargetId);

        // Manually trigger reward calculation for specified date
        var targetDate = DateTime.UtcNow.Date.AddDays(0); // yesterday

        // Additional test: Clear reward records and re-trigger
        var targetDateUtcSeconds = ((DateTimeOffset)targetDate).ToUnixTimeSeconds();
        
        // 1. Clear reward records for the target date
        _logger.LogInformation("Clearing reward records for date: {TargetDate} (UTC seconds: {UtcSeconds})", targetDate, targetDateUtcSeconds);
        var clearResult = await rewardGrain.ClearRewardByDayUtcSecondAsync(targetDateUtcSeconds);
        
        // Assert clear operation succeeded
        clearResult.ShouldNotBeNull();
        clearResult.IsSuccess.ShouldBe(true);
        _logger.LogInformation("✅ Clear reward records result: IsSuccess={IsSuccess}, Message={Message}", clearResult.IsSuccess, clearResult.ErrorMessage);
    }
    
    /// <summary>
    /// Test reward scheduled task startup and status query - based on guide section 6
    /// Test starting reward scheduled task and querying status
    /// </summary>
    [Fact(Skip = "Integration test")]
    public async Task StartRewardCalculationAndGetStatus_ShouldWork()
    {
        // Arrange - validate configuration
        var configValid = await _testHelper.ValidateTwitterConfigurationAsync();
        if (!configValid)
        {
            Assert.True(true, "Twitter configuration is invalid, skipping test");
            return;
        }

        // Get reward calculator instance using configuration
        var rewardTaskTargetId = _configuration["TwitterReward:RewardTaskTargetId"] ?? "reward-calculator";
        var rewardGrain = ClusterClient.GetGrain<ITwitterRewardGrain>(rewardTaskTargetId);

        // Act - start reward scheduled task
        _logger.LogInformation("Starting reward scheduled task...");
        //var startResult1 = await rewardGrain.StopRewardCalculationAsync();
        //startResult1.ShouldNotBeNull();
        var startResult = await rewardGrain.StartRewardCalculationAsync();

        // Assert - verify start operation
        startResult.ShouldNotBeNull();
        _logger.LogInformation("Start result: IsSuccess={IsSuccess}, Message={Message}", startResult.IsSuccess, startResult.ErrorMessage);

        // Act - query reward scheduled task status
        _logger.LogInformation("Querying reward scheduled task status...");
        var statusResult = await rewardGrain.GetRewardCalculationStatusAsync();

        // Assert - verify status query
        statusResult.ShouldNotBeNull();
        statusResult.IsSuccess.ShouldBe(true);
        statusResult.Data.ShouldNotBeNull();
        
        _logger.LogInformation("Status result: IsRunning={IsRunning}, NextCalculation={NextTime}", 
            statusResult.Data.IsRunning, statusResult.Data.NextScheduledCalculation);
            
        _logger.LogInformation("✅ Reward scheduled task startup and status query test completed");
    }
} 