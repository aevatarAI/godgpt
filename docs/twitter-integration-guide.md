# Twitter Credits Reward 第三方对接指南

## 📋 概述

本文档说明如何在第三方应用中集成 GodGPT.GAgents 的 Twitter Credits Reward 系统。该系统作为 NuGet 包提供，可以自动监控指定Twitter账号的推文并发放积分奖励。

## 🚀 1. 完整的Controller接口封装

### TwitterRewardController 完整示例

```csharp
[ApiController]
[Route("api/twitter-reward")]
public class TwitterRewardController : ControllerBase
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<TwitterRewardController> _logger;

    public TwitterRewardController(IGrainFactory grainFactory, ILogger<TwitterRewardController> logger)
    {
        _grainFactory = grainFactory;
        _logger = logger;
    }

    #region 1. 任务管理接口

    /// <summary>
    /// 启动推文监控任务
    /// </summary>
    [HttpPost("tasks/monitor/start")]
    public async Task<IActionResult> StartMonitorTask()
    {
        try
        {
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            var result = await systemManager.StartTweetMonitorAsync();
            
            _logger.LogInformation($"推文监控任务启动: {(result ? "成功" : "失败")}");
            return Ok(new { success = result, message = result ? "推文监控任务已启动" : "启动失败" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动推文监控任务失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 启动奖励计算任务
    /// </summary>
    [HttpPost("tasks/reward/start")]
    public async Task<IActionResult> StartRewardTask()
    {
        try
        {
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            var result = await systemManager.StartRewardCalculationAsync();
            
            _logger.LogInformation($"奖励计算任务启动: {(result ? "成功" : "失败")}");
            return Ok(new { success = result, message = result ? "奖励计算任务已启动" : "启动失败" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动奖励计算任务失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 停止推文监控任务
    /// </summary>
    [HttpPost("tasks/monitor/stop")]
    public async Task<IActionResult> StopMonitorTask()
    {
        try
        {
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            var result = await systemManager.StopTweetMonitorAsync();
            
            _logger.LogInformation($"推文监控任务停止: {(result ? "成功" : "失败")}");
            return Ok(new { success = result, message = result ? "推文监控任务已停止" : "停止失败" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止推文监控任务失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 停止奖励计算任务
    /// </summary>
    [HttpPost("tasks/reward/stop")]
    public async Task<IActionResult> StopRewardTask()
    {
        try
        {
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            var result = await systemManager.StopRewardCalculationAsync();
            
            _logger.LogInformation($"奖励计算任务停止: {(result ? "成功" : "失败")}");
            return Ok(new { success = result, message = result ? "奖励计算任务已停止" : "停止失败" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止奖励计算任务失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 获取所有任务状态
    /// </summary>
    [HttpGet("tasks/status")]
    public async Task<IActionResult> GetTaskStatus()
    {
        try
        {
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            var status = await systemManager.GetAllTaskStatusAsync();
            
            return Ok(new { success = true, data = status });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取任务状态失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    #endregion

    #region 2. 历史数据查询接口（前N天）

    /// <summary>
    /// 查询历史推文数据（前N天）
    /// </summary>
    [HttpGet("history/tweets")]
    public async Task<IActionResult> GetHistoricalTweets([FromQuery] int days = 5)
    {
        try
        {
            var tweetMonitor = _grainFactory.GetGrain<ITweetMonitorGrain>("TweetMonitor");
            
            // 计算时间范围（前N天）
            var endTimestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var startTimestamp = endTimestamp - (days * 24 * 60 * 60);
            
            var tweets = await tweetMonitor.GetTweetsByPeriodAsync(startTimestamp, endTimestamp);
            
            _logger.LogInformation($"查询历史推文: {days}天，共{tweets.Data?.Count ?? 0}条");
            return Ok(new { success = true, data = tweets.Data, period = $"过去{days}天" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"查询历史推文失败: days={days}");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 查询用户奖励历史（前N天）
    /// </summary>
    [HttpGet("history/rewards/{userId}")]
    public async Task<IActionResult> GetUserRewardHistory(string userId, [FromQuery] int days = 5)
    {
        try
        {
            var rewardGrain = _grainFactory.GetGrain<ITwitterRewardGrain>("TwitterReward");
            var rewards = await rewardGrain.GetRewardHistoryAsync(userId, days);
            
            _logger.LogInformation($"查询用户{userId}奖励历史: {days}天，共{rewards.Data?.Count ?? 0}条");
            return Ok(new { success = true, data = rewards.Data, userId, period = $"过去{days}天" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"查询用户奖励历史失败: userId={userId}, days={days}");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 获取系统数据统计
    /// </summary>
    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics([FromQuery] int days = 7)
    {
        try
        {
            var endTimestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var startTimestamp = endTimestamp - (days * 24 * 60 * 60);
            
            var tweetMonitor = _grainFactory.GetGrain<ITweetMonitorGrain>("TweetMonitor");
            var rewardGrain = _grainFactory.GetGrain<ITwitterRewardGrain>("TwitterReward");
            
            var dataStats = await tweetMonitor.GetDataStatisticsAsync();
            var rewardStats = await rewardGrain.GetRewardStatisticsAsync(startTimestamp, endTimestamp);
            
            return Ok(new 
            { 
                success = true, 
                data = new { 
                    tweetStats = dataStats.Data,
                    rewardStats = rewardStats.Data,
                    period = $"过去{days}天"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"获取统计信息失败: days={days}");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    #endregion

    #region 3. 手动触发接口（带详细日志）

    /// <summary>
    /// 手动拉取推文（带详细日志）
    /// </summary>
    [HttpPost("manual/pull-tweets")]
    public async Task<IActionResult> ManualPullTweets([FromBody] ManualPullRequest request = null)
    {
        try
        {
            _logger.LogInformation("开始手动拉取推文...");
            
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            
            PullTweetResultDto result;
            if (request?.StartTimestamp != null && request?.EndTimestamp != null)
            {
                _logger.LogInformation($"手动拉取指定时间段: {request.StartTimestamp} - {request.EndTimestamp}");
                result = await systemManager.ManualPullTweetsAsync(request.StartTimestamp.Value, request.EndTimestamp.Value);
            }
            else
            {
                _logger.LogInformation("手动拉取最新推文");
                result = await systemManager.ManualPullTweetsAsync();
            }
            
            // 详细日志记录
            _logger.LogInformation($"推文拉取完成: 总计{result.TotalFound}条, 新增{result.NewTweets}条, 重复跳过{result.DuplicateSkipped}条, 类型过滤{result.TypeFilteredOut}条");
            
            return Ok(new { success = result.Success, data = result, message = "手动拉取完成" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "手动拉取推文失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 手动计算奖励（带详细日志）
    /// </summary>
    [HttpPost("manual/calculate-rewards")]
    public async Task<IActionResult> ManualCalculateRewards([FromBody] ManualRewardRequest request = null)
    {
        try
        {
            _logger.LogInformation("开始手动计算奖励...");
            
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            
            RewardCalculationResultDto result;
            if (request?.StartTimestamp != null && request?.EndTimestamp != null)
            {
                _logger.LogInformation($"计算指定时间段奖励: {request.StartTimestamp} - {request.EndTimestamp}");
                result = await systemManager.ManualCalculateRewardsAsync(request.StartTimestamp.Value, request.EndTimestamp.Value);
            }
            else
            {
                _logger.LogInformation("计算当前时间段奖励");
                result = await systemManager.ManualCalculateRewardsAsync();
            }
            
            // 详细日志记录
            _logger.LogInformation($"奖励计算完成: 处理推文{result.ProcessedTweets}条, 影响用户{result.AffectedUsers}个, 发放积分{result.TotalCreditsAwarded}个");
            
            if (result.UserRewards?.Any() == true)
            {
                foreach (var userReward in result.UserRewards)
                {
                    _logger.LogInformation($"用户{userReward.UserId}奖励详情: 基础{userReward.BaseRewards}, 附加{userReward.BonusRewards}, 总计{userReward.TotalRewards}, 已发送:{userReward.RewardsSent}");
                }
            }
            
            return Ok(new { success = result.Success, data = result, message = "手动奖励计算完成" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "手动计算奖励失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    #endregion

    #region 4. 测试专用重置接口

    /// <summary>
    /// 重置指定用户某天的领取状态（测试专用）
    /// </summary>
    [HttpPost("testing/reset-user-status")]
    public async Task<IActionResult> ResetUserDailyStatus([FromBody] ResetUserStatusRequest request)
    {
        try
        {
            _logger.LogWarning($"[测试重置] 准备重置用户状态: UserId={request.UserId}, Date={request.UtcDateTimestamp}");
            
            var testingGrain = _grainFactory.GetGrain<ITwitterTestingGrain>("TwitterTesting");
            
            // 获取重置前状态
            var beforeStatus = await testingGrain.GetUserDailyStatusAsync(request.UserId, request.UtcDateTimestamp);
            _logger.LogWarning($"[测试重置] 重置前状态: {System.Text.Json.JsonSerializer.Serialize(beforeStatus)}");
            
            // 执行重置
            var result = await testingGrain.ResetUserDailyStatusAsync(request.UserId, request.UtcDateTimestamp, request.ResetReason ?? "API测试重置");
            
            // 获取重置后状态
            var afterStatus = await testingGrain.GetUserDailyStatusAsync(request.UserId, request.UtcDateTimestamp);
            _logger.LogWarning($"[测试重置] 重置后状态: {System.Text.Json.JsonSerializer.Serialize(afterStatus)}");
            
            _logger.LogWarning($"[测试重置] 重置操作完成: UserId={request.UserId}, Success={result.Success}");
            
            return Ok(new 
            { 
                success = result.Success, 
                data = new { 
                    beforeStatus = beforeStatus.Data,
                    afterStatus = afterStatus.Data,
                    resetResult = result
                },
                message = result.Success ? "用户状态重置成功" : "重置失败"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"重置用户状态失败: UserId={request.UserId}");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 重置指定任务某天的执行状态（测试专用）
    /// </summary>
    [HttpPost("testing/reset-task-status")]
    public async Task<IActionResult> ResetTaskExecutionStatus([FromBody] ResetTaskStatusRequest request)
    {
        try
        {
            _logger.LogWarning($"[测试重置] 准备重置任务状态: TaskName={request.TaskName}, Date={request.UtcDateTimestamp}");
            
            var testingGrain = _grainFactory.GetGrain<ITwitterTestingGrain>("TwitterTesting");
            
            // 获取重置前状态
            var beforeStatus = await testingGrain.GetTaskExecutionStatusAsync(request.TaskName, request.UtcDateTimestamp);
            _logger.LogWarning($"[测试重置] 任务重置前状态: {System.Text.Json.JsonSerializer.Serialize(beforeStatus)}");
            
            // 执行重置
            var result = await testingGrain.ResetTaskExecutionStatusAsync(request.TaskName, request.UtcDateTimestamp, request.ResetReason ?? "API测试重置");
            
            // 获取重置后状态
            var afterStatus = await testingGrain.GetTaskExecutionStatusAsync(request.TaskName, request.UtcDateTimestamp);
            _logger.LogWarning($"[测试重置] 任务重置后状态: {System.Text.Json.JsonSerializer.Serialize(afterStatus)}");
            
            _logger.LogWarning($"[测试重置] 任务重置操作完成: TaskName={request.TaskName}, Success={result.Success}");
            
            return Ok(new 
            { 
                success = result.Success, 
                data = new { 
                    beforeStatus = beforeStatus.Data,
                    afterStatus = afterStatus.Data,
                    resetResult = result
                },
                message = result.Success ? "任务状态重置成功" : "重置失败"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"重置任务状态失败: TaskName={request.TaskName}");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 获取测试环境摘要信息
    /// </summary>
    [HttpGet("testing/summary")]
    public async Task<IActionResult> GetTestingSummary()
    {
        try
        {
            var testingGrain = _grainFactory.GetGrain<ITwitterTestingGrain>("TwitterTesting");
            var summary = await testingGrain.GetTestDataSummaryAsync();
            
            return Ok(new { success = true, data = summary });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取测试摘要失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    #endregion

    #region 5. 配置管理接口

    /// <summary>
    /// 获取当前配置
    /// </summary>
    [HttpGet("config")]
    public async Task<IActionResult> GetCurrentConfig()
    {
        try
        {
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            var config = await systemManager.GetCurrentConfigAsync();
            
            return Ok(new { success = true, data = config.Data });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取配置失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 更新时间配置（支持热更新）
    /// </summary>
    [HttpPut("config/time")]
    public async Task<IActionResult> UpdateTimeConfig([FromBody] UpdateTimeConfigRequest request)
    {
        try
        {
            _logger.LogInformation($"更新时间配置: OffsetMinutes={request.TimeOffsetMinutes}, WindowMinutes={request.TimeWindowMinutes}");
            
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            var result = await systemManager.UpdateTimeConfigAsync("RewardCalculation", request.TimeOffsetMinutes, request.TimeWindowMinutes);
            
            _logger.LogInformation($"时间配置更新: {(result ? "成功" : "失败")} - 新配置将在下次任务执行时生效");
            
            return Ok(new { success = result, message = result ? "时间配置已更新，将在下次执行时生效" : "配置更新失败" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新时间配置失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    #endregion

    #region 6. 系统健康检查

    /// <summary>
    /// 系统健康检查
    /// </summary>
    [HttpGet("health")]
    public async Task<IActionResult> GetSystemHealth()
    {
        try
        {
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            var health = await systemManager.GetSystemHealthAsync();
            
            return Ok(new { success = true, data = health.Data });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取系统健康状态失败");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// 获取处理历史
    /// </summary>
    [HttpGet("history/processing")]
    public async Task<IActionResult> GetProcessingHistory([FromQuery] int days = 7)
    {
        try
        {
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            var history = await systemManager.GetProcessingHistoryAsync(days);
            
            return Ok(new { success = true, data = history, period = $"过去{days}天" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"获取处理历史失败: days={days}");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    #endregion
}

#region 请求/响应DTO

public class ManualPullRequest
{
    public int? StartTimestamp { get; set; }
    public int? EndTimestamp { get; set; }
}

public class ManualRewardRequest
{
    public int? StartTimestamp { get; set; }
    public int? EndTimestamp { get; set; }
}

public class ResetUserStatusRequest
{
    public string UserId { get; set; }
    public int UtcDateTimestamp { get; set; }
    public string ResetReason { get; set; }
}

public class ResetTaskStatusRequest
{
    public string TaskName { get; set; }
    public int UtcDateTimestamp { get; set; }
    public string ResetReason { get; set; }
}

public class UpdateTimeConfigRequest
{
    public int TimeOffsetMinutes { get; set; }
    public int TimeWindowMinutes { get; set; }
}

#endregion

## 🔧 2. 第三方应用配置

### appsettings.json 配置添加

```json
{
  "TwitterReward": {
    // Twitter API 配置 (必需)
    "BearerToken": "your_twitter_bearer_token",
    "ApiKey": "your_twitter_api_key", 
    "ApiSecret": "your_twitter_api_secret",
    
    // 监控配置
    "MonitorHandle": "@demo_account",  // 你指定的推特账号
    "ShareLinkDomain": "https://app.godgpt.fun",
    "SelfAccountId": "your_system_account_id",
    
    // 定时任务配置
    "PullIntervalMinutes": 30,        // 推文拉取间隔（分钟）
    "PullBatchSize": 100,             // 批量拉取数量
    "EnablePullTask": true,           // 启用推文监控
    "EnableRewardTask": true,         // 启用奖励发放
    
    // 时间控制配置
    "TimeOffsetMinutes": 2880,        // 48小时前开始检查
    "TimeWindowMinutes": 1440,        // 24小时时间窗口
    
    // 数据管理
    "DataRetentionDays": 5,           // 数据保留天数
    "MaxRetryAttempts": 3,
    
    // 唯一标识符 (应用级固定配置，确保系统单实例运行)
    "PullTaskTargetId": "your-app-twitter-monitor",     // 建议格式: {appname}-twitter-monitor
    "RewardTaskTargetId": "your-app-twitter-reward"     // 建议格式: {appname}-twitter-reward
  }
}
```

### 依赖注入注册

**重要**：配置注册已在 `GodGPTGAgentModule.cs` 中完成，第三方应用无需额外注册。

```csharp
// 在 GodGPTGAgentModule.cs 中（已由系统提供）
public override void ConfigureServices(ServiceConfigurationContext context)
{
    // ... 其他配置 ...
    Configure<TwitterRewardOptions>(configuration.GetSection("TwitterReward"));
    // ...
}
```

**第三方应用只需要**：
1. ✅ 在 `appsettings.json` 中添加 `TwitterReward` 配置段
2. ✅ 确保引用了 `GodGPT.GAgents` NuGet包
3. ✅ 无需额外的服务注册

### 🏗️ 架构说明

```
第三方应用 Silo
├── appsettings.json          ← 添加 TwitterReward 配置
├── Program.cs                ← 引用 GodGPTGAgentModule
└── 业务代码                  ← 调用 Twitter 相关 Grain

