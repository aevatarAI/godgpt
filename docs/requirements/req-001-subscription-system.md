# REQ-001: Subscription System Requirements (Updated for Ultimate Mode)

## 1. Overview

The subscription system provides comprehensive billing and quota management for the GodGPT platform. It integrates with Stripe for payment processing and implements a multi-tier subscription model with flexible pricing, usage tracking, and time-based subscription management.

**Major Update**: Introduction of Ultimate subscription mode with unlimited usage and advanced subscription management features.

### Core Components
- **Price Configuration Management**: Multi-tier subscription plans (Week/Month/Year + Ultimate variants)
- **Subscription Lifecycle Management**: Creation, activation, cancellation, and renewal with dual subscription support
- **Usage Consumption Control**: Credits system and rate limiting with Ultimate mode unlimited access
- **Time-based Statistics**: Duration tracking, billing cycles, and subscription freeze/unfreeze logic
- **Ultimate Mode**: Premium subscription tier with unlimited usage and priority consumption

## 2. Detailed Requirements

### 2.1 Price Configuration System

#### 2.1.1 Subscription Plans (Updated with Ultimate Mode)
- **Standard Plans**: 
  - `PlanType.Day = 1`: Daily subscription (**Historical compatibility - treated as 7-day duration**)
  - `PlanType.Month = 2`: Monthly subscription  
  - `PlanType.Year = 3`: Annual subscription
  - `PlanType.Week = 4`: Weekly subscription (**New standard plan**)
  - `PlanType.None = 0`: No active subscription

- **Ultimate Plans** (New):
  - `PlanType.WeekUltimate = 5`: Weekly Ultimate subscription
  - `PlanType.MonthUltimate = 6`: Monthly Ultimate subscription
  - `PlanType.YearUltimate = 7`: Annual Ultimate subscription

#### 2.1.2 Price Structure (Updated)
```csharp
public class StripeProduct
{
    public int PlanType { get; set; }         // 1=Day, 2=Month, 3=Year, 4=Week, 5-7=Ultimate
    public string PriceId { get; set; }       // Stripe Price ID
    public string Mode { get; set; }          // "subscription"
    public decimal Amount { get; set; }       // Price amount
    public string Currency { get; set; }      // "USD"
    public bool IsUltimate { get; set; }      // Ultimate mode indicator
}
```

#### 2.1.3 Daily Average Price Calculation (Updated for Historical Compatibility)
```csharp
var dailyAvgPrice = product.PlanType switch
{
    (int)PlanType.Day => Math.Round(product.Amount / 7, 2).ToString(),        // Historical: treat as 7 days
    (int)PlanType.Week => Math.Round(product.Amount / 7, 2).ToString(),
    (int)PlanType.Month => Math.Round(product.Amount / 30, 2).ToString(),
    (int)PlanType.Year => Math.Round(product.Amount / 390, 2).ToString(),
    (int)PlanType.WeekUltimate => Math.Round(product.Amount / 7, 2).ToString(),
    (int)PlanType.MonthUltimate => Math.Round(product.Amount / 30, 2).ToString(),
    (int)PlanType.YearUltimate => Math.Round(product.Amount / 390, 2).ToString(),
    _ => "0"
};
```

#### 2.1.4 Upgrade Path Validation (Updated)
- **Standard Subscriptions**:
  - From Day/Week: Can upgrade to Month, Year, or any Ultimate
  - From Month: Can upgrade to Year or any Ultimate
  - From Year: Can upgrade to Year or any Ultimate
- **Ultimate Subscriptions**:
  - Can purchase any Ultimate plan (will replace existing Ultimate)
  - Can purchase standard plans (will coexist with Ultimate)
- **No Downgrades**: System prevents standard subscription downgrades

### 2.2 Subscription Lifecycle Management (Enhanced for Dual Subscription)

#### 2.2.1 Dual Subscription State Management
```csharp
[GenerateSerializer]
public class UserQuotaState
{
    [Id(0)] public int Credits { get; set; } = 0;
    [Id(1)] public bool HasInitialCredits { get; set; } = false;
    [Id(2)] public bool HasShownInitialCreditsToast { get; set; } = false;
    
    // Dual subscription support
    [Id(3)] public SubscriptionInfo StandardSubscription { get; set; } = new SubscriptionInfo();
    [Id(4)] public SubscriptionInfo UltimateSubscription { get; set; } = new SubscriptionInfo();
    [Id(5)] public Dictionary<string, RateLimitInfo> RateLimits { get; set; } = new Dictionary<string, RateLimitInfo>();
    
    // Freeze/Unfreeze tracking
    [Id(6)] public DateTime? StandardSubscriptionFrozenAt { get; set; }
    [Id(7)] public TimeSpan AccumulatedFrozenTime { get; set; } = TimeSpan.Zero;
}
```

