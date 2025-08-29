# 推送通知系统 - 前端接口文档

## 接口概览

推送通知系统为前端提供**2个核心接口**，支持设备注册和已读状态标记。

## API 基础信息

- **Base URL**: `/api/push`
- **认证方式**: Bearer Token (用户登录后获取)
- **内容类型**: `application/json`
- **响应格式**: JSON

## 1. 设备注册/更新接口 (平台无关)

### 接口信息
- **URL**: `POST /api/push/device`
- **用途**: 注册或更新用户设备的推送信息
- **调用频率**: 极低频 (每用户每月0-3次)
- **平台支持**: Firebase自动识别iOS/Android，无需手动指定平台

### 使用场景
- ✅ 应用首次安装并获得推送权限
- ✅ 推送token刷新 (Firebase token变化)
- ✅ 用户手动更改时区设置
- ✅ 用户切换应用语言设置
- ✅ 应用重新安装  
- ✅ 系统升级导致推送配置变化
- ❌ **避免**: 每次应用启动时调用
- ❌ **避免**: 无变化的重复注册

### 平台无关设计
- **自动识别**: Firebase根据pushToken自动识别iOS/Android平台
- **统一接口**: 前端无需判断Platform.OS，直接调用统一接口
- **未来兼容**: 支持Web Push、桌面应用等新平台，无需修改接口

### 请求参数

```typescript
interface DeviceRequest {
  deviceId: string;           // 前端生成的持久化设备ID (必须，主键)
  pushToken?: string;         // Firebase推送令牌 (可选，token更新时传递)
  timeZoneId?: string;        // IANA时区ID (可选，时区变化时传递)
  pushEnabled?: boolean;      // 推送开关 (可选，开关变化时传递)
}

// ✅ deviceId: 前端生成的持久化ID，设备生命周期内不变
// ✅ pushToken: Firebase令牌可能变化，需要时才传递
// ✅ 其他字段: 只在需要更新时传递，支持部分更新
```

### 请求示例

```typescript
// 1. 首次设备注册 (需要完整信息)
const deviceId = await generateDeviceId(); // 生成持久化ID
const registerRequest = {
  deviceId: deviceId,                       // 持久化设备ID (必须)
  pushToken: await messaging().getToken(),  // Firebase令牌 (首次注册必须)
  timeZoneId: "Asia/Shanghai",              // 设备时区 (首次注册必须)
  pushEnabled: true                         // 推送开关 (可选，默认true)
};

// 2. Token更新 (只传变化的字段)
const tokenUpdateRequest = {
  deviceId: deviceId,                       // 设备ID (必须)
  pushToken: newToken                       // 新的Firebase令牌
};

// 3. 推送开关切换
const switchRequest = {
  deviceId: deviceId,                       // 设备ID (必须)
  pushEnabled: false                        // 关闭推送
};

// HTTP Headers (重要: 包含语言信息)
headers: {
  "Content-Type": "application/json",
  "Authorization": "Bearer {token}",
  "GodgptLanguage": "zh-tw"  // ⭐️ 语言头，自动保存到设备语言设置
}

### 前端DeviceID生成逻辑

```typescript
// 生成持久化设备ID
const generateDeviceId = async (): Promise<string> => {
  // 1. 检查本地存储
  let deviceId = await AsyncStorage.getItem('deviceId');
  
  if (!deviceId) {
    // 2. 生成新的设备ID
    const deviceInfo = await DeviceInfo.getDeviceId(); // 设备硬件ID
    const installTime = await AsyncStorage.getItem('firstInstallTime') || Date.now().toString();
    const platform = Platform.OS; // 'ios' 或 'android'
    
    // 3. 组合生成唯一ID (限制32字符)
    deviceId = `${platform}_${deviceInfo}_${installTime}`.substring(0, 32);
    
    // 4. 持久化存储
    await AsyncStorage.setItem('deviceId', deviceId);
    await AsyncStorage.setItem('firstInstallTime', installTime);
  }
  
  return deviceId;
};