GodGPT.GAgents (NuGet包)
├── GodGPTGAgentModule.cs     ← 自动注册 TwitterRewardOptions
├── TwitterSystemManagerGrain ← 提供管理接口
├── TweetMonitorGrain         ← 推文监控
├── TwitterRewardGrain        ← 奖励计算
└── TwitterInteractionGrain   ← Twitter API 交互
```

**配置注册流程**：
1. 第三方应用引用 `GodGPT.GAgents` NuGet包
2. `GodGPTGAgentModule` 自动注册 `TwitterRewardOptions`
3. 系统从第三方应用的 `appsettings.json` 读取配置
4. 第三方应用通过 Grain 接口调用功能

### 💡 设计理念：为什么配置文件固定TargetId？

#### ✅ 好处分析

| 传统做法 | 配置驱动做法 | 优势对比 |
|---------|-------------|----------|
| `StartTaskAsync("TweetMonitor", "id1")` | `StartTweetMonitorAsync()` | 🎯 API更简洁 |
| 手动管理多个ID | 配置文件统一管理 | 🔧 配置集中化 |
| 容易传错参数 | 无需传参，零错误 | 🛡️ 避免人为错误 |
| 开发者需要记住ID | 专注业务逻辑 | 🚀 开发效率提升 |

#### 🎯 核心原则

**一个应用 = 一组任务 = 一套配置**

```csharp
// ❌ 不推荐：手动管理ID，容易出错
await systemManager.StartTaskAsync("TweetMonitor", "some-id-123");
await systemManager.StartTaskAsync("RewardCalculation", "another-id-456");

