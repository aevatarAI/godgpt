# Fortune Prediction 重构计划

## ✅ 已完成部分 (约40%)

### 1. State/Event/DTO 定义重写 ✅
- `FortunePredictionState` - 新的统一结构 (9个字段)
- `PredictionResultDto` - 新的扁平返回结构 (9个字段)
- `PredictionGeneratedEvent` - 新的事件结构 (8个字段)
- `LanguagesTranslatedEvent` - 新的多语言事件结构
- `FortuneUserProfileState` - WelcomeNote改为多语言字典

### 2. FortunePredictionGAgent 核心方法重构 ✅
- `GetPredictionAsync` - 完全重写，使用新结构
- `GAgentTransitionState` - 适配新事件结构
- `GeneratePredictionAsync` - 核心逻辑重写：
  - ✅ 变量声明（`parsedResults`, `multilingualResults`）
  - ✅ 字段注入逻辑（Lifetime四柱、Yearly星座）
  - ✅ RaiseEvent（使用新的`PredictionGeneratedEvent`）
  - ✅ 返回DTO构建（使用新的`PredictionResultDto`）
- `GetOrGeneratePredictionAsync` - 缓存检查逻辑重写 ✅

---

## ⚠️ 剩余工作 (约60% - 64个编译错误)

### 🔧 Priority 1: FortunePredictionGAgent 剩余方法

#### 1.1 ParseMultilingualDailyResponse (line ~1267)
**当前签名**:
```csharp
private (Dictionary<string, Dictionary<string, string>>?, 
         Dictionary<string, Dictionary<string, Dictionary<string, string>>>?) 
ParseMultilingualDailyResponse(string aiResponse)
```

**修改为**:
```csharp
private (Dictionary<string, string>?, 
         Dictionary<string, Dictionary<string, string>>?) 
ParseMultilingualDailyResponse(string aiResponse)
```

**内部修改**:
- 找到返回语句（约line 1350-1400）
- 将嵌套的 `Dictionary<string, Dictionary<string, string>>` 改为扁平的 `Dictionary<string, string>`
- 可能需要调用 `FlattenNestedJsonToFlat` 方法

#### 1.2 ParseMultilingualLifetimeResponse (line ~1395)
**当前签名**: ✅ 已经正确（`Dictionary<string, string>`）
**但需要检查**: 内部逻辑是否真的返回扁平结构

#### 1.3 GenerateRemainingLanguagesAsync (line ~700-850)
**需要修改**:
- 适配新的 `LanguagesTranslatedEvent` 结构
- 移除对 `State.DailyGeneratedLanguages` / `YearlyGeneratedLanguages` / `LifetimeGeneratedLanguages` 的引用
- 改用统一的 `State.GeneratedLanguages`
- RaiseEvent时使用新的事件结构：
  ```csharp
  RaiseEvent(new LanguagesTranslatedEvent
  {
      Type = type,
      PredictionDate = predictionDate,
      TranslatedLanguages = translatedContent,
      AllGeneratedLanguages = new List<string> { initialLanguage, ...translatedLangs }
  });
  ```

#### 1.4 删除废弃方法
**删除以下方法**:
1. `ApplyLocalization` (约line 1800-1850) - 已在Grain层完成
2. `ExtractEnumValues` (约line 1900-2000) - 枚举字段已在results中

---

### 🔧 Priority 2: FortunePredictionHistoryGAgent

**错误示例** (line 136, 170):
```
error CS0029: Cannot implicitly convert type 
'System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>' 
to 'System.Collections.Generic.Dictionary<string, string>'
```

**修改策略**:
1. 所有对 `State.Results` 的赋值/读取都要适配扁平结构
2. 移除对 `State.LifetimeForecast` / `YearlyForecast` / `MultilingualLifetime` 等字段的引用
3. 使用统一的 `State.Results` 和 `State.MultilingualResults`

**关键方法**:
- `GetHistoryAsync` - 返回历史预测列表
- `GAgentTransitionState` - 事件处理逻辑

---

### 🔧 Priority 3: FortuneUserProfileGAgent

#### 3.1 WelcomeNote 多语言生成 (New Feature)
**当前**: `State.WelcomeNote` 是 `Dictionary<string, string>`（单语言）
**修改为**: `State.MultilingualWelcomeNote` 是 `Dictionary<string, Dictionary<string, string>>`