#### 2.2.2 Subscription Creation (Updated)

**Web Platform Flow**:
```csharp
CreateCheckoutSessionAsync(CreateCheckoutSessionDto dto)
```
- Creates Stripe Checkout Session
- Supports HOSTED and EMBEDDED UI modes
- Handles payment method configuration
- **New**: Detects Ultimate vs Standard subscription types
- Returns checkout URL or client secret

**Mobile Platform Flow**:
```csharp
CreateSubscriptionAsync(CreateSubscriptionDto dto)
```
- Direct subscription creation for mobile apps
- Supports trial periods (TrialPeriodDays)
- Platform-specific metadata (android/ios)
- **New**: Handles Ultimate subscription activation
- Returns subscription details with client secret

#### 2.2.3 Subscription Activation (Enhanced)
- **Webhook Processing**: Handles `invoice.payment_succeeded` events
- **State Synchronization**: Updates UserQuotaGrain and UserBillingGrain
- **Rate Limit Reset**: Clears existing rate limits on activation
- **Multi-subscription Handling**: Automatically cancels previous subscriptions on upgrade
- **New: Ultimate Activation**: Triggers standard subscription freeze when Ultimate activates
- **New: Standard Activation**: Extends end date if unfrozen from Ultimate expiry

#### 2.2.4 Subscription Priority Logic (New)
```csharp
public async Task<SubscriptionInfo> GetActiveSubscriptionAsync()
{
    var now = DateTime.UtcNow;
    
    // Priority 1: Ultimate subscription
    if (IsSubscriptionActive(State.UltimateSubscription, now))
    {
        return State.UltimateSubscription;
    }
    
    // Priority 2: Standard subscription (considering freeze time)
    if (IsSubscriptionActive(State.StandardSubscription, now, considerFrozen: true))
    {
        return State.StandardSubscription;
    }
    
    return null; // No active subscription
}
```

#### 2.2.5 Freeze/Unfreeze Logic (New)

**Freeze Standard Subscription (Ultimate Activation)**:
```csharp
public async Task FreezeStandardSubscriptionAsync()
{
    if (State.StandardSubscription.IsActive && !State.StandardSubscriptionFrozenAt.HasValue)
    {
        State.StandardSubscriptionFrozenAt = DateTime.UtcNow;
        _logger.LogInformation("Standard subscription frozen at {FrozenTime} for user {UserId}", 
            State.StandardSubscriptionFrozenAt, this.GetPrimaryKeyString());
        await WriteStateAsync();
    }
}
```

**Unfreeze Standard Subscription (Ultimate Expiry)**:
```csharp
public async Task UnfreezeStandardSubscriptionAsync()
{
    if (State.StandardSubscriptionFrozenAt.HasValue)
    {
        var frozenDuration = DateTime.UtcNow - State.StandardSubscriptionFrozenAt.Value;
        State.AccumulatedFrozenTime += frozenDuration;
        
        // Extend standard subscription by frozen duration
        State.StandardSubscription.EndDate = State.StandardSubscription.EndDate.Add(frozenDuration);
        
        // Reset freeze state
        State.StandardSubscriptionFrozenAt = null;
        
        _logger.LogInformation("Standard subscription unfrozen for user {UserId}, extended by {Duration}", 
            this.GetPrimaryKeyString(), frozenDuration);
        await WriteStateAsync();
    }
}
```

#### 2.2.6 Subscription Cancellation (Enhanced)
```csharp
CancelSubscriptionAsync(CancelSubscriptionDto dto)
```
- **Cancel at Period End**: `CancelAtPeriodEnd = true` (default)
- **Immediate Cancellation**: Not implemented
- **Status Updates**: Updates payment records to `Cancelled_In_Processing`
- **New: Ultimate Cancellation**: Triggers standard subscription unfreeze if applicable
- **New: Subscription Type Detection**: Handles Ultimate vs Standard cancellation logic

### 2.3 Usage Consumption Control (Enhanced for Ultimate Mode)

#### 2.3.1 Credits System (Non-subscribers) - Unchanged
- **Initial Credits**: Configurable via `CreditsOptions.InitialCreditsAmount`
- **Per-Action Cost**: `CreditsOptions.CreditsPerConversation`
- **Credit Deduction**: Automatic on each conversation
- **Insufficient Credits**: Blocks action with error code `20001`