// ✅ 推荐：配置驱动，专注业务
await systemManager.StartTweetMonitorAsync();    // 配置自动处理
await systemManager.StartRewardCalculationAsync(); // 配置自动处理
```

#### 🛡️ 避免常见错误

1. **ID拼写错误** → 配置文件统一管理
2. **环境混淆** → 不同环境不同配置文件
3. **重复ID冲突** → 应用级命名规范
4. **遗忘清理** → 停止任务无需指定ID

## 🚀 2. 系统启动和管理

### 获取系统管理 Grain

```csharp
var systemManager = grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
```

### 启动定时任务（推荐使用简化API）

```csharp
// ✅ 推荐方式：自动从配置读取TargetId（专注业务逻辑）
await systemManager.StartTweetMonitorAsync();        // 自动使用 PullTaskTargetId
await systemManager.StartRewardCalculationAsync();   // 自动使用 RewardTaskTargetId
```

### 🎯 设计理念：配置驱动，避免错误

**核心原则**：一个应用系统只需要一组定时任务，TargetId应该在配置文件中固定。

**优势**：
- ✅ **专注业务**: 开发者无需关心TargetId管理
- ✅ **避免错误**: 消除手动传参的人为错误
- ✅ **配置统一**: 所有环境配置在一处管理
- ✅ **运维友好**: 部署时只需修改配置文件

```csharp
// 配置文件中固定定义
{
  "TwitterReward": {
    "PullTaskTargetId": "app-twitter-monitor-prod",    // 固定ID
    "RewardTaskTargetId": "app-twitter-reward-prod"    // 固定ID
  }
}