// Token变化处理
messaging().onTokenRefresh(async (newToken) => {
  const deviceId = await generateDeviceId(); // 同样的设备ID
  
  // 只更新token
  await fetch('/api/push/device', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${authToken}`,
      'GodgptLanguage': getCurrentLanguage()
    },
    body: JSON.stringify({
      deviceId: deviceId,
      pushToken: newToken // 只传新token
    })
  });
});
}
```

### 响应格式

```typescript
// 成功响应
{
  "success": true
}

// 错误响应
{
  "success": false,
  "error": "Device registration too frequent, please try again later"
}
```

### 常见时区ID

```typescript
const commonTimeZones = {
  // 中国
  "Asia/Shanghai": "中国标准时间 (UTC+8)",
  
  // 美国
  "America/New_York": "美国东部时间 (UTC-5/-4)",
  "America/Los_Angeles": "美国西部时间 (UTC-8/-7)",
  "America/Chicago": "美国中部时间 (UTC-6/-5)",
  
  // 欧洲
  "Europe/London": "英国时间 (UTC+0/+1)",
  "Europe/Paris": "欧洲中部时间 (UTC+1/+2)",
  
  // 亚洲
  "Asia/Tokyo": "日本标准时间 (UTC+9)",
  "Asia/Seoul": "韩国标准时间 (UTC+9)",
  "Asia/Kolkata": "印度标准时间 (UTC+5:30)",
  
  // 澳洲
  "Australia/Sydney": "澳洲东部时间 (UTC+10/+11)",
  
  // 其他
  "UTC": "协调世界时 (UTC+0)"
};
```

### 客户端最佳实践

```typescript
class PushNotificationManager {
  private lastRegistrationTime: number = 0;
  private readonly COOLDOWN_PERIOD = 30 * 60 * 1000; // 30分钟冷却
  
  async registerDeviceIfNeeded() {
    // 1. 检查冷却期
    const now = Date.now();
    if (now - this.lastRegistrationTime < this.COOLDOWN_PERIOD) {
      console.log('Device registration in cooldown period');
      return;
    }
    
      // 2. 获取当前设备信息 (平台无关)
  const currentInfo = await this.getCurrentDeviceInfo();
  const cachedInfo = this.getCachedDeviceInfo();
    
    // 3. 只有信息变化时才调用注册接口
    if (this.hasDeviceInfoChanged(currentInfo, cachedInfo)) {
      try {
        await this.callRegistrationAPI(currentInfo);
        this.cacheDeviceInfo(currentInfo);
        this.lastRegistrationTime = now;
        console.log('Device registration completed');
      } catch (error) {
        console.error('Device registration failed:', error);
      }
    }
  }
  
  private async getCurrentDeviceInfo() {
    const pushToken = await messaging().getToken();     // Firebase自动识别平台
    const timeZoneId = RNLocalize.getTimeZone();
    const appLanguage = this.getCurrentAppLanguage();   // 应用语言设置
    
    return { pushToken, timeZoneId, appLanguage };
  }
  
  private hasDeviceInfoChanged(current: any, cached: any): boolean {
    return !cached || 
           current.pushToken !== cached.pushToken ||
           current.timeZoneId !== cached.timeZoneId ||
           current.appLanguage !== cached.appLanguage; // ⭐️ 语言变化检测
  }
  
  private getCurrentAppLanguage(): string {
    // 获取当前应用语言设置
    // 可以从应用设置、用户偏好或系统语言获取
    const appSetting = getUserLanguageSetting(); // 应用内语言设置
    if (appSetting) return appSetting;
    
    // 降级到系统语言
    const systemLang = RNLocalize.getLocales()[0]?.languageTag;
    return this.normalizeLanguageCode(systemLang);
  }
  
  private normalizeLanguageCode(languageTag: string): string {
    // 将系统语言标签转换为服务端支持的语言代码
    if (languageTag?.startsWith('zh')) {
      return languageTag.includes('CN') ? 'zh-cn' : 'zh-tw';
    } else if (languageTag?.startsWith('es')) {
      return 'es';
    }
    return 'en'; // 默认英语
  }
  
  private async callRegistrationAPI(deviceInfo: any) {
    const response = await fetch('/api/push/device', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${await getAuthToken()}`,
        'GodgptLanguage': deviceInfo.appLanguage // ⭐️ 重要：传递语言信息
      },
      body: JSON.stringify({
        deviceType: deviceInfo.deviceType,
        deviceId: deviceInfo.deviceId,
        pushToken: deviceInfo.pushToken,
        timeZoneId: deviceInfo.timeZoneId
      })
    });
    
    if (!response.ok) {
      throw new Error(`Registration failed: ${response.status}`);
    }
    
    return response.json();
  }
}
```

## 2. 推送已读标记接口 (平台无关)

### 接口信息
- **URL**: `POST /api/push/read`
- **用途**: 标记推送通知为已读状态
- **调用频率**: 低频 (每用户每天0-2次)
- **权限验证**: 通过pushToken自动识别用户和设备

### 使用场景
- ✅ 用户点击推送通知进入应用时
- ✅ 用户在应用内查看推送内容时
- ❌ **避免**: 重复标记同一条推送

### 设计说明
- **平台无关**: pushToken自动识别设备平台，无需手动指定
- **统一请求体**: pushId和pushToken都在请求体中，接口设计一致
- **双重标识**: pushToken标识设备和用户，pushId标识具体推送
- **权限验证**: 后端验证pushToken所属用户是否有权限标记该pushId
- **自动幂等**: 重复调用不会产生副作用

### 请求参数

```typescript
// 请求体参数 (统一在请求体中)
interface MarkReadRequest {
  pushToken: string; // 设备pushToken (用于查找设备，标记今日已读)
  // ❌ 不再需要 pushId - 简化为标记今日已读
  // ❌ 不再需要 deviceId - pushToken可以找到对应设备
}
```

### 请求示例

```typescript
// 标记推送为已读 (简化：只需pushToken)
const request = {
  pushToken: await messaging().getToken()        // 当前设备token
  // ❌ 不再需要 pushId - 点击任意推送都标记今日已读
};