**实现步骤**:
1. 修改 `GenerateWelcomeNote` 方法签名：
   ```csharp
   private Dictionary<string, Dictionary<string, string>> GenerateWelcomeNote(
       FortuneUserInfo userInfo, string initialLanguage)
   ```

2. 生成初始语言版本后，触发异步翻译：
   ```csharp
   var welcomeContent = new Dictionary<string, string>
   {
       ["rhythm"] = rhythm,
       ["essence"] = essence
   };
   
   var multilingual = new Dictionary<string, Dictionary<string, string>>
   {
       [initialLanguage] = welcomeContent
   };
   
   // Async translate to other languages
   _ = Task.Run(async () => await TranslateWelcomeNoteAsync(welcomeContent, initialLanguage));
   
   return multilingual;
   ```

3. 添加 `TranslateWelcomeNoteAsync` 方法（类似 `GenerateRemainingLanguagesAsync`）

#### 3.2 ZodiacSign/ChineseZodiac 多语言返回 (约line 60-80)
**当前**: `GetUserProfileAsync` 返回固定的英文zodiac名称
**修改**: 根据 `Accept-Language` 返回对应语言

**实现**:
```csharp
public Task<GetUserProfileResult> GetUserProfileAsync(Guid userId, string userLanguage = "en")
{
    // ... existing logic ...
    
    // Translate zodiac based on user language
    var zodiacTranslated = TranslateZodiacSign(zodiacSign, userLanguage);
    var chineseZodiacTranslated = TranslateChineseZodiac(chineseZodiac, userLanguage);
    
    // Get localized welcome note
    Dictionary<string, string> localizedWelcomeNote;
    if (State.MultilingualWelcomeNote.ContainsKey(userLanguage))
    {
        localizedWelcomeNote = State.MultilingualWelcomeNote[userLanguage];
    }
    else if (State.MultilingualWelcomeNote.ContainsKey("en"))
    {
        localizedWelcomeNote = State.MultilingualWelcomeNote["en"];
    }
    else
    {
        localizedWelcomeNote = GenerateFallbackWelcomeNote(userInfo);
    }
    
    var profileDto = new FortuneUserProfileDto
    {
        // ... existing fields ...
        ZodiacSign = zodiacTranslated,
        ChineseZodiac = chineseZodiacTranslated,
        WelcomeNote = localizedWelcomeNote, // Now returns only requested language
        // ... enums ...
    };
    
    return Task.FromResult(new GetUserProfileResult { ... });
}
```

**新增翻译方法**:
```csharp
private string TranslateZodiacSign(string zodiacSign, string language)
{
    return language switch
    {
        "zh-tw" or "zh" => zodiacSign switch
        {
            "Aries" => "白羊座",
            "Taurus" => "金牛座",
            // ... all 12 signs
            _ => zodiacSign
        },
        "es" => zodiacSign switch
        {
            "Aries" => "Aries",
            "Taurus" => "Tauro",
            // ... all 12 signs
            _ => zodiacSign
        },
        _ => zodiacSign // English default
    };
}

private string TranslateChineseZodiac(string chineseZodiac, string language)
{
    // Extract animal name (e.g., "Wood Pig" -> "Pig")
    var animalName = chineseZodiac.Split(' ').Last();
    
    return language switch
    {
        "zh-tw" or "zh" => animalName switch
        {
            "Rat" => "鼠",
            "Ox" => "牛",
            // ... all 12 animals
            _ => animalName
        },
        "es" => animalName switch
        {
            "Rat" => "Rata",
            "Ox" => "Buey",
            // ... all 12 animals
            _ => $"El {animalName}"
        },
        _ => chineseZodiac // English default (keep full name like "Wood Pig")
    };
}
```

---

### 🔧 Priority 4: FortuneCalculator 新增方法