// 业务代码中简单调用，无需传参
await systemManager.StartTweetMonitorAsync();    // 自动读取配置
await systemManager.StartRewardCalculationAsync(); // 自动读取配置
```

### 配置文件自动读取机制

- `StartTweetMonitorAsync()` → 自动读取 `PullTaskTargetId`
- `StartRewardCalculationAsync()` → 自动读取 `RewardTaskTargetId`

**业务逻辑专注点**：开发者只需关心启动/停止任务，无需管理ID。🎯

## 📊 3. 查询历史推特信息接口

### 获取推文监控状态

```csharp
var tweetMonitor = grainFactory.GetGrain<ITweetMonitorGrain>("TweetMonitor");

// 获取任务状态
var status = await tweetMonitor.GetTaskStatusAsync();

// 获取统计信息
var stats = await tweetMonitor.GetStatisticsAsync(startTime, endTime);

// 查询指定时间区间的推文
var tweets = await tweetMonitor.GetTweetsByPeriodAsync(startTimestamp, endTimestamp);
```

### 手动触发推文拉取

```csharp
// 简化方法：立即拉取推文 ✨
await systemManager.ManualPullTweetsAsync();

// 指定时间区间拉取 (如果支持)
// var result = await systemManager.ManualPullTweetsAsync(startTimestamp, endTimestamp);
```

## 🎯 4. 奖励发放定时任务接口

### 获取奖励计算状态

```csharp
var rewardGrain = grainFactory.GetGrain<ITwitterRewardGrain>("TwitterReward");

// 获取奖励任务状态
var rewardStatus = await rewardGrain.GetTaskStatusAsync();

// 查询奖励历史
var rewardHistory = await rewardGrain.GetRewardHistoryAsync(userId, days: 30);
```

### 手动触发奖励计算

```csharp
// 简化方法：立即计算奖励 ✨
await systemManager.ManualCalculateRewardsAsync();