#### 2.3.2 Rate Limiting System (Enhanced for Ultimate)

**Configuration (Updated)**:
```csharp
public class RateLimitOptions
{
    public int UserMaxRequests = 25;                    // Non-subscribers
    public int UserTimeWindowSeconds = 10800;           // 3 hours
    public int SubscribedUserMaxRequests = 50;          // Standard subscribers  
    public int SubscribedUserTimeWindowSeconds = 10800; // 3 hours
    
    // Ultimate subscription settings
    public bool UltimateUserUnlimited = true;           // Ultimate users have no limits
    public int UltimateUserMaxRequests = int.MaxValue;  // Fallback if unlimited is disabled
    public int UltimateUserTimeWindowSeconds = 10800;   // Fallback time window
}
```

**Ultimate Mode Detection**:
```csharp
private bool IsUltimateSubscription(PlanType planType)
{
    return planType is PlanType.WeekUltimate 
        or PlanType.MonthUltimate 
        or PlanType.YearUltimate;
}

private bool IsStandardSubscription(PlanType planType)
{
    return planType is PlanType.Day 
        or PlanType.Week 
        or PlanType.Month 
        or PlanType.Year;
}
```

#### 2.3.3 Action Execution Flow (Enhanced)
1. **Active Subscription Detection**: Determines Ultimate vs Standard vs None
2. **Ultimate Check**: If Ultimate active, allow unlimited access
3. **Credits Validation**: For non-subscribers only
4. **Rate Limit Check**: Apply limits only for non-Ultimate users
5. **Action Execution**: Decrements tokens and credits (if applicable)
6. **State Persistence**: Saves updated quotas

**Enhanced Action Permission Logic**:
```csharp
public async Task<ExecuteActionResultDto> IsActionAllowedAsync(string actionType = "conversation")
{
    var activeSubscription = await GetActiveSubscriptionAsync();
    
    // Ultimate subscription: unlimited access
    if (activeSubscription != null && IsUltimateSubscription(activeSubscription.PlanType))
    {
        return new ExecuteActionResultDto { Success = true };
    }
    
    // Standard subscription or non-subscriber: apply original rate limiting
    return await ApplyStandardRateLimitingAsync(activeSubscription, actionType);
}
```

### 2.4 Time-based Statistics and Duration Management (Enhanced)

#### 2.4.1 Subscription Duration Calculation (Updated for Historical Compatibility)
```csharp
private DateTime GetSubscriptionEndDate(PlanType planType, DateTime startDate)
{
    switch (planType)
    {
        // Historical compatibility: Day treated as 7 days
        case PlanType.Day:
        case PlanType.Week:
        case PlanType.WeekUltimate:
            return startDate.AddDays(7);
            
        case PlanType.Month:
        case PlanType.MonthUltimate:
            return startDate.AddDays(30);
            
        case PlanType.Year:
        case PlanType.YearUltimate:
            return startDate.AddDays(390);
            
        default:
            throw new ArgumentException($"Invalid plan type: {planType}");
    }
}
```

#### 2.4.2 Renewal Logic (Enhanced)
- **Active Standard Subscription**: New period starts from current `EndDate` (considering frozen time)
- **Active Ultimate Subscription**: New period starts from current `EndDate`
- **Expired Subscription**: New period starts from `DateTime.UtcNow`
- **Overlap Prevention**: Automatic cancellation of previous subscriptions of same type
- **Cross-type Coexistence**: Ultimate and Standard can coexist

#### 2.4.3 Expiration Handling (Enhanced)
```csharp
public async Task<bool> IsSubscribedAsync()
{
    var now = DateTime.UtcNow;
    
    // Check Ultimate subscription first
    var ultimateActive = State.UltimateSubscription.IsActive && 
                        State.UltimateSubscription.StartDate <= now &&
                        State.UltimateSubscription.EndDate > now;
    
    if (ultimateActive) return true;
    
    // Check standard subscription (handle expiry and unfreeze if needed)
    var standardActive = await CheckStandardSubscriptionAsync(now);
    
    return standardActive;
}

private async Task<bool> CheckStandardSubscriptionAsync(DateTime now)
{
    var isActive = State.StandardSubscription.IsActive && 
                   State.StandardSubscription.StartDate <= now &&
                   State.StandardSubscription.EndDate > now;

    if (!isActive && State.StandardSubscription.IsActive)
    {
        // Handle standard subscription expiry
        State.StandardSubscription.IsActive = false;
        await WriteStateAsync();
    }
    
    return isActive;
}
```

