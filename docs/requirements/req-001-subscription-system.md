# REQ-001: Subscription System Requirements (Configuration-Driven Ultimate Architecture)

## 1. 系统概述

GodGPT订阅系统采用配置驱动的Ultimate架构，解决了历史兼容性与业务逻辑不匹配的核心问题。系统支持Standard和Ultimate双订阅模式，通过统一接口保证100%向后兼容，同时提供灵活的配置管理和精确的逻辑顺序控制。

### 核心技术创新

**1. 逻辑顺序系统**
- 解决枚举值与业务逻辑顺序不匹配问题
- 历史兼容：枚举值Day=1, Month=2, Year=3, Week=4
- 业务逻辑：Day → Week → Month → Year
- 通过`SubscriptionHelper.GetPlanTypeLogicalOrder()`实现正确比较

**2. 配置驱动Ultimate检测**
- 移除硬编码Ultimate枚举值（WeekUltimate等）
- 使用`StripeProduct.IsUltimate`配置标志
- 灵活配置任意PlanType为Ultimate模式
- 零停机部署和配置变更

**3. 统一接口设计**
- 外部系统无需修改代码
- 内部智能路由Ultimate vs Standard处理
- 保持100%向后兼容性
- 新增`HasUnlimitedAccessAsync()`接口

### 系统架构

```
External Systems (UserBillingGrain, APIs)
         ↓
Unified Interface Layer (UserQuotaGrain public methods)
         ↓
Smart Routing Layer (Ultimate vs Standard detection)
         ↓
Internal Processing (Ultimate/Standard specific logic)
         ↓
Storage Layer (Dual subscription state management)
```

## 2. PlanType枚举与逻辑顺序系统

### 2.1 PlanType枚举定义

```csharp
public enum PlanType
{
    None = 0,    // 无订阅
    Day = 1,     // 日订阅（历史兼容，按7天处理）
    Month = 2,   // 月订阅
    Year = 3,    // 年订阅
    Week = 4     // 周订阅（新标准计划）
}
```

**重要说明：**
- 枚举值不能修改（历史兼容性要求）
- Day订阅历史遗留，实际按7天周期处理
- Week为新增的标准周订阅计划

### 2.2 逻辑顺序系统

#### 2.2.1 核心问题
枚举值顺序与业务逻辑顺序不匹配：
- **枚举值顺序**：Day=1 < Month=2 < Year=3 < Week=4
- **业务逻辑顺序**：Day=1 < Week=2 < Month=3 < Year=4

#### 2.2.2 解决方案

**SubscriptionHelper逻辑顺序映射：**
```csharp
public static int GetPlanTypeLogicalOrder(PlanType planType)
{
    return planType switch
    {
        PlanType.None => 0,
        PlanType.Day => 1,     // 逻辑顺序：1级
        PlanType.Week => 2,    // 逻辑顺序：2级  
        PlanType.Month => 3,   // 逻辑顺序：3级
        PlanType.Year => 4,    // 逻辑顺序：4级
        _ => 0
    };
}
```

**逻辑顺序比较方法：**
```csharp
// 基于逻辑顺序的比较（非枚举值）
public static int ComparePlanTypes(PlanType plan1, PlanType plan2);
public static bool IsUpgrade(PlanType fromPlan, PlanType toPlan);
public static bool IsUpgradeOrSameLevel(PlanType fromPlan, PlanType toPlan);
```

#### 2.2.3 实际应用

**修正前（错误）：**
```csharp
// 直接枚举值比较 - 逻辑错误
if (targetPlanType >= activeSubscription.PlanType) // Week=4 > Month=2 ???
```

**修正后（正确）：**
```csharp
// 逻辑顺序比较 - 业务正确
if (SubscriptionHelper.IsUpgradeOrSameLevel(activeSubscription.PlanType, targetPlanType))
```

## 3. 配置驱动Ultimate检测

### 3.1 Ultimate检测机制