fetch('/api/push/read', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'Authorization': `Bearer ${authToken}`
  },
  body: JSON.stringify(request)
});
```

### 响应格式

```typescript
// 成功响应
{
  "success": true
}

// 错误响应 (pushId不存在或已过期)
{
  "success": false,
  "error": "Push notification not found or expired"
}
```

### 推送Payload格式

推送通知的payload中会包含以下信息：

```typescript
interface PushPayload {
  // Firebase标准字段
  notification: {
    title: string;    // 本地化标题，如 "每日精选内容"、"Daily Content"、"Contenido Diario"
    body: string;     // 本地化内容，如 "📌 标题1\n内容1\n\n📌 标题2\n内容2"
  };
  
  // 自定义数据字段
  data: {
    type: "daily_push";           // 推送类型
    user_id: string;              // 用户ID
    device_id: string;            // 设备ID
    push_date: string;            // 推送日期 (ISO格式)
    is_retry: string;             // "true"/"false" 是否为下午重试
    content_ids: string;          // 内容ID列表 (逗号分隔)
    push_id: string;              // 推送唯一标识 ⭐️ 用于标记已读
    language: string;             // 推送语言 "en"/"zh-tw"/"es" ⭐️ 新增
  };
}
```

### 客户端处理示例

```typescript
// React Native推送处理
import messaging from '@react-native-firebase/messaging';

class PushHandler {
  
  // 处理推送点击
  async handlePushNotificationOpen(remoteMessage: any) {
    const { data } = remoteMessage;
    
    if (data?.type === 'daily_push' && data?.push_id) {
      // 标记为已读
      await this.markPushAsRead(data.push_id);
      
      // 导航到相应页面
      this.navigateToContent(data);
    }
  }
  
