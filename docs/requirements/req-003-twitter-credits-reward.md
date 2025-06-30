# REQ-003: Twitter Credits Reward System

## 需求概述

基于用户发送推特给用户奖励 GodGPT Credits 的系统，通过监控带有 @GodGPT_ 标签的推文，根据推文表现和用户粉丝数发放相应积分奖励。

## 技术约束

1. **架构约束**: 使用 `IGrainWithStringKey` 结构，不使用 `IChatManagerGAgent : IGAgent`
2. **模块化设计**: 功能划分合理，保持解耦合
3. **测试友好**: 时间区间需要考虑测试场景
4. **配置外置**: Twitter 交互所需的授权密钥等信息从配置文件获取
5. **开发阶段**: 积分发放暂时使用 TODO，重点计算推特ID和积分额度

## 功能需求

### 4.1 基础奖励机制

- **触发条件**: 用户发布带有 `@GodGPT_` 的推文
- **奖励额度**: 每条推文奖励 2 Credits
- **奖励上限**: 每用户最多 20 Credits（对应10条推文）
- **监控要求**: 系统监听 @GodGPT_ 的推文，记录推文ID、作者ID及发布时间

### 4.2 附加奖励机制

- **执行时间**: 每日 00:00 UTC
- **检索范围**: 过去72-48小时之间（倒数第三天）的24小时内推文
- **筛选条件**: 带有 @GodGPT_ 标签且 views ≥ 20 的推文
- **奖励计算**: 根据【浏览量 + 粉丝数】档位发放 Credits

#### 奖励档位表

| 浏览量范围 | 粉丝数要求 | 奖励积分 |
|-----------|-----------|---------|
| 20-100 views | 10+ followers | 5 Credits |
| 100-300 views | 20+ followers | 10 Credits |
| 300-500 views | 30+ followers | 15 Credits |
| 500-1000 views | 50+ followers | 20 Credits |
| 1000-3000 views | 100+ followers | 30 Credits |
| 3000-5000 views | 300+ followers | 50 Credits |
| 5000-10000 views | 500+ followers | 80 Credits |
| 10000+ views | 1000+ followers | 120 Credits |

### 4.3 奖励调整规则

- **档位匹配**: 浏览量和粉丝数要求必须同时满足
- **向下调整**: 若满足浏览量区间但粉丝数未达要求，奖励按满足的最低档位发放
- **示例**: 推文1200 views，粉丝60 → 按"500-1000 views + 50+ followers"发放 20 Credits

### 4.4 分享链接加成

- **触发条件**: 推文包含有效的 godgpt 分享链接
- **链接格式**: `https://app.godgpt.fun/*`
- **加成倍数**: 所得积分 × 1.1（向下取整）
- **验证要求**: 需验证链接的有效性

### 4.5 每日限制

- **附加奖励上限**: 每用户每日最多获得 500 Credits 附加奖励
- **发放频次**: 每用户按天统计和发放，基础奖励和附加奖励一次性计算

### 4.6 数据保留

- **数据保存期**: 推文数据最多保存 7 天
- **记录内容**: 用户ID、推特ID、阅读数、用户粉丝数、发送时间、积分记录

## 系统架构设计

### Agent 分工

#### Agent1: TwitterInteractionGrain
- **职责**: 被动与 Twitter API 交互
- **功能**: 
  - 获取推文详情（浏览量、作者信息）
  - 验证分享链接有效性
  - Twitter API 认证管理

#### Agent2: TweetMonitorGrain  
- **职责**: 定时记录发推@指定账号的推文
- **功能**:
  - 监控 @GodGPT_ 推文
  - 存储推文基础信息
  - 数据清理（7天过期）
  - 时间区间查询接口

#### Agent3: TwitterRewardGrain
- **职责**: 定时计算和发放积分
- **功能**:
  - 调用 TweetMonitorGrain 获取指定时间区间推文
  - 根据业务规则计算积分
  - 记录每日发放历史
  - 执行用户积分上限控制

## 配置项设计

```json
{
  "TwitterReward": {
    "BearerToken": "...",
    "ApiKey": "...",
    "ApiSecret": "...",
    "MonitorHandle": "@GodGPT_",
    "ShareLinkDomain": "https://app.godgpt.fun",
    "RewardSchedule": "0 0 * * *",  // 每日00:00 UTC
    "MonitorSchedule": "*/15 * * * *",  // 每15分钟监控
    "DataRetentionDays": 7,
    "TestTimeOffset": 0  // 测试时的时间偏移(小时)
  }
}
```

## 数据模型

### TweetRecord
```csharp
public class TweetRecord
{
    public string TweetId { get; set; }
    public string AuthorId { get; set; }
    public string AuthorHandle { get; set; }
    public DateTime CreatedAt { get; set; }
    public int ViewCount { get; set; }
    public int FollowerCount { get; set; }
    public bool HasShareLink { get; set; }
    public string ShareLinkUrl { get; set; }
    public bool IsShareLinkValid { get; set; }
}
```

### RewardRecord
```csharp
public class RewardRecord
{
    public string UserId { get; set; }
    public DateTime RewardDate { get; set; }
    public int BaseRewards { get; set; }
    public int BonusRewards { get; set; }
    public int TotalRewards { get; set; }
    public List<string> ProcessedTweetIds { get; set; }
}
```

## 测试考虑

### 时间相关测试
- **配置化时间偏移**: 支持测试环境时间偏移
- **模拟时间区间**: 支持模拟不同时间段的推文数据
- **定时任务测试**: 支持手动触发定时任务进行测试

### 边界条件测试
- **积分上限验证**: 测试每日积分上限机制
- **重复处理防护**: 测试同一推文不会重复发放积分
- **API异常处理**: 测试Twitter API异常情况的处理

## 实施计划

### Phase 1: 基础架构
- 创建三个 Grain 的基础结构
- 实现配置管理
- 建立数据模型

### Phase 2: Twitter 集成
- 实现 Twitter API 交互
- 推文监控和数据存储
- 分享链接验证

### Phase 3: 奖励计算
- 实现奖励计算逻辑
- 每日限制和重复防护
- 定时任务调度

### Phase 4: 测试和优化
- 完善测试覆盖
- 性能优化
- 监控和日志

## 风险评估

1. **Twitter API 限制**: 需要考虑API调用频率限制
2. **数据一致性**: 分布式环境下的数据一致性保证
3. **时区处理**: UTC时间和本地时间的正确处理
4. **异常恢复**: 系统异常时的数据恢复机制 