// 指定时间区间计算 (如果支持)
// var rewardResult = await systemManager.ManualCalculateRewardsAsync(startTimestamp, endTimestamp);
```

### 系统健康检查

```csharp
// 获取系统整体健康状态
var health = await systemManager.GetSystemHealthAsync();
```

## 🔒 5. 防重复机制说明

### 每用户只领一次的保证

系统使用 `UserDailyRewardRecord` 确保每用户每天只能领取一次：

- **用户标识**: 基于 Twitter UserId
- **日期标识**: 使用 UTC 0点时间戳作为日期标识
- **重复检查**: 发放前检查用户当天是否已领取
- **限额控制**: 每用户每天最多500 Credits

```csharp
// 数据结构示例
public class UserDailyRewardRecord
{
    public string UserId { get; set; }
    public int UtcDateTimestamp { get; set; }  // UTC日期标识
    public bool HasReceivedBonusReward { get; set; }
    public int BonusTotalRewards { get; set; }
    // ...
}
```

### 定时任务每天只执行一次的保证

系统使用 `TaskDailyExecutionRecord` 和 Orleans Reminders：

- **时间控制**: 严格的UTC 00:00执行时机
- **执行记录**: `TaskDailyExecutionRecord` 记录每日执行状态
- **防重复**: 检查当天是否已执行过
- **唯一实例**: `ReminderTargetId` 确保单实例执行

```csharp
// 执行记录结构
public class TaskDailyExecutionRecord
{
    public string TaskName { get; set; }
    public int UtcDateTimestamp { get; set; }    // UTC日期标识
    public bool IsExecuted { get; set; }         // 当天是否已执行
    public int ExecutionTimestamp { get; set; }  // 执行时间戳
    // ...
}
```

## ⚙️ 6. 配置说明

### 关键配置参数

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `MonitorHandle` | 监控的Twitter账号 | `@GodGPT_` |
| `TimeOffsetMinutes` | 检查时间偏移(分钟) | `2880` (48小时) |
| `TimeWindowMinutes` | 时间窗口长度(分钟) | `1440` (24小时) |
| `PullIntervalMinutes` | 拉取间隔(分钟) | `30` |
| `DataRetentionDays` | 数据保留天数 | `5` |

### 奖励规则

- **基础奖励**: 每条原创推文 2 Credits
- **附加奖励**: 根据浏览量和粉丝数阶梯发放 (5-120 Credits)
- **分享加成**: 包含有效分享链接 ×1.1倍
- **每日上限**: 每用户最多500 Credits

## 🔍 7. 系统监控

### 获取系统指标

```csharp
// 获取系统指标
var metrics = await systemManager.GetSystemMetricsAsync();

// 获取处理历史
var history = await systemManager.GetProcessingHistoryAsync();

// 获取任务状态概览
var taskStatus = await systemManager.GetTaskStatusAsync();
```

### 停止任务

```csharp
// 简化方法 (推荐) ✨
await systemManager.StopTweetMonitorAsync();
await systemManager.StopRewardCalculationAsync();

// 通用方法
await systemManager.StopTaskAsync("TweetMonitor");
await systemManager.StopTaskAsync("RewardCalculation");
```

## ⚠️ 注意事项

1. **Twitter API 配额**: 注意API调用限制，建议使用付费账号
2. **时区处理**: 系统统一使用UTC时间
3. **数据备份**: 定期备份重要的奖励记录
4. **监控告警**: 建议配置系统健康监控
5. **测试环境**: 使用 `TwitterTestingGrain` 进行功能测试

## 🆘 故障排除

### 常见问题

1. **任务不执行**: 检查 `EnablePullTask` 和 `EnableRewardTask` 配置
2. **重复执行**: 确保 `TargetId` 配置唯一
3. **API调用失败**: 检查Twitter API密钥配置
4. **时间问题**: 确保服务器时间准确

### 数据恢复

```csharp
// 使用恢复组件修复缺失数据
var recovery = grainFactory.GetGrain<ITwitterRecoveryGrain>("TwitterRecovery");
await recovery.RecoverPeriodAsync(startTimestamp, endTimestamp);
```

## 📝 完整使用示例

```csharp
public class TwitterServiceExample
{
    private readonly IGrainFactory _grainFactory;
    
    public TwitterServiceExample(IGrainFactory grainFactory)
    {
        _grainFactory = grainFactory;
    }
    
    /// <summary>
    /// 启动Twitter奖励系统 - 配置驱动，无需手动管理ID
    /// </summary>
    public async Task StartTwitterRewardSystemAsync()
    {
        // 1. 获取系统管理器
        var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
        
        // 2. 启动任务 (自动从配置读取TargetId，专注业务逻辑)
        await systemManager.StartTweetMonitorAsync();         // 推文监控
        await systemManager.StartRewardCalculationAsync();    // 奖励计算
        
        Console.WriteLine("✅ Twitter奖励系统启动成功 - 配置驱动，无错误风险");
    }
    
    /// <summary>
    /// 获取系统运行状态
    /// </summary>
    public async Task<bool> CheckSystemHealthAsync()
    {
        var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
        
        // 检查系统健康状态
        var health = await systemManager.GetSystemHealthAsync();
        var isHealthy = health.Data?.IsHealthy ?? false;
        
        Console.WriteLine($"系统健康状态: {(isHealthy ? "✅ 正常" : "❌ 异常")}");
        return isHealthy;
    }
    
    /// <summary>
    /// 查询Twitter相关数据
    /// </summary>
    public async Task QueryTwitterDataAsync(string userId)
    {
        // 查询推文数据
        var tweetMonitor = _grainFactory.GetGrain<ITweetMonitorGrain>("TweetMonitor");
        var tweets = await tweetMonitor.GetTweetsByPeriodAsync(startTimestamp, endTimestamp);
        
        // 查询奖励历史
        var rewardGrain = _grainFactory.GetGrain<ITwitterRewardGrain>("TwitterReward");
        var rewards = await rewardGrain.GetRewardHistoryAsync(userId, days: 7);
        
        Console.WriteLine($"查询到 {tweets.Data?.Count ?? 0} 条推文，{rewards.Data?.Count ?? 0} 条奖励记录");
    }
    
    /// <summary>
    /// 优雅停止系统
    /// </summary>
    public async Task StopSystemAsync()
    {
        var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
        
        // 优雅停止所有任务 (配置驱动，无需指定ID)
        await systemManager.StopTweetMonitorAsync();
        await systemManager.StopRewardCalculationAsync();
        
        Console.WriteLine("✅ Twitter奖励系统已安全停止");
    }
}
``` 

## 🔧 配置模式优化 ✅

### 现在的正确配置模式
所有Twitter相关的Grain都已经使用了正确的配置注入模式：

```csharp
// ✅ 正确的配置模式 - 支持热更新
private readonly IOptionsMonitor<TwitterRewardOptions> _options;