  private async markPushAsRead(pushId: string) {
    try {
      const deviceType = Platform.OS === 'ios' ? 1 : 2;
      
      await fetch(`/api/push/read/${pushId}`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${await getAuthToken()}`
        },
        body: JSON.stringify({ deviceType })
      });
      
      console.log(`Push ${pushId} marked as read`);
    } catch (error) {
      console.error('Failed to mark push as read:', error);
    }
  }
  
  private navigateToContent(pushData: any) {
    // 根据推送数据导航到相应页面
    // 例如：导航到每日内容页面
    navigation.navigate('DailyContent', {
      date: pushData.push_date,
      contentIds: pushData.content_ids?.split(',') || []
    });
  }
}

// 初始化推送监听器
const setupPushNotifications = () => {
  const pushHandler = new PushHandler();
  
  // 应用在前台时收到推送
  messaging().onMessage(async remoteMessage => {
    console.log('Foreground push received:', remoteMessage);
    // 显示应用内通知或直接处理
  });
  
  // 用户点击推送通知
  messaging().onNotificationOpenedApp(remoteMessage => {
    console.log('Push notification opened app:', remoteMessage);
    pushHandler.handlePushNotificationOpen(remoteMessage);
  });
  
  // 应用从终止状态被推送启动
  messaging().getInitialNotification().then(remoteMessage => {
    if (remoteMessage) {
      console.log('App opened by push notification:', remoteMessage);
      pushHandler.handlePushNotificationOpen(remoteMessage);
    }
  });
};
```

## 错误处理

### 常见错误码

```typescript
interface ApiError {
  success: false;
  error: string;
  code?: string;
}

// 可能的错误情况
const errorCodes = {
  'RATE_LIMITED': '请求过于频繁，请稍后再试',
  'INVALID_TOKEN': '推送令牌无效',
  'INVALID_TIMEZONE': '时区ID格式错误',
  'PUSH_NOT_FOUND': '推送通知不存在或已过期',
  'UNAUTHORIZED': '用户未登录或token过期'
};
```

### 客户端错误处理示例

```typescript
async function safeApiCall<T>(apiCall: () => Promise<T>): Promise<T | null> {
  try {
    return await apiCall();
  } catch (error) {
    if (error.status === 429) {
      // 频率限制
      console.warn('API rate limited, backing off');
      return null;
    } else if (error.status === 401) {
      // 认证失败
      console.warn('Authentication failed, redirecting to login');
      redirectToLogin();
      return null;
    } else {
      // 其他错误
      console.error('API call failed:', error);
      return null;
    }
  }
}
```

## 注意事项

### ⚠️ 重要提醒

1. **平台无关**: 无需判断iOS/Android，Firebase自动识别平台 ⭐️ **简化**
2. **频率限制**: 设备注册接口有1小时冷却期，避免频繁调用
3. **缓存机制**: 客户端应缓存设备信息，只在变化时调用注册接口
4. **时区获取**: 使用系统API获取准确的时区信息
5. **语言头必须**: 每次调用注册接口都必须包含 `GodgptLanguage` 请求头
6. **设备级语言**: 同一用户的不同设备可以设置不同推送语言
7. **错误重试**: API调用失败时应适当重试，但要避免无限重试
8. **推送权限**: 确保用户已授权推送通知权限

### 🔍 调试技巧

```typescript
// 调试用的设备信息打印
const logDeviceInfo = async () => {
  console.log('=== Device Info Debug ===');
  console.log('Platform:', Platform.OS);
  console.log('Device ID:', await DeviceInfo.getUniqueId());
  console.log('Timezone:', RNLocalize.getTimeZone());
  console.log('Push Token:', await messaging().getToken());
  console.log('App Language:', getCurrentAppLanguage()); // ⭐️ 新增语言调试
  console.log('System Locale:', RNLocalize.getLocales()[0]); // ⭐️ 系统语言
  console.log('========================');
};
```

### 📱 测试建议

1. **开发环境**: 使用Firebase测试token进行推送测试
2. **时区测试**: 手动修改设备时区验证接口调用
3. **语言测试**: 切换应用语言验证推送语言自动更新 ⭐️ **新增**
4. **多设备测试**: 同一账号不同设备设置不同语言验证独立性 ⭐️ **新增**
5. **网络测试**: 模拟网络异常情况测试错误处理
6. **权限测试**: 测试推送权限关闭/开启的场景

## 📱 前端接收的推送Payload格式

### Firebase推送消息结构 (完整配置)

```json
// ✅ 第一次推送 (早上8:00 或测试模式)
{
  "messageId": "0:1705123456789%31bd1c9631bd1c96",
  "from": "123456789012",
  "sentTime": 1705123456789,
  "ttl": 2419200,
  "notification": {
    "title": "每日精選內容 (1/2)",
    "body": "Three-minute breath return",
    "android": {
      "channelId": "daily_push_channel",
      "color": "#FF6B35",                 // 🎨 小图标颜色 (后端指定)
      "sound": "default",                 // 🔊 通知声音
      "tag": "daily_push_2024_01_15",     // 🏷️ 通知标签
      "clickAction": "FLUTTER_NOTIFICATION_CLICK"
      // ❌ 小图标由前端配置，不在推送消息中指定
    },
    "ios": {
      "sound": "notification_sound.aiff", // 🔊 自定义声音
      "badge": 1,                         // 📍 角标数字
      "category": "DAILY_PUSH_CATEGORY",  // 📂 通知类别
      "threadId": "daily_push_thread"     // 🧵 线程分组
    }
  },
  "data": {
    "pushType": "1"     // ✅ 枚举数字值 (DailyPush = 1)
  }
}

// ✅ 第二次推送 (早上8:03-8:05 或测试模式+30秒)
{
  "notification": {
    "title": "每日精選內容 (2/2)",
    "body": "Listen to the silent voice"
  },
  "data": {
    "pushType": "1"     // ✅ 枚举数字值 (DailyPush = 1)
  }
}

// ✅ 下午重试推送 (如果早上未读)
{
  "notification": {
    "title": "重要提醒：每日精選內容 (1/2)",
    "body": "您還沒有查看：Three-minute breath return"
  },
  "data": {
    "pushType": "1"     // ✅ 枚举数字值 (DailyPush = 1)
  }
}
```

### 前端处理示例

```typescript
// PushType枚举定义 (与后端保持一致)
export enum PushType {
  DailyPush = 1
}

// 接收推送的处理 (极简化)
messaging().onMessage(async (remoteMessage) => {
  const { notification, data } = remoteMessage;
  
  if (parseInt(data.pushType) === PushType.DailyPush) {
    // 显示通知
    showNotification(notification.title, notification.body);
    
    // 点击处理 (统一逻辑)
    onNotificationClick(async () => {
      // 标记今日已读 (无论点击第几条都标记整日已读)
      await markTodayAsRead();
      
      // 可选：导航到每日内容页面
      navigateToDailyContentPage();
    });
  }
});

// 简化的已读标记 (移除pushId)
const markTodayAsRead = async () => {
  const pushToken = await messaging().getToken();
  
  await fetch('/api/push/read', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${authToken}`
    },
    body: JSON.stringify({ pushToken }) // ✅ 只需pushToken
  });
};
```

## 📦 **前端资源配置**

### Android小图标配置 (前端配置)

```xml
<!-- ⚠️ 关键：小图标必须配置在前端，后端只指定颜色 -->

