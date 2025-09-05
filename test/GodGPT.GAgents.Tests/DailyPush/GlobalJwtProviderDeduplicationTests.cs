using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;
using GodGPT.GAgents.DailyPush;
using Aevatar.GodGPT.Tests;

namespace GodGPT.GAgents.Tests.DailyPush;

[Collection("ClusterCollection")]
public class GlobalJwtProviderDeduplicationTests : AevatarGodGPTTestsBase
{
    private readonly IGlobalJwtProviderGAgent _globalJwtProvider;
    private readonly ILogger<GlobalJwtProviderGAgent> _logger;

    public GlobalJwtProviderDeduplicationTests()
    {
        _logger = Substitute.For<ILogger<GlobalJwtProviderGAgent>>();
        _globalJwtProvider = Cluster.GrainFactory.GetGrain<IGlobalJwtProviderGAgent>(Guid.Empty);
        
        // Clear static deduplication data before each test
        ClearStaticDeduplicationData();
    }

    private static void ClearStaticDeduplicationData()
    {
        // Use reflection to clear the static _lastPushDates field
        var type = typeof(GlobalJwtProviderGAgent);
        var field = type.GetField("_lastPushDates", BindingFlags.NonPublic | BindingFlags.Static);
        if (field?.GetValue(null) is ConcurrentDictionary<string, DateOnly> dict)
        {
            dict.Clear();
        }
    }

    [Fact]
    public async Task CanSendPushAsync_SameDeviceAndHour_ShouldBlockSecondPush()
    {
        // Ensure clean state
        ClearStaticDeduplicationData();
        
        // Arrange
        var deviceId = "test-device-123";
        var pushToken = "test-token-456";
        var timeZoneId = "Asia/Shanghai";

        // Act
        var firstResult = await _globalJwtProvider.CanSendPushAsync(pushToken, timeZoneId, false, deviceId);
        var secondResult = await _globalJwtProvider.CanSendPushAsync(pushToken, timeZoneId, false, deviceId);

        // Assert
        firstResult.ShouldBeTrue("First push should be allowed");
        secondResult.ShouldBeFalse("Second push should be blocked");
    }

    [Fact]
    public async Task CanSendPushAsync_DifferentDevices_ShouldAllowBothPushes()
    {
        // Ensure clean state
        ClearStaticDeduplicationData();
        
        // Arrange
        var deviceId1 = "device-001";
        var deviceId2 = "device-002";
        var pushToken = "shared-token-123";
        var timeZoneId = "Asia/Shanghai";

        // Act
        var result1 = await _globalJwtProvider.CanSendPushAsync(pushToken, timeZoneId, false, deviceId1);
        var result2 = await _globalJwtProvider.CanSendPushAsync(pushToken, timeZoneId, false, deviceId2);

        // Assert
        result1.ShouldBeTrue("First device should be allowed");
        result2.ShouldBeTrue("Second device should be allowed (different deviceId)");
    }

    [Fact]
    public async Task CanSendPushAsync_DifferentTimezones_ShouldAllowBothPushes()
    {
        // Ensure clean state
        ClearStaticDeduplicationData();
        
        // Arrange
        var deviceId = "test-device-123";
        var pushToken = "test-token-456";
        var timeZoneId1 = "Asia/Shanghai";
        var timeZoneId2 = "America/New_York";

        // Act
        var result1 = await _globalJwtProvider.CanSendPushAsync(pushToken, timeZoneId1, false, deviceId);
        var result2 = await _globalJwtProvider.CanSendPushAsync(pushToken, timeZoneId2, false, deviceId);

        // Assert
        result1.ShouldBeTrue("First timezone should be allowed");
        result2.ShouldBeTrue("Second timezone should be allowed (different local time)");
    }