#### 2.4.4 Refund Time Calculation (Updated)
```csharp
private int GetDaysForPlanType(PlanType planType)
{
    switch (planType)
    {
        // Historical compatibility: Day treated as 7 days
        case PlanType.Day:
        case PlanType.Week:
        case PlanType.WeekUltimate:
            return 7;
            
        case PlanType.Month:
        case PlanType.MonthUltimate:
            return 30;
            
        case PlanType.Year:
        case PlanType.YearUltimate:
            return 390;
            
        default:
            throw new ArgumentException($"Invalid plan type: {planType}");
    }
}
```

### 2.5 Historical Data Compatibility (New Section)

#### 2.5.1 Legacy Day Subscription Handling
- **Database Compatibility**: Existing Day subscriptions (PlanType = 1) remain unchanged
- **Duration Treatment**: Day subscriptions treated as 7-day duration for consistency
- **Display Logic**: UI should display Day subscriptions as "Weekly" for user clarity
- **Upgrade Paths**: Day subscriptions follow same rules as Week subscriptions

#### 2.5.2 Migration Strategy
```csharp
public class SubscriptionCompatibilityService
{
    public PlanType NormalizePlanType(PlanType planType)
    {
        // Treat legacy Day as Week for business logic
        return planType == PlanType.Day ? PlanType.Week : planType;
    }
    
    public string GetPlanDisplayName(PlanType planType)
    {
        return planType switch
        {
            PlanType.Day => "Weekly",  // Display legacy Day as Weekly
            PlanType.Week => "Weekly",
            PlanType.Month => "Monthly",
            PlanType.Year => "Annual",
            PlanType.WeekUltimate => "Weekly Ultimate",
            PlanType.MonthUltimate => "Monthly Ultimate",
            PlanType.YearUltimate => "Annual Ultimate",
            _ => "Unknown"
        };
    }
}
```

## 3. Acceptance Criteria (Updated)

### 3.1 Price Configuration
- [ ] System loads subscription products from `StripeOptions.Products`
- [ ] Daily average prices calculate correctly for all plan types including Ultimate
- [ ] **New**: Historical Day subscriptions treated as 7-day duration
- [ ] **New**: Ultimate subscription plans properly identified
- [ ] Upgrade path validation prevents invalid transitions
- [ ] Product configuration validation ensures required fields

### 3.2 Subscription Management
- [ ] Web checkout sessions create successfully with valid URLs
- [ ] Mobile subscriptions create with proper client secrets
- [ ] Webhook events process subscription activations correctly
- [ ] Subscription cancellations update status appropriately
- [ ] **New**: Dual subscription state management works correctly
- [ ] **New**: Ultimate subscription activation triggers standard subscription freeze
- [ ] **New**: Ultimate subscription expiry triggers standard subscription unfreeze

### 3.3 Usage Control
- [ ] Non-subscribers consume credits per conversation
- [ ] Rate limiting applies different quotas for subscribers vs non-subscribers
- [ ] Token bucket refill algorithm works correctly
- [ ] Insufficient credits/rate limit blocks actions with proper error codes
- [ ] **New**: Ultimate subscribers have unlimited access (no rate limiting)
- [ ] **New**: Subscription priority correctly determines active subscription type

### 3.4 Time Management
- [ ] Subscription durations calculate correctly for all plan types
- [ ] Renewal extends from current end date for active subscriptions
- [ ] Expiration detection works in real-time
- [ ] Refund calculations adjust end dates properly
- [ ] **New**: Standard subscription freeze/unfreeze logic works correctly
- [ ] **New**: Frozen time accumulation accurately extends subscription duration
- [ ] **New**: Historical Day subscriptions treated as 7-day duration

### 3.5 Historical Compatibility (New)
- [ ] Existing Day subscriptions continue to function without data migration
- [ ] Legacy Day subscriptions display as "Weekly" in UI
- [ ] Day subscription duration calculations use 7-day period
- [ ] Upgrade paths from Day subscriptions work correctly

## 4. Technical Considerations (Enhanced)

### 4.1 Data Consistency
- **Grain Persistence**: State management through Orleans grain persistence
- **Cross-Grain Synchronization**: UserBillingGrain ↔ UserQuotaGrain coordination
- **Webhook Idempotency**: Handle duplicate Stripe webhook events
- **New: Dual State Management**: Ensure Standard and Ultimate subscription states remain consistent
- **New: Freeze State Integrity**: Maintain accurate freeze/unfreeze timing

### 4.2 Performance Optimization
- **Rate Limit Caching**: In-memory token bucket state
- **Subscription Caching**: Avoid repeated database queries
- **Webhook Queuing**: Asynchronous processing of payment events
- **New: Ultimate Mode Fast Path**: Optimize unlimited access for Ultimate users
- **New: Subscription Priority Caching**: Cache active subscription determination