<!-- android/app/src/main/res/drawable/ic_stat_notification.xml -->
<!-- 小图标必须是白色的，系统会自动应用后端指定的颜色 -->
<vector xmlns:android="http://schemas.android.com/apk/res/android"
    android:width="24dp"
    android:height="24dp"
    android:viewportWidth="24"
    android:viewportHeight="24">
  <path
      android:fillColor="#FFFFFF"
      android:pathData="M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M11,16.5L6.5,12L7.91,10.59L11,13.67L16.59,8.09L18,9.5L11,16.5Z"/>
</vector>

<!-- android/app/src/main/AndroidManifest.xml -->
<application>
    <!-- 指定默认通知小图标 -->
    <meta-data
        android:name="com.google.firebase.messaging.default_notification_icon"
        android:resource="@drawable/ic_stat_notification" />
    
    <!-- 指定默认通知颜色 (可选，后端会覆盖) -->
    <meta-data
        android:name="com.google.firebase.messaging.default_notification_color"
        android:resource="@color/notification_color" />
</application>
```

### iOS图标配置 (自动使用App图标)

```xml
<!-- ios/Runner/Info.plist -->
<key>UIBackgroundModes</key>
<array>
    <string>remote-notification</string>
</array>

<!-- iOS推送通知自动使用App图标，不需要单独配置小图标 -->
<!-- App图标在ios/Runner/Assets.xcassets/AppIcon.appiconset/中配置 -->

<!-- 可选：自定义通知声音 -->
<!-- ios/Runner/notification_sound.aiff -->
```

### Flutter通知权限配置

```dart
// 初始化推送权限
await FirebaseMessaging.instance.requestPermission(
  alert: true,
  badge: true,
  sound: true,
  provisional: false,
);

// Android通知渠道配置
const AndroidNotificationChannel channel = AndroidNotificationChannel(
  'daily_push_channel',           // 渠道ID (与后端一致)
  'Daily Push Notifications',     // 渠道名称
  description: 'Daily content push notifications for users', // 渠道描述
  importance: Importance.high,    // 重要级别
  enableVibration: true,         // 启用震动
  enableLights: true,            // 启用呼吸灯
  ledColor: Color(0xFFFF6B35),   // LED颜色
  sound: RawResourceAndroidNotificationSound('notification_sound'), // 自定义声音
);

// 创建通知渠道
await FlutterLocalNotificationsPlugin()
    .resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>()
    ?.createNotificationChannel(channel);
```

### 推送效果预览

```
📱 Android效果:
┌─────────────────────────────┐
│ 🔸 每日精選內容 (1/2)        │  ← 橙色小图标
│   Three-minute breath return │
│                        now   │
└─────────────────────────────┘

🍎 iOS效果:
┌─────────────────────────────┐
│ 📱 App Name              🔴1│  ← 红色角标
│ 每日精選內容 (1/2)           │
│ Three-minute breath return  │
│                        now   │
└─────────────────────────────┘
```

## 3. 设备状态查询接口 (新增)

### 接口信息
- **URL**: `GET /api/push/device/{deviceId}`
- **用途**: 查询设备的时区、开关状态等信息
- **调用频率**: 低频 (按需查询)
- **权限验证**: 只能查询当前用户的设备

### 使用场景
- ✅ 设备状态调试和检查
- ✅ 设备设置同步验证
- ✅ 推送状态故障排查
- ❌ **避免**: 频繁轮询设备状态

### 请求示例

```typescript
// 查询设备状态
const deviceId = "user_device_001";

