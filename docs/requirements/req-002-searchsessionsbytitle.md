# 会话模糊搜索功能需求规范

## 1、功能需求概述
对下游 IChatManagerGAgent 接口要求
新增接口方法
/// <summary>
/// 模糊搜索用户会话
/// </summary>
/// <param name="keyword">搜索关键词</param>
/// <returns>匹配的会话列表，格式与GetSessionListAsync()保持一致并扩展</returns>
IChatManagerGAgent.SearchSessionsAsync(string keyword);

## 2、数据模型扩展要求

### 2.1 SessionInfoDto 结构调整
现有 SessionInfoDto 结构扩展如下：

```csharp
[GenerateSerializer]
public class SessionInfoDto
{
    // === 原有字段（保持不变） ===
    [Id(0)] public Guid SessionId { get; set; }        // 会话唯一标识
    [Id(1)] public string Title { get; set; }          // 会话标题
    [Id(2)] public DateTime CreateAt { get; set; }     // 创建时间
    [Id(3)] public string? Guider { get; set; }        // 角色信息（如"Doctor", "Teacher"等）
    
    // === 新增字段（搜索功能专用） ===
    [Id(4)] public string Content { get; set; } = string.Empty;   // 聊天内容预览（前60字符）
    [Id(5)] public bool IsMatch { get; set; } = false;            // 是否为搜索匹配结果标识
}
```

### 2.2 字段详细说明

#### 2.2.1 原有字段说明
| 字段名 | 类型 | 说明 | 注意事项 |
|--------|------|------|----------|
| `SessionId` | `Guid` | 会话唯一标识符 | 主键，不可为空 |
| `Title` | `string` | 会话标题 | 可能为空字符串，搜索时的主要匹配字段 |
| `CreateAt` | `DateTime` | 会话创建时间 | 用于排序，UTC时间格式 |
| `Guider` | `string?` | 角色扮演信息 | 可为null，如"Doctor"、"Teacher"等 |

#### 2.2.2 新增字段详细说明

**📝 Content 字段**
- **用途**: 存储聊天内容的预览文本，用于搜索匹配和结果展示
- **格式**: 
  - 长度 ≤ 60字符：完整内容
  - 长度 > 60字符：前60字符 + "..."
- **内容来源优先级**:
  1. 优先选择用户消息（长度>5字符）
  2. 回退到助手消息（长度>5字符）
  3. 最后选择任意非空消息
- **默认值**: 空字符串（非null）
- **编码**: UTF-8，支持中英文混合

**🔍 IsMatch 字段**
- **用途**: 标识该记录是否为搜索结果，用于前端区分显示
- **取值**:
  - `true`: 搜索结果记录
  - `false`: 普通会话列表记录
- **使用场景**: 
  - `IChatManagerGAgent.SearchSessionsAsync()` 返回的记录均为 `true`
  - `IChatManagerGAgent.GetSessionListAsync()` 返回的记录均为 `false`
- **默认值**: `false`

### 2.3 Orleans序列化注意事项

#### 2.3.1 序列化ID分配
- 新增字段使用 `[Id(4)]` 和 `[Id(5)]`，避免与现有字段冲突
- ID分配遵循递增原则，确保向后兼容性
- 不得修改现有字段的ID值

#### 2.3.2 兼容性保证
- 新增字段均有默认值，确保反序列化兼容性
- 支持新旧版本混合部署环境
- 字段类型变更需要版本升级策略

## 3、下游实现要求
### 3.1 搜索范围和逻辑
- 搜索用户最近1000条会话记录
- 同时搜索会话标题(title)和消息内容进行模糊匹配
- 不区分大小写
- 支持多关键词（空格分隔），使用OR逻辑

### 3.2 返回数据格式
- **Title**: 保持原有会话标题
- **Content**: 提取聊天内容的前60个字符
  - 如果内容长度 ≤ 60字符：直接返回完整内容
  - 如果内容长度 > 60字符：截取前60字符 + "..."
- **IsMatch**: 设置为 `true`，表示这是搜索结果
- 其他字段(SessionId, CreateAt, Guider)保持原有格式

### 3.3 排序规则
- 标题匹配优先于内容匹配
- 最近创建的会话优先
- 完全匹配优先于部分匹配
-历史会话记录显示（按照时间顺序）

### 3.4 内容提取逻辑示例
public static string ExtractChatContent(List<ChatMessage> messages)
{
    // 获取会话中第一条用户消息或助手消息的内容
    var firstMessage = messages
        .Where(m => !string.IsNullOrWhiteSpace(m.Content))
        .FirstOrDefault();
    if (firstMessage?.Content == null)
        return "";
    string content = firstMessage.Content.Trim();
    if (content.Length <= 60)
        return content;
    else
        return content.Substring(0, 60) + "...";
}

