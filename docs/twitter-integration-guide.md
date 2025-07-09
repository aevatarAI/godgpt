## 1、按照配置手动拉取

默认 [（上一次运行结束/当前）-1小时 ] -（当前-30 秒），每次 100 条，会存库
var tweetMonitor = ClusterClient.GetGrain<ITweetMonitorGrain>("test-monitor");

        // Act - manually trigger tweet fetching
        _logger.LogInformation("Starting manual tweet fetch test...");
        var result = await tweetMonitor.FetchTweetsManuallyAsync();

## 2、按照时间区间手动拉取推特数据，不会覆盖本地，有则跳过（必须使用 UTC 时间）

// 方式1: 直接使用UTC秒数（推荐）
var timeRange = new TimeRangeDto
{
    StartTimeUtcSecond = ((DateTimeOffset)DateTime.UtcNow.AddHours(-24)).ToUnixTimeSeconds(),
    EndTimeUtcSecond = ((DateTimeOffset)DateTime.UtcNow.AddMinutes(-30)).ToUnixTimeSeconds()
};

// 方式2: 使用便利方法
var timeRange2 = TimeRangeDto.FromDateTime(DateTime.UtcNow.AddHours(-24), DateTime.UtcNow.AddMinutes(-30));

// 方式3: 使用预定义方法
var timeRange3 = TimeRangeDto.LastHours(24); // 最近24小时

var tweetMonitor = ClusterClient.GetGrain<ITweetMonitorGrain>("test-monitor");
var refetchResult = await tweetMonitor.RefetchTweetsByTimeRangeAsync(timeRange);

## 3、定时任务的启动和停止

// 启动定时任务
var tweetMonitor = ClusterClient.GetGrain<ITweetMonitorGrain>("test-monitor");
var startResult = await tweetMonitor.StartMonitoringAsync();

// 停止定时任务
var stopResult = await tweetMonitor.StopMonitoringAsync();

// 查看定时任务状态
var statusResult = await tweetMonitor.GetMonitoringStatusAsync();

## 4、手动触发奖励计算（和定时任务使用相同逻辑）

// 获取奖励计算器实例
var rewardGrain = ClusterClient.GetGrain<ITwitterRewardGrain>("reward-calculator");

// 手动触发指定日期的奖励计算
var targetDate = DateTime.UtcNow.Date.AddDays(-1); // 昨天
var targetDateUtcSeconds = ((DateTimeOffset)targetDate).ToUnixTimeSeconds();
var result = await rewardGrain.TriggerRewardCalculationAsync(targetDateUtcSeconds);


## 5、清空领奖记录（用于测试）

var rewardGrain = ClusterClient.GetGrain<ITwitterRewardGrain>("reward-calculator");

// 清空指定日期的奖励记录
var targetDate = DateTime.UtcNow.Date.AddDays(-1); // 昨天
var targetDateUtcSeconds = ((DateTimeOffset)targetDate).ToUnixTimeSeconds();
var result = await rewardGrain.ClearRewardByDayUtcSecondAsync(targetDateUtcSeconds);

## 6、奖励定时任务启动和状态查询

// 启动奖励定时任务
var rewardGrain = ClusterClient.GetGrain<ITwitterRewardGrain>("reward-calculator");
var startResult = await rewardGrain.StartRewardCalculationAsync();

// 查询奖励定时任务状态
var statusResult = await rewardGrain.GetRewardCalculationStatusAsync();

## 7、奖励定时任务完整生命周期管理

var rewardGrain = ClusterClient.GetGrain<ITwitterRewardGrain>("reward-calculator");

// 启动奖励定时任务
var startResult = await rewardGrain.StartRewardCalculationAsync();
_logger.LogInformation("Start result: {IsSuccess}, Message: {Message}", startResult.IsSuccess, startResult.ErrorMessage);

// 查询状态
var statusResult1 = await rewardGrain.GetRewardCalculationStatusAsync();
_logger.LogInformation("Status after start: IsRunning={IsRunning}, NextCalculation={NextTime}", 
    statusResult1.Data.IsRunning, statusResult1.Data.NextScheduledCalculation);

// 停止奖励定时任务
var stopResult = await rewardGrain.StopRewardCalculationAsync();
_logger.LogInformation("Stop result: {IsSuccess}, Message: {Message}", stopResult.IsSuccess, stopResult.ErrorMessage);

// 再次查询状态确认已停止
var statusResult2 = await rewardGrain.GetRewardCalculationStatusAsync();
_logger.LogInformation("Status after stop: IsRunning={IsRunning}", statusResult2.Data.IsRunning);