#### 3.1.1 配置结构
```csharp
public class StripeProduct
{
    public int PlanType { get; set; }         // 1=Day, 2=Month, 3=Year, 4=Week
    public string PriceId { get; set; }       // Stripe Price ID
    public decimal Amount { get; set; }       // 价格金额
    public string Currency { get; set; }      // 货币
    public bool IsUltimate { get; set; }      // Ultimate模式标志 ⭐新增
}
```

#### 3.1.2 检测方法
```csharp
// 配置驱动检测（推荐）
public static bool IsUltimateSubscription(bool isUltimate)
{
    return isUltimate;
}

// 向后兼容方法（已弃用）
[Obsolete("Use IsUltimateSubscription(bool isUltimate) instead")]
public static bool IsUltimateSubscription(PlanType planType)
{
    return false; // 所有计划默认为Standard，Ultimate由配置决定
}
```

### 3.2 配置示例

**Standard计划配置：**
```json
{
  "PlanType": 4,
  "PriceId": "price_week_standard",
  "Amount": 9.99,
  "Currency": "USD",
  "IsUltimate": false
}
```

**Ultimate计划配置：**
```json
{
  "PlanType": 4,
  "PriceId": "price_week_ultimate", 
  "Amount": 19.99,
  "Currency": "USD",
  "IsUltimate": true
}
```

### 3.3 优势

1. **灵活配置**：任意PlanType可配置为Ultimate
2. **零停机部署**：配置变更无需代码部署
3. **A/B测试支持**：可为同一PlanType配置多个价格层级
4. **向后兼容**：现有代码无需修改

## 4. SubscriptionHelper工具类

### 4.1 核心功能

**SubscriptionHelper**是解决逻辑顺序和配置驱动检测的核心工具类：

```csharp
public static class SubscriptionHelper
{
    // 逻辑顺序系统
    public static int GetPlanTypeLogicalOrder(PlanType planType);
    public static int ComparePlanTypes(PlanType plan1, PlanType plan2);
    public static bool IsUpgrade(PlanType fromPlan, PlanType toPlan);
    public static bool IsUpgradeOrSameLevel(PlanType fromPlan, PlanType toPlan);
    
    // 配置驱动检测
    public static bool IsUltimateSubscription(bool isUltimate);
    public static bool IsStandardSubscription(PlanType planType);
    
    // 订阅计算
    public static DateTime GetSubscriptionEndDate(PlanType planType, DateTime startDate);
    public static int GetDaysForPlanType(PlanType planType);
    public static decimal CalculateDailyAveragePrice(PlanType planType, decimal amount);
    
    // 显示和验证
    public static string GetPlanDisplayName(PlanType planType, bool isUltimate = false);
    public static bool IsUpgradePathValid(PlanType fromPlan, PlanType toPlan, bool fromIsUltimate = false, bool toIsUltimate = false);
}
```

### 4.2 关键实现

#### 4.2.1 升级路径验证
```csharp
public static bool IsUpgradePathValid(PlanType fromPlan, PlanType toPlan, bool fromIsUltimate = false, bool toIsUltimate = false)
{
    // Standard订阅升级规则
    if (IsStandardSubscription(fromPlan) && !fromIsUltimate)
    {
        // 可升级到任何Ultimate
        if (toIsUltimate) return true;
        
        // Standard升级基于逻辑顺序
        var fromOrder = GetPlanTypeLogicalOrder(fromPlan);
        var toOrder = GetPlanTypeLogicalOrder(toPlan);
        
        // 允许升级（更高逻辑顺序）或同计划（续费）
        return toOrder >= fromOrder;
    }

    // Ultimate订阅可被任何Ultimate替换或与Standard共存
    if (fromIsUltimate)
    {
        return toIsUltimate || IsStandardSubscription(toPlan);
    }

    return false;
}
```