    [Fact]
    public async Task CanSendPushAsync_RetryPush_ShouldBypassDeduplication()
    {
        // Ensure clean state
        ClearStaticDeduplicationData();
        
        // Arrange
        var deviceId = "test-device-123";
        var pushToken = "test-token-456";
        var timeZoneId = "Asia/Shanghai";

        // First push (normal)
        await _globalJwtProvider.CanSendPushAsync(pushToken, timeZoneId, false, deviceId);

        // Act - Retry push should bypass deduplication
        var retryResult = await _globalJwtProvider.CanSendPushAsync(pushToken, timeZoneId, true, deviceId);

        // Assert
        retryResult.ShouldBeTrue("Retry push should bypass deduplication");
    }

    [Fact]
    public async Task CanSendPushAsync_NullDeviceId_ShouldFallbackToPushTokenDeduplication()
    {
        // Ensure clean state
        ClearStaticDeduplicationData();
        
        // Arrange
        var pushToken = "test-token-456";
        var timeZoneId = "Asia/Shanghai";

        // Act
        var firstResult = await _globalJwtProvider.CanSendPushAsync(pushToken, timeZoneId, false, null);
        var secondResult = await _globalJwtProvider.CanSendPushAsync(pushToken, timeZoneId, false, null);

        // Assert
        firstResult.ShouldBeTrue("First push should be allowed");
        secondResult.ShouldBeFalse("Second push should be blocked by pushToken deduplication");
    }

    [Fact]
    public async Task CanSendPushAsync_EmptyDeviceId_ShouldFallbackToPushTokenDeduplication()
    {
        // Ensure clean state
        ClearStaticDeduplicationData();
        
        // Arrange
        var pushToken = "test-token-456";
        var timeZoneId = "Asia/Shanghai";

        // Act
        var firstResult = await _globalJwtProvider.CanSendPushAsync(pushToken, timeZoneId, false, "");
        var secondResult = await _globalJwtProvider.CanSendPushAsync(pushToken, timeZoneId, false, "");

        // Assert
        firstResult.ShouldBeTrue("First push should be allowed");
        secondResult.ShouldBeFalse("Second push should be blocked by pushToken deduplication");
    }