public TwitterSystemManagerGrain(IOptionsMonitor<TwitterRewardOptions> options)
{
    _options = options;  // 支持热更新
}

// 使用时获取最新配置
private void SomeMethod()
{
    var config = _options.CurrentValue;  // 每次获取最新配置
    var targetId = config.PullTaskTargetId;
    var interval = config.PullIntervalMinutes;
    // ...
}
```

### 🚀 配置热更新优势

| 特性 | 支持情况 | 说明 |
|------|----------|------|
| 配置热更新 | ✅ 完全支持 | 修改appsettings.json后无需重启 |
| 运行时获取 | ✅ 实时生效 | `CurrentValue`始终返回最新配置 |
| 系统稳定性 | ✅ 生产就绪 | 与现有系统保持一致 |
| 内存效率 | ✅ 优化设计 | 仅在需要时读取配置 |

### 📋 已更新的组件

以下Grain都已使用 `IOptionsMonitor<TwitterRewardOptions>` 模式：

- ✅ `TwitterSystemManagerGrain` - 系统管理器
- ✅ `TweetMonitorGrain` - 推文监控器  
- ✅ `TwitterRewardGrain` - 奖励计算器
- ✅ `TwitterInteractionGrain` - Twitter API交互器

> **注意**: `TwitterRecoveryGrain` 和 `TwitterTestingGrain` 不使用配置注入，因此无需修改。

### 🎯 使用建议

1. **生产环境**: 配置变更会自动生效，无需重启服务
2. **开发环境**: 可以动态调整参数进行测试
3. **监控告警**: 配置变更会立即反映在系统行为中
4. **版本兼容**: 完全向后兼容，现有代码无需修改
5. **配置驱动**: TargetId固定在配置文件，API调用零参数，专注业务逻辑

### 🌟 最佳实践总结

```csharp
// 🎯 完美的第三方集成方式
public class MyAppTwitterService
{
    private readonly IGrainFactory _grainFactory;
    
    // 1. 配置文件中固定ID（appsettings.json）
    // "PullTaskTargetId": "myapp-twitter-monitor"
    // "RewardTaskTargetId": "myapp-twitter-reward"
    
    // 2. 业务代码专注逻辑，无需管理ID
    public async Task StartAsync()
    {
        var manager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
        await manager.StartTweetMonitorAsync();      // 配置自动处理
        await manager.StartRewardCalculationAsync(); // 配置自动处理
    }
    
    // 3. 停止同样简单，无错误风险
    public async Task StopAsync()
    {
        var manager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
        await manager.StopTweetMonitorAsync();
        await manager.StopRewardCalculationAsync();
    }
}
```

✨ **现在你的系统已经完全使用了最佳实践的配置管理模式！配置驱动，专注业务，零错误风险！**

## 🚀 3. 部署配置指南

### 生产环境部署步骤

```bash
# 1. 配置应用程序设置
# 编辑 appsettings.Production.json
{
  "TwitterReward": {
    "BearerToken": "your_production_bearer_token",
    "ApiKey": "your_production_api_key",
    "ApiSecret": "your_production_api_secret",
    "MonitorHandle": "@your_production_account",
    "PullTaskTargetId": "prod-twitter-monitor-v1",
    "RewardTaskTargetId": "prod-twitter-reward-v1",
    "EnablePullTask": true,
    "EnableRewardTask": true
  }
}

# 2. 验证配置文件
dotnet run --environment=Production --verify-config

# 3. 启动应用
dotnet run --environment=Production

# 4. 验证系统健康状态
curl http://localhost:5000/api/twitter-reward/health
```

### 配置文件模板生成器

```csharp
public static class TwitterConfigGenerator
{
    public static string GenerateProductionConfig(string appName, string environment)
    {
        var config = new
        {
            TwitterReward = new
            {
                // 必填项
                BearerToken = "REPLACE_WITH_YOUR_BEARER_TOKEN",
                ApiKey = "REPLACE_WITH_YOUR_API_KEY", 
                ApiSecret = "REPLACE_WITH_YOUR_API_SECRET",
                MonitorHandle = "@REPLACE_WITH_MONITOR_HANDLE",
                
                // 自动生成唯一ID
                PullTaskTargetId = $"{appName}-twitter-monitor-{environment}",
                RewardTaskTargetId = $"{appName}-twitter-reward-{environment}",
                
                // 默认配置
                ShareLinkDomain = "https://app.godgpt.fun",
                PullIntervalMinutes = 30,
                EnablePullTask = true,
                EnableRewardTask = true,
                TimeOffsetMinutes = 2880,
                TimeWindowMinutes = 1440,
                DataRetentionDays = 5
            }
        };
        
        return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
    }
}

