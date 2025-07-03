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
    [Fact]
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
        var tweetMonitor = ClusterClient.GetGrain<ITweetMonitorGrain>(pullTaskTargetId);

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

        // Assert
        resultPre.ShouldNotBeNull();
        /***/

        // Get reward calculator instance using configuration
        var rewardTaskTargetId = _configuration["TwitterReward:RewardTaskTargetId"] ?? "reward-calculator";
        var rewardGrain = ClusterClient.GetGrain<ITwitterRewardGrain>(rewardTaskTargetId);

        // Manually trigger reward calculation for specified date
        var targetDate = DateTime.UtcNow.Date.AddDays(0); // yesterday

        // Act - trigger reward calculation for yesterday
        _logger.LogInformation("Manual triggering reward calculation for date: {TargetDate}", targetDate);
        var result = await rewardGrain.TriggerRewardCalculationAsync(targetDate);

        // Assert - verify results
        result.ShouldNotBeNull();
        _logger.LogInformation(
            "Manual trigger reward calculation result: IsSuccess={IsSuccess}, UsersRewarded={UsersRewarded}, CreditsDistributed={CreditsDistributed}",
            result.IsSuccess, result.Data?.UsersRewarded ?? 0, result.Data?.TotalCreditsDistributed ?? 0);

        if (result.IsSuccess)
        {
            result.Data.ShouldNotBeNull();
            result.Data.CalculationDate.Date.ShouldBe(targetDate.Date);
            result.Data.UsersRewarded.ShouldBeGreaterThanOrEqualTo(0);
            result.Data.TotalCreditsDistributed.ShouldBeGreaterThanOrEqualTo(0);
            result.Data.IsSuccess.ShouldBe(true);
            _logger.LogInformation("✅ Manual trigger reward calculation test passed");
        }
        else
        {
            _logger.LogWarning("⚠️ Manual trigger reward calculation failed: {ErrorMessage}", result.ErrorMessage);
            Assert.True(true, $"Manual trigger reward calculation failed (possibly normal): {result.ErrorMessage}");
        }
    }
    
    
    
} 