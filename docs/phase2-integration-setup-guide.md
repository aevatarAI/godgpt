# Google Pay 集成设置指南

## 🎯 目标

从Mock测试环境过渡到真实的Google Play环境，验证API集成和支付流程。

## 📋 配置清单汇总

### 测试环境
以下项目需要在Google Play开发者控制台手动配置:
1. 注册Google Play开发者账户($25费用)
2. 创建测试应用(内部测试轨道)
3. 配置订阅商品(周、月、年订阅商品 - 测试版本)
4. 添加许可测试员账户
5. 配置Service Account和API密钥
6. 设置RTDN通知(测试环境)

### 正式环境
以下项目需要在Google Play开发者控制台手动配置:
1. 注册Google Play开发者账户($25费用)
2. 创建正式应用(生产轨道)
3. 配置订阅商品(周、月、年订阅商品 - 正式版本)
4. 配置正式用户购买流程
5. 配置Service Account和API密钥
6. 设置RTDN通知(生产环境)

## 📝 必备条件

- [ ] $25 USD用于Google Play开发者账户注册
- [ ] 有效的Gmail账户
- [ ] 有效的付款方式（信用卡/PayPal）
- [ ] 测试用的Android设备或模拟器

## 🚀 集成步骤

### 步骤1：注册Google Play开发者账户

```bash
# 访问并完成注册
https://play.google.com/console/
```

**注册信息**：
- 开发者名称：`GodGPT Development Team`
- 开发者类型：`组织`
- 一次性注册费：$25 USD

### 步骤2：创建应用

#### 测试环境
```yaml
应用基本信息:
  应用名称: "GodGPT Test App"
  默认语言: "英语（美国）"
  包名: "com.godgpt.app.test"
  应用类别: "生产力"
```

**上传测试APK**：
```bash
上传路径: Google Play Console > 应用 > 版本管理 > 应用版本 > 内部测试轨道
```

#### 正式环境
```yaml
应用基本信息:
  应用名称: "GodGPT"
  默认语言: "英语（美国）"
  包名: "com.godgpt.app"
  应用类别: "生产力"
```

**上传正式APK**：
```bash
上传路径: Google Play Console > 应用 > 版本管理 > 应用版本 > 生产轨道
```

### 步骤3：配置订阅商品

#### 测试环境
创建三个测试订阅商品：

```yaml
# 商品1：周度订阅
订阅详情:
  产品ID: "premium_weekly_test"
  订阅名称: "Premium Weekly Test"
  描述: "GodGPT Premium weekly subscription for testing"

基础计划:
  计划ID: "weekly-autorenewing"
  计费周期: "1周"
  价格: $6.00 USD
  续费类型: "自动续费"

# 商品2：月度订阅
订阅详情:
  产品ID: "premium_monthly_test"
  订阅名称: "Premium Monthly Test"
  描述: "GodGPT Premium monthly subscription for testing"

基础计划:
  计划ID: "monthly-autorenewing"
  计费周期: "1个月"
  价格: $20.00 USD
  续费类型: "自动续费"

# 商品3：年度订阅  
订阅详情:
  产品ID: "premium_yearly_test"
  订阅名称: "Premium Yearly Test"
  描述: "GodGPT Premium yearly subscription for testing"

基础计划:
  计划ID: "yearly-autorenewing"
  计费周期: "12个月"
  价格: $200.00 USD
  续费类型: "自动续费"
```

#### 正式环境
创建三个正式订阅商品：

```yaml
# 商品1：周度订阅
订阅详情:
  产品ID: "premium_weekly"
  订阅名称: "Premium Weekly"
  描述: "GodGPT Premium weekly subscription"

基础计划:
  计划ID: "weekly-autorenewing"
  计费周期: "1周"
  价格: $6.99 USD
  续费类型: "自动续费"

# 商品2：月度订阅
订阅详情:
  产品ID: "premium_monthly"
  订阅名称: "Premium Monthly"
  描述: "GodGPT Premium monthly subscription"

基础计划:
  计划ID: "monthly-autorenewing"
  计费周期: "1个月"
  价格: $19.99 USD
  续费类型: "自动续费"

# 商品3：年度订阅  
订阅详情:
  产品ID: "premium_yearly"
  订阅名称: "Premium Yearly"
  描述: "GodGPT Premium yearly subscription"

基础计划:
  计划ID: "yearly-autorenewing"
  计费周期: "12个月"
  价格: $199.99 USD
  续费类型: "自动续费"
```

### 步骤4：配置用户测试