const response = await fetch(`/api/push/device/${deviceId}`, {
  method: 'GET',
  headers: {
    'Authorization': `Bearer ${authToken}`
  }
});

const result = await response.json();
```

### 响应格式

```typescript
// 成功响应 (包含deviceId确认)
{
  "success": true,
  "data": {
    "deviceId": "user_device_001",    // ✅ 确认设备存在
    "timeZoneId": "Asia/Shanghai",
    "pushEnabled": true,
    "pushLanguage": "zh-cn"
  }
}

// 设备不存在
{
  "success": false,
  "error": "Device not found",
  "code": "DEVICE_NOT_FOUND"
}
```

### TypeScript接口定义

```typescript
interface DeviceStatusResponse {
  success: boolean;
  data?: {
    deviceId: string;       // 设备ID (确认设备存在)
    timeZoneId: string;     // 设备时区
    pushEnabled: boolean;   // 推送开关状态
    pushLanguage: string;   // 推送语言设置
  };
  error?: string;
  code?: string;
}
```

### 前端使用示例

```typescript
class DeviceManager {
  
  async getDeviceStatus(deviceId: string): Promise<DeviceStatusResponse> {
    try {
      const response = await fetch(`/api/push/device/${deviceId}`, {
        method: 'GET',
        headers: {
          'Authorization': `Bearer ${await getAuthToken()}`
        }
      });
      
      return await response.json();
    } catch (error) {
      console.error('Failed to get device status:', error);
      throw error;
    }
  }
  
  async checkDeviceSettings(deviceId: string) {
    const result = await this.getDeviceStatus(deviceId);
    
    if (result.success && result.data) {
      const device = result.data;
      console.log(`Device ${device.deviceId}:`);
      console.log(`- Timezone: ${device.timeZoneId}`);
      console.log(`- Push Enabled: ${device.pushEnabled}`);
      console.log(`- Language: ${device.pushLanguage}`);
      
      return device;
    } else {
      console.log(`Device ${deviceId} not found or access denied`);
      return null;
    }
  }
  