#### 4.1 ParseZodiacSignEnum (New Method)
```csharp
public static ZodiacSignEnum ParseZodiacSignEnum(string zodiacSign)
{
    return zodiacSign switch
    {
        "Aries" => ZodiacSignEnum.Aries,
        "Taurus" => ZodiacSignEnum.Taurus,
        "Gemini" => ZodiacSignEnum.Gemini,
        "Cancer" => ZodiacSignEnum.Cancer,
        "Leo" => ZodiacSignEnum.Leo,
        "Virgo" => ZodiacSignEnum.Virgo,
        "Libra" => ZodiacSignEnum.Libra,
        "Scorpio" => ZodiacSignEnum.Scorpio,
        "Sagittarius" => ZodiacSignEnum.Sagittarius,
        "Capricorn" => ZodiacSignEnum.Capricorn,
        "Aquarius" => ZodiacSignEnum.Aquarius,
        "Pisces" => ZodiacSignEnum.Pisces,
        _ => ZodiacSignEnum.Unknown
    };
}
```

#### 4.2 ParseChineseZodiacEnum (New Method)
```csharp
public static ChineseZodiacEnum ParseChineseZodiacEnum(string chineseZodiac)
{
    // Extract animal name (e.g., "Wood Pig" -> "Pig")
    var animalName = chineseZodiac.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
    
    return animalName switch
    {
        "Rat" => ChineseZodiacEnum.Rat,
        "Ox" => ChineseZodiacEnum.Ox,
        "Tiger" => ChineseZodiacEnum.Tiger,
        "Rabbit" => ChineseZodiacEnum.Rabbit,
        "Dragon" => ChineseZodiacEnum.Dragon,
        "Snake" => ChineseZodiacEnum.Snake,
        "Horse" => ChineseZodiacEnum.Horse,
        "Goat" or "Sheep" => ChineseZodiacEnum.Goat,
        "Monkey" => ChineseZodiacEnum.Monkey,
        "Rooster" => ChineseZodiacEnum.Rooster,
        "Dog" => ChineseZodiacEnum.Dog,
        "Pig" or "Boar" => ChineseZodiacEnum.Pig,
        _ => ChineseZodiacEnum.Unknown
    };
}
```

---

### 🔧 Priority 5: FortuneController API 层适配

#### 5.1 移除 ApplyLocalizationToPrediction 调用
**已在 FortuneController 中删除此方法** ✅

#### 5.2 确认所有API返回使用新DTO
检查以下接口:
- `GetUserProfileAsync` - 返回 `FortuneUserProfileDto` (with multilingual welcome note)
- `GetTodayPredictionAsync` - 返回 `PredictionResultDto` (flat results)
- `GetYearlyPredictionAsync` - 返回 `PredictionResultDto` (flat results)
- `GetLifetimePredictionAsync` - 返回 `PredictionResultDto` (flat results)
- `TriggerPredictionGenerationAsync` - 不返回预测内容，只返回成功/失败

---

## 📋 完整错误修复Checklist

### FortunePredictionGAgent.cs
- [ ] Line 463: 修改 `ParseMultilingualDailyResponse` 返回类型
- [ ] Line 700-850: 修改 `GenerateRemainingLanguagesAsync` 使用新事件
- [ ] Line 1267: 更新 `ParseMultilingualDailyResponse` 签名和返回逻辑
- [ ] Line 1395: 检查 `ParseMultilingualLifetimeResponse` 是否返回扁平结构
- [ ] Line 1800-1850: 删除 `ApplyLocalization` 方法
- [ ] Line 1900-2000: 删除 `ExtractEnumValues` 方法

### FortunePredictionHistoryGAgent.cs
- [ ] Line 136: 修复 `State.Results` 赋值类型
- [ ] Line 170: 修复 `State.Results` 赋值类型
- [ ] Line 176-177: 修复日志方法调用参数
- [ ] 全文搜索并移除对以下字段的引用:
  - `State.LifetimeForecast`
  - `State.YearlyForecast`
  - `State.MultilingualLifetime`
  - `State.MultilingualYearly`
  - `State.DailyGeneratedLanguages`
  - `State.YearlyGeneratedLanguages`
  - `State.LifetimeGeneratedLanguages`

### FortuneUserProfileGAgent.cs
- [ ] 修改 `GenerateWelcomeNote` 为多语言生成
- [ ] 添加 `TranslateWelcomeNoteAsync` 方法
- [ ] 修改 `GetUserProfileAsync` 根据 `userLanguage` 返回本地化内容
- [ ] 添加 `TranslateZodiacSign` 方法
- [ ] 添加 `TranslateChineseZodiac` 方法