## 4、上游对接标准和注意事项

### 4.1 接口调用规范

#### 4.1.1 方法签名
```csharp
[ReadOnly]
IChatManagerGAgent.SearchSessionsAsync(string keyword, int maxResults = 1000);
```

#### 4.1.2 参数验证要求
- **keyword**: 
  - 不能为 `null` 或空白字符串
  - 建议长度限制：1-200字符
  - 支持中英文、数字、常见符号
- **maxResults**: 
  - 可选参数，默认值1000
  - 有效范围：1-1000
  - 超出范围时使用默认值

#### 4.1.3 返回值处理
- 返回 `List<SessionInfoDto>` 类型
- 空搜索结果返回空列表（非null）
- 所有返回的 `SessionInfoDto` 对象的 `IsMatch` 字段均为 `true`
- `Content` 字段保证非null（可能为空字符串）

### 4.2 错误处理标准

#### 4.2.1 输入验证错误
```csharp
// 空关键词处理
if (string.IsNullOrWhiteSpace(keyword))
{
    return new List<SessionInfoDto>(); // 返回空列表，不抛异常
}

// 超长关键词处理
if (keyword.Length > 200)
{
    // 记录警告日志，返回空列表
    Logger.LogWarning($"Search keyword too long: {keyword.Length} characters");
    return new List<SessionInfoDto>();
}
```

#### 4.2.2 运行时异常处理
- 单个会话处理失败不应影响整体搜索
- 网络异常、数据访问异常应被捕获并记录
- 关键异常记录Warning级别日志
- 确保方法始终返回有效的List对象

### 4.3 性能考虑事项

#### 4.3.1 调用频率限制
- 建议实现防抖机制，避免频繁调用
- 推荐最小调用间隔：300ms
- 考虑实现客户端缓存机制

#### 4.3.2 超时设置
- 建议设置合理的超时时间：5-10秒
- 超时后应优雅降级，返回空结果

#### 4.3.3 资源使用
- 搜索限制在最近1000条会话，避免全量搜索
- 内存使用可控，单次搜索内存消耗 < 10MB
- 支持并发调用，但建议限制并发数

### 4.4 兼容性要求

#### 4.4.1 Orleans框架兼容
- 方法标记 `[ReadOnly]` 属性，确保只读操作
- `SessionInfoDto` 新增字段使用正确的序列化ID
- 支持分布式环境下的调用

#### 4.4.2 向后兼容
- 新增字段有默认值，不影响现有序列化
- 现有 `GetSessionListAsync()` 方法行为不变
- 不影响现有会话管理功能

### 4.5 最佳实践建议

#### 4.5.1 客户端实现建议
```csharp
// 推荐的调用方式
public async Task<List<SessionInfoDto>> SearchSessionsWithValidation(string keyword)
{
    // 1. 输入验证
    if (string.IsNullOrWhiteSpace(keyword))
    {
        return new List<SessionInfoDto>();
    }
    
    // 2. 长度限制
    if (keyword.Length > 200)
    {
        keyword = keyword.Substring(0, 200);
    }
    
    try
    {
        // 3. 调用搜索接口
        var results = await chatManagerGAgent.SearchSessionsAsync(keyword.Trim());
        return results ?? new List<SessionInfoDto>();
    }
    catch (Exception ex)
    {
        // 4. 异常处理
        Logger.LogError(ex, $"Search sessions failed for keyword: {keyword}");
        return new List<SessionInfoDto>();
    }
}
```

#### 4.5.2 UI交互建议
- 实现搜索防抖，用户停止输入300ms后再搜索
- 显示搜索状态（搜索中、无结果、错误状态）
- 高亮显示匹配的关键词
- 提供搜索历史功能

#### 4.5.3 日志记录建议
- Debug级别：记录搜索关键词和结果数量
- Warning级别：记录输入验证失败、单个会话处理失败
- Error级别：记录严重异常和系统错误

### 4.6 测试验证要点

#### 4.6.1 功能测试
- [ ] 空关键词处理
- [ ] 正常关键词搜索
- [ ] 超长关键词处理
- [ ] 特殊字符关键词
- [ ] 中英文混合关键词
- [ ] 大小写不敏感验证

#### 4.6.2 性能测试
- [ ] 1000条会话搜索性能
- [ ] 并发搜索测试
- [ ] 内存使用监控
- [ ] 响应时间测试

#### 4.6.3 异常测试
- [ ] 网络异常处理
- [ ] 数据访问异常
- [ ] 超时处理
- [ ] 并发安全性

---

**文档版本**: v1.1  
**最后更新**: 2024年12月  
**负责人**: 开发团队  
**审核状态**: 待审核