  // ✅ 新增：设备存在性检查
  async isDeviceRegistered(deviceId: string): Promise<boolean> {
    try {
      const result = await this.getDeviceStatus(deviceId);
      return result.success && !!result.data?.deviceId;
    } catch (error) {
      console.error('Failed to check device registration:', error);
      return false;
    }
  }
}
```

---

**接口文档版本**: v2.5 (设备状态查询接口)  
**最后更新**: 2024年  
**维护团队**: 后端API团队

### 版本更新日志
- v2.5: ✅ 新增设备状态查询接口 `GET /api/push/device/{deviceId}`
- v2.4: DeviceID主键设计
- v2.3: 推送开关优化 + 极简配置
- v2.2: 简化已读逻辑 + pushType枚举
- v2.1: 多语言支持 + 时区优化
- v2.0: 初始设计

## 📋 更新日志

### v2.5 (2024年) - 设备状态查询接口 ⭐️ **当前版本**
- ✅ 新增 `GET /api/push/device/{deviceId}` 查询设备状态
- ✅ 返回数据：`deviceId`、`timeZoneId`、`pushEnabled`、`pushLanguage`
- ✅ 设备存在性确认：返回deviceId可验证设备是否已注册
- ✅ 权限隔离：只能查询当前用户的设备
- ✅ 隐私保护：不返回敏感信息如pushToken等

### v2.4 (2024年) - DeviceID主键设计
- ✅ 引入 `deviceId` 作为设备主键，解决token变化问题
- ✅ `pushToken` 改为可选字段，支持独立更新
- ✅ 支持部分更新：只传需要变化的字段
- ✅ Token自动刷新不影响设备标识和推送功能

### v2.3 (2024年) - 极简化Payload
- ✅ data字段极简化，只保留 `pushType: "1"`
- ✅ 移除 `sequence`、`contentId`、`date`、`isTestMode` 等冗余字段
- ✅ 前端处理逻辑大幅简化，统一已读标记

### v2.2 (2024年) - 简化已读逻辑 + pushType枚举
- ✅ 移除 `pushId` 字段，简化为标记今日已读
- ✅ 添加 `pushType` 枚举字段 (值为 "DailyPush")  
- ✅ 推送两次，点击任意一次即标记今日已读
- ✅ 更新Excel导入格式适配 (contentKey, title_*/content_*)
- ✅ 支持英文、繁体中文、西班牙语三种语言

### v2.1 (2024年) - 平台无关设计
- ✅ 完全移除deviceType参数 (Firebase自动识别平台)
- ✅ 设备注册接口只需pushToken和timeZoneId  
- ✅ 简化前端代码，无需判断iOS/Android
- ✅ 真正的跨平台统一接口
- ✅ 未来支持Web Push、桌面应用等新平台

### v2.0 (2024年) - 接口参数统一
- ✅ 标记已读接口改为 `POST /api/push/read` (移除URL参数)
- ✅ pushId和pushToken统一放在请求体中
- ✅ 移除冗余的deviceType参数
- ✅ 接口设计更加一致和简洁

### v1.9 (2024年) - 部分更新支持
- ✅ 支持部分字段更新，无需传入完整数据
- ✅ pushToken作为唯一必需字段 (设备标识)
- ✅ 首次注册要求deviceType和timeZoneId
- ✅ 后续更新只传需要修改的字段
- ✅ 智能检测新注册 vs 部分更新

### v1.8 (2024年) - 数据结构精简
- ✅ 精简UserDeviceInfo字段 (6个字段 → 6个核心字段)
- ✅ 移除冗余的LastActiveTime、IsActive、PushDisabledAt
- ✅ 统一使用PushEnabled作为唯一推送控制开关
- ✅ 保留RegisteredAt用于故障排查

### v1.7 (2024年) - 设备级时区
- ✅ 明确时区归属于设备而非用户
- ✅ 支持同一用户设备在不同时区推送
- ✅ 移除deviceName字段，简化数据结构
- ✅ 强调timeZoneId的设备级重要性

### v1.6 (2024年) - pushToken Hash
- ✅ 使用pushToken hash作为设备key (后端自动生成)
- ✅ 前端无需生成deviceId，简化集成
- ✅ 支持同一用户多个设备 (不同pushToken自动区分)
- ✅ 12字符短hash key，Redis友好

### v1.5 (2024年) - 已优化
- ❌ 手动deviceId生成 (发现问题：前端复杂度高)

### v1.4 (2024年) - 已废弃
- ❌ 移除deviceId字段 (发现问题：多设备支持不足)
- ❌ pushToken作为key (发现问题：key太长)

### v1.3 (2024年)
- ✅ 新增下午15:00重试推送机制
- ✅ 实现推送已读状态检测
- ✅ 支持智能推送重试 (未读早晨推送才重试)
- ✅ 优化推送流程，避免重复推送给已读用户

### v1.2 (2024年)
- ✅ 新增设备推送开关字段 (`pushEnabled`)
- ✅ 通过现有 `/api/push/device` 接口管理推送开关
- ✅ 支持设备级推送开关管理
- ✅ 新增推送开关使用场景和最佳实践
- ✅ 优化推送逻辑，避免向已关闭推送的设备发送通知

### v1.1 (2024年)
- ✅ 新增设备级语言支持
- ✅ 新增 `GodgptLanguage` 请求头要求
- ✅ 推送payload中新增 `language` 字段
- ✅ 更新客户端最佳实践，包含语言检测逻辑
- ✅ 新增多设备多语言测试建议

### v1.0 (2024年)
- ✅ 初始版本，基础设备注册和推送已读功能

## 3. 设备推送开关管理

### 通过设备注册接口管理
推送开关通过 `/api/push/device` 接口的 `pushEnabled` 字段来管理，无需独立接口。

### 推送开关字段

```typescript
interface DeviceRequest {
  pushToken: string;        // 必须: 设备标识 (Firebase自动识别平台)
  timeZoneId?: string;      // 可选: 设备时区 (首次注册必须)
  pushEnabled?: boolean;    // 可选: 推送开关
}
```

### 使用场景

1. **首次注册**: 必须提供完整信息
```typescript
const request = {
  pushToken: await messaging().getToken(),     // Firebase自动识别iOS/Android
  timeZoneId: RNLocalize.getTimeZone(),        // 必须
  pushEnabled: true  // 可选，默认为true
};
```

2. **仅修改推送开关**: 只传需要修改的字段
```typescript
const request = {
  pushToken: await messaging().getToken(),  // 必须：设备标识
  pushEnabled: false                        // 只修改推送开关
  // 不需要传deviceType和timeZoneId
};
```

3. **仅修改时区**: 设备移动到新时区
```typescript
const request = {
  pushToken: await messaging().getToken(),  // 必须：设备标识
  timeZoneId: "Europe/London"               // 只修改时区
  // 不需要传其他字段
};
```

4. **pushToken刷新**: Firebase token更新时 (简化处理)
```typescript
// 监听Firebase token刷新事件
messaging().onTokenRefresh(async (newToken) => {
  // 简化策略：直接当作新设备注册
  const request = {
    pushToken: newToken,                    // 新的pushToken (Firebase自动识别平台)
    timeZoneId: RNLocalize.getTimeZone(),
    pushEnabled: true                       // 默认启用推送
  };
  
  await this.callRegistrationAPI(request);
  await AsyncStorage.setItem('pushToken', newToken);
  
  // 注意：用户可能需要重新设置推送偏好
  this.notifyUserToCheckPushSettings();
});
```

5. **同时修改多个字段**: 
```typescript
const request = {
  pushToken: await messaging().getToken(),
  timeZoneId: "America/New_York",           // 修改时区
  pushEnabled: true                         // 同时开启推送
};
```

6. **多设备场景**: 每个设备独立管理
```typescript
// iPhone首次注册
const iPhoneRequest = {
  pushToken: "iPhonePushToken...",
  deviceType: 1,                    // iOS
  timeZoneId: "Asia/Shanghai",
  pushEnabled: true
};

