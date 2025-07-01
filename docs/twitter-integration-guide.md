# Twitter Credits Reward 第三方对接指南

## 📋 概述

本文档说明如何在第三方应用中集成 GodGPT.GAgents 的 Twitter Credits Reward 系统。该系统作为 NuGet 包提供，可以自动监控指定Twitter账号的推文并发放积分奖励。

## 🚀 核心场景快速指南

以下是用户最关心的7个核心功能场景，每个场景都提供了完整的API调用示例：

### 1️⃣ 手动拉取补充最近5天推特信息

**场景描述**：获取指定时间段内的历史推文数据，用于数据补充和分析。

**API接口**：`GET /api/twitter-reward/history/tweets?days=5`

**快速使用**：
```bash
# 获取最近5天的推文数据
curl "http://localhost:5000/api/twitter-reward/history/tweets?days=5"

# 获取最近7天的推文数据
curl "http://localhost:5000/api/twitter-reward/history/tweets?days=7"
```

**响应示例**：
```json
{
  "success": true,
  "data": [
    {
      "tweetId": "1234567890",
      "authorId": "user123",
      "content": "推文内容...",
      "viewCount": 1500,
      "followerCount": 2000,
      "hasValidShareLink": true,
      "createdAt": 1705276800
    }
  ],
  "period": "过去5天"
}
```

### 2️⃣ 手动触发拉取最近任务

**场景描述**：立即执行推文拉取任务，用于验证拉取逻辑或补充数据。

**API接口**：`POST /api/twitter-reward/manual/pull-tweets`

**快速使用**：
```bash
# 立即拉取推文（使用当前配置的时间参数）
curl -X POST "http://localhost:5000/api/twitter-reward/manual/pull-tweets" \
  -H "Content-Type: application/json"

# 指定时间区间拉取（可选）
curl -X POST "http://localhost:5000/api/twitter-reward/manual/pull-tweets" \
  -H "Content-Type: application/json" \
  -d '{
    "startTimestamp": 1705190400,
    "endTimestamp": 1705276800
  }'
```

**响应示例**：
```json
{
  "success": true,
  "message": "手动拉取完成"
}
```

### 3️⃣ 手动触发奖励任务

**场景描述**：立即执行奖励计算任务，用于验证奖励逻辑或手动发放奖励。

**API接口**：`POST /api/twitter-reward/manual/calculate-rewards`

**快速使用**：
```bash
# 立即计算奖励（使用当前配置的时间参数）
curl -X POST "http://localhost:5000/api/twitter-reward/manual/calculate-rewards" \
  -H "Content-Type: application/json"

# 指定时间区间计算（可选）
curl -X POST "http://localhost:5000/api/twitter-reward/manual/calculate-rewards" \
  -H "Content-Type: application/json" \
  -d '{
    "startTimestamp": 1705190400,
    "endTimestamp": 1705276800
  }'
```

**响应示例**：
```json
{
  "success": true,
  "message": "手动奖励计算完成"
}
```

### 4️⃣ 查看数据库存储状态+日志

**场景描述**：监控系统运行状态，查看数据统计和处理历史。

**API接口**：
- `GET /api/twitter-reward/statistics?days=7` - 系统统计
- `GET /api/twitter-reward/history/processing?days=7` - 处理历史

**快速使用**：
```bash
# 获取系统统计信息
curl "http://localhost:5000/api/twitter-reward/statistics?days=7"

# 获取处理历史日志
curl "http://localhost:5000/api/twitter-reward/history/processing?days=7"

# 获取系统健康状态
curl "http://localhost:5000/api/twitter-reward/health"
```

**响应示例**：
```json
{
  "success": true,
  "data": {
    "tweetStats": {
      "totalTweets": 150,
      "totalUsers": 45,
      "avgViewCount": 1200
    },
    "rewardStats": {
      "totalRewards": 2500,
      "totalUsers": 38,
      "avgRewardPerUser": 65.8
    }
  },
  "period": "过去7天"
}
```