#### 4.2.2 历史兼容处理
```csharp
public static int GetDaysForPlanType(PlanType planType)
{
    return planType switch
    {
        // 历史兼容：Day按7天处理
        PlanType.Day or PlanType.Week => 7,
        PlanType.Month => 30,
        PlanType.Year => 390,
        _ => throw new ArgumentException($"Invalid plan type: {planType}")
    };
}

public static string GetPlanDisplayName(PlanType planType, bool isUltimate = false)
{
    var baseName = planType switch
    {
        PlanType.Day => "Weekly",    // 历史Day显示为Weekly
        PlanType.Week => "Weekly",
        PlanType.Month => "Monthly", 
        PlanType.Year => "Annual",
        PlanType.None => "No Subscription",
        _ => "Unknown"
    };

    return isUltimate ? $"{baseName} Ultimate" : baseName;
}
```

## 5. 统一接口设计

### 5.1 接口兼容性原则

**零破坏性变更：**
- 所有现有public方法签名100%不变
- 参数类型和返回值类型完全兼容
- 外部系统现有调用代码无需修改
- 内部智能路由处理Ultimate vs Standard

### 5.2 核心统一接口

#### 5.2.1 订阅更新（统一入口）
```csharp
// 主要接口 - 自动路由Ultimate vs Standard
Task UpdateSubscriptionAsync(SubscriptionInfoDto subscriptionInfoDto);

// Legacy接口 - 向后兼容
Task UpdateSubscriptionAsync(string planTypeName, DateTime endDate);
```

**内部路由逻辑：**
```csharp
public async Task UpdateSubscriptionAsync(SubscriptionInfoDto subscriptionInfoDto)
{
    // 配置驱动Ultimate检测
    if (subscriptionInfoDto.IsUltimate)
    {
        await UpdateUltimateSubscriptionAsync(subscriptionInfoDto);
    }
    else
    {
        await UpdateStandardSubscriptionAsync(subscriptionInfoDto);
    }
}
```

#### 5.2.2 订阅查询（优先级返回）
```csharp
// 统一查询接口 - 返回最高优先级订阅
Task<SubscriptionInfoDto> GetSubscriptionAsync();

// 内部优先级逻辑：Ultimate > Standard > None
```

#### 5.2.3 新增无限访问检查
```csharp
// 新增接口 - Ultimate用户检测
Task<bool> HasUnlimitedAccessAsync();
```

### 5.3 外部系统集成影响

#### 5.3.1 UserBillingGrain调用
```csharp
// 调用方式完全不变
await userQuotaGrain.UpdateSubscriptionAsync(subscriptionDto);

// 内部自动检测Ultimate vs Standard并路由
```

#### 5.3.2 API端点
```csharp
// 现有端点保持不变
POST /api/subscription/checkout  // 支持Ultimate配置
GET /api/subscription/status     // 返回活跃订阅
POST /api/subscription/cancel    // 智能取消路由
```

#### 5.3.3 Webhook处理
```csharp
// Webhook处理逻辑无变化
var subscriptionDto = new SubscriptionInfoDto 
{
    PlanType = (PlanType)productConfig.PlanType,
    IsUltimate = productConfig.IsUltimate,  // 配置驱动
    // ... 其他字段
};

// 统一接口调用，内部自动路由
await userQuotaGrain.UpdateSubscriptionAsync(subscriptionDto);
```

## 6. 订阅业务逻辑

### 6.1 订阅优先级系统

**优先级顺序：**
1. **Ultimate订阅**（最高优先级）
2. **Standard订阅**（次优先级）
3. **无订阅**（默认状态）

**实现逻辑：**
```csharp
public async Task<SubscriptionInfoDto> GetSubscriptionAsync()
{
    var now = DateTime.UtcNow;
    
    // 优先级1：Ultimate订阅
    if (IsSubscriptionActive(State.UltimateSubscription, now))
    {
        return ConvertToDto(State.UltimateSubscription, true);
    }
    
    // 优先级2：Standard订阅
    if (IsSubscriptionActive(State.StandardSubscription, now))
    {
        return ConvertToDto(State.StandardSubscription, false);
    }
    
    // 优先级3：无活跃订阅
    return CreateEmptySubscription();
}
```

### 6.2 订阅场景处理