#### 测试环境
```yaml
配置路径: Google Play Console > 设置 > 许可测试

测试账户配置:
  测试邮箱列表:
    - your-email@gmail.com
    - team-member1@company.com  
    - team-member2@company.com
    
测试权限:
  - 免费购买所有应用内商品
  - 立即取消订阅
  - 无实际扣费
```

#### 正式环境
```yaml
用户购买配置:
  正式用户购买流程:
    - 真实付费购买
    - 按实际价格收费
    - 正常订阅续费流程
    
用户管理:
  - 客服支持流程
  - 退款政策设置
  - 订阅管理功能
```

### 步骤5：配置Service Account

#### 通用配置（测试和正式环境共用）

**5.1 Google Cloud Console配置**
```bash
# 1. 访问Google Cloud Console
https://console.cloud.google.com/

# 2. 创建或选择项目
测试环境项目名称: "godgpt-play-api-test"
正式环境项目名称: "godgpt-play-api-prod"

# 3. 启用必要的API
- Google Play Developer API
- Google Play Billing API
```

**5.2 创建Service Account**
```yaml
测试环境Service Account:
  名称: "godgpt-play-api-test-service"
  描述: "Service account for GodGPT Google Play API access (Testing)"

正式环境Service Account:
  名称: "godgpt-play-api-prod-service"
  描述: "Service account for GodGPT Google Play API access (Production)"
  
角色权限:
  - Service Account User
```

**5.3 生成密钥文件**
```bash
# 在Service Account页面
1. 点击"创建密钥"
2. 选择"JSON"格式
3. 下载密钥文件
4. 测试环境: 重命名为 service-account-key-test.json
5. 正式环境: 重命名为 service-account-key-prod.json
6. 安全存储，不要提交到Git
```

**5.4 Google Play Console授权**
```yaml
授权路径: Google Play Console > 设置 > API访问

授权步骤:
  1. 点击"关联"按钮
  2. 选择对应的Google Cloud项目(测试/正式)
  3. 授权相应的Service Account
  4. 分配权限:
     - 查看应用信息和下载批量报告
     - 查看和管理订单和订阅
```

### 步骤6：设置RTDN通知

#### 测试环境
```yaml
配置路径: Google Play Console > 获利设置 > 实时开发者通知

RTDN配置:
  端点URL: "https://your-test-api.com/webhook/google-play"
  通知类型:
    - 订阅购买
    - 订阅续费
    - 订阅取消
    - 订阅暂停
```

#### 正式环境
```yaml
配置路径: Google Play Console > 获利设置 > 实时开发者通知

RTDN配置:
  端点URL: "https://your-prod-api.com/webhook/google-play"
  通知类型:
    - 订阅购买
    - 订阅续费
    - 订阅取消
    - 订阅暂停
```

## 🔧 验证测试

#### 测试环境验证
```bash
#!/bin/bash
# test-environment-verification.sh

echo "🔍 验证Google Play测试环境配置..."

# 1. 检查测试环境Service Account密钥
if [ ! -f "./config/service-account-key-test.json" ]; then
    echo "❌ 测试环境Service Account密钥文件缺失"
    exit 1
fi

# 2. 测试API连接
echo "📡 测试Google Play Developer API连接(测试环境)..."

# 3. 验证测试商品配置
echo "🛍️ 验证测试商品配置..."

echo "✅ 测试环境验证完成"
```

#### 正式环境验证
```bash
#!/bin/bash
# production-environment-verification.sh

echo "🔍 验证Google Play正式环境配置..."

# 1. 检查正式环境Service Account密钥
if [ ! -f "./config/service-account-key-prod.json" ]; then
    echo "❌ 正式环境Service Account密钥文件缺失"
    exit 1
fi

# 2. 测试API连接
echo "📡 测试Google Play Developer API连接(正式环境)..."

# 3. 验证正式商品配置
echo "🛍️ 验证正式商品配置..."

echo "✅ 正式环境验证完成"
```

## 📊 进度跟踪

### 测试环境
- [ ] Google Play开发者账户注册完成
- [ ] 测试应用创建并上传APK(内部测试轨道)
- [ ] 测试订阅商品配置完成
- [ ] 许可测试账户设置完成
- [ ] 测试环境Service Account配置完成
- [ ] 测试环境RTDN通知配置完成
- [ ] 测试环境验证通过

### 正式环境
- [ ] 正式应用创建并上传APK(生产轨道)
- [ ] 正式订阅商品配置完成
- [ ] 正式用户购买流程配置完成
- [ ] 正式环境Service Account配置完成
- [ ] 正式环境RTDN通知配置完成
- [ ] 正式环境验证通过