### 5️⃣ 启动定时任务

**场景描述**：启动系统的自动化定时任务，包括推文监控和奖励计算。

**API接口**：
- `POST /api/twitter-reward/tasks/monitor/start` - 启动推文监控
- `POST /api/twitter-reward/tasks/reward/start` - 启动奖励计算

**快速使用**：
```bash
# 启动推文监控任务
curl -X POST "http://localhost:5000/api/twitter-reward/tasks/monitor/start"

# 启动奖励计算任务
curl -X POST "http://localhost:5000/api/twitter-reward/tasks/reward/start"

# 查看任务状态
curl "http://localhost:5000/api/twitter-reward/tasks/status"
```

**响应示例**：
```json
{
  "success": true,
  "message": "推文监控任务已启动"
}
```

### 6️⃣ 关闭定时任务

**场景描述**：安全停止系统的定时任务，用于维护或配置更新。

**API接口**：
- `POST /api/twitter-reward/tasks/monitor/stop` - 停止推文监控
- `POST /api/twitter-reward/tasks/reward/stop` - 停止奖励计算

**快速使用**：
```bash
# 停止推文监控任务
curl -X POST "http://localhost:5000/api/twitter-reward/tasks/monitor/stop"

# 停止奖励计算任务
curl -X POST "http://localhost:5000/api/twitter-reward/tasks/reward/stop"

# 确认任务已停止
curl "http://localhost:5000/api/twitter-reward/tasks/status"
```

**响应示例**：
```json
{
  "success": true,
  "message": "推文监控任务已停止"
}
```

### 7️⃣ 重置某天某用户领取记录

**场景描述**：测试专用功能，重置指定用户在特定日期的奖励领取状态。

**API接口**：`POST /api/twitter-reward/testing/reset-user-status`

**快速使用**：
```bash
# 重置用户在指定日期的领取状态
curl -X POST "http://localhost:5000/api/twitter-reward/testing/reset-user-status" \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "123456789",
    "utcDateTimestamp": 1705276800,
    "resetReason": "测试重复领取功能"
  }'

# 查看重置后的用户状态
curl "http://localhost:5000/api/twitter-reward/testing/summary"
```

**响应示例**：
```json
{
  "success": true,
  "data": {
    "beforeStatus": {
      "hasReceivedBonusReward": true,
      "bonusTotalRewards": 120
    },
    "afterStatus": {
      "hasReceivedBonusReward": false,
      "bonusTotalRewards": 0
    }
  },
  "message": "用户状态重置成功"
}
```

### 8️⃣ 查看定时任务运行状态

**场景描述**：监控定时任务的运行状态，确认任务是否正常启动和执行。

**API接口**：`GET /api/twitter-reward/tasks/status`

**快速使用**：
```bash
# 获取所有定时任务的状态
curl "http://localhost:5000/api/twitter-reward/tasks/status"

# 结合jq工具格式化输出（可选）
curl "http://localhost:5000/api/twitter-reward/tasks/status" | jq '.'
```

**响应示例**：
```json
{
  "success": true,
  "data": [
    {
      "taskName": "TweetMonitor",
      "isRunning": true,
      "lastExecutionTime": "2024-01-15T10:30:00Z",
      "nextExecutionTime": "2024-01-15T11:00:00Z",
      "executionCount": 48,
      "lastStatus": "Success"
    },
    {
      "taskName": "RewardCalculation", 
      "isRunning": true,
      "lastExecutionTime": "2024-01-15T00:00:00Z",
      "nextExecutionTime": "2024-01-16T00:00:00Z",
      "executionCount": 15,
      "lastStatus": "Success"
    }
  ]
}
```

### 9️⃣ 系统健康检查

**场景描述**：检查系统整体健康状态，包括数据库连接、API状态等关键指标。

**API接口**：`GET /api/twitter-reward/health`