#### 6.2.1 Standard订阅购买
```csharp
private async Task UpdateStandardSubscriptionAsync(SubscriptionInfoDto dto)
{
    // 只影响Standard订阅数据
    State.StandardSubscription = ConvertFromDto(dto);
    await WriteStateAsync();
}
```

#### 6.2.2 Ultimate订阅购买
```csharp
private async Task UpdateUltimateSubscriptionAsync(SubscriptionInfoDto dto)
{
    // 1. 累积Standard剩余时间（如果有）
    var accumulatedTime = CalculateStandardRemainingTime();
    
    // 2. 设置Ultimate订阅（基础时间 + 累积时间）
    var endDate = dto.EndDate.Add(accumulatedTime);
    State.UltimateSubscription = ConvertFromDto(dto);
    State.UltimateSubscription.EndDate = endDate;
    
    // 3. 如果Standard活跃，延长其有效期
    if (State.StandardSubscription.IsActive)
    {
        State.StandardSubscription.EndDate = State.StandardSubscription.EndDate.Add(dto.EndDate - dto.StartDate);
    }
    
    await WriteStateAsync();
}
```

#### 6.2.3 订阅取消
```csharp
public async Task CancelSubscriptionAsync()
{
    var activeSubscription = await GetActiveSubscriptionInternalAsync();
    
    if (activeSubscription.IsUltimate)
    {
        await CancelUltimateSubscriptionAsync();
    }
    else
    {
        await CancelStandardSubscriptionAsync();
    }
}
```

### 6.3 时间计算和累积

#### 6.3.1 订阅时长计算
```csharp
public static DateTime GetSubscriptionEndDate(PlanType planType, DateTime startDate)
{
    return planType switch
    {
        // 历史兼容：Day按7天处理
        PlanType.Day or PlanType.Week => startDate.AddDays(7),
        PlanType.Month => startDate.AddDays(30),
        PlanType.Year => startDate.AddDays(390),
        _ => throw new ArgumentException($"Invalid plan type: {planType}")
    };
}
```

#### 6.3.2 Ultimate时间累积
```csharp
private TimeSpan CalculateStandardRemainingTime()
{
    if (!State.StandardSubscription.IsActive) return TimeSpan.Zero;
    
    var now = DateTime.UtcNow;
    var remainingTime = State.StandardSubscription.EndDate - now;
    
    return remainingTime > TimeSpan.Zero ? remainingTime : TimeSpan.Zero;
}
```

## 7. 无限访问和速率限制

### 7.1 Ultimate无限访问

#### 7.1.1 检测逻辑
```csharp
public async Task<bool> HasUnlimitedAccessAsync()
{
    var activeSubscription = await GetActiveSubscriptionInternalAsync();
    return activeSubscription?.IsUltimate == true;
}
```

#### 7.1.2 速率限制绕过
```csharp
public async Task<ExecuteActionResultDto> IsActionAllowedAsync(string actionType = "conversation")
{
    // Ultimate用户：无限访问
    if (await HasUnlimitedAccessAsync())
    {
        return new ExecuteActionResultDto { Success = true };
    }
    
    // Standard/非订阅用户：应用速率限制
    return await ApplyStandardRateLimitingAsync(actionType);
}
```

### 7.2 速率限制配置

```csharp
public class RateLimitOptions
{
    public int UserMaxRequests = 25;                    // 非订阅用户
    public int UserTimeWindowSeconds = 10800;           // 3小时窗口
    public int SubscribedUserMaxRequests = 50;          // Standard订阅用户
    public int SubscribedUserTimeWindowSeconds = 10800; // 3小时窗口
    
    // Ultimate用户无速率限制
    public bool UltimateUserUnlimited = true;
}
```

## 8. 存储架构

### 8.1 UserQuotaState结构