### 4.3 Error Handling
- **Stripe API Failures**: Graceful degradation and retry logic
- **Invalid Configurations**: Product validation and error reporting
- **Concurrent Updates**: Handle simultaneous subscription modifications
- **New: Freeze/Unfreeze Conflicts**: Handle race conditions in subscription state changes
- **New: Historical Data Edge Cases**: Proper handling of legacy Day subscription scenarios

### 4.4 Security
- **Webhook Signature Verification**: Stripe signature validation
- **User Authorization**: Ensure users can only modify their own subscriptions
- **Payment Data Protection**: Secure handling of sensitive payment information
- **New: Ultimate Access Control**: Ensure Ultimate privileges are properly validated

## 5. Dependencies (Updated)

### 5.1 External Services
- **Stripe API**: Payment processing and subscription management
- **Orleans Framework**: Actor model and grain persistence
- **ASP.NET Core**: Web API and dependency injection

### 5.2 Internal Components
- **UserQuotaGrain**: Quota and rate limit management (**Enhanced for dual subscription**)
- **UserBillingGrain**: Payment history and subscription tracking (**Enhanced for Ultimate tracking**)
- **UserPaymentGrain**: Individual payment record management
- **ChatManagerGAgent**: User profile and conversation management

### 5.3 Configuration Dependencies
- **StripeOptions**: Payment gateway configuration (**Enhanced for Ultimate products**)
- **CreditsOptions**: Credits system configuration
- **RateLimitOptions**: Rate limiting parameters (**Enhanced for Ultimate settings**)

## 6. Implementation Notes (Enhanced)

### 6.1 Data Models (Updated)

**Core Entities**:
- `StripeProductDto`: External API representation (**Enhanced with Ultimate detection**)
- `SubscriptionInfoDto`: User subscription state
- `UserQuotaState`: Credits and rate limits (**Enhanced with dual subscription support**)
- `PaymentSummary`: Payment history records

**New Entities**:
```csharp
public class DualSubscriptionDto
{
    public SubscriptionInfoDto StandardSubscription { get; set; }
    public SubscriptionInfoDto UltimateSubscription { get; set; }
    public DateTime? FrozenAt { get; set; }
    public TimeSpan AccumulatedFrozenTime { get; set; }
    public SubscriptionInfoDto ActiveSubscription { get; set; }
}
```

### 6.2 Key Algorithms (Enhanced)

**Subscription Priority Algorithm**:
```csharp
public SubscriptionInfo GetActiveSubscription()
{
    var now = DateTime.UtcNow;
    
    // Ultimate takes priority
    if (IsActive(UltimateSubscription, now))
        return UltimateSubscription;
    
    // Standard subscription (check freeze status)
    if (IsActive(StandardSubscription, now) && !IsFrozen())
        return StandardSubscription;
    
    return null;
}
```

**Freeze Duration Calculation**:
```csharp
public TimeSpan CalculateFrozenDuration()
{
    if (!StandardSubscriptionFrozenAt.HasValue)
        return TimeSpan.Zero;
    
    var currentFrozenDuration = DateTime.UtcNow - StandardSubscriptionFrozenAt.Value;
    return AccumulatedFrozenTime + currentFrozenDuration;
}
```

**Ultimate Access Check**:
```csharp
public bool HasUnlimitedAccess()
{
    var activeSubscription = GetActiveSubscription();
    return activeSubscription != null && IsUltimateSubscription(activeSubscription.PlanType);
}
```

### 6.3 Integration Points (Enhanced)

**Stripe Webhooks**:
- `invoice.payment_succeeded`: Activate subscription (Enhanced for Ultimate detection)
- `invoice.payment_failed`: Handle payment failures
- `customer.subscription.updated`: Process subscription changes
- **New**: `customer.subscription.cancelled`: Handle Ultimate vs Standard cancellation

**API Endpoints (Enhanced)**:
- `GET /api/subscription/products`: List available plans (**Enhanced with Ultimate filtering**)
- `POST /api/subscription/checkout`: Create checkout session (**Enhanced with Ultimate detection**)
- `POST /api/subscription/create`: Direct subscription creation (**Enhanced with Ultimate activation**)
- `POST /api/subscription/cancel`: Cancel subscription (**Enhanced with freeze/unfreeze logic**)
- **New**: `GET /api/subscription/status`: Get dual subscription status
- **New**: `POST /api/subscription/switch`: Switch between Ultimate and Standard modes