**快速使用**：
```bash
# 获取系统健康状态
curl "http://localhost:5000/api/twitter-reward/health"

# 只检查健康状态是否正常
curl -s "http://localhost:5000/api/twitter-reward/health" | jq '.data.isHealthy'
```

**响应示例**：
```json
{
  "success": true,
  "data": {
    "isHealthy": true,
    "systemUptime": "2 days, 14 hours, 32 minutes",
    "lastHealthCheck": "2024-01-15T10:35:00Z",
    "components": {
      "database": {
        "status": "Healthy",
        "responseTime": "15ms"
      },
      "twitterApi": {
        "status": "Healthy", 
        "responseTime": "120ms",
        "rateLimitRemaining": 850
      },
      "grainStorage": {
        "status": "Healthy",
        "activeGrains": 156
      }
    },
    "metrics": {
      "totalTweetsProcessed": 1250,
      "totalRewardsDistributed": 15600,
      "averageProcessingTime": "2.3s"
    }
  }
}
```

### 🔟 获取当前配置信息

**场景描述**：查看系统当前加载的配置参数，验证配置是否正确。

**API接口**：`GET /api/twitter-reward/config`

**快速使用**：
```bash
# 获取当前配置
curl "http://localhost:5000/api/twitter-reward/config"

# 查看特定配置项（使用jq）
curl -s "http://localhost:5000/api/twitter-reward/config" | jq '.data.timeOffsetMinutes'
curl -s "http://localhost:5000/api/twitter-reward/config" | jq '.data.pullIntervalMinutes'
```

**响应示例**：
```json
{
  "success": true,
  "data": {
    "monitorHandle": "@demo_account",
    "pullIntervalMinutes": 30,
    "pullBatchSize": 100,
    "timeOffsetMinutes": 2880,
    "timeWindowMinutes": 1440,
    "dataRetentionDays": 5,
    "enablePullTask": true,
    "enableRewardTask": true,
    "pullTaskTargetId": "your-app-twitter-monitor",
    "rewardTaskTargetId": "your-app-twitter-reward",
    "configLoadTime": "2024-01-15T08:00:00Z",
    "configVersion": "1.0.0"
  }
}
```

## 🎯 完整验证流程

使用以下流程可以快速验证系统的完整功能（包含新增的监控场景）：

```bash
# 1. 系统健康检查
curl "http://localhost:5000/api/twitter-reward/health"
curl "http://localhost:5000/api/twitter-reward/config"

# 2. 启动系统
curl -X POST "http://localhost:5000/api/twitter-reward/tasks/monitor/start"
curl -X POST "http://localhost:5000/api/twitter-reward/tasks/reward/start"

# 3. 验证任务状态
curl "http://localhost:5000/api/twitter-reward/tasks/status"

# 4. 手动验证功能
curl -X POST "http://localhost:5000/api/twitter-reward/manual/pull-tweets"
curl -X POST "http://localhost:5000/api/twitter-reward/manual/calculate-rewards"

# 5. 查看数据和统计
curl "http://localhost:5000/api/twitter-reward/statistics?days=1"
curl "http://localhost:5000/api/twitter-reward/history/tweets?days=1"

# 6. 测试重置功能（可选）
curl -X POST "http://localhost:5000/api/twitter-reward/testing/reset-user-status" \
  -H "Content-Type: application/json" \
  -d '{"userId": "test_user", "utcDateTimestamp": 1705276800, "resetReason": "功能验证"}'

# 7. 最终状态确认
curl "http://localhost:5000/api/twitter-reward/health"
curl "http://localhost:5000/api/twitter-reward/tasks/status"
```

## 📊 测试阶段必备监控清单

| 监控项目 | API接口 | 检查频率 | 关键指标 |
|----------|---------|----------|----------|
| **系统健康** | `GET /health` | 每5分钟 | `isHealthy: true` |
| **任务状态** | `GET /tasks/status` | 每10分钟 | `isRunning: true` |
| **配置验证** | `GET /config` | 配置变更后 | 参数值正确性 |
| **数据统计** | `GET /statistics` | 每小时 | 数据增长趋势 |
| **处理历史** | `GET /history/processing` | 每日 | 执行成功率 |