// iPad首次注册 (不同pushToken自动区分)
const iPadRequest = {
  pushToken: "iPadPushToken...",    // 不同token = 不同设备
  deviceType: 1,                    // iOS
  timeZoneId: "America/New_York"    // 不同时区
};

// 后续只修改iPad的推送开关
const iPadUpdateRequest = {
  pushToken: "iPadPushToken...",    // 设备标识
  pushEnabled: false                // 只修改开关
};
```

### 推送开关最佳实践

```typescript
// 推送开关管理
class PushSettingsManager {
  
  // 更新推送开关 (复用设备注册接口)
  async setPushEnabled(enabled: boolean): Promise<boolean> {
    try {
      const deviceInfo = await this.getCurrentDeviceInfo(); // 平台无关
      
      const response = await fetch('/api/push/device', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${getAuthToken()}`,
          'GodgptLanguage': getCurrentLanguage()
        },
        body: JSON.stringify({
          ...deviceInfo,
          pushEnabled: enabled  // 只更新推送开关
        })
      });
      
      const result = await response.json();
      return result.success;
    } catch (error) {
      console.error('Failed to update push settings:', error);
      return false;
    }
  }
  
  // 首次注册设备信息
  async registerDevice() {
    const request = {
      pushToken: await messaging().getToken(),     // Firebase自动识别平台
      timeZoneId: RNLocalize.getTimeZone(),
      pushEnabled: true
    };
    
    return await this.callRegistrationAPI(request);
  }
  
  // 仅更新推送开关
  async updatePushSettings(enabled: boolean) {
    const request = {
      pushToken: await messaging().getToken(),
      pushEnabled: enabled
    };
    
    return await this.callRegistrationAPI(request);
  }
  
  // 仅更新时区 (设备移动时调用)
  async updateTimeZone() {
    const request = {
      pushToken: await messaging().getToken(),
      timeZoneId: RNLocalize.getTimeZone()
    };
    
    return await this.callRegistrationAPI(request);
  }
  
  // pushToken刷新处理 (简化版)
  async handleTokenRefresh(newToken: string) {
    // 简化策略：直接当作新设备注册
    const request = {
      pushToken: newToken,                        // Firebase自动识别平台
      timeZoneId: RNLocalize.getTimeZone(),
      pushEnabled: true  // 默认启用，用户可后续调整
    };
    
    const result = await this.callRegistrationAPI(request);
    if (result.success) {
      await AsyncStorage.setItem('pushToken', newToken);
      console.log('New device registered after token refresh');
      
      // 可选：提醒用户检查推送设置
      this.showPushSettingsReminder();
    }
  }
  
  // 初始化时设置token刷新监听
  setupTokenRefreshListener() {
    messaging().onTokenRefresh(this.handleTokenRefresh.bind(this));
  }
  
  // 提醒用户检查推送设置
  showPushSettingsReminder() {
    // 显示通知或弹窗，提醒用户检查推送设置
    Alert.alert(
      "推送设置",
      "检测到应用重新安装，请检查您的推送偏好设置",
      [{ text: "知道了" }]
    );
  }
}
```

如有疑问，请联系后端开发团队。