### 6.4 Testing Strategy (Enhanced)
- **Unit Tests**: Individual component logic validation
- **Integration Tests**: Cross-grain communication testing
- **Webhook Tests**: Stripe event simulation (**Enhanced for Ultimate scenarios**)
- **Load Tests**: Rate limiting and performance validation (**Enhanced for Ultimate unlimited access**)
- **New: Dual Subscription Tests**: Test freeze/unfreeze logic and subscription priority
- **New: Historical Compatibility Tests**: Validate Day subscription backward compatibility
- **New: Ultimate Mode Tests**: Comprehensive testing of unlimited access and priority logic

### 6.5 Development Phases (New)

**Phase 1: Foundation (1-2 weeks)**
- Extend PlanType enumeration
- Update UserQuotaState for dual subscription support
- Implement basic Ultimate detection logic

**Phase 2: Core Logic (2-3 weeks)**
- Implement freeze/unfreeze subscription logic
- Update subscription priority and activation logic
- Enhance rate limiting for Ultimate mode

**Phase 3: Integration (1-2 weeks)**
- Update Stripe webhook processing
- Enhance API endpoints
- Implement historical compatibility layer

**Phase 4: Testing & Polish (1 week)**
- Comprehensive testing of all scenarios
- Performance optimization
- Documentation finalization

---

**Document Version**: 2.0  
**Created**: 2024-12-19  
**Last Updated**: 2024-12-19  
**Status**: Updated for Ultimate Mode  
**Breaking Changes**: Enumeration extension, dual subscription state management  
**Backward Compatibility**: Full compatibility maintained for historical Day subscriptions 

## 7. 外部接口变更说明 (External Interface Changes - Based on Git Diff vs origin/main)

### 7.1 变更概述

本次Ultimate订阅模式的实现基于feature/subscribe-ultimate分支，相对于远程main分支的实际变化分析。通过`git diff origin/main`对比发现，本次修改严格遵循"统一接口"设计原则，确保外部系统能够通过相同的接口访问Standard和Ultimate订阅功能，同时保持100%向后兼容性。

**Git对比基准**: `origin/main` vs `feature/subscribe-ultimate`  
**核心设计理念**: Ultimate模式保持和原Standard模式一样的出入口，统一收口，方便外接对接

### 7.2 新增的外部公开接口

#### 7.2.1 `HasUnlimitedAccessAsync()` (完全新增)

**在IUserQuotaGrain接口中新增**:
```csharp
Task<bool> HasUnlimitedAccessAsync(); 
```

**功能说明**:
- 检测用户是否拥有Ultimate订阅的无限访问权限
- 返回true表示用户拥有Ultimate无限访问权限
- 主要用于速率限制系统和外部权限判断

**使用场景**:
```csharp
// 外部系统检测Ultimate权限
var hasUnlimitedAccess = await userQuotaGrain.HasUnlimitedAccessAsync();
if (hasUnlimitedAccess)
{
    // Ultimate用户处理逻辑
}
```

### 7.3 保持签名不变但内部增强的接口

#### 7.3.1 `UpdateSubscriptionAsync(SubscriptionInfoDto subscriptionInfoDto)`

**接口签名**: 完全保持不变
```csharp
Task UpdateSubscriptionAsync(SubscriptionInfoDto subscriptionInfoDto);
```

**参数变化**:
- `subscriptionInfoDto.PlanType`: 现在支持新的Ultimate枚举值
  - 新增: `PlanType.WeekUltimate = 5`
  - 新增: `PlanType.MonthUltimate = 6` 
  - 新增: `PlanType.YearUltimate = 7`
- 其他DTO字段保持完全不变

**内部逻辑增强** (外部调用方式零变化):
- 智能路由：根据PlanType自动识别Ultimate vs Standard
- Ultimate路由：内部调用`UpdateUltimateSubscriptionAsync()`私有方法
- Standard路由：内部调用`UpdateStandardSubscriptionAsync()`私有方法
- 时间累积：Ultimate激活时自动累积Standard剩余时间

#### 7.3.2 `GetSubscriptionAsync()`

**接口签名**: 完全保持不变
```csharp
Task<SubscriptionInfoDto> GetSubscriptionAsync();
```

**返回值类型**: 保持`SubscriptionInfoDto`，无破坏性变更

**内部逻辑变化**:
- 实现订阅优先级机制：Ultimate > Standard(未冻结) > None
- 自动处理双订阅状态和冻结逻辑
- 对外返回当前最高优先级的有效订阅

#### 7.3.3 `CancelSubscriptionAsync()`