```csharp
[GenerateSerializer]
public class UserQuotaState
{
    [Id(0)] public int Credits { get; set; } = 0;
    [Id(1)] public bool HasInitialCredits { get; set; } = false;
    [Id(2)] public bool HasShownInitialCreditsToast { get; set; } = false;
    
    // 双订阅支持 - 简化架构
    [Id(3)] public SubscriptionInfo StandardSubscription { get; set; } = new SubscriptionInfo();  // 重用现有字段
    [Id(5)] public SubscriptionInfo UltimateSubscription { get; set; } = new SubscriptionInfo();   // 新增字段
    
    [Id(4)] public Dictionary<string, RateLimitInfo> RateLimits { get; set; } = new Dictionary<string, RateLimitInfo>();
}
```

**设计原则：**
- Standard订阅重用现有字段（Id=3）确保历史兼容
- Ultimate订阅使用新字段（Id=5）
- 移除复杂的冻结/解冻机制，简化为直接时间操作

### 8.2 SubscriptionInfoDto增强

```csharp
public class SubscriptionInfoDto
{
    public PlanType PlanType { get; set; }
    public bool IsUltimate { get; set; }        // 配置驱动Ultimate标志
    public bool IsActive { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public PaymentStatus Status { get; set; }
    public List<string> SubscriptionIds { get; set; }
    public List<string> InvoiceIds { get; set; }
}
```

## 9. 测试策略

### 9.1 逻辑顺序测试

```csharp
[Fact]
public void GetPlanTypeLogicalOrder_Should_Return_Correct_Logical_Order()
{
    // 验证逻辑顺序映射
    SubscriptionHelper.GetPlanTypeLogicalOrder(PlanType.Day).ShouldBe(1);
    SubscriptionHelper.GetPlanTypeLogicalOrder(PlanType.Week).ShouldBe(2);
    SubscriptionHelper.GetPlanTypeLogicalOrder(PlanType.Month).ShouldBe(3);
    SubscriptionHelper.GetPlanTypeLogicalOrder(PlanType.Year).ShouldBe(4);
}

[Fact]
public void IsUpgrade_Should_Use_Logical_Order()
{
    // 验证升级判断使用逻辑顺序而非枚举值
    SubscriptionHelper.IsUpgrade(PlanType.Day, PlanType.Week).ShouldBeTrue();
    SubscriptionHelper.IsUpgrade(PlanType.Week, PlanType.Month).ShouldBeTrue();
    SubscriptionHelper.IsUpgrade(PlanType.Month, PlanType.Year).ShouldBeTrue();
}
```

### 9.2 配置驱动测试

```csharp
[Fact] 
public async Task UpdateSubscriptionAsync_Should_Route_Ultimate_Internally()
{
    // 验证配置驱动Ultimate检测和路由
    var ultimateSubscription = new SubscriptionInfoDto
    {
        PlanType = PlanType.Week,
        IsUltimate = true,  // 配置驱动标志
        // ...
    };
    
    await userQuotaGrain.UpdateSubscriptionAsync(ultimateSubscription);
    
    var hasUnlimitedAccess = await userQuotaGrain.HasUnlimitedAccessAsync();
    hasUnlimitedAccess.ShouldBeTrue();
}
```

### 9.3 统一接口测试

```csharp
[Fact]
public async Task ExternalSystem_Should_Successfully_Update_Standard_Subscription()
{
    // 验证外部系统调用兼容性
    var updateResult = await SimulateExternalSystemSubscriptionUpdate(
        userQuotaGrain, 
        PlanType.Month, 
        startDate, 
        endDate);
    
    updateResult.ShouldBeTrue();
    // 验证路由到Standard处理
}
```

## 10. 部署和迁移

### 10.1 部署安全性

**零停机部署：**
- 代码完全向后兼容，可直接部署
- 无数据格式变更，回滚安全
- 配置变更无需维护窗口

**分阶段启用：**
1. **阶段1**：部署代码（Ultimate逻辑自动可用）
2. **阶段2**：配置Stripe Ultimate产品
3. **阶段3**：前端支持Ultimate选项（可选）
4. **阶段4**：启用Ultimate订阅销售

### 10.2 历史数据兼容

**现有数据处理：**
- Day订阅继续按7天周期工作
- 所有Standard订阅无需迁移
- 枚举值保持不变
- 业务逻辑透明升级