    [Fact]
    public async Task CanSendPushAsync_ConcurrentRequests_ShouldOnlyAllowOne()
    {
        // Ensure clean state
        ClearStaticDeduplicationData();
        
        // Arrange
        var deviceId = "test-device-123";
        var pushToken = "test-token-456";
        var timeZoneId = "Asia/Shanghai";
        var concurrentTasks = 10;
        var tasks = new List<Task<bool>>();

        // Act - Create multiple concurrent requests
        for (int i = 0; i < concurrentTasks; i++)
        {
            tasks.Add(_globalJwtProvider.CanSendPushAsync(pushToken, timeZoneId, false, deviceId));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        var allowedCount = results.Count(r => r);
        allowedCount.ShouldBe(1, "Only one concurrent request should be allowed");
        
        var blockedCount = results.Count(r => !r);
        blockedCount.ShouldBe(concurrentTasks - 1, "All other requests should be blocked");
    }

    [Fact]
    public async Task CanSendPushAsync_TimezoneCalculation_ShouldUseLocalTime()
    {
        // Ensure clean state
        ClearStaticDeduplicationData();
        
        // Arrange
        var deviceId = "test-device-123";
        var pushToken = "test-token-456";
        
        // Test different timezones that should result in different local hours
        var utcNow = DateTime.UtcNow;
        var shanghaiFuture = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        var newYorkFuture = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        
        var shanghaiLocal = TimeZoneInfo.ConvertTimeFromUtc(utcNow, shanghaiFuture);
        var newYorkLocal = TimeZoneInfo.ConvertTimeFromUtc(utcNow, newYorkFuture);

        // Act - If local times are in different hours, both should be allowed
        var shanghaiResult = await _globalJwtProvider.CanSendPushAsync(pushToken, "Asia/Shanghai", false, deviceId);
        var newYorkResult = await _globalJwtProvider.CanSendPushAsync(pushToken, "America/New_York", false, deviceId);

        // Assert
        shanghaiResult.ShouldBeTrue("Shanghai push should be allowed");
        
        if (shanghaiLocal.Hour != newYorkLocal.Hour || shanghaiLocal.Date != newYorkLocal.Date)
        {
            newYorkResult.ShouldBeTrue("New York push should be allowed (different local time)");
        }
        else
        {
            newYorkResult.ShouldBeFalse("New York push should be blocked (same local time)");
        }
    }

    [Fact]
    public async Task CanSendPushAsync_SamePushTokenDifferentDevices_ShouldAllowBoth()
    {
        // Ensure clean state
        ClearStaticDeduplicationData();
        
        // Arrange - Simulate shared pushToken scenario (user switched devices)
        var deviceId1 = "device-001";
        var deviceId2 = "device-002";
        var sharedPushToken = "shared-token-123";
        var timeZoneId = "Asia/Shanghai";

        // Act
        var device1Result = await _globalJwtProvider.CanSendPushAsync(sharedPushToken, timeZoneId, false, deviceId1);
        var device2Result = await _globalJwtProvider.CanSendPushAsync(sharedPushToken, timeZoneId, false, deviceId2);

        // Assert
        device1Result.ShouldBeTrue("First device should be allowed");
        device2Result.ShouldBeTrue("Second device should be allowed (different deviceId)");
    }

    [Fact]
    public async Task CanSendPushAsync_SameDeviceIdDifferentPushTokens_ShouldBlockSecond()
    {
        // Ensure clean state
        ClearStaticDeduplicationData();
        
        // Arrange - Simulate same device with different pushTokens (app reinstall, etc.)
        var deviceId = "device-001";
        var pushToken1 = "token-001";
        var pushToken2 = "token-002";
        var timeZoneId = "Asia/Shanghai";

        // Act
        var token1Result = await _globalJwtProvider.CanSendPushAsync(pushToken1, timeZoneId, false, deviceId);
        var token2Result = await _globalJwtProvider.CanSendPushAsync(pushToken2, timeZoneId, false, deviceId);

        // Assert
        token1Result.ShouldBeTrue("First token should be allowed");
        token2Result.ShouldBeFalse("Second token should be blocked (same deviceId, same hour)");
    }

    [Fact]
    public async Task MarkPushSentAsync_ShouldNotAffectDeduplication()
    {
        // Ensure clean state
        ClearStaticDeduplicationData();
        
        // Arrange
        var deviceId = "test-device-123";
        var pushToken = "test-token-456";
        var timeZoneId = "Asia/Shanghai";

        // First check should pass
        var firstCheck = await _globalJwtProvider.CanSendPushAsync(pushToken, timeZoneId, false, deviceId);
        firstCheck.ShouldBeTrue();

        // Act - Mark push as sent
        await _globalJwtProvider.MarkPushSentAsync(pushToken, timeZoneId, false, true);

        // Assert - Second check should still be blocked
        var secondCheck = await _globalJwtProvider.CanSendPushAsync(pushToken, timeZoneId, false, deviceId);
        secondCheck.ShouldBeFalse("Marking push as sent should not reset deduplication");
    }

    [Fact]
    public async Task GetStatusAsync_ShouldReturnCorrectStatistics()
    {
        // Ensure clean state
        ClearStaticDeduplicationData();
        
        // Arrange
        var deviceId = "test-device-123";
        var pushToken = "test-token-456";
        var timeZoneId = "Asia/Shanghai";

        // Act - Perform some operations
        await _globalJwtProvider.CanSendPushAsync(pushToken, timeZoneId, false, deviceId);
        await _globalJwtProvider.CanSendPushAsync(pushToken, timeZoneId, false, deviceId); // Should be blocked

        var status = await _globalJwtProvider.GetStatusAsync();

        // Assert
        status.ShouldNotBeNull();
        status.TotalDeduplicationChecks.ShouldBe(2);
        status.PreventedDuplicates.ShouldBe(1);
    }
}