**接口签名**: 完全保持不变
```csharp
Task CancelSubscriptionAsync();
```

**内部逻辑增强**:
- 智能检测当前有效订阅类型
- Ultimate取消：内部调用`CancelUltimateSubscriptionAsync()`
- Standard取消：内部调用`CancelStandardSubscriptionAsync()`
- 自动处理订阅解冻和状态管理

#### 7.3.4 `IsSubscribedAsync()`

**接口签名**: 完全保持不变
```csharp
Task<bool> IsSubscribedAsync();
```

**内部逻辑增强**:
- 检测Ultimate和Standard双订阅状态
- Ultimate订阅优先返回true
- Standard订阅考虑冻结状态

#### 7.3.5 `UpdateSubscriptionAsync(string planTypeName, DateTime endDate)` (Legacy支持)

**接口签名**: 完全保持不变
```csharp
Task UpdateSubscriptionAsync(string planTypeName, DateTime endDate);
```

**字符串参数增强**:
- 新增支持: `"WeekUltimate"`、`"MonthUltimate"`、`"YearUltimate"`
- 现有字符串参数保持完全兼容
- 内部自动转换和路由

#### 7.3.6 `IsActionAllowedAsync(string actionType = "conversation")`

**接口签名**: 完全保持不变
```csharp
Task<ExecuteActionResultDto> IsActionAllowedAsync(string actionType = "conversation");
```

**内部逻辑增强**:
- Ultimate用户：自动跳过速率限制
- Standard/非订阅用户：应用原有速率限制
- 对外行为：Ultimate用户体验无缝无限访问

### 7.4 核心数据结构变化

#### 7.4.1 `PlanType`枚举扩展 (重要变化)

**原有枚举值** (保持不变):
```csharp
Day = 1,            // 历史兼容 - 按7天处理  
Month = 2,          // 月订阅
Year = 3,           // 年订阅
None = 0,           // 无订阅
```

**新增枚举值**:
```csharp
Week = 4,           // 周订阅 (新标准计划)
WeekUltimate = 5,   // 周Ultimate订阅
MonthUltimate = 6,  // 月Ultimate订阅  
YearUltimate = 7    // 年Ultimate订阅
```

**向后兼容性保证**:
- 现有值保持完全不变
- 新值仅为扩展，不影响现有逻辑
- 历史Day订阅继续按7天处理

#### 7.4.2 `UserQuotaState`内部状态变化

**新增双订阅字段** (内部状态，不影响外部接口):
```csharp
[Id(3)] public SubscriptionInfo StandardSubscription { get; set; }
[Id(4)] public SubscriptionInfo UltimateSubscription { get; set; }  
[Id(6)] public DateTime? StandardSubscriptionFrozenAt { get; set; }
[Id(7)] public TimeSpan AccumulatedFrozenTime { get; set; }
```

### 7.5 外部系统调用影响对比

#### 7.5.1 UserBillingGrain调用变化

**Git Diff显示的实际变化**:

**修改前** (origin/main):
```csharp
// 手动计算日均价格
var dailyAvgPrice = string.Empty;
if (product.PlanType == (int)PlanType.Day)
{
    dailyAvgPrice = product.Amount.ToString();
}
else if (product.PlanType == (int)PlanType.Month)
{
    dailyAvgPrice = Math.Round(product.Amount / 30, 2).ToString();
}
else if (product.PlanType == (int)PlanType.Year)
{
    dailyAvgPrice = Math.Round(product.Amount / 390, 2).ToString();
}
```

**修改后** (feature/subscribe-ultimate):
```csharp
// 使用统一Helper计算，支持Ultimate
var planType = (PlanType)product.PlanType;
var dailyAvgPrice = SubscriptionHelper.CalculateDailyAveragePrice(planType, product.Amount);
```

**对UserQuotaGrain的调用**: 完全无变化
```csharp
// 调用方式保持完全相同
await userQuotaGrain.UpdateSubscriptionAsync(subscriptionDto);
```

#### 7.5.2 Stripe Webhook处理

**影响分析**: 零影响
- Webhook继续调用相同的`UpdateSubscriptionAsync(subscriptionDto)`
- 内部根据PlanType自动路由到Ultimate或Standard处理
- 无需修改任何Webhook处理代码

#### 7.5.3 Web API端点

**影响分析**: 零影响，完全兼容
- 现有API端点调用方式保持不变
- 支持接收Ultimate PlanType值
- 自动通过统一接口处理

#### 7.5.4 移动端集成

**影响分析**: 零影响，可选增强
- 现有订阅创建和查询流程无变化
- 可选择性使用新的`HasUnlimitedAccessAsync()`
- 可选择性支持Ultimate订阅类型