**验证清单：**
- [ ] 现有Standard订阅创建/查询/取消正常
- [ ] Day订阅按7天处理
- [ ] 升级路径使用逻辑顺序
- [ ] 外部系统调用100%成功

## 11. 监控和观测

### 11.1 关键指标

**业务指标：**
- Standard vs Ultimate订阅创建成功率
- 升级路径验证正确性
- 订阅优先级选择准确性
- Ultimate用户无限访问使用率

**技术指标：**
- API兼容性成功率（100%目标）
- 内部路由准确性
- 逻辑顺序比较性能
- 配置加载成功率

### 11.2 日志策略

```csharp
// 关键路由点日志
_logger.LogInformation("Smart routing: {PlanType} IsUltimate={IsUltimate} -> {SubscriptionType}", 
    planType, isUltimate, subscriptionType);

// 逻辑顺序比较日志
_logger.LogDebug("Logical order comparison: {FromPlan}({FromOrder}) -> {ToPlan}({ToOrder}) = {IsUpgrade}", 
    fromPlan, fromOrder, toPlan, toOrder, isUpgrade);

// Ultimate特权访问日志
_logger.LogInformation("Ultimate unlimited access granted for user {UserId}", userId);
```

## 12. API参考

### 12.1 SubscriptionHelper公共方法

```csharp
// 逻辑顺序系统
public static int GetPlanTypeLogicalOrder(PlanType planType);
public static bool IsUpgrade(PlanType fromPlan, PlanType toPlan);
public static bool IsUpgradeOrSameLevel(PlanType fromPlan, PlanType toPlan);

// 配置驱动检测  
public static bool IsUltimateSubscription(bool isUltimate);
public static bool IsStandardSubscription(PlanType planType);

// 订阅计算
public static DateTime GetSubscriptionEndDate(PlanType planType, DateTime startDate);
public static string GetPlanDisplayName(PlanType planType, bool isUltimate = false);
```

### 12.2 UserQuotaGrain统一接口

```csharp
// 主要订阅接口
Task UpdateSubscriptionAsync(SubscriptionInfoDto subscriptionInfoDto);
Task<SubscriptionInfoDto> GetSubscriptionAsync();
Task CancelSubscriptionAsync();
Task<bool> IsSubscribedAsync();

// Ultimate特性接口
Task<bool> HasUnlimitedAccessAsync();

// 访问控制接口
Task<ExecuteActionResultDto> IsActionAllowedAsync(string actionType = "conversation");
```

## 13. 常见问题解答

### Q1: 为什么要实现逻辑顺序系统？
**A**: 历史原因导致枚举值顺序与业务逻辑不匹配（Week=4 > Month=2），直接比较枚举值会产生错误的升级判断。逻辑顺序系统确保业务逻辑正确：Day < Week < Month < Year。

### Q2: 配置驱动Ultimate检测有什么优势？
**A**: 
- 灵活性：任意PlanType可配置为Ultimate
- 零停机：配置变更无需代码部署
- 可扩展：支持A/B测试和多价格层级
- 兼容性：完全向后兼容现有代码

### Q3: 外部系统需要修改代码吗？
**A**: 完全不需要。统一接口设计确保100%向后兼容，外部系统使用相同的API调用方式，内部自动处理Ultimate vs Standard路由。

### Q4: Day订阅如何处理？
**A**: Day订阅（PlanType.Day=1）因历史兼容性保留，但在业务逻辑中按7天周期处理，显示名称为"Weekly"。

### Q5: Ultimate用户如何绕过速率限制？
**A**: 系统自动检测Ultimate用户（通过`HasUnlimitedAccessAsync()`），在`IsActionAllowedAsync()`中直接返回成功，跳过速率限制检查。

---

**文档版本**: 3.0  
**基于分支**: feature/subscribe-ultimate  
**创建日期**: 2024-12-19  
**最后更新**: 2024-12-19  
**状态**: 基于实际实现重写  
**核心特性**: 配置驱动Ultimate架构 + 逻辑顺序系统 + 统一接口设计  
**向后兼容**: 100%保证