// 使用示例
var prodConfig = TwitterConfigGenerator.GenerateProductionConfig("myapp", "prod");
File.WriteAllText("appsettings.Production.json", prodConfig);
```

## ⚡ 4. 配置热更新机制详解

### 🔥 支持热更新的配置项

| 配置项 | 热更新支持 | 生效时机 | 说明 |
|--------|------------|----------|------|
| `PullIntervalMinutes` | ✅ 完全支持 | 下次定时执行 | 拉取间隔动态调整 |
| `PullBatchSize` | ✅ 完全支持 | 立即生效 | 批量大小即时更新 |
| `TimeOffsetMinutes` | ✅ 完全支持 | 下次奖励计算 | 时间偏移参数更新 |
| `TimeWindowMinutes` | ✅ 完全支持 | 下次奖励计算 | 时间窗口参数更新 |
| `DataRetentionDays` | ✅ 完全支持 | 下次清理任务 | 数据保留策略更新 |
| `EnablePullTask` | ✅ 完全支持 | 立即生效 | 任务开关即时控制 |
| `EnableRewardTask` | ✅ 完全支持 | 立即生效 | 任务开关即时控制 |
| **不建议热更新** | ⚠️ | | |
| `PullTaskTargetId` | ❌ 不建议 | - | 会导致重复任务实例 |
| `RewardTaskTargetId` | ❌ 不建议 | - | 会导致重复任务实例 |
| `BearerToken` | ⚠️ 建议重启 | - | API认证敏感信息 |

### 🔄 配置热更新验证

```csharp
[HttpGet("config/validate-hotreload")]
public async Task<IActionResult> ValidateHotReload()
{
    var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
    
    // 1. 记录当前配置
    var beforeConfig = await systemManager.GetCurrentConfigAsync();
    var beforeTime = DateTime.UtcNow;
    
    // 2. 等待配置文件更新（用户手动修改appsettings.json）
    await Task.Delay(2000);
    
    // 3. 验证新配置已生效
    var afterConfig = await systemManager.GetCurrentConfigAsync();
    var afterTime = DateTime.UtcNow;
    
    var configChanged = !beforeConfig.Equals(afterConfig);
    
    return Ok(new 
    { 
        success = true,
        hotReloadWorking = configChanged,
        beforeConfig = beforeConfig.Data,
        afterConfig = afterConfig.Data,
        checkTime = afterTime,
        message = configChanged ? "✅ 配置热更新正常工作" : "⚠️ 配置未发生变化"
    });
}
```

### 📝 配置更新最佳实践

```bash
# 1. 备份当前配置
cp appsettings.json appsettings.json.backup

# 2. 修改配置文件（示例：调整拉取间隔）
{
  "TwitterReward": {
    "PullIntervalMinutes": 15,  // 从30分钟改为15分钟
    "TimeOffsetMinutes": 1440   // 从48小时改为24小时
  }
}

# 3. 验证配置更新是否生效（无需重启应用）
curl http://localhost:5000/api/twitter-reward/config

# 4. 查看任务状态确认新配置已应用
curl http://localhost:5000/api/twitter-reward/tasks/status
```

## 📊 5. 实际对接操作清单

### ✅ 对接操作检查清单

| 序号 | 操作 | API接口 | 状态检查 |
|------|------|---------|----------|
| 1 | 封装对接接口 | `TwitterRewardController` | ✅ 已提供完整示例 |
| 2 | 写Controller | 上述完整Controller | ✅ 包含6大功能模块 |
| 3 | 部署配置 | `appsettings.json` | ✅ 提供配置模板生成器 |
| 4 | 追溯历史信息（前5天） | `GET /history/tweets?days=5` | ✅ 支持自定义天数 |
| 5 | 手动拉取+日志查看 | `POST /manual/pull-tweets` | ✅ 详细日志记录 |
| 6 | 指定时间段奖励发放 | `POST /manual/calculate-rewards` | ✅ 支持时间段指定 |
| 7 | 启动定时任务 | `POST /tasks/*/start` | ✅ 配置驱动，零错误 |
| 8 | 停止定时任务 | `POST /tasks/*/stop` | ✅ 安全停止机制 |
| 9 | 修改任务参数热更新 | `PUT /config/time` | ✅ 无需重启，即时生效 |
| 10 | 测试重置功能 | `POST /testing/reset-*-status` | ✅ 安全的状态重置 |

### 🔧 测试场景专用接口

#### 重置用户某天领取状态
```bash
# 重置用户123在2024-01-15的领取状态
curl -X POST http://localhost:5000/api/twitter-reward/testing/reset-user-status \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "123456789",
    "utcDateTimestamp": 1705276800,
    "resetReason": "测试重复领取功能"
  }'
```

#### 重置任务执行状态
```bash
# 重置奖励计算任务在2024-01-15的执行状态
curl -X POST http://localhost:5000/api/twitter-reward/testing/reset-task-status \
  -H "Content-Type: application/json" \
  -d '{
    "taskName": "RewardCalculation",
    "utcDateTimestamp": 1705276800,
    "resetReason": "测试任务重复执行"
  }'
```

## 📋 6. 详细日志记录规范

### 用户奖励处理日志格式

```csharp
// 奖励处理前状态
_logger.LogInformation($"[TwitterReward] User {userId} reward processing started:");
_logger.LogInformation($"  - Before: BaseTweets={userRecord.BaseTweetCount}, BonusReceived={userRecord.HasReceivedBonusReward}, TotalRewards={userRecord.BaseTotalRewards + userRecord.BonusTotalRewards}");
_logger.LogInformation($"  - Processing Period: {startTime:yyyy-MM-dd HH:mm:ss UTC} - {endTime:yyyy-MM-dd HH:mm:ss UTC}");

// 奖励计算过程
foreach (var tweet in userTweets)
{
    _logger.LogInformation($"  - Tweet {tweet.TweetId}: Views={tweet.ViewCount}, Followers={tweet.FollowerCount}, BaseReward={baseReward}, BonusReward={bonusReward}, ShareLink={tweet.HasValidShareLink}");
}