### FortuneCalculator.cs
- [ ] 添加 `ParseZodiacSignEnum` 方法
- [ ] 添加 `ParseChineseZodiacEnum` 方法

### FortuneUserProfileState.cs
- [ ] 修改 `WelcomeNote` 字段为 `MultilingualWelcomeNote`
- [ ] 更新 `Id` 序号

### FortuneEventLog.cs
- [ ] 添加 `WelcomeNoteGeneratedEvent` (如果需要单独事件)
- [ ] 或在 `ProfileCreatedEvent` / `ProfileUpdatedEvent` 中包含 welcome note

---

## 🧪 测试验证

完成所有修改后，需要验证:

1. **Daily 预测**:
   - [ ] 返回扁平的 `results` 字段
   - [ ] 包含 `tarotCard_enum`, `luckyStone_enum`
   - [ ] 只返回请求语言的内容
   - [ ] `AvailableLanguages` 字段正确

2. **Yearly 预测**:
   - [ ] 返回扁平的 `results` 字段
   - [ ] 包含 `sunSign_enum`, `chineseZodiac_enum`
   - [ ] 只返回请求语言的内容

3. **Lifetime 预测**:
   - [ ] 返回扁平的 `results` 字段
   - [ ] 包含四柱8个字（`fourPillars_yearPillar_stem` 等）
   - [ ] 包含 `sunSign_enum`, `chineseZodiac_enum`
   - [ ] `chineseZodiac_title` 为用户生肖（不是当年生肖）
   - [ ] 只返回请求语言的内容
   - [ ] 包含 `currentPhase`

4. **Profile 接口**:
   - [ ] `WelcomeNote` 只返回请求语言版本
   - [ ] `ZodiacSign` 和 `ChineseZodiac` 根据语言翻译
   - [ ] 包含 `ZodiacSignEnum` 和 `ChineseZodiacEnum`

5. **缓存逻辑**:
   - [ ] Lifetime 永不过期（除非profile更新）
   - [ ] Yearly 每年过期
   - [ ] Daily 每天过期
   - [ ] 缓存命中时正确返回本地化内容

6. **幂等性**:
   - [ ] 并发调用 `trigger` 不会重复生成
   - [ ] 1分钟超时机制正常工作

---

## 📌 优先级建议

### 第一阶段: 编译通过 (2-3小时)
1. 修复 `FortunePredictionGAgent.cs` 中的解析方法
2. 修复 `FortunePredictionHistoryGAgent.cs` 类型错误
3. 添加 `FortuneCalculator` 新方法
4. 删除废弃方法

### 第二阶段: 功能完善 (3-4小时)
5. `FortuneUserProfileGAgent` 多语言支持
6. Profile接口多语言返回

### 第三阶段: 测试验证 (2-3小时)
7. 单元测试
8. 集成测试
9. API测试

**总预计时间**: 7-10小时

---

## 💡 快速修复命令

### 批量替换字段引用
```bash
# 在 FortunePredictionHistoryGAgent.cs 中
# 将 State.LifetimeForecast 替换为 State.Results (需要判断 State.Type)
# 将 State.YearlyForecast 替换为 State.Results
# 将 State.MultilingualLifetime 替换为 State.MultilingualResults
# 将 State.MultilingualYearly 替换为 State.MultilingualResults
# 将 State.DailyGeneratedLanguages 替换为 State.GeneratedLanguages
# 将 State.YearlyGeneratedLanguages 替换为 State.GeneratedLanguages
# 将 State.LifetimeGeneratedLanguages 替换为 State.GeneratedLanguages
```

### 编译并查看错误
```bash
cd /Users/zhengkaiwen/Repository/AIMining/godgpt
dotnet build 2>&1 | grep "error CS"
```

---

## 🎯 当前状态

- ✅ 完成率: ~40%
- ⚠️ 待修复编译错误: 64个
- 📝 剩余TODO: 9个
- 🔥 关键阻塞点: 
  1. 解析方法返回类型
  2. HistoryGAgent类型错误
  3. 废弃方法删除

**建议**: 按Priority 1 → Priority 2 → Priority 3顺序完成，每完成一个Priority后编译测试。