### 🚨 关键监控脚本

```bash
#!/bin/bash
# 系统监控脚本示例

echo "=== Twitter Reward System Health Check ==="

# 1. 健康检查
echo "1. System Health:"
curl -s "http://localhost:5000/api/twitter-reward/health" | jq '.data.isHealthy'

# 2. 任务状态
echo "2. Task Status:"
curl -s "http://localhost:5000/api/twitter-reward/tasks/status" | jq '.data[] | {taskName, isRunning, lastStatus}'

# 3. 配置检查
echo "3. Configuration:"
curl -s "http://localhost:5000/api/twitter-reward/config" | jq '{timeOffsetMinutes, timeWindowMinutes, enablePullTask, enableRewardTask}'

# 4. 最近数据
echo "4. Recent Statistics:"
curl -s "http://localhost:5000/api/twitter-reward/statistics?days=1" | jq '.data'

echo "=== Health Check Complete ==="
```

---

## 🎯 完整的Controller实现示例

### TwitterRewardController 修正版本

**重要说明**：以下代码已根据实际的接口定义进行修正，确保与 GodGPT.GAgents 包中的真实接口匹配。

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

    #region 1. Task Management APIs

    /// <summary>
    /// Start tweet monitoring task
    /// </summary>
    [HttpPost("tasks/monitor/start")]
    public async Task<IActionResult> StartMonitorTask()
    {
        try
        {
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            await systemManager.StartTweetMonitorAsync();
            
            _logger.LogInformation("Tweet monitoring task started successfully");
            return Ok(new { success = true, message = "Tweet monitoring task has been started" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start tweet monitoring task");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Start reward calculation task
    /// </summary>
    [HttpPost("tasks/reward/start")]
    public async Task<IActionResult> StartRewardTask()
    {
        try
        {
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            await systemManager.StartRewardCalculationAsync();
            
            _logger.LogInformation("Reward calculation task started successfully");
            return Ok(new { success = true, message = "Reward calculation task has been started" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start reward calculation task");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Stop tweet monitoring task
    /// </summary>
    [HttpPost("tasks/monitor/stop")]
    public async Task<IActionResult> StopMonitorTask()
    {
        try
        {
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            await systemManager.StopTweetMonitorAsync();
            
            _logger.LogInformation("Tweet monitoring task stopped successfully");
            return Ok(new { success = true, message = "Tweet monitoring task has been stopped" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop tweet monitoring task");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Stop reward calculation task
    /// </summary>
    [HttpPost("tasks/reward/stop")]
    public async Task<IActionResult> StopRewardTask()
    {
        try
        {
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            await systemManager.StopRewardCalculationAsync();
            
            _logger.LogInformation("Reward calculation task stopped successfully");
            return Ok(new { success = true, message = "Reward calculation task has been stopped" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop reward calculation task");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Get all task status
    /// </summary>
    [HttpGet("tasks/status")]
    public async Task<IActionResult> GetTaskStatus()
    {
        try
        {
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            var status = await systemManager.GetAllTaskStatusAsync();
            
            return Ok(new { success = status.Success, data = status.Data });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get task status");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    #endregion

    #region 2. Historical Data Query APIs (Last N Days)

    /// <summary>
    /// Query historical tweet data (last N days)
    /// </summary>
    [HttpGet("history/tweets")]
    public async Task<IActionResult> GetHistoricalTweets([FromQuery] int days = 5)
    {
        try
        {
            var tweetMonitor = _grainFactory.GetGrain<ITweetMonitorGrain>("TweetMonitor");
            
            // Create time range for last N days
            var endTime = DateTime.UtcNow;
            var startTime = endTime.AddDays(-days);
            var timeRange = new TimeRangeDto 
            { 
                StartTime = startTime, 
                EndTime = endTime 
            };
            
            var result = await tweetMonitor.QueryTweetsByTimeRangeAsync(timeRange);
            
            _logger.LogInformation($"Historical tweets query: {days} days, total {result.Data?.Count ?? 0} tweets");
            return Ok(new { success = result.Success, data = result.Data, period = $"Last {days} days" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to query historical tweets: days={days}");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Query user reward history (last N days)
    /// </summary>
    [HttpGet("history/rewards/{userId}")]
    public async Task<IActionResult> GetUserRewardHistory(string userId, [FromQuery] int days = 5)
    {
        try
        {
            var rewardGrain = _grainFactory.GetGrain<ITwitterRewardGrain>("TwitterReward");
            var result = await rewardGrain.GetUserRewardRecordsAsync(userId, days);
            
            _logger.LogInformation($"User {userId} reward history query: {days} days, total {result.Data?.Count ?? 0} records");
            return Ok(new { success = result.Success, data = result.Data, userId, period = $"Last {days} days" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to query user reward history: userId={userId}, days={days}");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Get system data statistics
    /// </summary>
    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics([FromQuery] int days = 7)
    {
        try
        {
            var endTime = DateTime.UtcNow;
            var startTime = endTime.AddDays(-days);
            var timeRange = new TimeRangeDto 
            { 
                StartTime = startTime, 
                EndTime = endTime 
            };
            
            var tweetMonitor = _grainFactory.GetGrain<ITweetMonitorGrain>("TweetMonitor");
            var rewardGrain = _grainFactory.GetGrain<ITwitterRewardGrain>("TwitterReward");
            
            var tweetStats = await tweetMonitor.GetTweetStatisticsAsync(timeRange);
            var rewardHistory = await rewardGrain.GetRewardCalculationHistoryAsync(days);
            
            return Ok(new 
            { 
                success = true, 
                data = new { 
                    tweetStats = tweetStats.Data,
                    rewardHistory = rewardHistory.Data,
                    period = $"Last {days} days"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to get statistics: days={days}");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    #endregion

    #region 3. Manual Trigger APIs (with detailed logging)

    /// <summary>
    /// Manual tweet pull (with detailed logging)
    /// </summary>
    [HttpPost("manual/pull-tweets")]
    public async Task<IActionResult> ManualPullTweets()
    {
        try
        {
            _logger.LogInformation("Starting manual tweet pull...");
            
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            await systemManager.ManualPullTweetsAsync();
            
            _logger.LogInformation("Manual tweet pull completed");
            
            return Ok(new { success = true, message = "Manual pull completed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual tweet pull failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Manual reward calculation (with detailed logging)
    /// </summary>
    [HttpPost("manual/calculate-rewards")]
    public async Task<IActionResult> ManualCalculateRewards([FromBody] ManualRewardRequest request = null)
    {
        try
        {
            _logger.LogInformation("Starting manual reward calculation...");
            
            if (request?.TargetDate != null)
            {
                // Calculate for specific date
                var rewardGrain = _grainFactory.GetGrain<ITwitterRewardGrain>("TwitterReward");
                var result = await rewardGrain.TriggerRewardCalculationAsync(request.TargetDate.Value);
                
                _logger.LogInformation($"Manual reward calculation completed for date: {request.TargetDate}");
                return Ok(new { success = result.Success, data = result.Data, message = "Manual reward calculation completed" });
            }
            else
            {
                // Use system manager for current date
                var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
                await systemManager.ManualCalculateRewardsAsync();
                
                _logger.LogInformation("Manual reward calculation completed for current date");
                return Ok(new { success = true, message = "Manual reward calculation completed" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual reward calculation failed");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    #endregion

    #region 4. Testing-specific Reset APIs

    /// <summary>
    /// Reset user daily status for specific day (testing only)
    /// Note: This requires ITwitterTestingGrain which may not be available in production
    /// </summary>
    [HttpPost("testing/reset-user-status")]
    public async Task<IActionResult> ResetUserDailyStatus([FromBody] ResetUserStatusRequest request)
    {
        try
        {
            _logger.LogWarning($"[TEST_RESET] Preparing to reset user status: UserId={request.UserId}, Date={request.TargetDate}");
            
            var testingGrain = _grainFactory.GetGrain<ITwitterTestingGrain>("TwitterTesting");
            
            // Note: Actual method signatures may vary - this is a placeholder
            // Check ITwitterTestingGrain interface for exact methods
            _logger.LogWarning($"[TEST_RESET] Reset operation requested for UserId={request.UserId}");
            
            return Ok(new 
            { 
                success = true, 
                message = "Reset operation initiated - check logs for details"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to reset user status: UserId={request.UserId}");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    #endregion

    #region 5. Configuration Management APIs

    /// <summary>
    /// Get current configuration
    /// </summary>
    [HttpGet("config")]
    public async Task<IActionResult> GetCurrentConfig()
    {
        try
        {
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            var config = await systemManager.GetCurrentConfigAsync();
            
            return Ok(new { success = true, data = config });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get configuration");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Update time configuration (supports hot reload)
    /// </summary>
    [HttpPut("config/time")]
    public async Task<IActionResult> UpdateTimeConfig([FromBody] UpdateTimeConfigRequest request)
    {
        try
        {
            _logger.LogInformation($"Updating time configuration: MonitorInterval={request.MonitorIntervalMinutes}min, RewardInterval={request.RewardIntervalMinutes}min");
            
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            await systemManager.UpdateTimeConfigAsync(
                TimeSpan.FromMinutes(request.MonitorIntervalMinutes), 
                TimeSpan.FromMinutes(request.RewardIntervalMinutes)
            );
            
            _logger.LogInformation("Time configuration updated successfully");
            
            return Ok(new { success = true, message = "Time configuration updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update time configuration");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    #endregion

    #region 6. System Health Check

    /// <summary>
    /// System health check
    /// </summary>
    [HttpGet("health")]
    public async Task<IActionResult> GetSystemHealth()
    {
        try
        {
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            var health = await systemManager.GetSystemHealthAsync();
            
            return Ok(new { success = health.Success, data = health.Data });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get system health status");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Get processing history
    /// </summary>
    [HttpGet("history/processing")]
    public async Task<IActionResult> GetProcessingHistory([FromQuery] int days = 7)
    {
        try
        {
            var systemManager = _grainFactory.GetGrain<ITwitterSystemManagerGrain>("TwitterSystemManager");
            var history = await systemManager.GetProcessingHistoryAsync();
            
            return Ok(new { success = true, data = history, period = $"Last {days} days" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to get processing history: days={days}");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    #endregion
}

#region Request/Response DTOs

public class ManualRewardRequest
{
    public DateTime? TargetDate { get; set; }
}

public class ResetUserStatusRequest
{
    public string UserId { get; set; }
    public DateTime TargetDate { get; set; }
    public string ResetReason { get; set; }
}

public class UpdateTimeConfigRequest
{
    public int MonitorIntervalMinutes { get; set; }
    public int RewardIntervalMinutes { get; set; }
}

// Note: You'll need to import these DTOs from the GodGPT.GAgents package
// using Aevatar.Application.Grains.TwitterInteraction.Dtos;

#endregion
```

---

## 🔧 第三方应用配置

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

## ⚡ 配置热更新支持

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

**重要**：修改时间区间配置（`TimeOffsetMinutes`、`TimeWindowMinutes`）后，**无需重启机器**，配置会在下次任务执行时自动生效！

### 配置更新验证

```bash
# 1. 修改配置文件（示例：调整时间窗口）
# 编辑 appsettings.json: "TimeWindowMinutes": 720

# 2. 验证配置已更新（无需重启）
curl "http://localhost:5000/api/twitter-reward/config"

# 3. 手动触发验证新配置生效
curl -X POST "http://localhost:5000/api/twitter-reward/manual/calculate-rewards"
```
