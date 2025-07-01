# Twitter Credits Reward 第三方对接指南

## 📋 概述

本文档说明如何在第三方应用中集成 GodGPT.GAgents 的 Twitter Credits Reward 系统。该系统作为 NuGet 包提供，可以自动监控指定Twitter账号的推文并发放积分奖励。

## 🔧 1. 第三方应用配置

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