// 奖励处理后状态
_logger.LogInformation($"  - After: BaseTweets={newUserRecord.BaseTweetCount}, BonusReceived={newUserRecord.HasReceivedBonusReward}, TotalRewards={newUserRecord.BaseTotalRewards + newUserRecord.BonusTotalRewards}");
_logger.LogInformation($"  - Credits Sent: {totalCreditsAwarded}, Success: {creditsSentSuccessfully}");
```

### 重置操作安全日志

```csharp
// 重置前记录
_logger.LogWarning($"[RESET_OPERATION] User Status Reset Initiated:");
_logger.LogWarning($"  - UserId: {userId}");
_logger.LogWarning($"  - UTC Date: {utcDateTimestamp} ({DateTimeOffset.FromUnixTimeSeconds(utcDateTimestamp):yyyy-MM-dd})");
_logger.LogWarning($"  - Operator: {operatorContext}");
_logger.LogWarning($"  - Reason: {resetReason}");
_logger.LogWarning($"  - Before Reset: {JsonSerializer.Serialize(beforeStatus)}");

// 重置操作执行
_logger.LogWarning($"[RESET_OPERATION] Executing reset for User {userId}...");

// 重置后记录
_logger.LogWarning($"[RESET_OPERATION] User Status Reset Completed:");
_logger.LogWarning($"  - After Reset: {JsonSerializer.Serialize(afterStatus)}");
_logger.LogWarning($"  - Success: {resetResult.Success}");
_logger.LogWarning($"  - Timestamp: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss UTC}");
```

## 🎯 7. 使用示例总结

### 完整的第三方集成示例

```csharp
public class TwitterRewardService
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<TwitterRewardService> _logger;
    
    public TwitterRewardService(IGrainFactory grainFactory, ILogger<TwitterRewardService> logger)
    {
        _grainFactory = grainFactory;
        _logger = logger;
    }
    
    /// <summary>
    /// 完整的系统启动流程
    /// </summary>
    public async Task<bool> StartSystemAsync()
    {
        try
        {
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            
            // 1. 检查系统健康状态
            var health = await systemManager.GetSystemHealthAsync();
            if (health.Data?.IsHealthy != true)
            {
                _logger.LogError("系统健康检查失败，无法启动");
                return false;
            }
            
            // 2. 启动任务（配置驱动，无需管理ID）
            var monitorResult = await systemManager.StartTweetMonitorAsync();
            var rewardResult = await systemManager.StartRewardCalculationAsync();
            
            if (monitorResult && rewardResult)
            {
                _logger.LogInformation("✅ Twitter奖励系统启动成功");
                
                // 3. 验证任务状态
                var taskStatus = await systemManager.GetAllTaskStatusAsync();
                _logger.LogInformation($"当前运行任务数: {taskStatus.Count(t => t.IsRunning)}");
                
                return true;
            }
            else
            {
                _logger.LogError($"任务启动失败: Monitor={monitorResult}, Reward={rewardResult}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动Twitter奖励系统失败");
            return false;
        }
    }
    
    /// <summary>
    /// 查询用户最近5天的数据
    /// </summary>
    public async Task<object> GetUserRecentDataAsync(string userId)
    {
        try
        {
            // 1. 查询用户奖励历史
            var rewardGrain = _grainFactory.GetGrain<ITwitterRewardGrain>("TwitterReward");
            var rewards = await rewardGrain.GetRewardHistoryAsync(userId, days: 5);
            
            // 2. 查询相关推文数据
            var tweetMonitor = _grainFactory.GetGrain<ITweetMonitorGrain>("TweetMonitor");
            var endTimestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var startTimestamp = endTimestamp - (5 * 24 * 60 * 60);
            var tweets = await tweetMonitor.GetTweetsByPeriodAsync(startTimestamp, endTimestamp);
            
            // 3. 过滤用户相关推文
            var userTweets = tweets.Data?.Where(t => t.AuthorId == userId).ToList() ?? new List<TweetRecordDto>();
            
            _logger.LogInformation($"用户{userId}最近5天数据: 奖励{rewards.Data?.Count ?? 0}条, 推文{userTweets.Count}条");
            
            return new
            {
                userId,
                period = "过去5天",
                rewards = rewards.Data,
                tweets = userTweets,
                summary = new
                {
                    totalRewards = rewards.Data?.Sum(r => r.TotalRewards) ?? 0,
                    totalTweets = userTweets.Count,
                    avgRewardPerTweet = userTweets.Count > 0 ? (rewards.Data?.Sum(r => r.TotalRewards) ?? 0) / userTweets.Count : 0
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"查询用户数据失败: userId={userId}");
            throw;
        }
    }
    
    /// <summary>
    /// 测试专用：重置用户状态并重新计算奖励
    /// </summary>
    public async Task<object> TestResetAndRecalculateAsync(string userId, int utcDateTimestamp)
    {
        try
        {
            var testingGrain = _grainFactory.GetGrain<ITwitterTestingGrain>("TwitterTesting");
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            
            _logger.LogWarning($"[测试流程] 开始重置用户{userId}在{utcDateTimestamp}的状态");
            
            // 1. 重置用户状态
            var resetResult = await testingGrain.ResetUserDailyStatusAsync(userId, utcDateTimestamp, "API测试重置");
            if (!resetResult.Success)
            {
                throw new Exception($"重置用户状态失败: {resetResult.ErrorMessage}");
            }
            
            // 2. 重置任务执行状态
            var taskResetResult = await testingGrain.ResetTaskExecutionStatusAsync("RewardCalculation", utcDateTimestamp, "API测试重置");
            if (!taskResetResult.Success)
            {
                throw new Exception($"重置任务状态失败: {taskResetResult.ErrorMessage}");
            }
            
            // 3. 手动触发奖励重新计算
            var startOfDay = utcDateTimestamp;
            var endOfDay = startOfDay + (24 * 60 * 60) - 1;
            var recalcResult = await systemManager.ManualCalculateRewardsAsync(startOfDay, endOfDay);
            
            _logger.LogWarning($"[测试流程] 重置和重计算完成: Success={recalcResult.Success}");
            
            return new
            {
                success = true,
                resetResult,
                taskResetResult,
                recalcResult,
                message = "测试重置和重计算完成"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"测试重置失败: userId={userId}");
            throw;
        }
    }
}
```