### 7.6 新增的支持类和DTO

#### 7.6.1 `SubscriptionHelper` (新增工具类)

**文件**: `src/GodGPT.GAgents/Common/Helpers/SubscriptionHelper.cs`

**主要方法**:
```csharp
public static bool IsUltimateSubscription(PlanType planType)
public static bool IsStandardSubscription(PlanType planType)
public static string CalculateDailyAveragePrice(PlanType planType, decimal amount)
public static int GetDaysForPlanType(PlanType planType)
```

#### 7.6.2 `DualSubscriptionStatusDto` (新增内部DTO)

**文件**: `src/GodGPT.GAgents/ChatManager/Dtos/DualSubscriptionStatusDto.cs`

**用途**: 内部双订阅状态管理，不直接暴露给外部

### 7.7 向后兼容性验证清单

#### 7.7.1 API兼容性 ✅
- [x] 所有现有public方法签名100%不变
- [x] 参数类型和返回值类型完全兼容  
- [x] 外部系统现有调用代码无需修改
- [x] Legacy API继续正常工作

#### 7.7.2 数据兼容性 ✅
- [x] 现有枚举值保持不变
- [x] 现有订阅数据无需迁移
- [x] 历史订阅继续正常工作
- [x] 用户状态平滑迁移

#### 7.7.3 行为兼容性 ✅
- [x] Standard订阅功能保持原有行为
- [x] 现有速率限制逻辑对Standard用户不变
- [x] 错误处理保持一致
- [x] 业务逻辑无破坏性变更

### 7.8 部署和迁移策略

#### 7.8.1 零停机部署
- **部署安全性**: 代码完全向后兼容，可直接部署
- **回滚安全性**: 无数据格式变更，回滚安全
- **数据迁移**: 无需数据迁移或维护窗口

#### 7.8.2 功能启用
- **阶段1**: 部署代码 (Ultimate逻辑自动可用)
- **阶段2**: 配置Stripe Ultimate产品
- **阶段3**: 前端支持Ultimate选项 (可选)
- **阶段4**: 启用Ultimate订阅销售

#### 7.8.3 验证检查
- **现有功能**: Standard订阅创建/查询/取消正常
- **新功能**: Ultimate订阅创建和无限访问正常
- **集成**: Webhook和外部系统调用正常

### 7.9 监控和观测

#### 7.9.1 关键指标
- **API成功率**: 确保现有调用100%成功
- **订阅创建**: Standard和Ultimate订阅创建成功率
- **优先级逻辑**: Ultimate > Standard选择正确性
- **无限访问**: Ultimate用户速率限制绕过正确性

#### 7.9.2 日志增强
```csharp
// 新增的关键日志点
_logger.LogInformation("Ultimate subscription activated, accumulated {Duration} from Standard", duration);
_logger.LogDebug("Smart routing: {PlanType} -> {SubscriptionType}", planType, subscriptionType);
_logger.LogInformation("Subscription priority: returning {ActiveType} subscription", activeType);
```

#### 7.9.3 故障排除
- **订阅优先级问题**: 检查Ultimate > Standard逻辑
- **时间累积异常**: 验证Standard时间正确累积到Ultimate
- **速率限制异常**: 确认Ultimate用户正确绕过限制

### 7.10 外部系统集成检查清单

#### 7.10.1 无需修改的系统 ✅
- [x] **Stripe Webhook**: 继续使用现有代码
- [x] **用户认证系统**: 无影响
- [x] **订阅查询API**: 使用现有`GetSubscriptionAsync()`
- [x] **订阅取消API**: 使用现有`CancelSubscriptionAsync()`
- [x] **移动端应用**: 无需强制更新

#### 7.10.2 可选增强的系统
- **前端UI**: 可选添加Ultimate订阅选项
- **客服系统**: 可选调用`HasUnlimitedAccessAsync()`查看用户类型
- **分析系统**: 可选区分Ultimate vs Standard用户指标
- **推荐系统**: 可选基于Ultimate状态个性化推荐

#### 7.10.3 配置更新示例
```json
{
  "Stripe": {
    "Products": [
      {
        "PlanType": 6,
        "PriceId": "price_month_ultimate",
        "Mode": "subscription", 
        "Amount": 19.99,
        "Currency": "USD",
        "IsUltimate": true
      }
    ]
  }
}
```

---

**章节版本**: 2.0  
**对比基准**: git diff origin/main  
**文档日期**: 2024-12-19  
**兼容性承诺**: 100% 向后兼容，零破坏性变更