# 本地调试 GodGPT + aevatar-gagents 指南

## 🎯 目标
在 IDE 中调试 `aevatar-gagents` 项目的 `GetStreamingTokenUsage` 方法，跟踪 token usage 提取过程。

## ✅ 准备工作（已完成）

1. **ProjectReference 配置**：已将 `godgpt/src/GodGPT.GAgents/GodGPT.GAgents.csproj` 中的以下包改为 ProjectReference：
   - Aevatar.GAgents.AIGAgent
   - Aevatar.GAgents.AI.Abstractions
   - Aevatar.GAgents.SemanticKernel
   - Aevatar.GAgents.ChatAgent

## 📋 启动步骤

### 第一步：启动 Orleans Silo（必须）

Orleans Silo 是运行 Grain 逻辑的服务器。

```bash
cd /Users/zhengkaiwen/Repository/AIMining/aevatar-station/station/src/Aevatar.Silo
dotnet run
```

**等待看到日志**：
```
[INFO] Silo starting
[INFO] Silo started successfully
```

### 第二步：启动 HttpApi.Host（必须）

API 服务，提供 HTTP 接口。

```bash
cd /Users/zhengkaiwen/Repository/AIMining/aevatar-station/station/src/Aevatar.HttpApi.Host
dotnet run
```

**等待看到日志**：
```
Now listening on: http://[::]:8001
```

### 第三步：在 IDE 中设置断点

1. 打开 `/Users/zhengkaiwen/Repository/AIMining/aevatar-gagents/src/Aevatar.GAgents.SemanticKernel/Brain/ChatBrain/OpenAIBrain.cs`
2. 在以下位置打断点：
   - **第 105 行**：`Logger.LogDebug($"[OpenAIBrain][GetStreamingTokenUsage] Processing {messageList.Count} messages");`
   - **第 115 行**：`if (streamingChatMessageContent.InnerContent is ChatCompletion completions)`
   - **第 124 行**：`if (completions.Usage.InputTokenDetails != null)`
   - **第 126 行**：`cachedTokens += completions.Usage.InputTokenDetails.CachedTokenCount;`

### 第四步：附加调试器

#### 方案 A：使用 Rider / Visual Studio
1. 菜单：**Run → Attach to Process**
2. 搜索 `Aevatar.Silo` 进程
3. 点击 **Attach**

#### 方案 B：使用 VS Code
1. 按 `F5` 或者点击 **Run and Debug**
2. 选择 **.NET: Attach to Process**
3. 搜索 `Aevatar.Silo`
4. 选择并附加

## 🔥 触发对话（调试）

### 方案 1：使用 cURL（推荐）

```bash
# 1. 首先获取认证 token（需要有效的用户账号）
# 如果没有 token，可以跳过认证，使用匿名对话接口（见方案2）

# 2. 发送对话请求
curl -X POST http://localhost:8001/api/gotgpt/chat \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -d '{
    "sessionId": "YOUR_SESSION_ID",
    "content": "Hello, how are you?",
    "region": null
  }'
```

### 方案 2：使用匿名对话接口（无需认证）

```bash
curl -X POST http://localhost:8001/api/godgpt/guest/chat \
  -H "Content-Type: application/json" \
  -d '{
    "content": "What is Python?",
    "language": "en"
  }'
```

### 方案 3：使用 Postman / Insomnia

1. 创建 POST 请求
2. URL: `http://localhost:8001/api/godgpt/guest/chat`
3. Headers:
   ```
   Content-Type: application/json
   ```
4. Body (JSON):
   ```json
   {
     "content": "Tell me about AI",
     "language": "en"
   }
   ```

## 🐛 调试要点

### 关键断点位置

1. **`OpenAIBrain.GetStreamingTokenUsage` 入口**（第 105 行）
   - 检查 `messageList.Count`（应该 > 0）
   - 检查 `messageList` 中的对象类型

2. **`InnerContent` 检查**（第 113-115 行）
   - 查看 `streamingChatMessageContent.InnerContent` 的实际类型
   - 如果不是 `ChatCompletion`，说明数据结构不对

3. **Token Usage 提取**（第 117-132 行）
   - 检查 `completions.Usage.InputTokenCount` 的值
   - 检查 `completions.Usage.InputTokenDetails` 是否为 null
   - 查看 `completions.Usage.InputTokenDetails.CachedTokenCount` 的值

### 预期行为

- ✅ `messageList` 应该包含多个 `StreamingChatMessageContent` 对象
- ✅ **最后一个** chunk 的 `InnerContent` 应该是 `ChatCompletion` 类型
- ✅ `ChatCompletion.Usage` 应该包含 token 统计信息
- ✅ `InputTokenDetails.CachedTokenCount` 应该 > 0（如果有缓存命中）

### 可能的问题

- ❌ `messageList` 为空或只有一个元素
- ❌ `InnerContent` 不是 `ChatCompletion` 类型
- ❌ `Usage` 为 null
- ❌ `InputTokenDetails` 为 null
- ❌ `CachedTokenCount` = 0（没有缓存命中）

## 📊 查看调试变量

在断点处，使用 IDE 的 **Watch** 或 **Immediate Window** 查看：

```csharp
// 查看 messageList 内容
messageList.Count
messageList[0].GetType().FullName

// 查看 InnerContent
streamingChatMessageContent.InnerContent?.GetType().FullName

// 查看 Usage
completions.Usage.InputTokenCount
completions.Usage.OutputTokenCount
completions.Usage.InputTokenDetails
completions.Usage.InputTokenDetails?.CachedTokenCount
```

## 🔧 故障排除

### 问题1：断点没有命中
- **原因**：Silo 使用的是 NuGet 包而不是本地代码
- **解决**：确认 `godgpt` 项目使用了 ProjectReference

### 问题2：无法附加到 Silo 进程
- **原因**：进程未以调试模式运行
- **解决**：使用 `dotnet run --configuration Debug` 启动 Silo

### 问题3：API 返回 401/403
- **原因**：需要认证
- **解决**：使用匿名接口 `/api/godgpt/guest/chat`

### 问题4：找不到 station 项目
- **解决**：station 项目路径应该是：
  ```
  /Users/zhengkaiwen/Repository/AIMining/aevatar-station/station
  ```

## 🎉 调试完成后

记得将 `GodGPT.GAgents.csproj` 改回 PackageReference，避免影响正常部署：

```bash
cd /Users/zhengkaiwen/Repository/AIMining/godgpt
git checkout src/GodGPT.GAgents/GodGPT.GAgents.csproj
```

## 📝 调试记录模板

```
【调试日期】2025-01-XX
【断点位置】OpenAIBrain.GetStreamingTokenUsage:105
【messageList.Count】X
【InnerContent 类型】XXX
【InputTokenCount】XXX
【OutputTokenCount】XXX
【CachedTokenCount】XXX
【结论】XXX
```

---
**提示**：如果 aevatar-station 项目不在本地，可以只调试 godgpt 的部分，然后通过日志分析 aevatar-gagents 的行为。

