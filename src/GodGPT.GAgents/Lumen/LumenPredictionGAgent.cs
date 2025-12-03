using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Common;
using Aevatar.Application.Grains.Lumen.Dtos;
using Aevatar.Application.Grains.Lumen.Helpers;
using Aevatar.Application.Grains.Lumen.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Aevatar.Application.Grains.Lumen.Options;
using Aevatar.GAgents.AI.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Orleans.Concurrency;
using SwissEphNet;

namespace Aevatar.Application.Grains.Lumen;

/// <summary>
/// Prediction type enumeration
/// </summary>
public enum PredictionType
{
    Daily = 0, // Daily prediction - updates every day
    Yearly = 1, // Yearly prediction - updates every year
    Lifetime = 2 // Lifetime prediction - never updates (unless profile changes)
}

/// <summary>
/// Lifetime prediction batch type for parallel LLM calls
/// </summary>
public enum LifetimeBatchType
{
    /// <summary>Western Astrology: Sun/Moon/Rising archetypes, combined essence, zodiac whisper</summary>
    WesternAstrology = 0,
    
    /// <summary>Chinese Astrology: Four Pillars, Chinese traits, zodiac cycle</summary>
    ChineseAstrology = 1,
    
    /// <summary>Life Traits: Strengths, challenges, destiny paths</summary>
    LifeTraits = 2,
    
    /// <summary>Timeline & Plot: 10-year luck cycles, life plot narrative, mantras</summary>
    TimelinePlot = 3
}

/// <summary>
/// Interface for Lumen Prediction GAgent - manages lumen prediction generation
/// </summary>
public interface ILumenPredictionGAgent : IGAgent
{
    Task<GetTodayPredictionResult> GetOrGeneratePredictionAsync(LumenUserDto userInfo,
        PredictionType type = PredictionType.Daily, string userLanguage = "en", DateOnly? predictionDate = null);
    
    [ReadOnly]
    Task<PredictionResultDto?> GetPredictionAsync(string userLanguage = "en");
    
    [ReadOnly]
    Task<PredictionStatusDto?> GetPredictionStatusAsync(DateTime? profileUpdatedAt = null);
    
    Task ClearCurrentPredictionAsync();
    
    [ReadOnly]
    Task<Dictionary<string, string>> GetCalculatedValuesAsync(LumenUserDto userInfo, string userLanguage = "en");
    
    /// <summary>
    /// Trigger translation for this prediction to target language (fire-and-forget, triggered by language switch)
    /// </summary>
    Task TriggerTranslationAsync(LumenUserDto userInfo, string targetLanguage);
    
    /// <summary>
    /// Update daily reminder with new timezone (triggered when user updates timezone)
    /// </summary>
    Task UpdateTimeZoneReminderAsync(string timeZoneId);
}

[GAgent(nameof(LumenPredictionGAgent))]
[Reentrant]
public class LumenPredictionGAgent : GAgentBase<LumenPredictionState, LumenPredictionEventLog>, 
    ILumenPredictionGAgent,
    IRemindable
{
    private readonly ILogger<LumenPredictionGAgent> _logger;
    private readonly IClusterClient _clusterClient;
    private readonly LumenPredictionOptions _options;

    // Default fallback values if options are not configured
    private const string DEFAULT_DAILY_REMINDER_NAME = "LumenDailyPredictionReminder";
    private const int DEFAULT_PROMPT_VERSION = 28;
    private const int DEFAULT_MAX_RETRY_COUNT = 3;
    
    /// <summary>
    /// Global prompt version - increment this when prompt content is updated
    /// This will allow all users to regenerate predictions on the same day
    /// 
    /// ⚠️ TODO: REMOVE THIS FEATURE BEFORE PRODUCTION LAUNCH
    /// Currently set to 7 for testing purposes (all existing users will regenerate).
    /// Before launch, either:
    /// 1. Remove prompt version checking entirely, OR
    /// 2. Set CURRENT_PROMPT_VERSION = 0 to avoid mass regeneration
    /// 
    /// Version 4: Migrated from JSON to TSV format for improved reliability and performance
    /// Version 5: Simplified TSV keys to ultra-short format (e.g. career, stone, fate_do) with mapping layer
    /// Version 6: Moved format and language purity constraints to system prompt for stronger LLM compliance
    /// Version 7: Fixed conflicting format requirements in singleLanguagePrefix (removed JSON requirements)
    /// Version 8: Simplified system prompt, clarified language purity for ALL languages (not just Chinese)
    /// Version 9: Renamed sensitive fields to reduce LLM refusal (health→wellness, wealth→prosperity, destiny→path, fate→fortune)
    /// Version 10: Fixed 	 literal text issue - clarified LLM should use actual tab character, not the text '	'
    /// Version 11: Strengthened language enforcement - added language requirement to system prompt and used native language names (简体中文 instead of Simplified Chinese)
    /// Version 12: Ultra-strong language enforcement - write language instructions IN the target language itself (e.g., "必须用简体中文" for Chinese)
    /// Version 13: Clarified field name vs field value distinction - field names in English, field values in target language, with concrete examples
    /// Version 14: Fixed prompt contradictions - aligned system/user prompts on language requirements, replaced all [TAB] placeholders with actual tab characters in examples
    /// Version 15: Added explicit template translation reminder - LLM must translate English template text to target language with concrete examples
    /// Version 16: Translated zodiac signs in prompts to ensure language consistency
    /// Version 17: Removed birthTime/birthCity from prompts for privacy; Changed wording from "prediction/fortune" to "reflection/insight" to reduce LLM refusal rate
    /// Version 18: Removed fullname from prompts; Relaxed format requirements; Simplified fixed-format fields (path_title, cn_year, sun_arch, moon_arch, rising_arch) - backend constructs these
    /// Version 19: Fixed multilingual templates for path_title and archetype fields (sunArchetype, moonArchetype, risingArchetype)
    /// Version 20: Removed user name examples from prompts; Removed "addressing by name" from pillars_id field; Simplified language instruction blocks
    /// Version 21: Softened command language - replaced MUST/NOT/CRITICAL with please/avoid/guideline; Added clear rules about using Display Name only
    /// Version 22: Strengthened language requirements with explicit examples - added ✓/✗ examples, "check before finishing" reminder, self-correction prompt
    /// Version 23: Changed card_name/card_orient/stone fields to use English standard names; Added automatic Chinese translation via dictionary lookup
    /// Version 24: Fixed Daily translation to replace original field values; Localized Lifetime cycle_title and cycle_intro templates
    /// Version 25: Moved Western Astrology calculation logic from WesternAstrologyCalculator into LumenPredictionGAgent to fix logger null issue
    /// Version 26: Changed zodiacCycle_title to be backend-constructed - LLM returns only year range (cycle_year_range), backend injects localized prefix
    /// Version 27: Dynamic cycle_name fields - always request cycle_name_zh plus one language-specific field (en/zh-tw/es) based on targetLanguage
    /// Version 28: Simplified Chinese (zh) only requests cycle_name_zh, which is copied to both zodiacCycle_cycleName and zodiacCycle_cycleNameChinese
    /// Version 29: Simplified cycle_name fields - unified cycle_name field for current language + cycle_name_zh for Chinese baseline (removed language-specific suffixes)
    /// </summary>
    [Obsolete("Use _options.PromptVersion instead. This constant is kept as fallback only.")]
    private const int CURRENT_PROMPT_VERSION = 28;
    
    // Daily reminder version control - change this GUID to invalidate all existing reminders
    // When logic changes (e.g., switching from UTC 00:00 to user timezone 08:00), update this value
    [Obsolete("Use _options.ReminderTargetId instead. This constant is kept as fallback only.")]
    private static readonly Guid CURRENT_REMINDER_TARGET_ID = new Guid("00000000-0000-0000-0000-000000000001");

    // Translation dictionaries for English -> Chinese (Simplified/Traditional)
    private static readonly Dictionary<string, (string zh, string zhTw)> TarotCardTranslations = new()
    {
        // Major Arcana
        ["The Fool"] = ("愚者", "愚者"),
        ["The Magician"] = ("魔术师", "魔術師"),
        ["The High Priestess"] = ("女祭司", "女祭司"),
        ["The Empress"] = ("女皇", "女皇"),
        ["The Emperor"] = ("皇帝", "皇帝"),
        ["The Hierophant"] = ("教皇", "教皇"),
        ["The Lovers"] = ("恋人", "戀人"),
        ["The Chariot"] = ("战车", "戰車"),
        ["Strength"] = ("力量", "力量"),
        ["The Hermit"] = ("隐士", "隱士"),
        ["Wheel of Fortune"] = ("命运之轮", "命運之輪"),
        ["Justice"] = ("正义", "正義"),
        ["The Hanged Man"] = ("倒吊者", "倒吊者"),
        ["Death"] = ("死亡", "死亡"),
        ["Temperance"] = ("节制", "節制"),
        ["The Devil"] = ("恶魔", "惡魔"),
        ["The Tower"] = ("塔", "塔"),
        ["The Star"] = ("星星", "星星"),
        ["The Moon"] = ("月亮", "月亮"),
        ["The Sun"] = ("太阳", "太陽"),
        ["Judgement"] = ("审判", "審判"),
        ["The World"] = ("世界", "世界"),
        
        // Minor Arcana - Wands
        ["Ace of Wands"] = ("权杖王牌", "權杖王牌"),
        ["Two of Wands"] = ("权杖二", "權杖二"),
        ["Three of Wands"] = ("权杖三", "權杖三"),
        ["Four of Wands"] = ("权杖四", "權杖四"),
        ["Five of Wands"] = ("权杖五", "權杖五"),
        ["Six of Wands"] = ("权杖六", "權杖六"),
        ["Seven of Wands"] = ("权杖七", "權杖七"),
        ["Eight of Wands"] = ("权杖八", "權杖八"),
        ["Nine of Wands"] = ("权杖九", "權杖九"),
        ["Ten of Wands"] = ("权杖十", "權杖十"),
        ["Page of Wands"] = ("权杖侍从", "權杖侍從"),
        ["Knight of Wands"] = ("权杖骑士", "權杖騎士"),
        ["Queen of Wands"] = ("权杖王后", "權杖王后"),
        ["King of Wands"] = ("权杖国王", "權杖國王"),
        
        // Minor Arcana - Cups
        ["Ace of Cups"] = ("圣杯王牌", "聖杯王牌"),
        ["Two of Cups"] = ("圣杯二", "聖杯二"),
        ["Three of Cups"] = ("圣杯三", "聖杯三"),
        ["Four of Cups"] = ("圣杯四", "聖杯四"),
        ["Five of Cups"] = ("圣杯五", "聖杯五"),
        ["Six of Cups"] = ("圣杯六", "聖杯六"),
        ["Seven of Cups"] = ("圣杯七", "聖杯七"),
        ["Eight of Cups"] = ("圣杯八", "聖杯八"),
        ["Nine of Cups"] = ("圣杯九", "聖杯九"),
        ["Ten of Cups"] = ("圣杯十", "聖杯十"),
        ["Page of Cups"] = ("圣杯侍从", "聖杯侍從"),
        ["Knight of Cups"] = ("圣杯骑士", "聖杯騎士"),
        ["Queen of Cups"] = ("圣杯王后", "聖杯王后"),
        ["King of Cups"] = ("圣杯国王", "聖杯國王"),
        
        // Minor Arcana - Swords
        ["Ace of Swords"] = ("宝剑王牌", "寶劍王牌"),
        ["Two of Swords"] = ("宝剑二", "寶劍二"),
        ["Three of Swords"] = ("宝剑三", "寶劍三"),
        ["Four of Swords"] = ("宝剑四", "寶劍四"),
        ["Five of Swords"] = ("宝剑五", "寶劍五"),
        ["Six of Swords"] = ("宝剑六", "寶劍六"),
        ["Seven of Swords"] = ("宝剑七", "寶劍七"),
        ["Eight of Swords"] = ("宝剑八", "寶劍八"),
        ["Nine of Swords"] = ("宝剑九", "寶劍九"),
        ["Ten of Swords"] = ("宝剑十", "寶劍十"),
        ["Page of Swords"] = ("宝剑侍从", "寶劍侍從"),
        ["Knight of Swords"] = ("宝剑骑士", "寶劍騎士"),
        ["Queen of Swords"] = ("宝剑王后", "寶劍王后"),
        ["King of Swords"] = ("宝剑国王", "寶劍國王"),
        
        // Minor Arcana - Pentacles
        ["Ace of Pentacles"] = ("金币王牌", "金幣王牌"),
        ["Two of Pentacles"] = ("金币二", "金幣二"),
        ["Three of Pentacles"] = ("金币三", "金幣三"),
        ["Four of Pentacles"] = ("金币四", "金幣四"),
        ["Five of Pentacles"] = ("金币五", "金幣五"),
        ["Six of Pentacles"] = ("金币六", "金幣六"),
        ["Seven of Pentacles"] = ("金币七", "金幣七"),
        ["Eight of Pentacles"] = ("金币八", "金幣八"),
        ["Nine of Pentacles"] = ("金币九", "金幣九"),
        ["Ten of Pentacles"] = ("金币十", "金幣十"),
        ["Page of Pentacles"] = ("金币侍从", "金幣侍從"),
        ["Knight of Pentacles"] = ("金币骑士", "金幣騎士"),
        ["Queen of Pentacles"] = ("金币王后", "金幣王后"),
        ["King of Pentacles"] = ("金币国王", "金幣國王"),
    };
    
    private static readonly Dictionary<string, (string zh, string zhTw)> StoneTranslations = new()
    {
        // Common gemstones
        ["Amethyst"] = ("紫水晶", "紫水晶"),
        ["Rose Quartz"] = ("粉水晶", "粉水晶"),
        ["Citrine"] = ("黄水晶", "黃水晶"),
        ["Clear Quartz"] = ("白水晶", "白水晶"),
        ["Smoky Quartz"] = ("茶晶", "茶晶"),
        ["Black Obsidian"] = ("黑曜石", "黑曜石"),
        ["Moonstone"] = ("月光石", "月光石"),
        ["Labradorite"] = ("拉长石", "拉長石"),
        ["Lapis Lazuli"] = ("青金石", "青金石"),
        ["Turquoise"] = ("绿松石", "綠松石"),
        ["Malachite"] = ("孔雀石", "孔雀石"),
        ["Jade"] = ("玉", "玉"),
        ["Emerald"] = ("祖母绿", "祖母綠"),
        ["Aquamarine"] = ("海蓝宝", "海藍寶"),
        ["Sapphire"] = ("蓝宝石", "藍寶石"),
        ["Ruby"] = ("红宝石", "紅寶石"),
        ["Garnet"] = ("石榴石", "石榴石"),
        ["Carnelian"] = ("红玛瑙", "紅瑪瑙"),
        ["Agate"] = ("玛瑙", "瑪瑙"),
        ["Moss Agate"] = ("苔藓玛瑙", "苔蘚瑪瑙"),
        ["Tiger's Eye"] = ("虎眼石", "虎眼石"),
        ["Hematite"] = ("赤铁矿", "赤鐵礦"),
        ["Pyrite"] = ("黄铁矿", "黃鐵礦"),
        ["Amazonite"] = ("天河石", "天河石"),
        ["Sodalite"] = ("方钠石", "方鈉石"),
        ["Aventurine"] = ("东陵石", "東陵石"),
        ["Fluorite"] = ("萤石", "螢石"),
        ["Peridot"] = ("橄榄石", "橄欖石"),
        ["Topaz"] = ("黄玉", "黃玉"),
        ["Opal"] = ("蛋白石", "蛋白石"),
        ["Pearl"] = ("珍珠", "珍珠"),
        ["Coral"] = ("珊瑚", "珊瑚"),
        ["Amber"] = ("琥珀", "琥珀"),
        ["Rhodonite"] = ("蔷薇辉石", "薔薇輝石"),
        ["Rhodochrosite"] = ("菱锰矿", "菱錳礦"),
        ["Kunzite"] = ("紫锂辉石", "紫鋰輝石"),
        ["Selenite"] = ("透石膏", "透石膏"),
        ["Calcite"] = ("方解石", "方解石"),
        ["Howlite"] = ("菱镁矿", "菱鎂礦"),
        ["Jasper"] = ("碧玉", "碧玉"),
        ["Bloodstone"] = ("血石", "血石"),
        ["Onyx"] = ("缟玛瑙", "縞瑪瑙"),
        ["Jet"] = ("煤玉", "煤玉"),
    };
    
    private static readonly Dictionary<string, (string zh, string zhTw)> OrientationTranslations = new()
    {
        ["Upright"] = ("正位", "正位"),
        ["Reversed"] = ("逆位", "逆位"),
    };

    public LumenPredictionGAgent(
        ILogger<LumenPredictionGAgent> logger,
        IClusterClient clusterClient,
        IOptions<LumenPredictionOptions> options)
    {
        _logger = logger;
        _clusterClient = clusterClient;
        _options = options?.Value ?? new LumenPredictionOptions(); // Fallback to default if options not configured
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Lumen prediction generation and caching");
    }

    /// <summary>
    /// Event-driven state transition handler
    /// </summary>
    protected sealed override void GAgentTransitionState(LumenPredictionState state,
        StateLogEventBase<LumenPredictionEventLog> @event)
    {
        switch (@event)
        {
            case PredictionGeneratedEvent generatedEvent:
                state.PredictionId = generatedEvent.PredictionId;
                state.UserId = generatedEvent.UserId;
                state.PredictionDate = generatedEvent.PredictionDate;
                state.CreatedAt = generatedEvent.CreatedAt;
                state.ProfileUpdatedAt = generatedEvent.ProfileUpdatedAt;
                state.Type = generatedEvent.Type;
                state.LastGeneratedDate = generatedEvent.LastGeneratedDate;
                
                // Store flattened results
                state.Results = generatedEvent.Results;
                
                // Store multilingual results
                if (generatedEvent.MultilingualResults != null)
                {
                    state.MultilingualResults = generatedEvent.MultilingualResults;
                }
                
                // Initialize language generation status
                if (!string.IsNullOrEmpty(generatedEvent.InitialLanguage))
                {
                    state.GeneratedLanguages = new List<string> { generatedEvent.InitialLanguage };
                    
                    // Initialize today's processed languages
                    state.TodayProcessDate = generatedEvent.LastGeneratedDate;
                    state.TodayProcessedLanguages = new List<string> { generatedEvent.InitialLanguage };
                }

                break;
                
            case LanguagesTranslatedEvent translatedEvent:
                // Update multilingual cache with translated languages
                if (translatedEvent.TranslatedLanguages != null)
                {
                    foreach (var lang in translatedEvent.TranslatedLanguages)
                    {
                        state.MultilingualResults[lang.Key] = lang.Value;
                    }
                }
                
                // Update generated languages list
                state.GeneratedLanguages = translatedEvent.AllGeneratedLanguages;
                state.LastGeneratedDate = translatedEvent.LastGeneratedDate;
                break;
                
            case GenerationLockSetEvent lockSetEvent:
                // Set generation lock (persisted to survive Grain deactivation)
                if (!state.GenerationLocks.ContainsKey(lockSetEvent.Type))
                {
                    state.GenerationLocks[lockSetEvent.Type] = new GenerationLockInfo();
                }

                state.GenerationLocks[lockSetEvent.Type].IsGenerating = true;
                state.GenerationLocks[lockSetEvent.Type].StartedAt = lockSetEvent.StartedAt;
                state.GenerationLocks[lockSetEvent.Type].RetryCount = lockSetEvent.RetryCount;
                break;
                
            case GenerationLockClearedEvent lockClearedEvent:
                // Clear generation lock (mark generation as completed or failed)
                if (state.GenerationLocks.ContainsKey(lockClearedEvent.Type))
                {
                    state.GenerationLocks[lockClearedEvent.Type].IsGenerating = false;
                    state.GenerationLocks[lockClearedEvent.Type].StartedAt = null;
                }

                break;
                
            case PredictionClearedEvent clearedEvent:
                // Clear current prediction data (for user deletion or profile update)
                state.PredictionId = Guid.Empty;
                state.UserId = string.Empty;
                state.PredictionDate = default;
                state.CreatedAt = default;
                state.ProfileUpdatedAt = null;
                state.Type = default;
                state.LastGeneratedDate = null;
                state.Results.Clear();
                state.MultilingualResults.Clear();
                state.GeneratedLanguages.Clear();
                state.GenerationLocks.Clear();
                state.TranslationLocks.Clear();
                state.TodayProcessDate = null;
                state.TodayProcessedLanguages.Clear();
                // Note: Do NOT clear LastActiveDate, DailyReminderTargetId, IsDailyReminderEnabled
                // These are user activity tracking fields, not prediction data
                break;
        }
    }

    public async Task<GetTodayPredictionResult> GetOrGeneratePredictionAsync(LumenUserDto userInfo,
        PredictionType type = PredictionType.Daily, string userLanguage = "en", DateOnly? predictionDate = null)
    {
        var totalStopwatch = Stopwatch.StartNew();
        try
        {
            // Use provided date or default to today
            var targetDate = predictionDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var currentYear = today.Year;
            
            // Note: Location warning is generated in GeneratePredictionAsync method
            
            _logger.LogInformation(
                $"[PERF][Lumen] {userInfo.UserId} START - Type: {type}, Date: {targetDate}, Language: {userLanguage}");
            
            // Update user activity and ensure daily reminder is registered (for Daily predictions only)
            await UpdateActivityAndEnsureReminderAsync();
            
            // ========== IDEMPOTENCY CHECK: Prevent concurrent generation for this type ==========
            if (State.GenerationLocks.TryGetValue(type, out var lockInfo) && lockInfo.IsGenerating)
            {
                // Check if generation timed out (5 minutes - handles service restart scenarios)
                // LLM calls can take 70-100+ seconds, so allow sufficient time
                if (lockInfo.StartedAt.HasValue)
            {
                    var elapsed = DateTime.UtcNow - lockInfo.StartedAt.Value;
                    
                    if (elapsed.TotalMinutes < 5)
                    {
                        // Generation is in progress, return waiting status
                        totalStopwatch.Stop();
                        _logger.LogWarning(
                            $"[Lumen] {userInfo.UserId} GENERATION_IN_PROGRESS - Type: {type}, StartedAt: {lockInfo.StartedAt}, Elapsed: {elapsed.TotalSeconds:F1}s");
                        
                        return new GetTodayPredictionResult
                        {
                            Success = false,
                            Message =
                                $"{type} prediction is currently being generated. Please wait a moment and try again."
                        };
                    }
                    else
                    {
                        // Generation timed out (service restart or actual timeout), reset lock and retry using Event Sourcing
                        _logger.LogWarning(
                            $"[Lumen] {userInfo.UserId} GENERATION_TIMEOUT - Type: {type}, StartedAt: {lockInfo.StartedAt}, Elapsed: {elapsed.TotalMinutes:F2} minutes, Resetting lock and retrying");
                        RaiseEvent(new GenerationLockClearedEvent
                        {
                            Type = type
                        });
                        await ConfirmEvents();
                    }
                }
            }
                
                // Check if profile has been updated since prediction was generated
                // If State.ProfileUpdatedAt is null (first generation or after delete), profile is considered "changed" (need to generate)
                // If State.ProfileUpdatedAt has value, check if profile update time is later than prediction time
            var profileNotChanged =
                State.ProfileUpdatedAt.HasValue && userInfo.UpdatedAt <= State.ProfileUpdatedAt.Value;
                
            // Check if prediction already exists (from cache/state)
            var hasCachedPrediction = State.PredictionId != Guid.Empty && 
                                     !State.Results.IsNullOrEmpty() && 
                                     State.Type == type;
            
            // Check expiration based on type
            bool notExpired = type switch
            {
                PredictionType.Lifetime => true, // Lifetime never expires
                PredictionType.Yearly => State.PredictionDate.Year == currentYear, // Yearly expires after 1 year
                PredictionType.Daily => State.PredictionDate == targetDate, // Daily expires when target date changes
                _ => false
            };
            
            // TODO: REMOVE BEFORE PRODUCTION - Prompt version check for testing only
            // Check if prompt version matches (if version changed, allow regeneration on the same day)
            var currentPromptVersion = _options?.PromptVersion ?? DEFAULT_PROMPT_VERSION;
            var promptVersionMatches = State.PromptVersion == currentPromptVersion;
            if (!promptVersionMatches)
            {
                _logger.LogInformation(
                    $"[Lumen] {userInfo.UserId} Prompt version mismatch - State: {State.PromptVersion}, Current: {currentPromptVersion}, Will regenerate prediction");
            }
            
            // ========== CACHE HIT: Return cached prediction only if all conditions are met ==========
            // Skip cache and trigger regeneration if:
            // - Prediction expired (based on type)
            // - Profile was updated after prediction was generated
            // - Prompt version changed
            if (hasCachedPrediction && notExpired && profileNotChanged && promptVersionMatches)
            {
                // Return cached prediction
                totalStopwatch.Stop();
                _logger.LogInformation(
                    $"[PERF][Lumen] {userInfo.UserId} Cache_Hit: {totalStopwatch.ElapsedMilliseconds}ms - Type: {type}");
                
                // ========== NEW LOGIC: No fallback, return empty Results if language not available ==========
                Dictionary<string, string> localizedResults;
                string returnedLanguage = userLanguage;
                bool isFallback = false;
                
                if (State.MultilingualResults.ContainsKey(userLanguage))
                {
                    // Requested language is available
                    localizedResults = State.MultilingualResults[userLanguage];
                    _logger.LogDebug(
                        $"[Lumen] {userInfo.UserId} Language '{userLanguage}' available, returning content");
                }
                else
                {
                    // Language not available - return EMPTY Results (no fallback, no auto-translation)
                    localizedResults = new Dictionary<string, string>();
                    isFallback = true; // Indicates content is not available in requested language
                        _logger.LogWarning(
                        $"[Lumen] {userInfo.UserId} Language '{userLanguage}' not available, returning empty Results with AvailableLanguages");
                }
                
                // Add currentPhase for Lifetime predictions
                if (type == PredictionType.Lifetime)
                {
                    var currentPhase = CalculateCurrentPhase(userInfo.BirthDate);
                    localizedResults = new Dictionary<string, string>(localizedResults);
                    localizedResults["currentPhase"] = currentPhase.ToString();
                }
                
                // Add lucky number for Daily predictions (backend-calculated, not from LLM)
                if (type == PredictionType.Daily && localizedResults.Count > 0)
                {
                    localizedResults = new Dictionary<string, string>(localizedResults);
                    var luckyNumberResult = Services.LuckyNumberService.CalculateLuckyNumber(
                        userInfo.BirthDate,
                        State.PredictionDate,
                        returnedLanguage);
                    
                    localizedResults["luckyAlignments_luckyNumber_number"] = luckyNumberResult.NumberWord;
                    localizedResults["luckyAlignments_luckyNumber_digit"] = luckyNumberResult.Digit.ToString();
                    localizedResults["luckyAlignments_luckyNumber_description"] = luckyNumberResult.Description;
                    localizedResults["luckyAlignments_luckyNumber_calculation"] = luckyNumberResult.CalculationFormula;
                }
                
                // Get available languages from MultilingualResults (actual available languages)
                // If MultilingualResults is empty (but not null), fallback to GeneratedLanguages
                var availableLanguages = (State.MultilingualResults != null && State.MultilingualResults.Count > 0)
                                         ? State.MultilingualResults.Keys.ToList()
                                         : (State.GeneratedLanguages ?? new List<string>());
                
                // Convert array fields to JSON array strings before returning to frontend
                localizedResults = ConvertArrayFieldsToJson(localizedResults);
                
                // Add quotes to affirmation text based on language
                localizedResults = AddQuotesToAffirmation(localizedResults, returnedLanguage);
                
                var cachedDto = new PredictionResultDto
                {
                    PredictionId = State.PredictionId,
                    UserId = State.UserId,
                    PredictionDate = State.PredictionDate,
                    CreatedAt = State.CreatedAt,
                    FromCache = true,
                    Type = State.Type,
                    Results = localizedResults,
                    AvailableLanguages = availableLanguages,
                    AllLanguagesGenerated = availableLanguages.Count == 4,
                    RequestedLanguage = userLanguage,
                    ReturnedLanguage = returnedLanguage,
                    IsFallback = isFallback,
                    Feedbacks = null
                };
                
            // Generate location warning message
            string? warning = null;
            if (string.IsNullOrWhiteSpace(userInfo.LatLong) && string.IsNullOrWhiteSpace(userInfo.LatLongInferred))
            {
                if (!string.IsNullOrWhiteSpace(userInfo.BirthCity))
                {
                    warning = "Location coordinates could not be determined from your birth city. Moon and Rising sign calculations may be unavailable. Please update your profile with latitude/longitude for more accurate predictions.";
                }
                else
                {
                    warning = "Birth city not provided. Moon and Rising sign calculations are unavailable. Please update your profile with birth city or latitude/longitude for more accurate predictions.";
                }
            }
                
                return new GetTodayPredictionResult
                {
                    Success = true,
                    Message = string.Empty,
                Prediction = cachedDto,
                Warning = warning
                };
            }
            
            // Log reason for regeneration
            if (hasCachedPrediction && !notExpired)
            {
                _logger.LogInformation(
                    "[LumenPredictionGAgent][GetOrGeneratePredictionAsync] {Type} expired, regenerating for {UserId}, TargetDate: {TargetDate}",
                    type, userInfo.UserId, targetDate);
            }

            if (hasCachedPrediction && !profileNotChanged)
            {
                _logger.LogInformation(
                    "[LumenPredictionGAgent][GetOrGeneratePredictionAsync] Profile updated, regenerating {Type} prediction for {UserId}",
                    type, userInfo.UserId);
            }

            // Prediction not found - trigger async generation and return error
            _logger.LogInformation(
                $"[Lumen][OnDemand] {userInfo.UserId} Prediction not found for {type}, triggering async generation");
            
            // Trigger async generation (wait for lock to be set, then fire-and-forget the actual generation)
            await TriggerOnDemandGenerationAsync(userInfo, today, type, userLanguage);
            
                totalStopwatch.Stop();
            return new GetTodayPredictionResult
            {
                Success = false,
                Message =
                    $"{type} prediction is not available yet. Generation has been triggered, please check status or try again in a moment."
            };
        }
        catch (Exception ex)
        {
            totalStopwatch.Stop();
            _logger.LogError(ex,
                $"[PERF][Lumen] {userInfo.UserId} Error: {totalStopwatch.ElapsedMilliseconds}ms - Exception in GetOrGeneratePredictionAsync");
            return new GetTodayPredictionResult
            {
                Success = false,
                Message = "Failed to generate prediction"
            };
        }
    }

    /// <summary>
    /// Get prediction from state without generating (NEW LOGIC: No fallback, return empty Results if language not available)
    /// </summary>
    public Task<PredictionResultDto?> GetPredictionAsync(string userLanguage = "en")
    {
        if (State.PredictionId == Guid.Empty)
        {
            _logger.LogWarning(
                "[LumenPredictionGAgent][GetPredictionAsync] No prediction data - PredictionId is empty. UserId: {UserId}, Type: {Type}",
                State.UserId, State.Type);
            return Task.FromResult<PredictionResultDto?>(null);
        }

        // NEW LOGIC: No fallback mechanism - if language not available, return empty Results
        Dictionary<string, string> localizedResults;
        string returnedLanguage = userLanguage;
        bool isFallback = false;

        // Check if requested language is available
        if (State.MultilingualResults != null && State.MultilingualResults.ContainsKey(userLanguage))
        {
            // Requested language is available
            localizedResults = State.MultilingualResults[userLanguage];
            _logger.LogDebug(
                "[LumenPredictionGAgent][GetPredictionAsync] Language '{Language}' available, returning content",
                userLanguage);
        }
        else
        {
            // Language not available - return EMPTY Results (no fallback)
            localizedResults = new Dictionary<string, string>();
            isFallback = true; // Indicates content is not available in requested language
                    _logger.LogWarning(
                "[LumenPredictionGAgent][GetPredictionAsync] Language '{Language}' not available, returning empty Results with AvailableLanguages",
                        userLanguage);
        }

        // Get available languages from MultilingualResults (actual available languages)
        // If MultilingualResults is empty (but not null), fallback to GeneratedLanguages
        var availableLanguages = (State.MultilingualResults != null && State.MultilingualResults.Count > 0)
                                 ? State.MultilingualResults.Keys.ToList()
                                 : (State.GeneratedLanguages ?? new List<string>());

        var predictionDto = new PredictionResultDto
        {
            PredictionId = State.PredictionId,
            UserId = State.UserId,
            PredictionDate = State.PredictionDate,
            CreatedAt = State.CreatedAt,
            FromCache = true,
            Type = State.Type,
            Results = localizedResults,
            AvailableLanguages = availableLanguages,
            AllLanguagesGenerated = availableLanguages.Count == 4,
            RequestedLanguage = userLanguage,
            ReturnedLanguage = returnedLanguage,
            IsFallback = isFallback,
            Feedbacks = null
        };

        return Task.FromResult<PredictionResultDto?>(predictionDto);
    }

    /// <summary>
    /// Get prediction generation status
    /// </summary>
    public async Task<PredictionStatusDto?> GetPredictionStatusAsync(DateTime? profileUpdatedAt = null)
    {
        // Determine the actual prediction type from GenerationLocks (since State.Type defaults to 0)
        // Each grain only handles one type, so use the first (and only) key if available
        var actualType = State.Type;
        if (State.GenerationLocks.Count > 0)
        {
            actualType = State.GenerationLocks.Keys.First();
        }
        
        // If no prediction has been generated yet, return a status indicating "never generated"
        if (State.PredictionId == Guid.Empty)
        {
            // Check if currently generating for the first time
            var isGenerating = false;
            DateTime? generationStartedAt = null;
            if (State.GenerationLocks.TryGetValue(actualType, out var lockInfo))
            {
                isGenerating = lockInfo.IsGenerating;
                generationStartedAt = lockInfo.StartedAt;
                
                // Check for stale lock (>5 minutes) and clear it using Event Sourcing
                // LLM calls can take 70-100+ seconds, so allow sufficient time
                if (isGenerating && lockInfo.StartedAt.HasValue && 
                    (DateTime.UtcNow - lockInfo.StartedAt.Value).TotalMinutes > 5)
                {
                    _logger.LogWarning(
                        $"[Lumen][Status] Detected stale generation lock for {actualType}, clearing it (elapsed: {(DateTime.UtcNow - lockInfo.StartedAt.Value).TotalMinutes:F2} minutes)");
                    
                    // Persist lock clearing using Event Sourcing
                    RaiseEvent(new GenerationLockClearedEvent
                    {
                        Type = actualType
                    });
                    await ConfirmEvents();
                    
                    isGenerating = false;
                    generationStartedAt = null;
                }
            }
            
            return new PredictionStatusDto
            {
                Type = actualType,
                IsGenerated = false,
                IsGenerating = isGenerating,
                GeneratedAt = null,
                GenerationStartedAt = generationStartedAt,
                PredictionDate = null,
                AvailableLanguages = new List<string>(),
                NeedsRegeneration = true, // Always needs generation if never generated
                TranslationStatus = null
            };
        }

        // Check if currently generating
        var isGenerating2 = false;
        DateTime? generationStartedAt2 = null;
        if (State.GenerationLocks.TryGetValue(actualType, out var lockInfo2))
        {
            isGenerating2 = lockInfo2.IsGenerating;
            generationStartedAt2 = lockInfo2.StartedAt;
            
            // Check for stale lock (>5 minutes) and clear it using Event Sourcing
            // LLM calls can take 70-100+ seconds, so allow sufficient time
            if (isGenerating2 && lockInfo2.StartedAt.HasValue && 
                (DateTime.UtcNow - lockInfo2.StartedAt.Value).TotalMinutes > 5)
            {
                _logger.LogWarning(
                    $"[Lumen][Status] Detected stale generation lock for {actualType}, clearing it (elapsed: {(DateTime.UtcNow - lockInfo2.StartedAt.Value).TotalMinutes:F2} minutes)");
                
                // Persist lock clearing using Event Sourcing
                RaiseEvent(new GenerationLockClearedEvent
                {
                    Type = actualType
                });
                await ConfirmEvents();
                
                isGenerating2 = false;
                generationStartedAt2 = null;
            }
        }

        // Check if needs regeneration (profile was updated after prediction was generated)
        // If State.ProfileUpdatedAt is null (first generation or after delete), treat as needs regeneration
        // If State.ProfileUpdatedAt has value, check if profile was updated after prediction
        var needsRegeneration = false;
        if (profileUpdatedAt.HasValue)
        {
            needsRegeneration = !State.ProfileUpdatedAt.HasValue ||
                                profileUpdatedAt.Value > State.ProfileUpdatedAt.Value;
        }

        // For Daily predictions, also check if prediction is for today
        if (State.Type == PredictionType.Daily)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (State.PredictionDate != today)
            {
                needsRegeneration = true;
            }
        }
        
        // ========== IF REGENERATION NEEDED, CHECK IF GENERATION IS IN PROGRESS ==========
        // NEW BEHAVIOR: Old data remains available while new data generates
        // Return isGenerated=true (old data exists) AND isGenerating=true (new data generating)
        if (needsRegeneration && State.PredictionId != Guid.Empty)
        {
            // Check if generation is currently in progress (even though old data exists)
            var isRegenerating = false;
            DateTime? regenerationStartedAt = null;
            if (State.GenerationLocks.TryGetValue(actualType, out var regenLockInfo))
            {
                isRegenerating = regenLockInfo.IsGenerating;
                regenerationStartedAt = regenLockInfo.StartedAt;
                
                // Check for stale lock (>5 minutes) and clear it
                if (isRegenerating && regenLockInfo.StartedAt.HasValue && 
                    (DateTime.UtcNow - regenLockInfo.StartedAt.Value).TotalMinutes > 5)
                {
                    _logger.LogWarning(
                        $"[Lumen][Status] Detected stale generation lock during regeneration for {actualType}, clearing it");
                    
                    RaiseEvent(new GenerationLockClearedEvent { Type = actualType });
                    await ConfirmEvents();
                    
                    isRegenerating = false;
                    regenerationStartedAt = null;
                }
            }
            
            _logger.LogInformation(
                $"[Lumen] Status check - Regeneration needed for {State.Type}, isGenerating: {isRegenerating}, old data available: true");
            
            // Build translation status (same logic as below)
            TranslationStatusInfo? translationStatusForStale = null;
            var activeTranslationsForStale = State.TranslationLocks
                .Where(kvp => kvp.Value.IsTranslating && kvp.Value.StartedAt.HasValue)
                .ToList();
            
            if (activeTranslationsForStale.Any())
            {
                var validTranslationsForStale = activeTranslationsForStale
                    .Where(kvp => (DateTime.UtcNow - kvp.Value.StartedAt!.Value).TotalMinutes <= 5)
                    .ToList();
                
                if (validTranslationsForStale.Any())
                {
                    var earliestStart = validTranslationsForStale.Min(kvp => kvp.Value.StartedAt!.Value);
                    var translatingLanguages = validTranslationsForStale.Select(kvp => kvp.Key).ToList();
                    
                    translationStatusForStale = new TranslationStatusInfo
                    {
                        IsTranslating = true,
                        StartedAt = earliestStart,
                        TargetLanguages = translatingLanguages
                    };
                }
            }
            
            // Return status indicating old data is available but new data is generating
            return new PredictionStatusDto
            {
                Type = actualType,
                IsGenerated = true, // ✅ Old data exists and is queryable
                IsGenerating = isRegenerating, // ✅ New data is generating (if true)
                GeneratedAt = State.CreatedAt, // When old data was generated
                GenerationStartedAt = regenerationStartedAt, // When regeneration started
                PredictionDate = State.PredictionDate, // Date of old prediction
                AvailableLanguages = State.GeneratedLanguages ?? new List<string>(),
                NeedsRegeneration = true, // ✅ Mark as stale/needs update
                TranslationStatus = translationStatusForStale
            };
        }

        // Build translation status by checking TranslationLocks
        TranslationStatusInfo? translationStatus = null;
        var activeTranslations = State.TranslationLocks
            .Where(kvp => kvp.Value.IsTranslating && kvp.Value.StartedAt.HasValue)
            .ToList();
        
        if (activeTranslations.Any())
        {
            // Filter out stale translation locks (>5 minutes) before returning status
            // LLM translation calls can take 70-100+ seconds, so allow sufficient time
            var validTranslations = activeTranslations
                .Where(kvp => (DateTime.UtcNow - kvp.Value.StartedAt!.Value).TotalMinutes <= 5)
                .ToList();
            
            // Log any stale locks detected
            var staleTranslations = activeTranslations.Except(validTranslations).ToList();
            if (staleTranslations.Any())
            {
                foreach (var kvp in staleTranslations)
                {
                    _logger.LogWarning(
                        $"[Lumen][Status] Detected stale translation lock for language: {kvp.Key}, elapsed: {(DateTime.UtcNow - kvp.Value.StartedAt!.Value).TotalMinutes:F2} minutes (not clearing in status call, will be cleared on next translation attempt)");
                }
            }
            
            if (validTranslations.Any())
            {
                // Find the earliest translation start time from valid translations
                var earliestStart = validTranslations.Min(kvp => kvp.Value.StartedAt!.Value);
                var translatingLanguages = validTranslations.Select(kvp => kvp.Key).ToList();
                
                translationStatus = new TranslationStatusInfo
                {
                    IsTranslating = true,
                    StartedAt = earliestStart,
                    TargetLanguages = translatingLanguages
                };
            }
        }

        // Get available languages from MultilingualResults (actual available languages)
        // If MultilingualResults is empty (but not null), fallback to GeneratedLanguages
        var statusAvailableLanguages = (State.MultilingualResults != null && State.MultilingualResults.Count > 0)
                                      ? State.MultilingualResults.Keys.ToList()
                                      : (State.GeneratedLanguages ?? new List<string>());

        // CRITICAL FIX: IsGenerated should be based on whether prediction data actually exists
        // If LLM refuses or parsing fails, PredictionId will be Guid.Empty even though generation "completed"
        var isGenerated = State.PredictionId != Guid.Empty && State.MultilingualResults != null &&
                          State.MultilingualResults.Count > 0;

        _logger.LogDebug(
            "[LumenPredictionGAgent][GetPredictionStatusAsync] Status check - Type: {Type}, PredictionId: {PredictionId}, isGenerated: {IsGenerated}, MultilingualResults count: {Count}, isGenerating: {IsGenerating}",
            State.Type, State.PredictionId, isGenerated, State.MultilingualResults?.Count ?? 0, isGenerating2);

        var statusDto = new PredictionStatusDto
        {
            Type = State.Type,
            IsGenerated = isGenerated,
            IsGenerating = isGenerating2,
            GeneratedAt = isGenerated ? State.CreatedAt : (DateTime?)null,
            GenerationStartedAt = generationStartedAt2,
            PredictionDate = isGenerated ? State.PredictionDate : (DateOnly?)null,
            AvailableLanguages = statusAvailableLanguages,
            NeedsRegeneration = needsRegeneration,
            TranslationStatus = translationStatus
        };

        return statusDto;
    }

    /// <summary>
    /// Generate new prediction using AI
    /// </summary>
    private async Task<GetTodayPredictionResult> GeneratePredictionAsync(LumenUserDto userInfo, DateOnly predictionDate,
        PredictionType type, string targetLanguage = "en")
    {
        try
        {
            // ========== TIMEZONE HANDLING ==========
            // IMPORTANT: Chinese Four Pillars (BaZi) MUST use LOCAL time, not UTC!
            // - BaZi is based on local solar time and Chinese calendar
            // - Day pillar changes at local midnight (子时)
            // - Hour pillar is determined by local time
            // 
            // Western Astrology (Sun/Moon/Rising signs) uses UTC for celestial calculations,
            // but we'll keep it simple and use local time for consistency.
            
            // Use LOCAL birth date and time (do NOT convert to UTC for BaZi)
            var calcBirthDate = userInfo.BirthDate;
            var calcBirthTime = userInfo.BirthTime;
            
            // For reference: log timezone info if available
            if (!string.IsNullOrWhiteSpace(userInfo.LatLong))
            {
                try
                {
                    var parts = userInfo.LatLong.Split(',', StringSplitOptions.TrimEntries);
                    if (parts.Length == 2 && 
                        double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) && 
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
                    {
                        var localDateTime = userInfo.BirthDate.ToDateTime(userInfo.BirthTime ?? TimeOnly.MinValue);
                        var (utcDateTime, offset, tzId) = LumenTimezoneHelper.GetUtcTimeFromLocal(localDateTime, lat, lon);
                        
                        _logger.LogInformation($"[LumenPredictionGAgent] Using LOCAL time for BaZi: {localDateTime} [{tzId}, UTC{offset}], UTC would be: {utcDateTime}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[LumenPredictionGAgent] Failed to get timezone info");
                }
            }
            
            // Pre-calculate Moon and Rising signs ONLY for Lifetime predictions (requires birth time and latlong)
            // Daily/Yearly predictions don't use these values in their prompts
            string? moonSign = null;
            string? risingSign = null;
            
            if (type == PredictionType.Lifetime)
            {
                // Determine effective LatLong: prioritize user-provided, fallback to LLM-inferred
                var effectiveLatLong = !string.IsNullOrWhiteSpace(userInfo.LatLong) 
                    ? userInfo.LatLong 
                    : userInfo.LatLongInferred;
                
                var latLongSource = !string.IsNullOrWhiteSpace(userInfo.LatLong) ? "user-provided" : 
                                   !string.IsNullOrWhiteSpace(userInfo.LatLongInferred) ? "LLM-inferred" : "none";
                
                // Diagnostic logging
                _logger.LogInformation(
                    $"[LumenPredictionGAgent][Lifetime] Moon/Rising calculation check - BirthTime: {userInfo.BirthTime}, BirthTime.HasValue: {userInfo.BirthTime.HasValue}, LatLong: '{userInfo.LatLong}', LatLongInferred: '{userInfo.LatLongInferred}', Effective: '{effectiveLatLong}', Source: {latLongSource}");
                
                if (calcBirthTime.HasValue && !string.IsNullOrWhiteSpace(effectiveLatLong))
                {
                    try
                    {
                        // Parse latitude and longitude from "lat, long" format
                        var parts = effectiveLatLong.Split(',', StringSplitOptions.TrimEntries);
                        _logger.LogInformation(
                            $"[LumenPredictionGAgent][Lifetime] Parsing LatLong ({latLongSource}) - Parts count: {parts.Length}, Part[0]: '{parts.ElementAtOrDefault(0)}', Part[1]: '{parts.ElementAtOrDefault(1)}'");
                        
                        if (parts.Length == 2 && 
                            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture,
                                out double latitude) &&
                            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture,
                                out double longitude))
                        {
                            _logger.LogInformation(
                                $"[LumenPredictionGAgent][Lifetime] Starting Western Astrology calculation for user {userInfo.UserId} at ({latitude}, {longitude}) [{latLongSource}] using Corrected UTC: {calcBirthDate} {calcBirthTime}");
                            var (_, calculatedMoonSign, calculatedRisingSign) = CalculateSigns(
                                calcBirthDate,
                                calcBirthTime.Value,
                                latitude,
                                longitude);
                            
                            moonSign = calculatedMoonSign;
                            risingSign = calculatedRisingSign;
                            
                            _logger.LogInformation(
                                $"[LumenPredictionGAgent][Lifetime] Calculated Moon: {moonSign}, Rising: {risingSign} for user {userInfo.UserId} at ({latitude}, {longitude}) [{latLongSource}]");
                        }
                        else
                        {
                            _logger.LogWarning(
                                $"[LumenPredictionGAgent][Lifetime] Invalid latlong format or parse failed ({latLongSource}): '{effectiveLatLong}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            $"[LumenPredictionGAgent][Lifetime] Failed to calculate Moon/Rising signs for user {userInfo.UserId}, will use Sun sign as fallback");
                    }
                }
                else
                {
                    _logger.LogInformation(
                        $"[LumenPredictionGAgent][Lifetime] Skipping Moon/Rising calculation - BirthTime: {calcBirthTime.HasValue}, LatLong available: {!string.IsNullOrWhiteSpace(effectiveLatLong)}");
                }
            }
            else
            {
                _logger.LogDebug(
                    $"[LumenPredictionGAgent] Skipping Moon/Rising calculation for {type} type (only needed for Lifetime)");
            }
            
            // Get language name for system prompt (use native language names)
            var languageMap = new Dictionary<string, string>
            {
                { "en", "English" },
                { "zh-tw", "繁體中文" },
                { "zh", "简体中文" },
                { "es", "Español" }
            };
            var languageName = languageMap.GetValueOrDefault(targetLanguage, "English");
            
            // Unified flat results structure (all types use same format now)
            Dictionary<string, string>? parsedResults = null;
            Dictionary<string, Dictionary<string, string>>? multilingualResults = null;
            
            // ========== LIFETIME: Use parallel batched generation for better quality and lower refusal rate ==========
            if (type == PredictionType.Lifetime)
            {
                _logger.LogInformation(
                    "[LumenPredictionGAgent][GeneratePredictionAsync] Using BATCHED generation for Lifetime prediction - User: {UserId}",
                    userInfo.UserId);
                
                // Create batch context with pre-calculated values
                var batchContext = CreateLifetimeBatchContext(userInfo, predictionDate, targetLanguage);
                
                // Generate using parallel batches
                (parsedResults, multilingualResults) = await GenerateLifetimeBatchedAsync(
                    userInfo, predictionDate, targetLanguage, moonSign, risingSign, batchContext);
                
                if (parsedResults == null)
                {
                    _logger.LogError(
                        "[LumenPredictionGAgent][GeneratePredictionAsync] Batched generation failed for user {UserId}",
                        userInfo.UserId);
                    return new GetTodayPredictionResult
                    {
                        Success = false,
                        Message = "AI service failed to generate prediction"
                    };
                }
                
                _logger.LogInformation(
                    "[LumenPredictionGAgent][GeneratePredictionAsync] Batched generation completed with {FieldCount} fields",
                    parsedResults.Count);
            }
            // ========== DAILY/YEARLY: Use single LLM call (smaller prompts, less risk of refusal) ==========
            else
            {
                // Build prompt
                var promptStopwatch = Stopwatch.StartNew();
                var prompt = BuildPredictionPrompt(userInfo, predictionDate, type, targetLanguage, moonSign, risingSign);
                promptStopwatch.Stop();
                var promptTokens = TokenHelper.EstimateTokenCount(prompt);
                _logger.LogInformation(
                    $"[PERF][Lumen] {userInfo.UserId} Prompt_Build: {promptStopwatch.ElapsedMilliseconds}ms, Length: {prompt.Length} chars, Tokens: ~{promptTokens}, Type: {type}, TargetLanguage: {targetLanguage}");
                
                var userGuid = CommonHelper.StringToGuid(userInfo.UserId);
                
                // Use deterministic grain key based on userId + predictionType
                // This enables concurrent LLM calls for different prediction types (daily/yearly/lifetime)
                // while keeping grain count minimal (3 grains per user)
                // Format: userId_daily, userId_yearly, userId_lifetime
                var predictionGrainKey = CommonHelper.StringToGuid($"{userInfo.UserId}_{type.ToString().ToLower()}");
                var godChat = _clusterClient.GetGrain<IGodChat>(predictionGrainKey);
                var chatId = Guid.NewGuid().ToString();

                var settings = new ExecutionPromptSettings
                {
                    Temperature = "0.7"
                };

                // System prompt with clear field value language requirement
                var systemPrompt =
                    $@"You are a creative astrology guide helping users explore self-reflection through symbolic and thematic narratives.

===== LANGUAGE REQUIREMENT (CRITICAL) =====
Target Language: {languageName}

RULES:
1. Write ALL field values in {languageName} ONLY
2. Field names remain in English
3. Do NOT mix languages in field values
4. If {languageName} is not English, translate ALL descriptive text
5. Check your output before finishing - no English text should remain in values

Example (if target is Chinese):
✓ CORRECT: dayTitle	反思与和谐之日
✗ WRONG: dayTitle	Day of Reflection
============================================

CONTEXT:
- All content is for entertainment, self-reflection, and personal exploration only
- Not deterministic predictions or professional advice
- Based on symbolic, archetypal, and philosophical interpretations
- Users explore possibilities, not fixed outcomes
- Focus on empowerment, awareness, and personal growth

CONTENT GUIDELINES:
- Word counts are approximate guidelines, not strict requirements - focus on meaningful content
- Use natural, flowing language rather than forcing exact word counts
- Quality and relevance matter more than precise length
- If you feel unable to meet a specific format, provide your best interpretation instead of refusing

Your task is to create engaging, inspirational, and reflective content that invites users to contemplate their unique path and potential.";

                // Use "LUMEN" region for LLM calls
                var llmStopwatch = Stopwatch.StartNew();
                var response = await godChat.ChatWithoutHistoryAsync(
                    userGuid, 
                    systemPrompt, 
                    prompt, 
                    chatId, 
                    settings, 
                    true, 
                    "LUMEN");
                llmStopwatch.Stop();
                _logger.LogInformation(
                    $"[PERF][Lumen] {userInfo.UserId} LLM_Call: {llmStopwatch.ElapsedMilliseconds}ms - Type: {type}");

                if (response == null || response.Count() == 0)
                {
                    _logger.LogWarning(
                        "[LumenPredictionGAgent][GeneratePredictionAsync] No response from AI for user {UserId}",
                        userInfo.UserId);
                    return new GetTodayPredictionResult
                    {
                        Success = false,
                        Message = "AI service returned no response"
                    };
                }

                var aiResponse = response[0].Content;
                _logger.LogInformation($"[PERF][Lumen] {userInfo.UserId} LLM_Response: {aiResponse.Length} chars");

                // Parse AI response based on type (returns flattened structure)
                var parseStopwatch = Stopwatch.StartNew();
                (parsedResults, multilingualResults) = type switch
                {
                    PredictionType.Yearly => ParseLifetimeResponse(aiResponse), // Yearly uses same parser
                    PredictionType.Daily => ParseDailyResponse(aiResponse),
                    _ => throw new ArgumentException($"Unsupported prediction type: {type}")
                };
                parseStopwatch.Stop();
                
                if (parsedResults == null)
                {
                    _logger.LogError("[LumenPredictionGAgent][GeneratePredictionAsync] Failed to parse {Type} response",
                        type);
                    return new GetTodayPredictionResult
                    {
                        Success = false,
                        Message = "Failed to parse AI response"
                    };
                }
            }
            
            // Extract and save inferred LatLong if provided by LLM (for Lifetime predictions only)
            // Note: Daily predictions no longer request LatLong inference to reduce LLM costs
            if (type == PredictionType.Lifetime && parsedResults.ContainsKey("location_latlong"))
            {
                var inferredLatLong = parsedResults["location_latlong"];
                if (!string.IsNullOrWhiteSpace(inferredLatLong))
                {
                    _logger.LogInformation(
                        $"[Lumen][Lifetime] {userInfo.UserId} LLM inferred LatLong: {inferredLatLong} from BirthCity: {userInfo.BirthCity}");
                    
                    // Save to UserProfileGAgent (fire-and-forget)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var userGrainId = CommonHelper.StringToGuid(userInfo.UserId);
                            var userProfileGAgent = _clusterClient.GetGrain<ILumenUserProfileGAgent>(userGrainId);
                            await userProfileGAgent.SaveInferredLatLongAsync(inferredLatLong, userInfo.BirthCity ?? "Unknown");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"[Lumen][Lifetime] {userInfo.UserId} Failed to save inferred LatLong");
                        }
                    });
                }
                
                // Remove from parsed results (not needed in prediction output)
                parsedResults.Remove("location_latlong");
            }
            
            // Filter multilingualResults to only include targetLanguage (LLM may return multiple languages, but we only requested one)
            // CRITICAL FIX: If multilingualResults is empty but parsedResults exists, populate it with targetLanguage
            if (multilingualResults == null || multilingualResults.Count == 0)
            {
                if (parsedResults != null && parsedResults.Count > 0)
                {
                    _logger.LogDebug(
                        "[LumenPredictionGAgent][GeneratePredictionAsync] multilingualResults is empty, populating with targetLanguage: {TargetLanguage}",
                        targetLanguage);
                    multilingualResults = new Dictionary<string, Dictionary<string, string>>
                    {
                        [targetLanguage] = parsedResults
                    };
                }
            }
            else if (multilingualResults.Count > 0)
            {
                // Check if LLM returned extra languages (before filtering)
                var originalLanguages = multilingualResults.Keys.ToList();
                
                if (multilingualResults.ContainsKey(targetLanguage))
                {
                    // Log warning if LLM returned extra languages
                    if (originalLanguages.Count > 1)
                    {
                        var extraLanguages = originalLanguages.Where(k => k != targetLanguage).ToList();
                        _logger.LogWarning(
                            "[LumenPredictionGAgent][GeneratePredictionAsync] LLM returned extra languages {ExtraLanguages} for {TargetLanguage}, filtering them out",
                            string.Join(", ", extraLanguages), targetLanguage);
                    }
                    
                    // Only keep the requested language
                    var filteredResults = new Dictionary<string, Dictionary<string, string>>
                    {
                        [targetLanguage] = multilingualResults[targetLanguage]
                    };
                    multilingualResults = filteredResults;
                    
                    // Update parsedResults to use targetLanguage version
                    parsedResults = multilingualResults[targetLanguage];
                }
                else
                {
                    // Target language not found, log warning but keep what we have
                    _logger.LogWarning(
                        "[LumenPredictionGAgent][GeneratePredictionAsync] Target language {TargetLanguage} not found in LLM response, available: {AvailableLanguages}",
                        targetLanguage, string.Join(", ", originalLanguages));
                }
            }

            // ========== INJECT BACKEND-CALCULATED FIELDS ==========
            // Pre-calculate values once (using timezone-corrected calcBirthDate from method start)
            var currentYear = DateTime.UtcNow.Year;
            var birthYear = calcBirthDate.Year;

            var sunSign = LumenCalculator.CalculateZodiacSign(calcBirthDate);
            var birthYearZodiac = LumenCalculator.GetChineseZodiacWithElement(birthYear);
            var birthYearAnimal = LumenCalculator.CalculateChineseZodiac(birthYear);
            var birthYearStemsComponents = LumenCalculator.GetStemsAndBranchesComponents(birthYear);
            var pastCycle = LumenCalculator.CalculateTenYearCycle(birthYear, -1);
            var currentCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 0);
            var futureCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 1);
            
            // Ensure moonSign and risingSign have fallback values (use sunSign if not calculated)
            moonSign = moonSign ?? sunSign;
            risingSign = risingSign ?? sunSign;
            
            if (type == PredictionType.Lifetime)
            {
                // Calculate Four Pillars (Ba Zi)
                var fourPillars = LumenCalculator.CalculateFourPillars(calcBirthDate, calcBirthTime);
                
                // Inject into primary language results
                // NOTE: Use birth year stems (年柱) to match BaZi year pillar
                parsedResults["chineseAstrology_currentYearStem"] = birthYearStemsComponents.stemChinese;
                parsedResults["chineseAstrology_currentYearStemPinyin"] = birthYearStemsComponents.stemPinyin;
                parsedResults["chineseAstrology_currentYearBranch"] = birthYearStemsComponents.branchChinese;
                parsedResults["chineseAstrology_currentYearBranchPinyin"] = birthYearStemsComponents.branchPinyin;
                
                parsedResults["sunSign_name"] = TranslateSunSign(sunSign, targetLanguage);
                parsedResults["sunSign_enum"] = ((int)LumenCalculator.ParseZodiacSignEnum(sunSign)).ToString();
                parsedResults["westernOverview_sunSign"] = TranslateSunSign(sunSign, targetLanguage);
                parsedResults["westernOverview_moonSign"] = TranslateSunSign(moonSign, targetLanguage);
                parsedResults["westernOverview_risingSign"] = TranslateSunSign(risingSign, targetLanguage);
                
                // Replace sign names in combined essence statement with backend-calculated translations
                // Then move the result to combinedEssence field (removing the redundant westernOverview_combinedEssenceStatement)
                if (parsedResults.TryGetValue("westernOverview_combinedEssenceStatement", out var combinedEssenceStatement))
                {
                    var sunSignTranslated = TranslateSunSign(sunSign, targetLanguage);
                    var moonSignTranslated = TranslateSunSign(moonSign, targetLanguage);
                    var risingSignTranslated = TranslateSunSign(risingSign, targetLanguage);
                    
                    // Replace any occurrence of sign names (case-insensitive) with accurate translations
                    foreach (var signToReplace in new[] { sunSign, moonSign, risingSign })
                    {
                        combinedEssenceStatement = System.Text.RegularExpressions.Regex.Replace(
                            combinedEssenceStatement,
                            $@"\b{signToReplace}\b",
                            match =>
                            {
                                if (signToReplace == sunSign) return sunSignTranslated;
                                if (signToReplace == moonSign) return moonSignTranslated;
                                if (signToReplace == risingSign) return risingSignTranslated;
                                return match.Value;
                            },
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase
                        );
                    }
                    
                    // Override combinedEssence with the formatted statement
                    parsedResults["combinedEssence"] = combinedEssenceStatement;
                    
                    // Remove the redundant westernOverview_combinedEssenceStatement field
                    parsedResults.Remove("westernOverview_combinedEssenceStatement");
                }
                
                parsedResults["chineseZodiac_animal"] = TranslateChineseZodiacAnimal(birthYearZodiac, targetLanguage);
                parsedResults["chineseZodiac_enum"] =
                    ((int)LumenCalculator.ParseChineseZodiacEnum(birthYearAnimal)).ToString();
                parsedResults["chineseZodiac_title"] = TranslateZodiacTitle(birthYearAnimal, targetLanguage);
                parsedResults["pastCycle_ageRange"] = TranslateCycleAgeRange(pastCycle.AgeRange, targetLanguage);
                parsedResults["pastCycle_period"] = TranslateCyclePeriod(pastCycle.Period, targetLanguage);
                parsedResults["currentCycle_ageRange"] = TranslateCycleAgeRange(currentCycle.AgeRange, targetLanguage);
                parsedResults["currentCycle_period"] = TranslateCyclePeriod(currentCycle.Period, targetLanguage);
                parsedResults["futureCycle_ageRange"] = TranslateCycleAgeRange(futureCycle.AgeRange, targetLanguage);
                parsedResults["futureCycle_period"] = TranslateCyclePeriod(futureCycle.Period, targetLanguage);
                
                // Construct zodiacCycle_title from backend prefix + LLM-generated year range
                if (parsedResults.TryGetValue("zodiacCycle_yearRange", out var yearRange) && !string.IsNullOrWhiteSpace(yearRange))
                {
                    var cycleTitlePrefix = targetLanguage switch
                    {
                        "zh" => "生肖周期影响",
                        "zh-tw" => "生肖週期影響",
                        "es" => "Influencia del Ciclo Zodiacal",
                        _ => "Zodiac Cycle Influence"
                    };
                    parsedResults["zodiacCycle_title"] = $"{cycleTitlePrefix} ({yearRange})";
                }
                
                // Fallback: if zodiacCycle_cycleName is missing, use zodiacCycle_cycleNameChinese
                // This handles cases where LLM didn't return cycle_name properly
                if ((!parsedResults.ContainsKey("zodiacCycle_cycleName") || 
                     string.IsNullOrWhiteSpace(parsedResults["zodiacCycle_cycleName"])) &&
                    parsedResults.TryGetValue("zodiacCycle_cycleNameChinese", out var fallbackCycleName) && 
                    !string.IsNullOrWhiteSpace(fallbackCycleName))
                {
                    parsedResults["zodiacCycle_cycleName"] = fallbackCycleName;
                    _logger.LogWarning(
                        "[Lumen][Lifetime] zodiacCycle_cycleName missing for language {Language}, using Chinese fallback",
                        targetLanguage);
                }
                
                // Inject Four Pillars data
                InjectFourPillarsData(parsedResults, fourPillars, targetLanguage);
                
                _logger.LogInformation(
                    $"[Lumen] {userInfo.UserId} Injected backend-calculated fields into Lifetime prediction");
            }
            else if (type == PredictionType.Yearly)
            {
                var yearlyYear = predictionDate.Year;
                var yearlyYearZodiac = LumenCalculator.GetChineseZodiacWithElement(yearlyYear);
                var yearlyTaishui = LumenCalculator.CalculateTaishuiRelationship(birthYear, yearlyYear);
                
                // Inject into primary language results
                // NOTE: Use birth year stems (年柱) to match BaZi year pillar (already calculated above)
                
                parsedResults["sunSign_name"] = TranslateSunSign(sunSign, targetLanguage);
                parsedResults["sunSign_enum"] = ((int)LumenCalculator.ParseZodiacSignEnum(sunSign)).ToString();
                parsedResults["chineseZodiac_animal"] = TranslateChineseZodiacAnimal(birthYearZodiac, targetLanguage);
                parsedResults["chineseZodiac_enum"] =
                    ((int)LumenCalculator.ParseChineseZodiacEnum(birthYearAnimal)).ToString();
                parsedResults["chineseAstrology_currentYearStem"] = birthYearStemsComponents.stemChinese;
                parsedResults["chineseAstrology_currentYearStemPinyin"] = birthYearStemsComponents.stemPinyin;
                parsedResults["chineseAstrology_currentYearBranch"] = birthYearStemsComponents.branchChinese;
                parsedResults["chineseAstrology_currentYearBranchPinyin"] = birthYearStemsComponents.branchPinyin;
                parsedResults["chineseAstrology_taishuiRelationship"] =
                    TranslateTaishuiRelationship(yearlyTaishui, targetLanguage);
                parsedResults["zodiacInfluence"] =
                    BuildZodiacInfluence(birthYearZodiac, yearlyYearZodiac, yearlyTaishui, targetLanguage);

                _logger.LogInformation(
                    $"[Lumen] {userInfo.UserId} Injected backend-calculated fields into Yearly prediction");
            }
            else if (type == PredictionType.Daily)
            {
                // Inject enum values for tarot card, lucky stone, and orientation
                // These are parsed from LLM text output into enum integers
                
                // Parse and inject tarot card enum
                if (parsedResults.TryGetValue("todaysReading_tarotCard_name", out var tarotCardName))
                {
                    var tarotCardEnum = ParseTarotCard(tarotCardName);
                    parsedResults["todaysReading_tarotCard_enum"] = ((int)tarotCardEnum).ToString();
                }
                
                // Parse and inject tarot orientation enum
                if (parsedResults.TryGetValue("todaysReading_tarotCard_orientation", out var orientation))
                {
                    var orientationEnum = ParseTarotOrientation(orientation);
                    parsedResults["todaysReading_tarotCard_orientation_enum"] = ((int)orientationEnum).ToString();
                }
                
                // Parse and inject lucky stone enum
                if (parsedResults.TryGetValue("luckyAlignments_luckyStone", out var luckyStone))
                {
                    var stoneEnum = ParseCrystalStone(luckyStone);
                    parsedResults["luckyAlignments_luckyStone_enum"] = ((int)stoneEnum).ToString();
                }
                
                // Construct todaysReading_pathTitle from path_adjective (backend formats the full title)
                if (parsedResults.TryGetValue("todaysReading_pathType", out var pathAdjective) && !string.IsNullOrWhiteSpace(pathAdjective))
                {
                    var pathTitle = targetLanguage switch
                    {
                        "zh" => $"今日之路 - {pathAdjective}之路",
                        "zh-tw" => $"今日之路 - {pathAdjective}之路",
                        "es" => $"Tu Camino Hoy - Un Camino {pathAdjective}",
                        _ => $"Your Path Today - A {pathAdjective} Path"
                    };
                    parsedResults["todaysReading_pathTitle"] = pathTitle;
                }
                
                _logger.LogInformation(
                    $"[Lumen] {userInfo.UserId} Injected enum fields and constructed path_title for Daily prediction");
                
                // Add Chinese translations for English-only fields (if user language is Chinese)
                AddChineseTranslations(parsedResults, targetLanguage);
            }

            var predictionId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            // Add currentPhase for lifetime predictions
            if (type == PredictionType.Lifetime)
            {
                var currentPhase = CalculateCurrentPhase(userInfo.BirthDate);
                parsedResults["currentPhase"] = currentPhase.ToString();
                
                // Construct cn_year from birthYearAnimal (backend-generated, not from LLM)
                var cnYearTranslated = TranslateChineseZodiacAnimal(birthYearZodiac, targetLanguage);
                parsedResults["chineseAstrology_currentYear"] = cnYearTranslated;
                
                // Construct sun_arch, moon_arch, rising_arch from arch_name fields
                if (parsedResults.TryGetValue("westernOverview_sunArchetypeName", out var sunArchName))
                {
                    var sunSignTranslated = TranslateSunSign(sunSign, targetLanguage);
                    parsedResults["westernOverview_sunArchetype"] =
                        BuildArchetypeString("Sun", sunSignTranslated, sunArchName, targetLanguage);
                }
                
                if (parsedResults.TryGetValue("westernOverview_moonArchetypeName", out var moonArchName))
                {
                    var moonSignTranslated = TranslateSunSign(moonSign ?? sunSign, targetLanguage);
                    parsedResults["westernOverview_moonArchetype"] =
                        BuildArchetypeString("Moon", moonSignTranslated, moonArchName, targetLanguage);
                }
                
                if (parsedResults.TryGetValue("westernOverview_risingArchetypeName", out var risingArchName))
                {
                    var risingSignTranslated = TranslateSunSign(risingSign ?? sunSign, targetLanguage);
                    parsedResults["westernOverview_risingArchetype"] = BuildArchetypeString("Rising",
                        risingSignTranslated, risingArchName, targetLanguage);
                }
                
                _logger.LogInformation(
                    $"[Lumen] {userInfo.UserId} Constructed cn_year and arch fields for Lifetime prediction");
            }

            // Add quotes to affirmation text
            parsedResults = AddQuotesToAffirmation(parsedResults, targetLanguage);

            // Add lucky number calculation for Daily predictions (backend-calculated, not from LLM)
            if (type == PredictionType.Daily)
            {
                var primaryLuckyNumber = Services.LuckyNumberService.CalculateLuckyNumber(
                    userInfo.BirthDate,
                    predictionDate,
                    targetLanguage);
                
                parsedResults["luckyAlignments_luckyNumber_number"] = primaryLuckyNumber.NumberWord;
                parsedResults["luckyAlignments_luckyNumber_digit"] = primaryLuckyNumber.Digit.ToString();
                parsedResults["luckyAlignments_luckyNumber_description"] = primaryLuckyNumber.Description;
                parsedResults["luckyAlignments_luckyNumber_calculation"] = primaryLuckyNumber.CalculationFormula;
            }

            // Raise event to save prediction (unified structure)
            RaiseEvent(new PredictionGeneratedEvent
            {
                PredictionId = predictionId,
                UserId = userInfo.UserId,
                PredictionDate = predictionDate,
                CreatedAt = now,
                ProfileUpdatedAt = userInfo.UpdatedAt,
                Type = type,
                Results = parsedResults,
                MultilingualResults = multilingualResults,
                InitialLanguage = targetLanguage,
                LastGeneratedDate = predictionDate,
                PromptVersion = _options?.PromptVersion ?? DEFAULT_PROMPT_VERSION
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation(
                "[LumenPredictionGAgent][GeneratePredictionAsync] {Type} prediction generated successfully for user {UserId}",
                type, userInfo.UserId);

            // Get available languages from multilingualResults (actual available languages)
            var availableLanguages = multilingualResults?.Keys.ToList() ?? new List<string> { targetLanguage };
            
            // Archive Daily predictions to yearly history (fire-and-forget)
            // Note: Must get grain reference BEFORE Task.Run to preserve Orleans context
            if (type == PredictionType.Daily && multilingualResults != null)
            {
                var yearlyHistoryGrainId = CommonHelper.StringToGuid($"{userInfo.UserId}-{predictionDate.Year}");
                var yearlyHistoryGrain = GrainFactory.GetGrain<ILumenDailyYearlyHistoryGAgent>(yearlyHistoryGrainId);
                
                // Fire-and-forget: Call the grain method and handle completion asynchronously
                _ = yearlyHistoryGrain.AddOrUpdateDailyPredictionAsync(
                        userInfo.UserId,
                        predictionId,
                        predictionDate,
                        multilingualResults,
                        availableLanguages)
                    .ContinueWith(task =>
                    {
                        if (task.IsFaulted)
                        {
                            _logger.LogError(task.Exception,
                                "[Lumen][YearlyHistory] Failed to archive daily prediction - UserId: {UserId}, Date: {Date}",
                                userInfo.UserId, predictionDate);
                        }
                        else
                        {
                            _logger.LogDebug(
                                "[Lumen][YearlyHistory] Daily prediction archived - UserId: {UserId}, Date: {Date}",
                                userInfo.UserId, predictionDate);
                        }
                    }, TaskScheduler.Default);
            }

            // Add quotes to affirmation text based on language
            parsedResults = AddQuotesToAffirmation(parsedResults, targetLanguage);

            // Build return DTO
            var newPredictionDto = new PredictionResultDto
            {
                PredictionId = predictionId,
                UserId = userInfo.UserId,
                PredictionDate = predictionDate,
                CreatedAt = now,
                FromCache = false,
                Type = type,
                Results = parsedResults,
                AvailableLanguages = availableLanguages,
                AllLanguagesGenerated = availableLanguages.Count == 4, // Will be true after async generation completes
                RequestedLanguage = targetLanguage,
                ReturnedLanguage = targetLanguage,
                IsFallback = false, // First generation always returns the requested language
                Feedbacks = null
            };
            
            // Generate location warning message
            string? warning = null;
            if (string.IsNullOrWhiteSpace(userInfo.LatLong) && string.IsNullOrWhiteSpace(userInfo.LatLongInferred))
            {
                if (!string.IsNullOrWhiteSpace(userInfo.BirthCity))
                {
                    warning = "Location coordinates could not be determined from your birth city. Moon and Rising sign calculations may be unavailable. Please update your profile with latitude/longitude for more accurate predictions.";
                }
                else
                {
                    warning = "Birth city not provided. Moon and Rising sign calculations are unavailable. Please update your profile with birth city or latitude/longitude for more accurate predictions.";
                }
            }
            
            return new GetTodayPredictionResult
            {
                Success = true,
                Message = string.Empty,
                Prediction = newPredictionDto,
                Warning = warning
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenPredictionGAgent][GeneratePredictionAsync] Error in AI generation");
            return new GetTodayPredictionResult
            {
                Success = false,
                Message = $"AI generation error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Build prediction prompt for AI (single language generation for first stage)
    /// </summary>
    private string BuildPredictionPrompt(LumenUserDto userInfo, DateOnly predictionDate, PredictionType type,
        string targetLanguage = "en", string? moonSign = null, string? risingSign = null)
    {
        // Build user info line dynamically based on available fields
        // Note: Full name intentionally excluded to reduce privacy exposure
        var userInfoParts = new List<string>();
        
        // Birth date only (no time or location for privacy)
        var birthDateStr = $"Birth: {userInfo.BirthDate:yyyy-MM-dd}";
        
        // Calendar type (only include if provided)
        if (userInfo.CalendarType.HasValue)
        {
            var calendarType = userInfo.CalendarType.Value == CalendarTypeEnum.Solar ? "Solar" : "Lunar";
            birthDateStr += $" ({calendarType} calendar)";
        }
        
        userInfoParts.Add(birthDateStr);
        
        // Gender
        userInfoParts.Add($"Gender: {userInfo.Gender}");
        
        // Current residence (optional)
        if (!string.IsNullOrWhiteSpace(userInfo.CurrentResidence))
        {
            userInfoParts.Add($"Current Residence: {userInfo.CurrentResidence}");
        }
        
        // Occupation (optional)
        if (!string.IsNullOrWhiteSpace(userInfo.Occupation))
        {
            userInfoParts.Add($"Occupation: {userInfo.Occupation}");
        }
        
        var userInfoLine = string.Join(", ", userInfoParts);
        
        // Calculate display name based on user language (for personalized greetings in predictions)
        // displayName is like fullName - it should NEVER be translated across languages
        var displayName = LumenCalculator.GetDisplayName($"{userInfo.FirstName} {userInfo.LastName}", targetLanguage);

        string prompt = string.Empty;
        
        // ========== PRE-CALCULATE ACCURATE ASTROLOGICAL VALUES ==========
        var currentYear = DateTime.UtcNow.Year;
        var birthYear = userInfo.BirthDate.Year;
        
        // Western Zodiac
        string sunSign = LumenCalculator.CalculateZodiacSign(userInfo.BirthDate);
        // Use provided moonSign and risingSign if available, otherwise fall back to sunSign
        moonSign = moonSign ?? sunSign;
        risingSign = risingSign ?? sunSign;
        
        // Chinese Zodiac & Element
        var birthYearZodiac = LumenCalculator.GetChineseZodiacWithElement(birthYear);
        var birthYearAnimal = LumenCalculator.CalculateChineseZodiac(birthYear);
        var birthYearElement = LumenCalculator.CalculateChineseElement(birthYear);
        
        var currentYearZodiac = LumenCalculator.GetChineseZodiacWithElement(currentYear);
        var currentYearAnimal = LumenCalculator.CalculateChineseZodiac(currentYear);
        var currentYearElement = LumenCalculator.CalculateChineseElement(currentYear);
        
        // Heavenly Stems & Earthly Branches
        var currentYearStemsFormatted = LumenCalculator.CalculateStemsAndBranches(currentYear);
        var birthYearStems = LumenCalculator.CalculateStemsAndBranches(birthYear);
        
        // Taishui Relationship
        var taishuiRelationship = LumenCalculator.CalculateTaishuiRelationship(birthYear, currentYear);
        
        // Age & 10-year Cycles
        var currentAge = LumenCalculator.CalculateAge(userInfo.BirthDate);
        var pastCycle = LumenCalculator.CalculateTenYearCycle(birthYear, -1);
        var currentCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 0);
        var futureCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 1);
        
        // Language-specific instruction prefix (use native language names)
        var languageMap = new Dictionary<string, string>
        {
            { "en", "English" },
            { "zh-tw", "繁體中文" },
            { "zh", "简体中文" },
            { "es", "Español" }
        };
        
        var languageName = languageMap.GetValueOrDefault(targetLanguage, "English");
        
        // Build language instruction in target language for stronger compliance
        var languageInstruction = targetLanguage switch
        {
            "zh" => @"===== 语言要求 =====
⚠️ 重要：所有字段值必须用简体中文书写（字段名保持英文）。
✓ 正确示例：dayTitle	反思与和谐之日 | career	专注于团队协作
✗ 错误示例：dayTitle	Day of Reflection | career	Focus on teamwork

⚠️ 例外：以下字段使用英文标准名称（便于后端解析）：
- card_name: 使用英文塔罗牌名称（如 ""The Fool"", ""The Moon"", ""The Star""）
- card_orient: 使用英文（""Upright"" 或 ""Reversed""）
- lucky_stone: 使用英文宝石名称（如 ""Amethyst"", ""Rose Quartz""）

注意：
1. 输出结构中的英文示例仅供参考格式，内容必须翻译为简体中文（除了上述例外字段）
2. 不要保留任何英文描述性文本，包括 ""The"", ""A"", 动词等
3. 如果发现自己在写英文，立即停下来改用简体中文
===================",
            "zh-tw" => @"===== 語言要求 =====
⚠️ 重要：所有字段值必須用繁體中文書寫（字段名保持英文）。
✓ 正確示例：dayTitle	反思與和諧之日 | career	專注於團隊協作
✗ 錯誤示例：dayTitle	Day of Reflection | career	Focus on teamwork

⚠️ 例外：以下字段使用英文標準名稱（便於後端解析）：
- card_name: 使用英文塔羅牌名稱（如 ""The Fool"", ""The Moon"", ""The Star""）
- card_orient: 使用英文（""Upright"" 或 ""Reversed""）
- lucky_stone: 使用英文寶石名稱（如 ""Amethyst"", ""Rose Quartz""）

注意：
1. 輸出結構中的英文示例僅供參考格式，內容必須翻譯為繁體中文（除了上述例外字段）
2. 不要保留任何英文描述性文本，包括 ""The"", ""A"", 動詞等
3. 如果發現自己在寫英文，立即停下來改用繁體中文
===================",
            "es" => @"===== REQUISITO DE IDIOMA =====
Todos los valores de campo deben estar en ESPAÑOL (los nombres de campo permanecen en inglés).
Ejemplo: dayTitle	El Día de Reflexión | career	Enfócate en el trabajo en equipo

⚠️ Excepciones: Los siguientes campos usan nombres estándar en INGLÉS (para facilitar el análisis del backend):
- card_name: Use nombres de tarot en inglés (ej. ""The Fool"", ""The Moon"", ""The Star"")
- card_orient: Use inglés (""Upright"" o ""Reversed"")
- lucky_stone: Use nombres de gemas en inglés (ej. ""Amethyst"", ""Rose Quartz"")

Nota: Los textos de ejemplo en inglés en OUTPUT STRUCTURE son solo referencia, deben traducirse al español (excepto los campos mencionados arriba).
================================",
            _ => $@"===== LANGUAGE REQUIREMENT =====
All field VALUES must be in {languageName} (field names stay in English).

⚠️ Exception: The following fields use standard ENGLISH names (for backend parsing):
- card_name: Use English tarot card names (e.g. ""The Fool"", ""The Moon"", ""The Star"")
- card_orient: Use English (""Upright"" or ""Reversed"")
- lucky_stone: Use English gem names (e.g. ""Amethyst"", ""Rose Quartz"")

Note: English example texts in OUTPUT STRUCTURE are for reference only and must be translated to {languageName} if not English (except the fields mentioned above).
================================"
        };
        
        var singleLanguagePrefix = $@"{languageInstruction}

Guidelines:
- When addressing the user, use the provided ""Display Name"" (if given in PRE-CALCULATED VALUES section)
- Avoid making up names - use only the provided Display Name or second-person pronouns (""你""/""you"")
- Chinese stems/branches (天干地支): Can include Chinese and pinyin like ""甲子 (Jiǎzǐ)""

FORMAT REQUIREMENT:
- Return raw TSV (Tab-Separated Values)
- Use ACTUAL TAB CHARACTER (\\t) between field name and value
- Arrays: item1|item2|item3 (pipe separator)
- NO JSON, NO markdown, NO extra text
- Start immediately with the data

";
        
        if (type == PredictionType.Lifetime)
        {
            // Translate zodiac signs for prompt (so LLM uses translated values in output)
            var sunSignTranslated = TranslateSunSign(sunSign, targetLanguage);
            var moonSignTranslated = TranslateSunSign(moonSign, targetLanguage);
            var risingSignTranslated = TranslateSunSign(risingSign, targetLanguage);
            var birthYearAnimalTranslated = TranslateChineseZodiacAnimal(birthYearZodiac, targetLanguage);
            
            // Check if we need to request LatLong inference from LLM (for calculating Moon/Rising signs)
            var needLatLongInference = !string.IsNullOrWhiteSpace(userInfo.BirthCity) 
                && string.IsNullOrWhiteSpace(userInfo.LatLong) 
                && string.IsNullOrWhiteSpace(userInfo.LatLongInferred);
            
            var lifetimeLatLongSection = needLatLongInference
                ? $@"

========== OPTIONAL: LOCATION INFERENCE ==========
Birth City: {userInfo.BirthCity}
⚠️ INSTRUCTION: If you can identify the latitude and longitude for the birth city above, please add this field to the output:
location_latlong	latitude,longitude (format: ""34.0522,-118.2437"")
⚠️ If the city name is ambiguous or you cannot determine coordinates, you may SKIP this field entirely."
                : string.Empty;
            
            // Note: cycleTitlePrefix is no longer used in prompt - backend will inject the localized prefix
            // LLM only needs to return the year range (e.g. "1984-2004")
            
            var cycleIntroInstruction = targetLanguage switch
            {
                "zh" => $"以\"你的生肖是{birthYearAnimalTranslated}…\"开头，描述20年象征周期",
                "zh-tw" => $"以\"你的生肖是{birthYearAnimalTranslated}…\"開頭，描述20年象徵週期",
                "es" => $"Comienza con 'Tu Zodiaco Chino es {birthYearAnimalTranslated}...' y describe el ciclo simbólico de 20 años",
                _ =>
                    $"Start with 'Your Chinese Zodiac is {birthYearAnimalTranslated}...' and describe the 20-year symbolic cycle"
            };

            // SIMPLIFIED DESCRIPTIONS (Lifetime - Relaxed constraints to avoid LLM refusal)
            // Use language-specific placeholders to avoid LLM confusion
            var desc_simple = targetLanguage switch
            {
                "zh" => "[内容]",
                "zh-tw" => "[內容]",
                "es" => "[contenido]",
                _ => "[content]"
            };
            
            var desc_title = targetLanguage switch
            {
                "zh" => "[标题]",
                "zh-tw" => "[標題]",
                "es" => "[título]",
                _ => "[title]"
            };
            
            var desc_text = targetLanguage switch
            {
                "zh" => "[描述]",
                "zh-tw" => "[描述]",
                "es" => "[descripción]",
                _ => "[description]"
            };
            
            // Only specify format for special fields
            var desc_combined_essence = targetLanguage switch
            {
                "zh" => $"如：你像{sunSignTranslated}一样思考，像{moonSignTranslated}一样感受，像{risingSignTranslated}一样行动",
                "zh-tw" => $"如：你像{sunSignTranslated}一樣思考，像{moonSignTranslated}一樣感受，像{risingSignTranslated}一樣行動",
                "es" => $"Ej: Piensas como {sunSignTranslated}, sientes como {moonSignTranslated}, te mueves como {risingSignTranslated}",
                _ => $"E.g.: You think like {sunSignTranslated}, feel like {moonSignTranslated}, move like {risingSignTranslated}"
            };
            
            var desc_whisper = targetLanguage switch
            {
                "zh" => $"以'{birthYearAnimalTranslated}'开头的消息",
                "zh-tw" => $"以'{birthYearAnimalTranslated}'開頭的訊息",
                "es" => $"Mensaje que comienza con '{birthYearAnimalTranslated}'",
                _ => $"Message starting with '{birthYearAnimalTranslated}'"
            };
            
            var desc_cn_essence = targetLanguage switch
            {
                "zh" => $"与{birthYearElement}相关的本质",
                "zh-tw" => $"與{birthYearElement}相關的本質",
                "es" => $"Esencia relacionada con {birthYearElement}",
                _ => $"Essence related to {birthYearElement}"
            };
            
            // Cycle name fields:
            // - cycle_name_zh: Always required (simplified Chinese) → zodiacCycle_cycleNameChinese
            // - cycle_name: Current language version → zodiacCycle_cycleName
            // For simplified Chinese: cycle_name is same as cycle_name_zh
            var cycleNameDesc = targetLanguage switch
            {
                "zh" => "[Simplified Chinese name for cycle theme (same as cycle_name_zh)]",
                "zh-tw" => "[Traditional Chinese name for cycle theme]",
                "es" => "[Spanish name for cycle theme]",
                _ => "[English name for cycle theme]"
            };
            
            prompt = singleLanguagePrefix + $@"Create lifetime astrological narrative.
User: {userInfoLine}, Current Year: {currentYear}

CONTEXT (Use these exact values):
Sun: {sunSignTranslated} | Moon: {moonSignTranslated} | Rising: {risingSignTranslated}
Birth Year: {birthYearZodiac} ({birthYearAnimalTranslated}, {birthYearElement})
Current Year: {currentYearZodiac} ({currentYearStemsFormatted})
Cycles: Past {pastCycle.AgeRange}, Current {currentCycle.AgeRange}, Future {futureCycle.AgeRange}{lifetimeLatLongSection}

Generate meaningful astrological content in TSV format (tab-separated).
Write naturally - quality over strict length. Be concise where appropriate.

Required fields (one per line, format: key	value):
pillars_id	{desc_text}
pillars_detail	{desc_text}
cn_trait1	{desc_simple}
cn_trait2	{desc_simple}
cn_trait3	{desc_simple}
cn_trait4	{desc_simple}
whisper	{desc_whisper}
sun_tag	{desc_text}
sun_arch_name	{desc_simple}
sun_desc	{desc_text}
moon_arch_name	{desc_simple}
moon_desc	{desc_text}
rising_arch_name	{desc_simple}
rising_desc	{desc_text}
combined_essence	{desc_combined_essence}
str_intro	{desc_text}
str1_title	{desc_title}
str1_desc	{desc_text}
str2_title	{desc_title}
str2_desc	{desc_text}
str3_title	{desc_title}
str3_desc	{desc_text}
chal_intro	{desc_text}
chal1_title	{desc_title}
chal1_desc	{desc_text}
chal2_title	{desc_title}
chal2_desc	{desc_text}
chal3_title	{desc_title}
chal3_desc	{desc_text}
destiny_intro	{desc_text}
path1_title	{desc_title}
path1_desc	{desc_text}
path2_title	{desc_title}
path2_desc	{desc_text}
path3_title	{desc_title}
path3_desc	{desc_text}
cn_essence	{desc_cn_essence}
cycle_year_range	YYYY-YYYY
cycle_name_zh	[Simplified Chinese name for cycle theme (always required)]
cycle_name	{cycleNameDesc}
cycle_intro	{desc_text}
cycle_pt1	{desc_simple}
cycle_pt2	{desc_simple}
cycle_pt3	{desc_simple}
cycle_pt4	{desc_simple}
ten_intro	{desc_text}
past_summary	{desc_simple}
past_detail	{desc_text}
curr_summary	{desc_simple}
curr_detail	{desc_text}
future_summary	{desc_simple}
future_detail	{desc_text}
plot_title	{desc_text}
plot_chapter	{desc_text}
plot_pt1	{desc_simple}
plot_pt2	{desc_simple}
plot_pt3	{desc_simple}
plot_pt4	{desc_simple}
act1_title	{desc_title}
act1_desc	{desc_text}
act2_title	{desc_title}
act2_desc	{desc_text}
act3_title	{desc_title}
act3_desc	{desc_text}
act4_title	{desc_title}
act4_desc	{desc_text}
mantra_title	{desc_title}
mantra_pt1	{desc_text}
mantra_pt2	{desc_text}
mantra_pt3	{desc_text}

Start output now with first field
";
        }
        else if (type == PredictionType.Yearly)
        {
            var yearlyYear = predictionDate.Year;
            var yearlyYearZodiac = LumenCalculator.GetChineseZodiacWithElement(yearlyYear);
            var yearlyTaishui = LumenCalculator.CalculateTaishuiRelationship(birthYear, yearlyYear);
            
            // Translate zodiac signs for prompt (so LLM uses translated values in output)
            var sunSignTranslated = TranslateSunSign(sunSign, targetLanguage);
            var birthYearZodiacTranslated = TranslateChineseZodiacAnimal(birthYearZodiac, targetLanguage);
            var yearlyTaishuiTranslated = TranslateTaishuiRelationship(yearlyTaishui, targetLanguage);
            
            // DYNAMIC DESCRIPTIONS BASED ON LANGUAGE (Yearly)
            var desc_theme_title = targetLanguage switch
            {
                "zh" => "4-7字，年度主题 (使用'之'字结构)",
                "zh-tw" => "4-7字，年度主題 (使用'之'字結構)",
                "es" => "4-7 palabras, tema anual (usando estructura 'de')",
                _ => "[VARIED: 4-7 words using 'of' structure]"
            };
            
            var desc_theme_glance = targetLanguage switch
            {
                "zh" => "15-20字，年度运势综述",
                "zh-tw" => "15-20字，年度運勢綜述",
                "es" => "15-20 palabras, resumen anual",
                _ => "[VARIED: 15-20 words on what both systems suggest]"
            };
            
            var desc_theme_detail = targetLanguage switch
            {
                "zh" => "60-80字，分三部分：1.能量模式 2.探索邀请 3.年度定义",
                "zh-tw" => "60-80字，分三部分：1.能量模式 2.探索邀請 3.年度定義",
                "es" => "60-80 palabras en 3 partes: patrón energético, invitación, definición anual",
                _ => "[VARIED: 60-80 words in 3 parts]"
            };

            var desc_tag = targetLanguage switch
            {
                "zh" => "10-15字，反思性标语",
                "zh-tw" => "10-15字，反思性標語",
                "es" => "10-15 palabras, lema reflexivo",
                _ => "[10-15 words invitational tagline]"
            };
            
            var desc_do = targetLanguage switch
            {
                "zh" => "建议1|建议2|建议3 (竖线分隔)",
                "zh-tw" => "建議1|建議2|建議3 (豎線分隔)",
                "es" => "item1|item2 (áreas a explorar, separado por |)",
                _ => "item1|item2 (areas to explore)"
            };
            
            var desc_avoid = targetLanguage switch
            {
                "zh" => "注意1|注意2|注意3 (竖线分隔)",
                "zh-tw" => "注意1|注意2|注意3 (豎線分隔)",
                "es" => "item1|item2 (patrones a tener en cuenta, separado por |)",
                _ => "item1|item2 (patterns to be mindful of)"
            };
            
            var desc_detail = targetLanguage switch
            {
                "zh" => "50-70字，分三部分：象征模式，能量质量，反思意义",
                "zh-tw" => "50-70字，分三部分：象徵模式，能量質量，反思意義",
                "es" => "50-70 palabras en 3 partes: patrón simbólico, calidad energética, significado reflexivo",
                _ => "[50-70 words in 3 parts: symbolic pattern, energy quality, reflective meaning]"
            };

            var desc_mantra = targetLanguage switch
            {
                "zh" => "18-25字，第一人称年度真言 ('我探索...' 或 '我沉思...')",
                "zh-tw" => "18-25字，第一人稱年度真言 ('我探索...' 或 '我沉思...')",
                "es" => "18-25 palabras en primera persona ('Exploro...' o 'Contemplo...')",
                _ => "[18-25 words using first-person 'I explore...' or 'I contemplate...']"
            };

            prompt = singleLanguagePrefix +
                     $@"Create a yearly astrological insight for {yearlyYear} to support self-reflection.
User: {userInfoLine}

========== CONTEXT VALUES (Use EXACT translated values) ==========
Sun Sign: {sunSignTranslated}
Birth Year Zodiac: {birthYearZodiacTranslated}
Yearly Year ({yearlyYear}): {yearlyYearZodiac}
Taishui Relationship: {yearlyTaishuiTranslated}

FORMAT (TSV - Tab-Separated Values):
Each field on ONE line: key	value
Use actual TAB character (not spaces) as separator. For arrays: Use pipe | to separate items.

Output format (TSV):
astro_overlay	{sunSign} Sun · [2-3 word archetype] — {yearlyYear} [Key planetary themes]
theme_title	{desc_theme_title}
theme_glance	{desc_theme_glance}
theme_detail	{desc_theme_detail}

# Career & Purpose
career_score	[1-5 based on symbolic analysis]
career_tag	{desc_tag}
career_do	{desc_do}
career_avoid	{desc_avoid}
career_detail	{desc_detail}

# Relationships & Love
love_score	[1-5]
love_tag	{desc_tag}
love_do	{desc_do}
love_avoid	{desc_avoid}
love_detail	{desc_detail}

# Wealth & Prosperity
prosperity_score	[1-5]
prosperity_tag	{desc_tag}
prosperity_do	{desc_do}
prosperity_avoid	{desc_avoid}
prosperity_detail	{desc_detail}

# Wellness & Balance
wellness_score	[1-5]
wellness_tag	{desc_tag}
wellness_do	{desc_do}
wellness_avoid	{desc_avoid}
wellness_detail	{desc_detail}

# Annual Mantra
mantra	{desc_mantra}

FORMAT REQUIREMENTS:
- Return TSV format: one field per line with TAB between field name and value
- Use actual tab character (\\t) as separator
- Array values: use | separator
- Scores: integer 1-5
- Avoid line breaks within field values
- Return only the data (no markdown wrappers)
";
        }
        else // PredictionType.Daily
        {
            // Determine user's zodiac element for personalized recommendations
            var zodiacElement = sunSign switch
            {
                "Aries" or "Leo" or "Sagittarius" => "Fire",
                "Taurus" or "Virgo" or "Capricorn" => "Earth",
                "Gemini" or "Libra" or "Aquarius" => "Air",
                "Cancer" or "Scorpio" or "Pisces" => "Water",
                _ => "Fire"
            };
            
            // DYNAMIC DESCRIPTIONS BASED ON LANGUAGE
            // Use language-specific placeholders to ensure correct output language
            var desc_dayTitle = targetLanguage switch
            {
                "zh" => "今日主题 (如：水与深度之日)",
                "zh-tw" => "今日主題 (如：水與深度之日)",
                "es" => "El Día de [palabra1] y [palabra2] (ej: El Día de Agua y Profundidad)",
                _ => "The Day of [word1] and [word2] (e.g. The Day of Water and Depth)"
            };

            // Tarot Section - Explicitly requesting ID to avoid translation issues
            var desc_card_name = targetLanguage switch
            {
                "zh" => "[保留英文原名] (如 \"The Fool\")",
                "zh-tw" => "[保留英文原名] (如 \"The Fool\")",
                "es" => "[Usar nombre en INGLÉS, ej. \"The Fool\"]",
                _ => "[Use ENGLISH Name e.g. \"The Fool\"]"
            };
            
            var desc_card_orient = targetLanguage switch
            {
                "zh" => "[保留英文枚举] (\"Upright\" 或 \"Reversed\")",
                "zh-tw" => "[保留英文列舉] (\"Upright\" 或 \"Reversed\")",
                "es" => "[Usar INGLÉS: \"Upright\" o \"Reversed\"]",
                _ => "[Use ENGLISH: \"Upright\" or \"Reversed\"]"
            };
            
            var desc_card_essence = targetLanguage switch
            {
                "zh" => "1-2个中文关键词",
                "zh-tw" => "1-2個中文關鍵詞",
                "es" => "1-2 palabras clave",
                _ => "1-2 words essence"
            };

            // Path Section
            var desc_path_type = targetLanguage switch
            {
                "zh" => "1个形容词 (如：勇敢的)",
                "zh-tw" => "1個形容詞 (如：勇敢的)",
                "es" => "1 adjetivo (ej: valiente)",
                _ => "1 adjective describing today's path"
            };
            
            var desc_path_intro = targetLanguage switch
            {
                "zh" => $"你好 {displayName} (15-25字)",
                "zh-tw" => $"你好 {displayName} (15-25字)",
                "es" => $"Hola {displayName} (15-25 palabras)",
                _ => $"15-25 words starting 'Hi {displayName}'"
            };
            
            var desc_path_detail = targetLanguage switch
            {
                "zh" => "30-40字，基于今日星象的深刻反思与智慧指引",
                "zh-tw" => "30-40字，基於今日星象的深刻反思與智慧指引",
                "es" => "30-40 palabras de sabiduría reflexiva",
                _ => "30-40 words of reflective wisdom"
            };

            // Life Areas
            var desc_career = targetLanguage switch
            {
                "zh" => "10-20字，关于工作的反思",
                "zh-tw" => "10-20字，關於工作的反思",
                "es" => "10-20 palabras sobre el trabajo",
                _ => "10-20 words for reflection on work energy"
            };
            
            var desc_love = targetLanguage switch
            {
                "zh" => "10-20字，关于关系的内省",
                "zh-tw" => "10-20字，關於關係的內省",
                "es" => "10-20 palabras sobre relaciones",
                _ => "10-20 words for reflection on relationships"
            };
            
            var desc_prosperity = targetLanguage switch
            {
                "zh" => "10-20字，关于财富观念的思考",
                "zh-tw" => "10-20字，關於財富觀念的思考",
                "es" => "10-20 palabras sobre abundancia",
                _ => "10-20 words for reflection on abundance"
            };
            
            var desc_wellness = targetLanguage switch
            {
                "zh" => "10-15字，关于身心平衡的建议",
                "zh-tw" => "10-15字，關於身心平衡的建議",
                "es" => "10-15 palabras sobre bienestar",
                _ => "10-15 words for reflection on wellbeing"
            };
            
            var desc_takeaway = targetLanguage switch
            {
                "zh" => $"15-25字，{displayName}，你的...",
                "zh-tw" => $"15-25字，{displayName}，你的...",
                "es" => $"15-25 palabras '{displayName}, tu...'",
                _ => $"15-25 words '{displayName}, your...'"
            };

            // Resonance (Lucky Number is now calculated by backend, removed from LLM prompt)
            var desc_stone = targetLanguage switch
            {
                "zh" => "[保留英文ID] (如 \"Amethyst\")",
                "zh-tw" => "[保留英文ID] (如 \"Amethyst\")",
                "es" => "[Usar nombre en INGLÉS, ej. \"Amethyst\"]",
                _ => "[Use ENGLISH Name as ID]"
            };
            
            var desc_stone_power = targetLanguage switch
            {
                "zh" => "15-20字，水晶能量描述",
                "zh-tw" => "15-20字，水晶能量描述",
                "es" => "15-20 palabras describiendo energía",
                _ => "15-20 words symbolic energy"
            };
            
            var desc_stone_use = targetLanguage switch
            {
                "zh" => "20-30字，具体使用建议，如：随身携带、放在办公桌、冥想时握住、睡前放枕边等",
                "zh-tw" => "20-30字，具體使用建議，如：隨身攜帶、放在辦公桌、冥想時握住、睡前放枕邊等",
                "es" => "20-30 palabras de sugerencia práctica, ej: 'Llévalo contigo', 'Colócalo en tu escritorio', etc.",
                _ => "20-30 words practical usage suggestion e.g. 'Carry with you', 'Place on your desk', 'Hold during meditation', 'Keep by your bedside', etc."
            };

            // Affirmation (Renamed from 'spell' to avoid filters)
            var desc_spell = targetLanguage switch
            {
                "zh" => "2个字的诗意短语",
                "zh-tw" => "2個字的詩意短語",
                "es" => "2 palabras poéticas",
                _ => "2 words poetic"
            };
            
            var desc_spell_words = targetLanguage switch
            {
                "zh" => "20-30字，鼓舞人心的肯定语（不要带引号）",
                "zh-tw" => "20-30字，鼓舞人心的肯定語（不要帶引號）",
                "es" => "20-30 palabras inspiradoras (sin comillas)",
                _ => "20-30 words inspirational affirmation (without quotes)"
            };
            
            var desc_spell_intent = targetLanguage switch
            {
                "zh" => "10-12字，意图",
                "zh-tw" => "10-12字，意圖",
                "es" => "10-12 palabras, intención",
                _ => "10-12 words 'To explore...'"
            };

            // Guidance (Renamed from 'fortune' to avoid filters in description)
            var desc_fortune_title = targetLanguage switch
            {
                "zh" => "4-8字，诗意隐喻",
                "zh-tw" => "4-8字，詩意隱喻",
                "es" => "4-8 palabras metafóricas",
                _ => "4-8 words poetic metaphor"
            };
            
            var desc_fortune_do = targetLanguage switch
            {
                "zh" => "5条具体行动短语 (如：散步片刻|写感恩日记|与朋友交流|冥想放松|整理空间，竖线分隔)",
                "zh-tw" => "5條具體行動短語 (如：散步片刻|寫感恩日記|與朋友交流|冥想放鬆|整理空間，豎線分隔)",
                "es" => "5 frases de acción concretas (ej: Dar un paseo|Escribir un diario|Hablar con amigos|Meditar|Organizar tu espacio, separado por |)",
                _ => "5 specific action phrases (e.g. Take a walk|Write a journal|Talk with friends|Meditate|Organize your space, separated by |)"
            };
            
            var desc_fortune_avoid = targetLanguage switch
            {
                "zh" => "5条具体注意事项 (如：过度承诺|匆忙决策|忽视内心|过度消费|忽略休息，竖线分隔)",
                "zh-tw" => "5條具體注意事項 (如：過度承諾|匆忙決策|忽視內心|過度消費|忽略休息，豎線分隔)",
                "es" => "5 cosas específicas a evitar (ej: Prometer demasiado|Decisiones apresuradas|Ignorar intuición|Gastar en exceso|Saltarte descansos, separado por |)",
                _ => "5 specific things to avoid (e.g. Over-committing|Rushing decisions|Ignoring intuition|Overspending|Skipping rest, separated by |)"
            };
            
            var desc_fortune_tip = targetLanguage switch
            {
                "zh" => "10-15字，今日反思贴士",
                "zh-tw" => "10-15字，今日反思貼士",
                "es" => "10-15 palabras, consejo reflexivo",
                _ => "10-15 words 'Today's reflection invites...'"
            };

            // Note: LatLong inference is NOT needed for Daily predictions anymore
            // It's only useful for Lifetime predictions to calculate Moon/Rising signs
            var latLongInferenceSection = string.Empty;
            
            // Calculate today's numerology energy as a date seed for variation
            var dailyEnergyNumber = Services.LuckyNumberService.CalculateLuckyNumber(
                userInfo.BirthDate,
                predictionDate,
                targetLanguage);

            prompt = singleLanguagePrefix + $@"Generate a daily reflection entry.
Date: {predictionDate:yyyy-MM-dd}
User: {userInfoLine}

========== CONTEXT VALUES (Personalization) ==========
Display Name: {displayName}
Sun Sign: {sunSign}
Element: {zodiacElement}
Birth Year Zodiac: {birthYearZodiac}
Today's Numerology Energy: {dailyEnergyNumber.Digit} (Use this as a seed for daily variation){latLongInferenceSection}

========== OUTPUT FORMAT (TSV) ==========
Key	Value

=== 1. THEME ===
daily_theme_title	{desc_dayTitle}

=== 2. INSIGHTS ===
# Tarot Symbolism (Select based on numerology energy {dailyEnergyNumber.Digit}, {sunSign}/{zodiacElement}, and date {predictionDate:yyyy-MM-dd} - ensure daily variation)
tarot_card_name	{desc_card_name}
tarot_card_essence	{desc_card_essence}
tarot_card_orientation	{desc_card_orient}

# Your Path
path_adjective	{desc_path_type}
path_greeting	{desc_path_intro}
path_wisdom	{desc_path_detail}

# Life Reflections
reflection_career	{desc_career}
reflection_relationships	{desc_love}
reflection_wealth	{desc_prosperity}
reflection_wellbeing	{desc_wellness}

# Summary
daily_takeaway	{desc_takeaway}

=== 3. RESONANCE ===
# Crystal (Select for {zodiacElement} element based on numerology energy {dailyEnergyNumber.Digit} and date {predictionDate:yyyy-MM-dd} - ensure daily variation)
crystal_stone_id	{desc_stone}
crystal_power	{desc_stone_power}
crystal_usage	{desc_stone_use}

# Daily Affirmation
affirmation_poetic	{desc_spell}
affirmation_text	{desc_spell_words}
affirmation_intent	{desc_spell_intent}

=== 4. GUIDANCE ===
guidance_metaphor	{desc_fortune_title}
guidance_suggestions	{desc_fortune_do}
guidance_mindful_of	{desc_fortune_avoid}
guidance_tip	{desc_fortune_tip}

IMPORTANT:
- Output strictly valid TSV.
- Start immediately with `daily_theme_title`.
- Array values: use | separator.
- Do NOT generate ANY explanation text before or after the data.
";

            return prompt;
        }

        return prompt;
    }

    /// <summary>
    /// Build translation prompt for remaining languages (second stage)
    /// </summary>
        private string BuildTranslationPrompt(Dictionary<string, string> sourceContent, string sourceLanguage,
            List<string> targetLanguages, PredictionType type)
    {
        var languageMap = new Dictionary<string, string>
        {
            { "en", "English" },
            { "zh-tw", "繁體中文" },
            { "zh", "简体中文" },
            { "es", "Español" }
        };
        
        var sourceLangName = languageMap.GetValueOrDefault(sourceLanguage, "English");
            var targetLangNames = string.Join(", ",
                targetLanguages.Select(lang => languageMap.GetValueOrDefault(lang, lang)));
        
        // Convert source content to TSV format
            var sourceTsv = new StringBuilder();
        foreach (var kvp in sourceContent)
        {
            sourceTsv.AppendLine($"{kvp.Key}\t{kvp.Value}");
        }
        
            var translationPrompt =
                $@"TASK: Translate the following {type} astrological reflection from {sourceLangName} into {targetLangNames}.

TRANSLATION RULES:
- TRANSLATE content, do NOT regenerate or reinterpret
- Keep exact same meaning and structure
- Maintain natural, fluent expression in each target language
- For Chinese (zh-tw, zh): Adapt English grammar naturally
  * Remove/adapt articles: ""The Star"" → ""星星""
  * Adjust to natural Chinese word order

OUTPUT FORMAT:
[LANGUAGE_CODE]
fieldName	translatedValue
...

Example:
[zh-tw]
dayTitle	祥龍之日
path_title	Sean今日的道路 - 寧靜之路

[zh]
dayTitle	祥龙之日
path_title	Sean今日的道路 - 宁静之路

SOURCE CONTENT ({sourceLangName} - TSV Format):
{sourceTsv}

Generate translations for: {targetLangNames}
";

        return translationPrompt;
    }

    /// <summary>
    /// Trigger on-demand generation for a prediction
    /// Waits for lock to be persisted, then starts generation in background
    /// </summary>
        private async Task TriggerOnDemandGenerationAsync(LumenUserDto userInfo, DateOnly predictionDate,
            PredictionType type, string targetLanguage)
    {
        // Check if generation is already in progress (memory-based check, may not survive deactivation)
        if (State.GenerationLocks.TryGetValue(type, out var lockInfo) && lockInfo.IsGenerating)
        {
                _logger.LogInformation(
                    $"[Lumen][OnDemand] {userInfo.UserId} Generation already in progress for {type}, skipping");
            return;
        }
        
            _logger.LogInformation(
                $"[Lumen][OnDemand] {userInfo.UserId} Triggering async generation for {type}, Language: {targetLanguage}");
        
        // Set generation lock using Event Sourcing (persisted to survive Grain deactivation)
        // IMPORTANT: Wait for ConfirmEvents to complete before returning, so status interface can see the lock
        RaiseEvent(new GenerationLockSetEvent
        {
            Type = type,
            StartedAt = DateTime.UtcNow,
            RetryCount = 0 // Reset retry count for new generation
        });
        await ConfirmEvents();
        
            _logger.LogInformation(
                $"[Lumen][OnDemand] {userInfo.UserId} GENERATION_LOCK_SET (Persisted) - Type: {type}");
        
        // Fire and forget - Process directly in the grain context instead of using Task.Run
        // This ensures proper Orleans activation context access
        _ = GeneratePredictionInBackgroundAsync(userInfo, predictionDate, type, targetLanguage);
    }
    
    /// <summary>
    /// Generate prediction in background (handles concurrent generation requests safely)
    /// </summary>
        private async Task GeneratePredictionInBackgroundAsync(LumenUserDto userInfo, DateOnly predictionDate,
            PredictionType type, string targetLanguage)
    {
        var maxRetryCount = _options?.MaxRetryCount ?? DEFAULT_MAX_RETRY_COUNT;
        
        try
        {
            // Check if data needs regeneration (date expired, profile updated, or prompt version changed)
            var needsRegeneration = false;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var currentYear = DateTime.UtcNow.Year;
            
            // Check expiration based on type
            bool dataExpired = type switch
            {
                PredictionType.Lifetime => false, // Lifetime never expires by date
                PredictionType.Yearly => State.PredictionDate.Year != currentYear, // Yearly expires after 1 year
                PredictionType.Daily => State.PredictionDate != today, // Daily expires every day
                _ => false
            };
            
            // Check profile update
            // If State.ProfileUpdatedAt is null (first generation or after delete), treat as changed
            // If State.ProfileUpdatedAt has value, check if profile was updated after prediction
            bool profileChanged = !State.ProfileUpdatedAt.HasValue 
                                 || userInfo.UpdatedAt > State.ProfileUpdatedAt.Value;
            
            // Check prompt version
            var currentPromptVersion = _options?.PromptVersion ?? DEFAULT_PROMPT_VERSION;
            bool promptVersionChanged = State.PromptVersion != currentPromptVersion;
            
            needsRegeneration = dataExpired || profileChanged || promptVersionChanged;
            
            // If data is expired/outdated, clear old data before generating new
            if (needsRegeneration && State.PredictionId != Guid.Empty)
            {
                    _logger.LogInformation(
                        $"[Lumen][OnDemand] {userInfo.UserId} Clearing outdated {type} data - Date: {State.PredictionDate}, Profile: {profileChanged}, Prompt: {promptVersionChanged}");
                
                // Clear the state but keep the grain active for new generation
                // Note: We don't use ClearPredictionAsync here to avoid interfering with the ongoing generation process
            }
            
            // Double-check if already exists AND is not expired (in case of concurrent calls or Grain reactivation)
                if (!needsRegeneration && State.MultilingualResults != null &&
                    State.MultilingualResults.ContainsKey(targetLanguage))
            {
                    _logger.LogInformation(
                        $"[Lumen][OnDemand] {userInfo.UserId} Language {targetLanguage} already exists and not expired, skipping generation");
                return;
            }
            
            var generateStopwatch = Stopwatch.StartNew();
            var predictionResult = await GeneratePredictionAsync(userInfo, predictionDate, type, targetLanguage);
            generateStopwatch.Stop();
            
            if (!predictionResult.Success)
            {
                // Get current retry count
                var currentRetryCount = State.GenerationLocks.ContainsKey(type) 
                    ? State.GenerationLocks[type].RetryCount 
                    : 0;
                
                    _logger.LogWarning(
                        $"[Lumen][OnDemand] {userInfo.UserId} Generation_Failed: {generateStopwatch.ElapsedMilliseconds}ms for {type}, RetryCount: {currentRetryCount}/{maxRetryCount}");
                
                // Check if we should retry (any failure with retry budget remaining)
                if (currentRetryCount < maxRetryCount)
                {
                    // Increment retry count using Event Sourcing
                    var newRetryCount = currentRetryCount + 1;
                    RaiseEvent(new GenerationLockSetEvent
                    {
                        Type = type,
                        StartedAt = DateTime.UtcNow,
                        RetryCount = newRetryCount
                    });
                    await ConfirmEvents();
                    
                        _logger.LogInformation(
                            $"[Lumen][OnDemand] {userInfo.UserId} RETRY_TRIGGERED for {type} (Attempt {newRetryCount}/{maxRetryCount}), Reason: {predictionResult.Message}");
                    
                    // Trigger retry (fire-and-forget)
                    _ = GeneratePredictionInBackgroundAsync(userInfo, predictionDate, type, targetLanguage);
                    return; // Don't release lock in finally block, as we're retrying
                }
                else
                {
                        _logger.LogError(
                            $"[Lumen][OnDemand] {userInfo.UserId} MAX_RETRY_EXCEEDED for {type}, giving up after {currentRetryCount} attempts, Last error: {predictionResult.Message}");
                }
            }
            else
            {
                    _logger.LogInformation(
                        $"[Lumen][OnDemand] {userInfo.UserId} Generation_Success: {generateStopwatch.ElapsedMilliseconds}ms for {type}");
                
                // Reset retry count on success (using Event Sourcing)
                if (State.GenerationLocks.ContainsKey(type) && State.GenerationLocks[type].RetryCount > 0)
                {
                    RaiseEvent(new GenerationLockSetEvent
                    {
                        Type = type,
                        StartedAt = State.GenerationLocks[type].StartedAt ?? DateTime.UtcNow,
                        RetryCount = 0 // Reset to 0 on success
                    });
                    await ConfirmEvents();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[Lumen][OnDemand] {userInfo.UserId} Error generating {type}");
        }
        finally
        {
            // Clear generation lock using Event Sourcing
            if (State.GenerationLocks.ContainsKey(type))
            {
                RaiseEvent(new GenerationLockClearedEvent
                {
                    Type = type
                });
                await ConfirmEvents();
                    _logger.LogInformation(
                        $"[Lumen][OnDemand] {userInfo.UserId} GENERATION_LOCK_RELEASED (Persisted) - Type: {type}");
            }
        }
    }

    /// <summary>
    /// Trigger on-demand translation for a specific language (Fire-and-forget)
    /// </summary>
        private void TriggerOnDemandTranslationAsync(LumenUserDto userInfo, DateOnly predictionDate,
            PredictionType type, string sourceLanguage, Dictionary<string, string> sourceContent, string targetLanguage)
    {
        // Validate source content first
        if (sourceContent == null || sourceContent.Count == 0)
        {
                _logger.LogError(
                    $"[Lumen][OnDemand] {userInfo.UserId} Cannot translate to {targetLanguage}: Source content is empty (sourceLanguage: {sourceLanguage})");
                return;
            }
            
        // Check if already exists (most reliable check - persisted state)
        if (State.MultilingualResults.ContainsKey(targetLanguage))
        {
                _logger.LogInformation(
                    $"[Lumen][OnDemand] {userInfo.UserId} Language {targetLanguage} already exists, skipping translation");
            return;
        }
            
            _logger.LogInformation(
                $"[Lumen][OnDemand] {userInfo.UserId} Triggering translation: {sourceLanguage} → {targetLanguage} for {type}, source fields: {sourceContent.Count}");
        
        // Fire and forget - Process directly in the grain context instead of using Task.Run
        // This ensures proper Orleans activation context access
        _ = TranslateAndSaveAsync(userInfo, predictionDate, type, sourceLanguage, sourceContent, targetLanguage);
    }
    
    /// <summary>
    /// Translate and save to state (handles concurrent translation requests safely)
    /// </summary>
        private async Task TranslateAndSaveAsync(LumenUserDto userInfo, DateOnly predictionDate, PredictionType type,
            string sourceLanguage, Dictionary<string, string> sourceContent, string targetLanguage)
    {
        try
        {
            // Double-check if already exists (in case of concurrent calls)
            if (State.MultilingualResults.ContainsKey(targetLanguage))
            {
                    _logger.LogInformation(
                        $"[Lumen][OnDemand] {userInfo.UserId} Language {targetLanguage} already exists (double-check), skipping");
                return;
            }
            
            // Set translation lock for progress tracking
            if (!State.TranslationLocks.ContainsKey(targetLanguage))
            {
                State.TranslationLocks[targetLanguage] = new TranslationLockInfo();
            }

            State.TranslationLocks[targetLanguage].IsTranslating = true;
            State.TranslationLocks[targetLanguage].StartedAt = DateTime.UtcNow;
            State.TranslationLocks[targetLanguage].SourceLanguage = sourceLanguage;
                _logger.LogInformation(
                    $"[Lumen][OnDemand] {userInfo.UserId} TRANSLATION_LOCK_SET - Language: {targetLanguage}, Source: {sourceLanguage}");
            
                await TranslateSingleLanguageAsync(userInfo, predictionDate, type, sourceLanguage, sourceContent,
                    targetLanguage);
        }
        catch (Exception ex)
        {
                _logger.LogError(ex,
                    $"[Lumen][OnDemand] {userInfo.UserId} Error translating to {targetLanguage} for {type}");
        }
        finally
        {
            // Clear translation lock
            if (State.TranslationLocks.ContainsKey(targetLanguage))
            {
                State.TranslationLocks[targetLanguage].IsTranslating = false;
                State.TranslationLocks[targetLanguage].StartedAt = null;
                    _logger.LogInformation(
                        $"[Lumen][OnDemand] {userInfo.UserId} TRANSLATION_LOCK_RELEASED - Language: {targetLanguage}");
            }
        }
    }

    /// <summary>
    /// Translate to a single language (used for on-demand translation)
    /// </summary>
        private async Task TranslateSingleLanguageAsync(LumenUserDto userInfo, DateOnly predictionDate,
            PredictionType type, string sourceLanguage, Dictionary<string, string> sourceContent, string targetLanguage)
    {
        try
        {
            // Validate source content early
            if (sourceContent == null || sourceContent.Count == 0)
            {
                    _logger.LogError(
                        $"[Lumen][OnDemandTranslation] {userInfo.UserId} Cannot translate {sourceLanguage} → {targetLanguage}: Source content is empty");
                return;
            }
            
                _logger.LogInformation(
                    $"[Lumen][OnDemandTranslation] {userInfo.UserId} Translating {sourceLanguage} → {targetLanguage} for {type}, source fields: {sourceContent.Count}");
            
            // Filter fields that don't need translation
            var filteredForTranslation = FilterFieldsForTranslation(sourceContent);
            var skippedFields = sourceContent.Where(kvp => !filteredForTranslation.ContainsKey(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            
                _logger.LogInformation(
                    $"[Lumen][OnDemandTranslation] {userInfo.UserId} Filtered {skippedFields.Count} fields (enums/numbers), {filteredForTranslation.Count} fields to translate");
            
            // Build single-language translation prompt
            var promptBuildStopwatch = Stopwatch.StartNew();
                var translationPrompt =
                    BuildSingleLanguageTranslationPrompt(filteredForTranslation, sourceLanguage, targetLanguage, type);
            promptBuildStopwatch.Stop();
            var translationPromptTokens = TokenHelper.EstimateTokenCount(translationPrompt);
                _logger.LogInformation(
                    $"[PERF][Lumen][OnDemandTranslation] {userInfo.UserId} {targetLanguage} Prompt_Build: {promptBuildStopwatch.ElapsedMilliseconds}ms, Prompt_Length: {translationPrompt.Length} chars, Tokens: ~{translationPromptTokens}, Source_Fields: {filteredForTranslation.Count}");
            
            // Call LLM for translation
            var llmStopwatch = Stopwatch.StartNew();
            var userGuid = CommonHelper.StringToGuid(userInfo.UserId);
            
            // Use the same grain key as prediction generation (userId + type)
            // Translation for different languages will be serialized within the same grain
            // This keeps grain count minimal (3 grains per user)
            var translationGrainKey = CommonHelper.StringToGuid($"{userInfo.UserId}_{type.ToString().ToLower()}");
            var godChat = _clusterClient.GetGrain<IGodChat>(translationGrainKey);
            var chatId = Guid.NewGuid().ToString();
            
            // Simple system prompt for translation
                var translationSystemPrompt =
                    @"You are a professional translator for astrological and philosophical reflection content.
All content is for entertainment, self-exploration, and contemplative purposes only.";
            
            var response = await godChat.ChatWithoutHistoryAsync(
                userGuid,
                translationSystemPrompt,
                translationPrompt,
                chatId,
                null,
                true,
                "LUMEN");
            llmStopwatch.Stop();
                _logger.LogInformation(
                    $"[Lumen][OnDemandTranslation] {userInfo.UserId} {targetLanguage} LLM_Call: {llmStopwatch.ElapsedMilliseconds}ms");
            
            if (response == null || response.Count() == 0)
            {
                    _logger.LogWarning(
                        $"[Lumen][OnDemandTranslation] {userInfo.UserId} {targetLanguage} No response from LLM");
                return;
            }
            
            var aiResponse = response[0].Content;
                _logger.LogInformation(
                    $"[PERF][Lumen][OnDemandTranslation] {userInfo.UserId} {targetLanguage} LLM_Response_Length: {aiResponse.Length} chars");
            
            // Parse response
            var parseStopwatch = Stopwatch.StartNew();
            
            // Try TSV format first (new format)
            var contentDict = new Dictionary<string, string>();
            var hasTabs = aiResponse.Contains("\t");
            var hasJsonStart = aiResponse.Trim().StartsWith("{") || aiResponse.Contains("```json");
            
            // Parse TSV format (required by translation prompt)
            _logger.LogDebug($"[Lumen][OnDemandTranslation] {userInfo.UserId} {targetLanguage} Parsing TSV format");
            var tsvResult = ParseTsvResponse(aiResponse);
            if (tsvResult != null && tsvResult.Count > 0)
            {
                contentDict = tsvResult;
            }
            else
            {
                    _logger.LogError(
                        $"[Lumen][OnDemandTranslation] {userInfo.UserId} {targetLanguage} TSV parse failed. LLM may have returned wrong format. Full response:\n{aiResponse}");
                return;
            }
            
            parseStopwatch.Stop();
                _logger.LogInformation(
                    $"[Lumen][OnDemandTranslation] {userInfo.UserId} {targetLanguage} Parse: {parseStopwatch.ElapsedMilliseconds}ms, Fields: {contentDict.Count}");
            
            // Separate skipped fields into backend-calculated vs others
            var backendCalculatedFields = new Dictionary<string, string>();
            var otherSkippedFields = new Dictionary<string, string>();
            
            var backendCalculatedPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "sunSign_name", "westernOverview_sunSign", "westernOverview_moonSign", "westernOverview_risingSign",
                "westernOverview_sunArchetype", "westernOverview_moonArchetype", "westernOverview_risingArchetype",
                "chineseZodiac_animal", "chineseZodiac_title",
                "chineseAstrology_currentYearStem", "chineseAstrology_currentYearStemPinyin",
                "chineseAstrology_currentYearBranch", "chineseAstrology_currentYearBranchPinyin",
                "chineseAstrology_taishuiRelationship", "chineseAstrology_currentYear",
                "pastCycle_ageRange", "pastCycle_period", "currentCycle_ageRange", "currentCycle_period",
                "futureCycle_ageRange", "futureCycle_period", "zodiacCycle_title", "zodiacCycle_cycleName",
                "zodiacInfluence", "currentPhase",
                "todaysReading_tarotCard_name", "todaysReading_tarotCard_orientation",
                "luckyAlignments_luckyStone", "todaysReading_pathTitle"
            };
            
            foreach (var skipped in skippedFields)
            {
                if (backendCalculatedPatterns.Contains(skipped.Key) || 
                    skipped.Key.StartsWith("fourPillars_year", StringComparison.OrdinalIgnoreCase) ||
                    skipped.Key.StartsWith("fourPillars_month", StringComparison.OrdinalIgnoreCase) ||
                    skipped.Key.StartsWith("fourPillars_day", StringComparison.OrdinalIgnoreCase) ||
                    skipped.Key.StartsWith("fourPillars_hour", StringComparison.OrdinalIgnoreCase))
                {
                    backendCalculatedFields[skipped.Key] = skipped.Value;
                }
                else
                {
                    otherSkippedFields[skipped.Key] = skipped.Value;
                }
            }
            
            // Merge back non-backend-calculated skipped fields (enums, numbers, etc.)
            foreach (var skipped in otherSkippedFields)
            {
                contentDict[skipped.Key] = skipped.Value;
            }
            
            // Re-inject backend-calculated fields for target language
            await InjectBackendFieldsForLanguageAsync(contentDict, userInfo, predictionDate, type, targetLanguage);
            
                _logger.LogInformation(
                    $"[Lumen][OnDemandTranslation] {userInfo.UserId} {targetLanguage} Merged {otherSkippedFields.Count} skipped fields, re-injected {backendCalculatedFields.Count} backend fields, Total: {contentDict.Count}");
            
            // Add quotes to affirmation text based on target language
            contentDict = AddQuotesToAffirmation(contentDict, targetLanguage);
            
            // Raise event to update state with this language
            var translatedLanguages = new Dictionary<string, Dictionary<string, string>>
            {
                { targetLanguage, contentDict }
            };
            
            var allLanguages = new List<string> { "en", "zh-tw", "zh", "es" };
                var updatedLanguages = (State.GeneratedLanguages ?? new List<string>()).Union(new[] { targetLanguage })
                    .ToList();
            
            // For Daily: LastGeneratedDate = predictionDate (since predictionDate changes daily)
            // For Yearly/Lifetime: LastGeneratedDate = today (to prevent duplicate translations on the same day)
            var lastGenDate = type == PredictionType.Daily 
                ? predictionDate 
                : DateOnly.FromDateTime(DateTime.UtcNow);
            
            RaiseEvent(new LanguagesTranslatedEvent
            {
                Type = type,
                PredictionDate = predictionDate,
                TranslatedLanguages = translatedLanguages,
                AllGeneratedLanguages = updatedLanguages,
                LastGeneratedDate = lastGenDate
            });
            
            // Confirm events to persist state changes
            await ConfirmEvents();
            
                _logger.LogInformation(
                    $"[Lumen][OnDemandTranslation] {userInfo.UserId} {targetLanguage} COMPLETED - Total: {llmStopwatch.ElapsedMilliseconds + parseStopwatch.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
                _logger.LogError(ex,
                    $"[Lumen][OnDemandTranslation] {userInfo.UserId} Error translating to {targetLanguage} for {type}");
        }
    }

    /// <summary>
    /// Build single-language translation prompt (for on-demand translation)
    /// </summary>
    /// <summary>
    /// Filter fields that don't need translation (enums, numbers, backend-calculated fields, etc.)
    /// </summary>
        private Dictionary<string, string> FilterFieldsForTranslation(Dictionary<string, string> sourceContent)
    {
        var filtered = new Dictionary<string, string>();
        
        // Backend-calculated field patterns (these are translated via backend dictionary, not LLM)
        var backendCalculatedPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Zodiac signs
            "sunSign_name",
            "westernOverview_sunSign",
            "westernOverview_moonSign",
            "westernOverview_risingSign",
            "westernOverview_sunArchetype",
            "westernOverview_moonArchetype",
            "westernOverview_risingArchetype",
            
            // Chinese zodiac
            "chineseZodiac_animal",
            "chineseZodiac_title",
            
            // Chinese astrology (stems/branches)
            "chineseAstrology_currentYearStem",
            "chineseAstrology_currentYearStemPinyin",
            "chineseAstrology_currentYearBranch",
            "chineseAstrology_currentYearBranchPinyin",
            "chineseAstrology_taishuiRelationship",
            "chineseAstrology_currentYear",
            
            // Cycles
            "pastCycle_ageRange",
            "pastCycle_period",
            "currentCycle_ageRange",
            "currentCycle_period",
            "futureCycle_ageRange",
            "futureCycle_period",
            
            // Zodiac cycle
            "zodiacCycle_title",
            "zodiacCycle_cycleName",
            "zodiacInfluence",
            
            // Phase
            "currentPhase",
            
            // Daily specific (these are translated by backend tarot/stone dictionaries)
            "todaysReading_tarotCard_name",
            "todaysReading_tarotCard_orientation",
            "luckyAlignments_luckyStone",
            "todaysReading_pathTitle"  // Constructed by backend
        };
        
        // Backend-calculated field prefixes (Four Pillars fields)
        var backendCalculatedPrefixes = new[]
        {
            "fourPillars_year",
            "fourPillars_month",
            "fourPillars_day",
            "fourPillars_hour"
        };
        
        foreach (var kvp in sourceContent)
        {
            var key = kvp.Key;
            var value = kvp.Value;
            
            // Skip empty or whitespace-only values
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }
            
            // Skip enum fields (ending with _enum)
            if (key.EndsWith("_enum", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            
            // Skip backend-calculated fields
            if (backendCalculatedPatterns.Contains(key))
            {
                continue;
            }
            
            // Skip backend-calculated prefixes (Four Pillars)
            if (backendCalculatedPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            
            // Skip pure number fields (integers or decimals)
            if (int.TryParse(value, out _) || double.TryParse(value, out _))
            {
                continue;
            }
            
            // Skip URL fields
            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            
            // Skip very short values (likely codes or IDs, e.g., "1", "A", "en")
            if (value.Length <= 2 && !value.Contains(" "))
            {
                continue;
            }
            
            // Keep this field for translation
            filtered[key] = value;
        }
        
        return filtered;
    }

    /// <summary>
    /// Re-inject backend-calculated fields for a specific language (used in OnDemandTranslation)
    /// </summary>
        private async Task InjectBackendFieldsForLanguageAsync(Dictionary<string, string> targetDict,
            LumenUserDto userInfo, DateOnly predictionDate, PredictionType type, string targetLanguage)
    {
        try
        {
            // Calculate timezone-corrected birth date/time (same logic as in GeneratePredictionAsync)
            var calcBirthDate = userInfo.BirthDate;
            var calcBirthTime = userInfo.BirthTime;
            
            var currentYear = DateTime.UtcNow.Year;
            var birthYear = calcBirthDate.Year;
            
            var sunSign = LumenCalculator.CalculateZodiacSign(calcBirthDate);
            var birthYearZodiac = LumenCalculator.GetChineseZodiacWithElement(birthYear);
            var birthYearAnimal = LumenCalculator.CalculateChineseZodiac(birthYear);
            var birthYearStemsComponents = LumenCalculator.GetStemsAndBranchesComponents(birthYear);
            var pastCycle = LumenCalculator.CalculateTenYearCycle(birthYear, -1);
            var currentCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 0);
            var futureCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 1);
            
            // Calculate Moon/Rising signs if available (simplified - reuse from source if needed)
            var effectiveLatLong = !string.IsNullOrWhiteSpace(userInfo.LatLong) 
                ? userInfo.LatLong 
                : userInfo.LatLongInferred;
            
            string? moonSign = null;
            string? risingSign = null;
            
            if (calcBirthTime.HasValue && !string.IsNullOrWhiteSpace(effectiveLatLong))
            {
                try
                {
                    var parts = effectiveLatLong.Split(',', StringSplitOptions.TrimEntries);
                    if (parts.Length == 2 && 
                        double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude) &&
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude))
                    {
                        var localDateTime = calcBirthDate.ToDateTime(calcBirthTime.Value);
                        var (utcDateTime, _, _) = LumenTimezoneHelper.GetUtcTimeFromLocal(localDateTime, latitude, longitude);
                        var (_, calculatedMoonSign, calculatedRisingSign) = CalculateSigns(calcBirthDate, calcBirthTime.Value, latitude, longitude);
                        moonSign = calculatedMoonSign;
                        risingSign = calculatedRisingSign;
                    }
                }
                catch { /* Ignore errors, use fallback */ }
            }
            
            moonSign = moonSign ?? sunSign;
            risingSign = risingSign ?? sunSign;
            
            if (type == PredictionType.Lifetime)
            {
                var fourPillars = LumenCalculator.CalculateFourPillars(calcBirthDate, calcBirthTime);
                
                targetDict["chineseAstrology_currentYearStem"] = birthYearStemsComponents.stemChinese;
                targetDict["chineseAstrology_currentYearStemPinyin"] = birthYearStemsComponents.stemPinyin;
                targetDict["chineseAstrology_currentYearBranch"] = birthYearStemsComponents.branchChinese;
                targetDict["chineseAstrology_currentYearBranchPinyin"] = birthYearStemsComponents.branchPinyin;
                targetDict["sunSign_name"] = TranslateSunSign(sunSign, targetLanguage);
                targetDict["sunSign_enum"] = ((int)LumenCalculator.ParseZodiacSignEnum(sunSign)).ToString();
                targetDict["westernOverview_sunSign"] = TranslateSunSign(sunSign, targetLanguage);
                targetDict["westernOverview_moonSign"] = TranslateSunSign(moonSign, targetLanguage);
                targetDict["westernOverview_risingSign"] = TranslateSunSign(risingSign, targetLanguage);
                
                // Replace sign names in combined essence statement with backend-calculated translations
                // Then move the result to combinedEssence field (removing the redundant westernOverview_combinedEssenceStatement)
                if (targetDict.TryGetValue("westernOverview_combinedEssenceStatement", out var combinedEssenceStatement))
                {
                    var sunSignTranslated = TranslateSunSign(sunSign, targetLanguage);
                    var moonSignTranslated = TranslateSunSign(moonSign, targetLanguage);
                    var risingSignTranslated = TranslateSunSign(risingSign, targetLanguage);
                    
                    // Replace any occurrence of sign names (case-insensitive) with accurate translations
                    // This ensures LLM-generated statement uses correct translations
                    foreach (var signToReplace in new[] { sunSign, moonSign, risingSign })
                    {
                        combinedEssenceStatement = System.Text.RegularExpressions.Regex.Replace(
                            combinedEssenceStatement,
                            $@"\b{signToReplace}\b",
                            match =>
                            {
                                if (signToReplace == sunSign) return sunSignTranslated;
                                if (signToReplace == moonSign) return moonSignTranslated;
                                if (signToReplace == risingSign) return risingSignTranslated;
                                return match.Value;
                            },
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase
                        );
                    }
                    
                    // Override combinedEssence with the formatted statement
                    targetDict["combinedEssence"] = combinedEssenceStatement;
                    
                    // Remove the redundant westernOverview_combinedEssenceStatement field
                    targetDict.Remove("westernOverview_combinedEssenceStatement");
                }
                
                targetDict["chineseZodiac_animal"] = TranslateChineseZodiacAnimal(birthYearZodiac, targetLanguage);
                targetDict["chineseZodiac_enum"] = ((int)LumenCalculator.ParseChineseZodiacEnum(birthYearAnimal)).ToString();
                targetDict["chineseZodiac_title"] = TranslateZodiacTitle(birthYearAnimal, targetLanguage);
                targetDict["pastCycle_ageRange"] = TranslateCycleAgeRange(pastCycle.AgeRange, targetLanguage);
                targetDict["pastCycle_period"] = TranslateCyclePeriod(pastCycle.Period, targetLanguage);
                targetDict["currentCycle_ageRange"] = TranslateCycleAgeRange(currentCycle.AgeRange, targetLanguage);
                targetDict["currentCycle_period"] = TranslateCyclePeriod(currentCycle.Period, targetLanguage);
                targetDict["futureCycle_ageRange"] = TranslateCycleAgeRange(futureCycle.AgeRange, targetLanguage);
                targetDict["futureCycle_period"] = TranslateCyclePeriod(futureCycle.Period, targetLanguage);
                
                // Construct zodiacCycle_title
                if (targetDict.TryGetValue("zodiacCycle_yearRange", out var yearRange) && !string.IsNullOrWhiteSpace(yearRange))
                {
                    var cycleTitlePrefix = targetLanguage switch
                    {
                        "zh" => "生肖周期影响",
                        "zh-tw" => "生肖週期影響",
                        "es" => "Influencia del Ciclo Zodiacal",
                        _ => "Zodiac Cycle Influence"
                    };
                    targetDict["zodiacCycle_title"] = $"{cycleTitlePrefix} ({yearRange})";
                }
                
                // Fallback: if zodiacCycle_cycleName is missing, use zodiacCycle_cycleNameChinese
                if ((!targetDict.ContainsKey("zodiacCycle_cycleName") || 
                     string.IsNullOrWhiteSpace(targetDict["zodiacCycle_cycleName"])) &&
                    targetDict.TryGetValue("zodiacCycle_cycleNameChinese", out var translationFallbackCycleName) && 
                    !string.IsNullOrWhiteSpace(translationFallbackCycleName))
                {
                    targetDict["zodiacCycle_cycleName"] = translationFallbackCycleName;
                    _logger.LogWarning(
                        "[Lumen][OnDemandTranslation] zodiacCycle_cycleName missing for language {Language}, using Chinese fallback",
                        targetLanguage);
                }
                
                InjectFourPillarsData(targetDict, fourPillars, targetLanguage);
                
                _logger.LogDebug($"[Lumen][OnDemandTranslation] Re-injected Lifetime backend fields for {targetLanguage}");
            }
            else if (type == PredictionType.Yearly)
            {
                var yearlyYear = predictionDate.Year;
                var yearlyYearZodiac = LumenCalculator.GetChineseZodiacWithElement(yearlyYear);
                var yearlyTaishui = LumenCalculator.CalculateTaishuiRelationship(birthYear, yearlyYear);
                
                targetDict["sunSign_name"] = TranslateSunSign(sunSign, targetLanguage);
                targetDict["sunSign_enum"] = ((int)LumenCalculator.ParseZodiacSignEnum(sunSign)).ToString();
                targetDict["chineseZodiac_animal"] = TranslateChineseZodiacAnimal(birthYearZodiac, targetLanguage);
                targetDict["chineseZodiac_enum"] = ((int)LumenCalculator.ParseChineseZodiacEnum(birthYearAnimal)).ToString();
                targetDict["chineseAstrology_currentYearStem"] = birthYearStemsComponents.stemChinese;
                targetDict["chineseAstrology_currentYearStemPinyin"] = birthYearStemsComponents.stemPinyin;
                targetDict["chineseAstrology_currentYearBranch"] = birthYearStemsComponents.branchChinese;
                targetDict["chineseAstrology_currentYearBranchPinyin"] = birthYearStemsComponents.branchPinyin;
                targetDict["chineseAstrology_taishuiRelationship"] = TranslateTaishuiRelationship(yearlyTaishui, targetLanguage);
                targetDict["zodiacInfluence"] = BuildZodiacInfluence(birthYearZodiac, yearlyYearZodiac, yearlyTaishui, targetLanguage);
                
                _logger.LogDebug($"[Lumen][OnDemandTranslation] Re-injected Yearly backend fields for {targetLanguage}");
            }
            else if (type == PredictionType.Daily)
            {
                // Construct todaysReading_pathTitle from path adjective (backend formats the full title)
                if (targetDict.TryGetValue("todaysReading_pathType", out var pathAdjective) && !string.IsNullOrWhiteSpace(pathAdjective))
                {
                    var pathTitle = targetLanguage switch
                    {
                        "zh" => $"今日之路 - {pathAdjective}之路",
                        "zh-tw" => $"今日之路 - {pathAdjective}之路",
                        "es" => $"Tu Camino Hoy - Un Camino {pathAdjective}",
                        _ => $"Your Path Today - A {pathAdjective} Path"
                    };
                    targetDict["todaysReading_pathTitle"] = pathTitle;
                }
                
                // For Daily, tarot/stone names are already translated by LLM
                // Only inject enum values if the text fields exist
                if (targetDict.TryGetValue("todaysReading_tarotCard_name", out var tarotCardName))
                {
                    var tarotCardEnum = ParseTarotCard(tarotCardName);
                    targetDict["todaysReading_tarotCard_enum"] = ((int)tarotCardEnum).ToString();
                }
                
                if (targetDict.TryGetValue("todaysReading_tarotCard_orientation", out var orientation))
                {
                    var orientationEnum = ParseTarotOrientation(orientation);
                    targetDict["todaysReading_tarotCard_orientation_enum"] = ((int)orientationEnum).ToString();
                }
                
                if (targetDict.TryGetValue("luckyAlignments_luckyStone", out var stoneId))
                {
                    var stoneEnum = ParseCrystalStone(stoneId);
                    targetDict["luckyAlignments_luckyStone_enum"] = ((int)stoneEnum).ToString();
                }
                
                // Add backend-calculated lucky number for target language
                var luckyNumberResult = Services.LuckyNumberService.CalculateLuckyNumber(
                    userInfo.BirthDate,
                    predictionDate,
                    targetLanguage);
                
                targetDict["luckyAlignments_luckyNumber_number"] = luckyNumberResult.NumberWord;
                targetDict["luckyAlignments_luckyNumber_digit"] = luckyNumberResult.Digit.ToString();
                targetDict["luckyAlignments_luckyNumber_description"] = luckyNumberResult.Description;
                targetDict["luckyAlignments_luckyNumber_calculation"] = luckyNumberResult.CalculationFormula;
                
                _logger.LogDebug($"[Lumen][OnDemandTranslation] Re-injected Daily backend enum fields and lucky number for {targetLanguage}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[Lumen][OnDemandTranslation] Error re-injecting backend fields for {targetLanguage}");
        }
    }

    /// <summary>
    /// Get fallback language based on priority: en > zh > zh-tw > es
    /// </summary>
    private string GetFallbackLanguage(Dictionary<string, Dictionary<string, string>> multilingualResults)
    {
        // Priority order
        var priorityOrder = new[] { "en", "zh", "zh-tw", "es" };
        
        foreach (var lang in priorityOrder)
        {
            if (multilingualResults.ContainsKey(lang) && 
                multilingualResults[lang] != null && 
                multilingualResults[lang].Count > 0)
            {
                return lang;
            }
        }
        
        // If none of the priority languages exist, return first available
        return multilingualResults.Keys.FirstOrDefault() ?? "en";
    }

        private string BuildSingleLanguageTranslationPrompt(Dictionary<string, string> sourceContent,
            string sourceLanguage, string targetLanguage, PredictionType type)
    {
        // Validate source content (should not happen as callers check, but defensive)
        if (sourceContent == null || sourceContent.Count == 0)
        {
                _logger.LogError(
                    $"[Lumen][BuildTranslationPrompt] Source content is empty for {type}, sourceLanguage: {sourceLanguage}, targetLanguage: {targetLanguage}");
            return string.Empty; // Return empty prompt to avoid exception in fire-and-forget task
        }
        
        var languageMap = new Dictionary<string, string>
        {
            { "en", "English" },
            { "zh-tw", "繁體中文" },
            { "zh", "简体中文" },
            { "es", "Español" }
        };
        
        var sourceLangName = languageMap.GetValueOrDefault(sourceLanguage, "English");
        var targetLangName = languageMap.GetValueOrDefault(targetLanguage, targetLanguage);
        
        // Convert source content to TSV format
            var sourceTsv = new StringBuilder();
        foreach (var kvp in sourceContent)
        {
            sourceTsv.AppendLine($"{kvp.Key}\t{kvp.Value}");
        }
        
            var translationPrompt =
                $@"You are a professional translator specializing in astrology and divination content.

TASK: Translate the following {type} prediction from {sourceLangName} into {targetLangName}.

⚠️ LANGUAGE GUIDELINE:
- Please translate content into {targetLangName}.
- Avoid mixing languages in the output.
- For Chinese (zh-tw/zh): Use Chinese text (proper names can stay as-is).
- For English/Spanish: Chinese characters are allowed for:
  * Heavenly Stems/Earthly Branches (天干地支): e.g., ""甲子 (Jiǎzǐ)""
  * Chinese Zodiac names if needed: e.g., ""Rat 鼠""

TRANSLATION GUIDELINES:
1. Translate content while keeping the exact same meaning and structure.
2. Keep user names unchanged (e.g., ""Sean"" stays ""Sean"")
   - In possessives: ""Sean's Path"" → ""Sean的道路"" (keep name, translate structure)
3. Keep stems-branch in Chinese characters (with pinyin for non-Chinese languages):
   - pastCycle_period, currentCycle_period, futureCycle_period
   - Chinese: '甲子' (no pinyin needed)
   - English/Spanish: '甲子 (Jiǎzǐ)' (keep pinyin for pronunciation)
4. LuckyNumber format:
   - English: ""Seven (7)"" - word + space + English parentheses ()
   - Spanish: ""Siete (7)"" - word + space + English parentheses ()
   - Chinese: ""七（7）"" - word + NO space + Chinese full-width parentheses （）
5. Maintain natural, fluent expression in {targetLangName}.
6. Keep all field names unchanged.
7. Preserve numbers, dates, and proper nouns.
8. For Chinese translations: Adapt English grammar naturally
   - Remove or adapt articles (""The/A"") as needed (e.g., ""The Star"" → ""星星"")
   - Adjust to natural Chinese word order
9. For array values (separated by |): Translate each item, keep the | separator

OUTPUT FORMAT (TSV - Tab-Separated Values):
- Each field on ONE line: fieldName	translatedValue
- Use TAB character (\\t) as separator
- For arrays: translate items but keep | structure (e.g., ""Walk|Meditate|Read"" → ""散步|冥想|阅读"")
- Avoid line breaks within field values
- Return TSV format only, no markdown or extra text

SOURCE CONTENT ({sourceLangName} - TSV Format):
{sourceTsv}

Output ONLY TSV format with translated values. Keep field names unchanged.
";

        return translationPrompt;
    }

    /// <summary>
    /// Calculate current life phase based on birth date
    /// </summary>
    private string CalculateCurrentPhase(DateOnly birthDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var age = today.Year - birthDate.Year;
        
        // Adjust if birthday hasn't occurred this year
        if (today < birthDate.AddYears(age))
        {
            age--;
        }
        
        if (age <= 20) return "phase1";
        if (age <= 35) return "phase2";
        return "phase3";
    }

    /// <summary>
    /// Parse Lifetime & Weekly AI response
    /// </summary>
        private (Dictionary<string, string>?, Dictionary<string, string>?) ParseLifetimeWeeklyResponse(
            string aiResponse)
    {
        try
        {
            var jsonStart = aiResponse.IndexOf('{');
            var jsonEnd = aiResponse.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonString = aiResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var response = JsonConvert.DeserializeObject<dynamic>(jsonString);
                
                if (response == null)
                {
                    return (null, null);
                }

                // Parse Lifetime
                var lifetimeDict = new Dictionary<string, string>();
                if (response.lifetime != null)
                {
                    lifetimeDict["title"] = response.lifetime.title?.ToString() ?? "";
                    lifetimeDict["description"] = response.lifetime.description?.ToString() ?? "";
                    
                    // Serialize complex objects to JSON strings
                    if (response.lifetime.traits != null)
                    {
                        lifetimeDict["traits"] = JsonConvert.SerializeObject(response.lifetime.traits);
                    }

                    if (response.lifetime.phases != null)
                    {
                        lifetimeDict["phases"] = JsonConvert.SerializeObject(response.lifetime.phases);
                    }
                }

                // Parse Weekly
                var weeklyDict = new Dictionary<string, string>();
                if (response.weekly != null)
                {
                    weeklyDict["health"] = response.weekly.health?.ToString() ?? "0";
                    weeklyDict["money"] = response.weekly.money?.ToString() ?? "0";
                    weeklyDict["career"] = response.weekly.career?.ToString() ?? "0";
                    weeklyDict["romance"] = response.weekly.romance?.ToString() ?? "0";
                    weeklyDict["focus"] = response.weekly.focus?.ToString() ?? "0";
                }

                return (lifetimeDict, weeklyDict);
            }

            return (null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenPredictionGAgent][ParseLifetimeWeeklyResponse] Failed to parse");
            return (null, null);
        }
    }

    /// <summary>
    /// Parse Daily AI response (6 dimensions)
    /// </summary>
    
    /// <summary>
    /// Convert array fields from pipe-separated strings to JSON array strings for frontend
    /// </summary>
    private Dictionary<string, string> ConvertArrayFieldsToJson(Dictionary<string, string> data)
    {
        if (data == null || data.Count == 0)
            return data;
        
        // Define all array field names (using full frontend keys)
        var arrayFieldNames = new HashSet<string>
        {
            // Daily prediction array fields
            "twistOfFate_favorable",
            "twistOfFate_avoid",
            
            // Yearly prediction array fields  
            "divineInfluence_career_bestMoves",
            "divineInfluence_career_avoid",
            "divineInfluence_love_bestMoves",
            "divineInfluence_love_avoid",
                "divineInfluence_wealth_bestMoves", // prosperity_* maps to this
                "divineInfluence_wealth_avoid", // prosperity_* maps to this
                "divineInfluence_health_bestMoves", // wellness_* maps to this
                "divineInfluence_health_avoid" // wellness_* maps to this
            
            // Lifetime prediction has no array fields
        };
        
        var result = new Dictionary<string, string>(data);
        
        foreach (var fieldName in arrayFieldNames)
        {
                if (result.ContainsKey(fieldName) && !string.IsNullOrEmpty(result[fieldName]) &&
                    result[fieldName].Contains('|'))
            {
                // Split pipe-separated string and convert to JSON array
                var items = result[fieldName].Split('|', StringSplitOptions.RemoveEmptyEntries)
                                             .Select(item => item.Trim())
                                             .ToList();
                result[fieldName] = JsonConvert.SerializeObject(items);
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Add quotes to affirmation text based on language
    /// Chinese: 「」, Others: ""
    /// </summary>
    private Dictionary<string, string> AddQuotesToAffirmation(Dictionary<string, string> data, string language)
    {
        if (data == null || data.Count == 0)
            return data;
        
        const string affirmationField = "luckyAlignments_luckySpell_description";
        
        if (!data.ContainsKey(affirmationField) || string.IsNullOrEmpty(data[affirmationField]))
            return data;
        
        var result = new Dictionary<string, string>(data);
        var affirmationText = result[affirmationField];
        
        // Remove existing quotes if any
        affirmationText = affirmationText.Trim().Trim('"', '「', '」', '"', '"');
        
        // Add appropriate quotes based on language
        bool isChinese = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        result[affirmationField] = isChinese 
            ? $"「{affirmationText}」"  // Chinese quotes
            : $"\"{affirmationText}\""; // English quotes
        
        return result;
    }
    
    /// <summary>
    /// Map shortened TSV keys to full field names expected by frontend
    /// </summary>
    private Dictionary<string, string> MapShortKeysToFullKeys(Dictionary<string, string> shortKeyData)
                {
        var keyMapping = new Dictionary<string, string>
        {
            // ===== DAILY PREDICTION MAPPINGS =====
                ["daily_theme_title"] = "dayTitle",

                // Tarot Card (tarot_*) - Semantic keys for prompt
                ["tarot_card_name"] = "todaysReading_tarotCard_name",
                ["tarot_card_essence"] = "todaysReading_tarotCard_represents",
                ["tarot_card_orientation"] = "todaysReading_tarotCard_orientation",
            
            // Path (path_*)
                ["path_adjective"] = "todaysReading_pathType", // Backend will construct path_title
                ["path_greeting"] = "todaysReading_pathDescription",
                ["path_wisdom"] = "todaysReading_pathDescriptionExpanded",

                // Life Areas (reflection_*)
                ["reflection_career"] = "todaysReading_careerAndWork",
                ["reflection_relationships"] = "todaysReading_loveAndRelationships",
                ["reflection_wealth"] = "todaysReading_wealthAndFinance",
                ["reflection_wellbeing"] = "todaysReading_healthAndWellness",
            
            // Takeaway
                ["daily_takeaway"] = "todaysTakeaway",

                // Lucky Number: Now calculated by backend (removed from LLM)
                // See GetCalculatedValuesAsync() for backend calculation

                // Lucky Stone (crystal_*)
                ["crystal_stone_id"] = "luckyAlignments_luckyStone",
                ["crystal_power"] = "luckyAlignments_luckyStone_description",
                ["crystal_usage"] = "luckyAlignments_luckyStone_guidance",

                // Affirmation (affirmation_*) - Replaces 'spell' to avoid filters
                ["affirmation_poetic"] = "luckyAlignments_luckySpell",
                ["affirmation_text"] = "luckyAlignments_luckySpell_description",
                ["affirmation_intent"] = "luckyAlignments_luckySpell_intent",

                // Guidance (guidance_*) - Replaces 'fortune' to avoid filters
                ["guidance_metaphor"] = "twistOfFate_title",
                ["guidance_suggestions"] = "twistOfFate_favorable",
                ["guidance_mindful_of"] = "twistOfFate_avoid",
                ["guidance_tip"] = "twistOfFate_todaysRecommendation",
            
            // ===== YEARLY PREDICTION MAPPINGS =====
            ["astro_overlay"] = "westernAstroOverlay",
            ["theme_title"] = "yearlyTheme_overallTheme",
            ["theme_glance"] = "yearlyTheme_atAGlance",
            ["theme_detail"] = "yearlyTheme_expanded",
            ["career_score"] = "divineInfluence_career_score",
            ["career_tag"] = "divineInfluence_career_tagline",
            ["career_do"] = "divineInfluence_career_bestMoves",
            ["career_avoid"] = "divineInfluence_career_avoid",
            ["career_detail"] = "divineInfluence_career_inANutshell",
            ["love_score"] = "divineInfluence_love_score",
            ["love_tag"] = "divineInfluence_love_tagline",
            ["love_do"] = "divineInfluence_love_bestMoves",
            ["love_avoid"] = "divineInfluence_love_avoid",
            ["love_detail"] = "divineInfluence_love_inANutshell",
            ["prosperity_score"] = "divineInfluence_wealth_score",
            ["prosperity_tag"] = "divineInfluence_wealth_tagline",
            ["prosperity_do"] = "divineInfluence_wealth_bestMoves",
            ["prosperity_avoid"] = "divineInfluence_wealth_avoid",
            ["prosperity_detail"] = "divineInfluence_wealth_inANutshell",
            ["wellness_score"] = "divineInfluence_health_score",
            ["wellness_tag"] = "divineInfluence_health_tagline",
            ["wellness_do"] = "divineInfluence_health_bestMoves",
            ["wellness_avoid"] = "divineInfluence_health_avoid",
            ["wellness_detail"] = "divineInfluence_health_inANutshell",
            ["mantra"] = "embodimentMantra",
            
            // ===== LIFETIME PREDICTION MAPPINGS =====
            ["pillars_id"] = "fourPillars_coreIdentity",
            ["pillars_detail"] = "fourPillars_coreIdentity_expanded",
            // cn_year removed - backend generates this directly
            ["cn_trait1"] = "chineseAstrology_trait1",
            ["cn_trait2"] = "chineseAstrology_trait2",
            ["cn_trait3"] = "chineseAstrology_trait3",
            ["cn_trait4"] = "chineseAstrology_trait4",
            ["whisper"] = "zodiacWhisper",
            ["sun_tag"] = "sunSign_tagline",
            ["sun_arch_name"] = "westernOverview_sunArchetypeName", // Backend will construct sun_arch
            ["sun_desc"] = "westernOverview_sunDescription",
            ["moon_arch_name"] = "westernOverview_moonArchetypeName", // Backend will construct moon_arch
            ["moon_desc"] = "westernOverview_moonDescription",
            ["rising_arch_name"] = "westernOverview_risingArchetypeName", // Backend will construct rising_arch
            ["rising_desc"] = "westernOverview_risingDescription",
            ["combined_essence"] = "westernOverview_combinedEssenceStatement", // LLM generates, backend replaces sign names and moves to combinedEssence
            ["str_intro"] = "strengths_overview",
            ["str1_title"] = "strengths_item1_title",
            ["str1_desc"] = "strengths_item1_description",
            ["str2_title"] = "strengths_item2_title",
            ["str2_desc"] = "strengths_item2_description",
            ["str3_title"] = "strengths_item3_title",
            ["str3_desc"] = "strengths_item3_description",
            ["chal_intro"] = "challenges_overview",
            ["chal1_title"] = "challenges_item1_title",
            ["chal1_desc"] = "challenges_item1_description",
            ["chal2_title"] = "challenges_item2_title",
            ["chal2_desc"] = "challenges_item2_description",
            ["chal3_title"] = "challenges_item3_title",
            ["chal3_desc"] = "challenges_item3_description",
            ["destiny_intro"] = "destiny_overview",
            ["path1_title"] = "destiny_path1_title",
            ["path1_desc"] = "destiny_path1_description",
            ["path2_title"] = "destiny_path2_title",
            ["path2_desc"] = "destiny_path2_description",
            ["path3_title"] = "destiny_path3_title",
            ["path3_desc"] = "destiny_path3_description",
            ["cn_essence"] = "chineseZodiac_essence",
            ["cycle_year_range"] = "zodiacCycle_yearRange",
            ["cycle_name_zh"] = "zodiacCycle_cycleNameChinese",
            ["cycle_name"] = "zodiacCycle_cycleName", // Current language version
            ["cycle_intro"] = "zodiacCycle_overview",
            ["cycle_pt1"] = "zodiacCycle_dayMasterPoint1",
            ["cycle_pt2"] = "zodiacCycle_dayMasterPoint2",
            ["cycle_pt3"] = "zodiacCycle_dayMasterPoint3",
            ["cycle_pt4"] = "zodiacCycle_dayMasterPoint4",
            ["ten_intro"] = "tenYearCycles_description",
            ["past_summary"] = "pastCycle_influenceSummary",
            ["past_detail"] = "pastCycle_meaning",
            ["curr_summary"] = "currentCycle_influenceSummary",
            ["curr_detail"] = "currentCycle_meaning",
            ["future_summary"] = "futureCycle_influenceSummary",
            ["future_detail"] = "futureCycle_meaning",
            ["plot_title"] = "lifePlot_title",
            ["plot_chapter"] = "lifePlot_chapter",
            ["plot_pt1"] = "lifePlot_point1",
            ["plot_pt2"] = "lifePlot_point2",
            ["plot_pt3"] = "lifePlot_point3",
            ["plot_pt4"] = "lifePlot_point4",
            ["act1_title"] = "activationSteps_step1_title",
            ["act1_desc"] = "activationSteps_step1_description",
            ["act2_title"] = "activationSteps_step2_title",
            ["act2_desc"] = "activationSteps_step2_description",
            ["act3_title"] = "activationSteps_step3_title",
            ["act3_desc"] = "activationSteps_step3_description",
            ["act4_title"] = "activationSteps_step4_title",
            ["act4_desc"] = "activationSteps_step4_description",
            ["mantra_title"] = "mantra_title",
            ["mantra_pt1"] = "mantra_point1",
            ["mantra_pt2"] = "mantra_point2",
            ["mantra_pt3"] = "mantra_point3",
        };

        var mappedData = new Dictionary<string, string>();
        
        foreach (var kvp in shortKeyData)
        {
            var key = kvp.Key;
            var value = kvp.Value;
            
            // Map short key to full key if mapping exists, otherwise keep original key
            var fullKey = keyMapping.ContainsKey(key) ? keyMapping[key] : key;
            mappedData[fullKey] = value;
        }
        
        return mappedData;
    }
    
    /// <summary>
    /// Parse TSV (Tab-Separated Values) response from LLM
    /// Format: fieldName	value (one per line)
    /// Arrays: fieldName	item1|item2|item3
    /// </summary>
    private Dictionary<string, string>? ParseTsvResponse(string aiResponse)
    {
        try
        {
            var result = new Dictionary<string, string>();
            
            // Remove markdown code blocks if present
            var cleanResponse = aiResponse.Trim();
            if (cleanResponse.StartsWith("```"))
            {
                var lines = cleanResponse.Split('\n');
                var contentLines = new List<string>();
                bool inCodeBlock = false;
                
                foreach (var line in lines)
                {
                    if (line.Trim().StartsWith("```"))
                    {
                        inCodeBlock = !inCodeBlock;
                        continue;
                    }

                    if (inCodeBlock)
                    {
                        contentLines.Add(line);
                    }
                }
                
                cleanResponse = string.Join("\n", contentLines);
            }
            
            // Parse TSV line by line
            var responseLines = cleanResponse.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in responseLines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine))
                {
                    continue;
                }
                
                // Split by tab character
                var parts = trimmedLine.Split('\t');
                
                if (parts.Length >= 2)
                {
                    var fieldName = parts[0].Trim();
                    // Join remaining parts in case value contains tabs
                    var value = string.Join("\t", parts.Skip(1)).Trim();
                    
                    if (!string.IsNullOrEmpty(fieldName) && !string.IsNullOrEmpty(value))
                    {
                        result[fieldName] = value;
                    }
                }
            }
            
            if (result.Count == 0)
            {
                    _logger.LogWarning(
                        $"[LumenPredictionGAgent][ParseTsvResponse] No valid TSV fields found. Full response:\n{aiResponse}");
                return null;
            }
            
                _logger.LogInformation(
                    $"[LumenPredictionGAgent][ParseTsvResponse] Successfully parsed {result.Count} fields from TSV response");
            
            // Map shortened keys to full field names expected by frontend
            var mappedResult = MapShortKeysToFullKeys(result);
                _logger.LogDebug(
                    $"[LumenPredictionGAgent][ParseTsvResponse] Mapped {result.Count} short keys to {mappedResult.Count} full keys");
            
            // Convert pipe-separated array fields to JSON array strings
            mappedResult = ConvertArrayFieldsToJson(mappedResult);
            
            return mappedResult;
        }
        catch (Exception ex)
        {
                _logger.LogError(ex,
                    "[LumenPredictionGAgent][ParseTsvResponse] TSV parse error. Full response:\n{Response}",
                    aiResponse);
            return null;
        }
    }
    
    /// <summary>
    /// Parse plain text response from LLM
    /// Format: fieldName: value (one per line)
    /// Arrays: field: item1|item2|item3
    /// </summary>
    private Dictionary<string, string>? ParsePlainTextResponse(string aiResponse)
    {
        try
        {
            var result = new Dictionary<string, string>();
            var lines = aiResponse.Trim().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            _logger.LogDebug($"[Lumen][ParsePlainText] Parsing {lines.Length} lines");
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine))
                    continue;
                
                // Find the first colon (to handle values containing colons)
                var colonIndex = trimmedLine.IndexOf(':');
                if (colonIndex == -1)
                {
                    // Log lines without colon for debugging
                        _logger.LogDebug(
                            $"[Lumen][ParsePlainText] Skipping line without colon: {(trimmedLine.Length > 100 ? trimmedLine.Substring(0, 100) + "..." : trimmedLine)}");
                    continue;
                }
                
                var key = trimmedLine.Substring(0, colonIndex).Trim();
                var value = trimmedLine.Substring(colonIndex + 1).Trim();
                
                // Skip empty keys or values
                if (string.IsNullOrWhiteSpace(key))
                {
                    _logger.LogDebug($"[Lumen][ParsePlainText] Skipping line with empty key");
                    continue;
                }
                
                // Handle array fields (favorable/avoid use | separator)
                if (key.Contains("favorable") || key.Contains("avoid"))
            {
                    // Remove any trailing parenthesis explanations like "(5 items)"
                    if (value.Contains('('))
                    {
                        value = value.Substring(0, value.IndexOf('(')).Trim();
                    }
                    
                    // Split by | and serialize as JSON array for consistency with old format
                    var items = value.Split('|')
                                     .Select(item => item.Trim())
                                     .Where(item => !string.IsNullOrWhiteSpace(item))
                                     .ToArray();
                    
                    // Serialize as JSON array string (to match old format)
                    result[key] = JsonConvert.SerializeObject(items);
                    _logger.LogDebug($"[Lumen][ParsePlainText] Parsed array field {key}: {items.Length} items");
                    
                    // Warn if array doesn't have expected count
                    if (items.Length != 5 && (key.Contains("favorable") || key.Contains("avoid")))
                    {
                            _logger.LogWarning(
                                $"[Lumen][ParsePlainText] Array field {key} has {items.Length} items, expected 5");
                    }
                }
                else
                {
                    result[key] = value;
                }
            }
            
                _logger.LogInformation(
                    $"[Lumen][ParsePlainText] Parsed {result.Count} fields from {lines.Length} lines");
            
            // Warn if parsed field count is suspiciously low
            if (result.Count < 5)
            {
                    _logger.LogWarning(
                        $"[Lumen][ParsePlainText] Only parsed {result.Count} fields, which seems low. First 500 chars of response: {(aiResponse.Length > 500 ? aiResponse.Substring(0, 500) : aiResponse)}...");
            }

            return result;
            }
        catch (Exception ex)
            {
            // Enhanced error logging with response preview
                _logger.LogError(ex,
                    "[LumenPredictionGAgent][ParsePlainTextResponse] Failed to parse plain text. First 500 chars: {ResponsePreview}",
                aiResponse.Length > 500 ? aiResponse.Substring(0, 500) : aiResponse);
            _logger.LogError("[LumenPredictionGAgent][ParsePlainTextResponse] Last 200 chars: {ResponseEnd}", 
                aiResponse.Length > 200 ? aiResponse.Substring(aiResponse.Length - 200) : aiResponse);
            return null;
            }
    }

    /// <summary>
    /// Parse daily response from AI (single language only)
    /// Returns (parsed results, null) - second parameter kept for signature compatibility
    /// </summary>
        private (Dictionary<string, string>?, Dictionary<string, Dictionary<string, string>>?) ParseDailyResponse(
            string aiResponse)
    {
        try
            {
            // Prompt requires TSV format, parse as TSV only (no fallback)
            _logger.LogDebug("[LumenPredictionGAgent][ParseDailyResponse] Parsing TSV format (required by prompt)");
            var tsvResult = ParseTsvResponse(aiResponse);
            if (tsvResult != null && tsvResult.Count > 0)
            {
                    _logger.LogInformation(
                        $"[LumenPredictionGAgent][ParseDailyResponse] Successfully parsed {tsvResult.Count} fields from TSV");
                // Return parsed results; multilingualResults will be populated by caller with targetLanguage
                return (tsvResult, null);
            }
            
            // TSV parsing failed - this indicates LLM did not follow prompt instructions
                _logger.LogError(
                    $"[LumenPredictionGAgent][ParseDailyResponse] TSV parse failed. LLM may have returned wrong format. Full response:\n{aiResponse}");
                return (null, null);
            }
        catch (Exception ex)
        {
                _logger.LogError(ex,
                    "[LumenPredictionGAgent][ParseDailyResponse] Exception during TSV parsing. Full response:\n{Response}",
                aiResponse);
            return (null, null);
        }
    }
    
    /// <summary>
    /// Parse lifetime/yearly response from AI (single language only)
    /// Returns (parsed results, null) - second parameter kept for signature compatibility
    /// </summary>
        private (Dictionary<string, string>?, Dictionary<string, Dictionary<string, string>>?) ParseLifetimeResponse(
            string aiResponse)
    {
        try
        {
            // Prompt requires TSV format, parse as TSV only (no fallback)
                _logger.LogDebug(
                    "[LumenPredictionGAgent][ParseLifetimeResponse] Parsing TSV format (required by prompt)");
            var tsvResult = ParseTsvResponse(aiResponse);
            if (tsvResult != null && tsvResult.Count > 0)
                        {
                    _logger.LogInformation(
                        $"[LumenPredictionGAgent][ParseLifetimeResponse] Successfully parsed {tsvResult.Count} fields from TSV");
                return (tsvResult, null);
            }
            
            // TSV parsing failed - this indicates LLM did not follow prompt instructions
                _logger.LogError(
                    $"[LumenPredictionGAgent][ParseLifetimeResponse] TSV parse failed. LLM may have returned wrong format. Full response:\n{aiResponse}");
            return (null, null);
        }
        catch (Exception ex)
        {
                _logger.LogError(ex,
                    "[LumenPredictionGAgent][ParseLifetimeResponse] Exception during TSV parsing. Full response:\n{Response}",
                aiResponse);
            return (null, null);
        }
    }
    
    /// <summary>
    /// Helper method to flatten nested dictionary structure
    /// </summary>
    private Dictionary<string, string> FlattenDictionary(Dictionary<string, string> source)
    {
        // This is a simplified version - you may need to enhance based on actual structure
        // For now, it just returns as-is since the actual parsing would convert nested objects to JSON strings
        return source;
    }
    
    /// <summary>
    /// Flatten nested JSON into Dictionary<field, value>
    /// Uses underscore to join nested keys (e.g., "tarotCard_name")
    /// Returns a completely flat dictionary
    /// </summary>
    private Dictionary<string, string>? FlattenNestedJsonToFlat(string json)
    {
        try
        {
            var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            if (data == null) return null;
            
            var flatResult = new Dictionary<string, string>();
            
            // Recursively flatten the entire object into a single-level dictionary
            FlattenObject(data, "", flatResult);
            
                _logger.LogDebug("[LumenPredictionGAgent][FlattenNestedJsonToFlat] Flattened {Count} fields",
                    flatResult.Count);
            
            return flatResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenPredictionGAgent][FlattenNestedJsonToFlat] Failed to flatten JSON");
            return null;
        }
    }
    
    /// <summary>
    /// Recursively flatten an object into a flat dictionary with underscore-separated keys
    /// </summary>
    private void FlattenObject(object obj, string prefix, Dictionary<string, string> result)
    {
        if (obj == null)
        {
            if (!string.IsNullOrEmpty(prefix))
            {
                result[prefix] = "";
            }

            return;
        }
        
        // Handle different value types
        switch (obj)
        {
            case string strValue:
                // Simple string - store directly
                if (!string.IsNullOrEmpty(prefix))
                {
                    result[prefix] = strValue;
                }

                break;
                
            case Dictionary<string, object> dict:
                // Dictionary - recurse into each key-value pair
                foreach (var kvp in dict)
                {
                    var newKey = string.IsNullOrEmpty(prefix) 
                        ? kvp.Key 
                        : $"{prefix}_{kvp.Key}";
                    FlattenObject(kvp.Value, newKey, result);
                }

                break;
                
                case JObject jObject:
                // Nested object - recurse into it
                foreach (var property in jObject.Properties())
                {
                    var newKey = string.IsNullOrEmpty(prefix) 
                        ? property.Name 
                        : $"{prefix}_{property.Name}";
                    FlattenObject(property.Value, newKey, result);
                }

                break;
                
                case JArray jArray:
                // Array - store as JSON string for now (could expand to array_0, array_1, etc.)
                if (!string.IsNullOrEmpty(prefix))
                {
                        result[prefix] = jArray.ToString(Formatting.None);
                }

                break;
                
                case JValue jValue:
                // Primitive value (number, boolean, etc.)
                if (!string.IsNullOrEmpty(prefix))
                {
                    result[prefix] = jValue.ToString();
                }

                break;
                
            default:
                // For other types, try to serialize as JSON and recurse
                try
                {
                    var json = JsonConvert.SerializeObject(obj);
                    var parsed = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    
                    if (parsed != null)
                    {
                        foreach (var kvp in parsed)
                        {
                            var newKey = string.IsNullOrEmpty(prefix) 
                                ? kvp.Key 
                                : $"{prefix}_{kvp.Key}";
                            FlattenObject(kvp.Value, newKey, result);
                        }
                    }
                    else
                    {
                        // Can't parse - store as JSON string
                        result[prefix] = json;
                    }
                }
                catch
                {
                    // Last resort - convert to string
                    result[prefix] = obj.ToString() ?? "";
                }

                break;
        }
    }
    
    /// <summary>
    /// Extract enum values from prediction results
    /// </summary>

    // Key mapping for Daily prediction prompt (Prompt Key -> Frontend Key)
    private static readonly Dictionary<string, string> DailyKeyMapping = new()
    {
        // Theme
        { "daily_theme_title", "dayTitle" },

        // Tarot (Use explicit 'tarot_' prefix in prompt for clarity)
        {
            "tarot_card_name", "todaysReading_tarotCard_name"
        }, // Note: The prompt generator injects parsing logic for this later
        {
            "tarot_card_essence", "todaysReading_tarotCard_essence"
        }, // Actual frontend key might be different, checking parsing logic...
        // WAIT: looking at line 1244 "todaysReading_tarotCard_name" seems to be the key used in injection logic
        // But let's look at the OLD prompt (line 1868): key was "card_name"
        // So parse logic likely outputs "card_name", and then injection logic (or frontend) expects...
        // Let's re-read parse logic. 
        // Ah, line 1244: if (parsedResults.TryGetValue("todaysReading_tarotCard_name", out var tarotCardName))
        // This implies the DICTIONARY has "todaysReading_tarotCard_name".
        // BUT the OLD PROMPT (line 1868) output "card_name".
        // So where did "card_name" become "todaysReading_tarotCard_name"?
        // MAYBE FlattenNestedJsonToFlat? No, we are in TSV.
        // Let's look at ParseDailyResponse -> ParseTsvResponse. It just splits by tab.
        // So if prompt says "card_name", the dict has "card_name".
        // Then Line 1244 tries to get "todaysReading_tarotCard_name".
        // THIS IS A DISCREPANCY in the current code I read vs what I see.
        // Let's look at the OLD prompt again carefully.
        // Line 1868: card_name\t...
        // Line 1244: if (parsedResults.TryGetValue("todaysReading_tarotCard_name"...
        // Unless there is a mapping I missed, or the code I read has a bug, or "todaysReading_" is added somewhere.
        // Let's Assume the FRONTEND expects what the OLD PROMPT output: "card_name".
        // AND the Injection logic MIGHT be looking for "todaysReading_tarotCard_name" which implies there WAS a mapping or I missed it.
        // Let's search for "todaysReading_" in the file.
    };

    private TarotCardEnum ParseTarotCard(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName)) return TarotCardEnum.Unknown;
        
        // Try Chinese mapping first (including common aliases)
        var chineseMapping = new Dictionary<string, TarotCardEnum>(StringComparer.OrdinalIgnoreCase)
        {
            // Major Arcana
            { "愚者", TarotCardEnum.TheFool }, { "魔术师", TarotCardEnum.TheMagician },
            { "女祭司", TarotCardEnum.TheHighPriestess },
            { "皇后", TarotCardEnum.TheEmpress }, { "皇帝", TarotCardEnum.TheEmperor },
            { "教皇", TarotCardEnum.TheHierophant },
            { "恋人", TarotCardEnum.TheLovers }, { "战车", TarotCardEnum.TheChariot }, { "力量", TarotCardEnum.Strength },
            { "隐士", TarotCardEnum.TheHermit }, { "命运之轮", TarotCardEnum.WheelOfFortune },
            { "正义", TarotCardEnum.Justice },
            { "倒吊人", TarotCardEnum.TheHangedMan }, { "倒吊者", TarotCardEnum.TheHangedMan }, // Alias: 倒吊者
            { "倒悬者", TarotCardEnum.TheHangedMan }, // Another alias
            { "死神", TarotCardEnum.Death }, { "死亡", TarotCardEnum.Death }, // Alias: 死亡
            { "节制", TarotCardEnum.Temperance },
            { "恶魔", TarotCardEnum.TheDevil }, { "魔鬼", TarotCardEnum.TheDevil }, // Alias: 魔鬼
            { "高塔", TarotCardEnum.TheTower }, { "塔", TarotCardEnum.TheTower }, // Alias: 塔
            { "星星", TarotCardEnum.TheStar }, { "星辰", TarotCardEnum.TheStar }, // Alias: 星辰
            { "月亮", TarotCardEnum.TheMoon }, { "月", TarotCardEnum.TheMoon }, // Alias: 月
            { "太阳", TarotCardEnum.TheSun }, { "日", TarotCardEnum.TheSun }, // Alias: 日
            { "审判", TarotCardEnum.Judgement }, { "审讯", TarotCardEnum.Judgement }, // Alias: 审讯
            { "世界", TarotCardEnum.TheWorld },
            // Wands (only showing some common ones, expand as needed)
            { "权杖王牌", TarotCardEnum.AceOfWands }, { "权杖二", TarotCardEnum.TwoOfWands },
            { "权杖三", TarotCardEnum.ThreeOfWands },
            { "权杖四", TarotCardEnum.FourOfWands }, { "权杖五", TarotCardEnum.FiveOfWands },
            { "权杖六", TarotCardEnum.SixOfWands },
            { "权杖七", TarotCardEnum.SevenOfWands }, { "权杖八", TarotCardEnum.EightOfWands },
            { "权杖九", TarotCardEnum.NineOfWands },
            { "权杖十", TarotCardEnum.TenOfWands }, { "权杖侍从", TarotCardEnum.PageOfWands },
            { "权杖骑士", TarotCardEnum.KnightOfWands },
            { "权杖王后", TarotCardEnum.QueenOfWands }, { "权杖国王", TarotCardEnum.KingOfWands },
            // Cups
            { "圣杯王牌", TarotCardEnum.AceOfCups }, { "圣杯二", TarotCardEnum.TwoOfCups },
            { "圣杯三", TarotCardEnum.ThreeOfCups },
            { "圣杯四", TarotCardEnum.FourOfCups }, { "圣杯五", TarotCardEnum.FiveOfCups },
            { "圣杯六", TarotCardEnum.SixOfCups },
            { "圣杯七", TarotCardEnum.SevenOfCups }, { "圣杯八", TarotCardEnum.EightOfCups },
            { "圣杯九", TarotCardEnum.NineOfCups },
            { "圣杯十", TarotCardEnum.TenOfCups }, { "圣杯侍从", TarotCardEnum.PageOfCups },
            { "圣杯骑士", TarotCardEnum.KnightOfCups },
            { "圣杯王后", TarotCardEnum.QueenOfCups }, { "圣杯国王", TarotCardEnum.KingOfCups },
            // Swords
            { "宝剑王牌", TarotCardEnum.AceOfSwords }, { "宝剑二", TarotCardEnum.TwoOfSwords },
            { "宝剑三", TarotCardEnum.ThreeOfSwords },
            { "宝剑四", TarotCardEnum.FourOfSwords }, { "宝剑五", TarotCardEnum.FiveOfSwords },
            { "宝剑六", TarotCardEnum.SixOfSwords },
            { "宝剑七", TarotCardEnum.SevenOfSwords }, { "宝剑八", TarotCardEnum.EightOfSwords },
            { "宝剑九", TarotCardEnum.NineOfSwords },
            { "宝剑十", TarotCardEnum.TenOfSwords }, { "宝剑侍从", TarotCardEnum.PageOfSwords },
            { "宝剑骑士", TarotCardEnum.KnightOfSwords },
            { "宝剑王后", TarotCardEnum.QueenOfSwords }, { "宝剑国王", TarotCardEnum.KingOfSwords },
            // Pentacles
            { "星币王牌", TarotCardEnum.AceOfPentacles }, { "星币二", TarotCardEnum.TwoOfPentacles },
            { "星币三", TarotCardEnum.ThreeOfPentacles },
            { "星币四", TarotCardEnum.FourOfPentacles }, { "星币五", TarotCardEnum.FiveOfPentacles },
            { "星币六", TarotCardEnum.SixOfPentacles },
            { "星币七", TarotCardEnum.SevenOfPentacles }, { "星币八", TarotCardEnum.EightOfPentacles },
            { "星币九", TarotCardEnum.NineOfPentacles },
            { "星币十", TarotCardEnum.TenOfPentacles }, { "星币侍从", TarotCardEnum.PageOfPentacles },
            { "星币骑士", TarotCardEnum.KnightOfPentacles },
            { "星币王后", TarotCardEnum.QueenOfPentacles }, { "星币国王", TarotCardEnum.KingOfPentacles }
        };
        
        var trimmedName = cardName.Trim();
        
        if (chineseMapping.TryGetValue(trimmedName, out var chineseResult))
        {
            return chineseResult;
        }
        
        // Try common Chinese aliases with normalization (for court cards)
        // Note: Only replace for suit cards (权杖/圣杯/宝剑/星币), not Major Arcana
        var normalizedChinese = trimmedName;
        
        if (trimmedName.Contains("权杖") || trimmedName.Contains("圣杯") || 
            trimmedName.Contains("宝剑") || trimmedName.Contains("星币"))
        {
            normalizedChinese = trimmedName
                .Replace("侍者", "侍从")
                .Replace("随从", "侍从")
                .Replace("武士", "骑士")
                .Replace("女王", "王后");
        }
        
        if (normalizedChinese != trimmedName && chineseMapping.TryGetValue(normalizedChinese, out var normalizedResult))
        {
            _logger.LogDebug(
                $"[LumenPredictionGAgent][ParseTarotCard] Normalized '{trimmedName}' to '{normalizedChinese}'");
            return normalizedResult;
        }
        
        // Try English parsing with normalization
        // Handle both "The Fool" and "TheFool", "Ace of Wands" and "AceOfWands"
        var normalized = cardName.Trim()
            .Replace(" of ", "Of") // "Ace of Wands" → "AceOfWands"
            .Replace(" ", ""); // Remove all spaces: "The Fool" → "TheFool"
        
        if (Enum.TryParse<TarotCardEnum>(normalized, true, out var result))
        {
            return result;
        }
        
        _logger.LogWarning(
            $"[LumenPredictionGAgent][ParseTarotCard] Unknown tarot card: {cardName}, normalized: {normalized}");
        return TarotCardEnum.Unknown;
    }
    
    /// <summary>
    /// Parse tarot card orientation to enum
    /// </summary>
    private TarotOrientationEnum ParseTarotOrientation(string orientation)
    {
        if (string.IsNullOrWhiteSpace(orientation)) return TarotOrientationEnum.Unknown;
        
        var normalized = orientation.Trim().ToLowerInvariant();
        
        return normalized switch
        {
            // English
            "upright" => TarotOrientationEnum.Upright,
            "reversed" => TarotOrientationEnum.Reversed,
            // Chinese
            "正位" => TarotOrientationEnum.Upright,
            "逆位" => TarotOrientationEnum.Reversed,
            // Spanish
            "derecha" => TarotOrientationEnum.Upright,
            "invertida" => TarotOrientationEnum.Reversed,
            _ => TarotOrientationEnum.Unknown
        };
    }
    
    /// <summary>
    /// Add Chinese translations for English-only fields (tarot card, stone, orientation)
    /// This is called after parsing to replace English values with Chinese translations
    /// </summary>
    private void AddChineseTranslations(Dictionary<string, string> parsedResults, string targetLanguage)
    {
        if (targetLanguage != "zh" && targetLanguage != "zh-tw")
        {
            return; // Only add translations for Chinese users
        }
        
        // Translate tarot card name - REPLACE the original field value
        if (parsedResults.TryGetValue("todaysReading_tarotCard_name", out var cardName) &&
            !string.IsNullOrWhiteSpace(cardName))
        {
            if (TarotCardTranslations.TryGetValue(cardName.Trim(), out var cardTranslation))
            {
                var translatedName = targetLanguage == "zh" ? cardTranslation.zh : cardTranslation.zhTw;
                parsedResults["todaysReading_tarotCard_name"] = translatedName; // Replace original value
                _logger.LogDebug(
                    $"[LumenPredictionGAgent][AddChineseTranslations] Translated tarot card: {cardName} → {translatedName}");
            }
            else
            {
                _logger.LogWarning(
                    $"[LumenPredictionGAgent][AddChineseTranslations] No translation found for tarot card: {cardName}");
            }
        }
        
        // Translate tarot card orientation - REPLACE the original field value
        if (parsedResults.TryGetValue("todaysReading_tarotCard_orientation", out var orientation) &&
            !string.IsNullOrWhiteSpace(orientation))
        {
            if (OrientationTranslations.TryGetValue(orientation.Trim(), out var orientationTranslation))
            {
                var translatedOrientation =
                    targetLanguage == "zh" ? orientationTranslation.zh : orientationTranslation.zhTw;
                parsedResults["todaysReading_tarotCard_orientation"] = translatedOrientation; // Replace original value
                _logger.LogDebug(
                    $"[LumenPredictionGAgent][AddChineseTranslations] Translated orientation: {orientation} → {translatedOrientation}");
            }
            else
            {
                _logger.LogWarning(
                    $"[LumenPredictionGAgent][AddChineseTranslations] No translation found for orientation: {orientation}");
            }
        }
        
        // Translate lucky stone - REPLACE the original field value
        if (parsedResults.TryGetValue("luckyAlignments_luckyStone", out var stone) && !string.IsNullOrWhiteSpace(stone))
        {
            if (StoneTranslations.TryGetValue(stone.Trim(), out var stoneTranslation))
            {
                var translatedStone = targetLanguage == "zh" ? stoneTranslation.zh : stoneTranslation.zhTw;
                parsedResults["luckyAlignments_luckyStone"] = translatedStone; // Replace original value
                _logger.LogDebug(
                    $"[LumenPredictionGAgent][AddChineseTranslations] Translated stone: {stone} → {translatedStone}");
            }
            else
            {
                _logger.LogWarning(
                    $"[LumenPredictionGAgent][AddChineseTranslations] No translation found for stone: {stone}");
            }
        }
    }
    
    /// <summary>
    /// Parse zodiac sign name to enum
    /// </summary>
    private ZodiacSignEnum ParseZodiacSign(string signName)
    {
        if (string.IsNullOrWhiteSpace(signName)) return ZodiacSignEnum.Unknown;
        
        var normalized = signName.Trim();
        
        if (Enum.TryParse<ZodiacSignEnum>(normalized, true, out var result))
        {
            return result;
        }
        
        _logger.LogWarning($"[LumenPredictionGAgent][ParseZodiacSign] Unknown zodiac sign: {signName}");
        return ZodiacSignEnum.Unknown;
    }
    
    /// <summary>
    /// Parse chinese zodiac animal to enum
    /// </summary>
    private ChineseZodiacEnum ParseChineseZodiac(string animalName)
    {
        if (string.IsNullOrWhiteSpace(animalName)) return ChineseZodiacEnum.Unknown;
        
        // Remove "The " prefix for matching
        var normalized = animalName.Replace("The ", "").Trim();
        
        if (Enum.TryParse<ChineseZodiacEnum>(normalized, true, out var result))
        {
            return result;
        }
        
        _logger.LogWarning($"[LumenPredictionGAgent][ParseChineseZodiac] Unknown chinese zodiac: {animalName}");
        return ChineseZodiacEnum.Unknown;
    }
    
    /// <summary>
    /// Parse crystal stone name to enum
    /// </summary>
    private CrystalStoneEnum ParseCrystalStone(string stoneName)
    {
        if (string.IsNullOrWhiteSpace(stoneName)) return CrystalStoneEnum.Unknown;
        
        // Remove spaces and special chars for matching
        var normalized = stoneName.Replace(" ", "").Replace("'", "").Replace("-", "").Trim();
        
        // Handle special cases including multilingual support
        var specialCases = new Dictionary<string, CrystalStoneEnum>(StringComparer.OrdinalIgnoreCase)
        {
            // English special cases
            { "RoseQuartz", CrystalStoneEnum.RoseQuartz },
            { "ClearQuartz", CrystalStoneEnum.ClearQuartz },
            { "SmokyQuartz", CrystalStoneEnum.SmokyQuartz },
            { "BlackTourmaline", CrystalStoneEnum.BlackTourmaline },
            { "TigersEye", CrystalStoneEnum.TigersEye },
            { "Tiger'sEye", CrystalStoneEnum.TigersEye },
            { "TigerEye", CrystalStoneEnum.TigersEye },
            { "LapisLazuli", CrystalStoneEnum.Lapis },
            { "Lapis", CrystalStoneEnum.Lapis },
            // Chinese mappings
            { "紫水晶", CrystalStoneEnum.Amethyst }, { "粉晶", CrystalStoneEnum.RoseQuartz },
            { "芙蓉石", CrystalStoneEnum.RoseQuartz },
            { "白水晶", CrystalStoneEnum.ClearQuartz }, { "黄水晶", CrystalStoneEnum.Citrine },
            { "茶晶", CrystalStoneEnum.SmokyQuartz },
            { "黑碧玺", CrystalStoneEnum.BlackTourmaline }, { "透石膏", CrystalStoneEnum.Selenite },
            { "拉长石", CrystalStoneEnum.Labradorite },
            { "月光石", CrystalStoneEnum.Moonstone }, { "红玛瑙", CrystalStoneEnum.Carnelian },
            { "虎眼石", CrystalStoneEnum.TigersEye },
            { "玉", CrystalStoneEnum.Jade }, { "绿松石", CrystalStoneEnum.Turquoise }, { "青金石", CrystalStoneEnum.Lapis },
            { "海蓝宝", CrystalStoneEnum.Aquamarine }, { "祖母绿", CrystalStoneEnum.Emerald },
            { "红宝石", CrystalStoneEnum.Ruby },
            { "蓝宝石", CrystalStoneEnum.Sapphire }, { "石榴石", CrystalStoneEnum.Garnet }, { "蛋白石", CrystalStoneEnum.Opal },
            { "黄玉", CrystalStoneEnum.Topaz }, { "橄榄石", CrystalStoneEnum.Peridot }, { "黑曜石", CrystalStoneEnum.Obsidian },
            { "孔雀石", CrystalStoneEnum.Malachite }, { "赤铁矿", CrystalStoneEnum.Hematite },
            { "黄铁矿", CrystalStoneEnum.Pyrite },
            { "萤石", CrystalStoneEnum.Fluorite }, { "东陵玉", CrystalStoneEnum.Aventurine },
            { "碧玉", CrystalStoneEnum.Jasper },
            { "玛瑙", CrystalStoneEnum.Agate }, { "血石", CrystalStoneEnum.Bloodstone }, { "黑玛瑙", CrystalStoneEnum.Onyx },
            { "菱镁矿", CrystalStoneEnum.Howlite }, { "天河石", CrystalStoneEnum.Amazonite }
        };
        
        if (specialCases.TryGetValue(normalized, out var specialResult))
        {
            return specialResult;
        }
        
        if (Enum.TryParse<CrystalStoneEnum>(normalized, true, out var result))
        {
            return result;
        }
        
        _logger.LogWarning($"[LumenPredictionGAgent][ParseCrystalStone] Unknown crystal stone: {stoneName}");
        return CrystalStoneEnum.Unknown;
    }
    
    /// <summary>
    /// Inject Four Pillars (Ba Zi) data into prediction dictionary with language-specific formatting
    /// </summary>
    private void InjectFourPillarsData(Dictionary<string, string> prediction, FourPillarsInfo fourPillars,
        string language)
    {
        // Year Pillar - Standardized field naming: separate stem and branch attributes
        prediction["fourPillars_yearPillar"] = fourPillars.YearPillar.GetFormattedString(language);
        // Stem attributes
        prediction["fourPillars_yearPillar_stemChinese"] = fourPillars.YearPillar.StemChinese;
        prediction["fourPillars_yearPillar_stemPinyin"] = fourPillars.YearPillar.StemPinyin;
        prediction["fourPillars_yearPillar_stemYinYang"] = TranslateYinYang(fourPillars.YearPillar.YinYang, language);
        prediction["fourPillars_yearPillar_stemElement"] = TranslateElement(fourPillars.YearPillar.Element, language);
        prediction["fourPillars_yearPillar_stemDirection"] =
            TranslateDirection(fourPillars.YearPillar.Direction, language);
        // Branch attributes
        prediction["fourPillars_yearPillar_branchChinese"] = fourPillars.YearPillar.BranchChinese;
        prediction["fourPillars_yearPillar_branchPinyin"] = fourPillars.YearPillar.BranchPinyin;
        prediction["fourPillars_yearPillar_branchYinYang"] =
            TranslateYinYang(fourPillars.YearPillar.BranchYinYang, language);
        prediction["fourPillars_yearPillar_branchElement"] =
            TranslateElement(fourPillars.YearPillar.BranchElement, language);
        prediction["fourPillars_yearPillar_branchZodiac"] =
            TranslateZodiac(fourPillars.YearPillar.BranchZodiac, language);
        
        // Month Pillar
        prediction["fourPillars_monthPillar"] = fourPillars.MonthPillar.GetFormattedString(language);
        // Stem attributes
        prediction["fourPillars_monthPillar_stemChinese"] = fourPillars.MonthPillar.StemChinese;
        prediction["fourPillars_monthPillar_stemPinyin"] = fourPillars.MonthPillar.StemPinyin;
        prediction["fourPillars_monthPillar_stemYinYang"] = TranslateYinYang(fourPillars.MonthPillar.YinYang, language);
        prediction["fourPillars_monthPillar_stemElement"] = TranslateElement(fourPillars.MonthPillar.Element, language);
        prediction["fourPillars_monthPillar_stemDirection"] =
            TranslateDirection(fourPillars.MonthPillar.Direction, language);
        // Branch attributes
        prediction["fourPillars_monthPillar_branchChinese"] = fourPillars.MonthPillar.BranchChinese;
        prediction["fourPillars_monthPillar_branchPinyin"] = fourPillars.MonthPillar.BranchPinyin;
        prediction["fourPillars_monthPillar_branchYinYang"] =
            TranslateYinYang(fourPillars.MonthPillar.BranchYinYang, language);
        prediction["fourPillars_monthPillar_branchElement"] =
            TranslateElement(fourPillars.MonthPillar.BranchElement, language);
        prediction["fourPillars_monthPillar_branchZodiac"] =
            TranslateZodiac(fourPillars.MonthPillar.BranchZodiac, language);
        
        // Day Pillar
        prediction["fourPillars_dayPillar"] = fourPillars.DayPillar.GetFormattedString(language);
        // Stem attributes
        prediction["fourPillars_dayPillar_stemChinese"] = fourPillars.DayPillar.StemChinese;
        prediction["fourPillars_dayPillar_stemPinyin"] = fourPillars.DayPillar.StemPinyin;
        prediction["fourPillars_dayPillar_stemYinYang"] = TranslateYinYang(fourPillars.DayPillar.YinYang, language);
        prediction["fourPillars_dayPillar_stemElement"] = TranslateElement(fourPillars.DayPillar.Element, language);
        prediction["fourPillars_dayPillar_stemDirection"] =
            TranslateDirection(fourPillars.DayPillar.Direction, language);
        // Branch attributes
        prediction["fourPillars_dayPillar_branchChinese"] = fourPillars.DayPillar.BranchChinese;
        prediction["fourPillars_dayPillar_branchPinyin"] = fourPillars.DayPillar.BranchPinyin;
        prediction["fourPillars_dayPillar_branchYinYang"] =
            TranslateYinYang(fourPillars.DayPillar.BranchYinYang, language);
        prediction["fourPillars_dayPillar_branchElement"] =
            TranslateElement(fourPillars.DayPillar.BranchElement, language);
        prediction["fourPillars_dayPillar_branchZodiac"] =
            TranslateZodiac(fourPillars.DayPillar.BranchZodiac, language);
        
        // Hour Pillar (always include fields, empty if birth time not provided)
        if (fourPillars.HourPillar != null)
        {
            prediction["fourPillars_hourPillar"] = fourPillars.HourPillar.GetFormattedString(language);
            // Stem attributes
            prediction["fourPillars_hourPillar_stemChinese"] = fourPillars.HourPillar.StemChinese;
            prediction["fourPillars_hourPillar_stemPinyin"] = fourPillars.HourPillar.StemPinyin;
            prediction["fourPillars_hourPillar_stemYinYang"] =
                TranslateYinYang(fourPillars.HourPillar.YinYang, language);
            prediction["fourPillars_hourPillar_stemElement"] =
                TranslateElement(fourPillars.HourPillar.Element, language);
            prediction["fourPillars_hourPillar_stemDirection"] =
                TranslateDirection(fourPillars.HourPillar.Direction, language);
            // Branch attributes
            prediction["fourPillars_hourPillar_branchChinese"] = fourPillars.HourPillar.BranchChinese;
            prediction["fourPillars_hourPillar_branchPinyin"] = fourPillars.HourPillar.BranchPinyin;
            prediction["fourPillars_hourPillar_branchYinYang"] =
                TranslateYinYang(fourPillars.HourPillar.BranchYinYang, language);
            prediction["fourPillars_hourPillar_branchElement"] =
                TranslateElement(fourPillars.HourPillar.BranchElement, language);
            prediction["fourPillars_hourPillar_branchZodiac"] =
                TranslateZodiac(fourPillars.HourPillar.BranchZodiac, language);
        }
        else
        {
            // Birth time not provided - fill with empty strings
            prediction["fourPillars_hourPillar"] = "";
            // Stem attributes
            prediction["fourPillars_hourPillar_stemChinese"] = "";
            prediction["fourPillars_hourPillar_stemPinyin"] = "";
            prediction["fourPillars_hourPillar_stemYinYang"] = "";
            prediction["fourPillars_hourPillar_stemElement"] = "";
            prediction["fourPillars_hourPillar_stemDirection"] = "";
            // Branch attributes
            prediction["fourPillars_hourPillar_branchChinese"] = "";
            prediction["fourPillars_hourPillar_branchPinyin"] = "";
            prediction["fourPillars_hourPillar_branchYinYang"] = "";
            prediction["fourPillars_hourPillar_branchElement"] = "";
            prediction["fourPillars_hourPillar_branchZodiac"] = "";
        }
    }
    
    private string TranslateYinYang(string yinYang, string language) => language switch
    {
        "zh-tw" or "zh" => yinYang == "Yang" ? "陽" : "陰",
        "es" => yinYang == "Yang" ? "Yang" : "Yin",
        _ => yinYang // English default
    };
    
    private string TranslateElement(string element, string language) => (element, language) switch
    {
        ("Wood", "zh-tw" or "zh") => "木",
        ("Fire", "zh-tw" or "zh") => "火",
        ("Earth", "zh-tw" or "zh") => "土",
        ("Metal", "zh-tw" or "zh") => "金",
        ("Water", "zh-tw" or "zh") => "水",
        ("Wood", "es") => "Madera",
        ("Fire", "es") => "Fuego",
        ("Earth", "es") => "Tierra",
        ("Metal", "es") => "Metal",
        ("Water", "es") => "Agua",
        _ => element // English default
    };
    
    private string TranslateDirection(string direction, string language) => (direction, language) switch
    {
        ("East 1", "zh-tw" or "zh") => "東一",
        ("East 2", "zh-tw" or "zh") => "東二",
        ("South 1", "zh-tw" or "zh") => "南一",
        ("South 2", "zh-tw" or "zh") => "南二",
        ("West 1", "zh-tw" or "zh") => "西一",
        ("West 2", "zh-tw" or "zh") => "西二",
        ("North 1", "zh-tw" or "zh") => "北一",
        ("North 2", "zh-tw" or "zh") => "北二",
        ("Centre", "zh-tw" or "zh") => "中",
        ("East 1", "es") => "Este 1",
        ("East 2", "es") => "Este 2",
        ("South 1", "es") => "Sur 1",
        ("South 2", "es") => "Sur 2",
        ("West 1", "es") => "Oeste 1",
        ("West 2", "es") => "Oeste 2",
        ("North 1", "es") => "Norte 1",
        ("North 2", "es") => "Norte 2",
        ("Centre", "es") => "Centro",
        _ => direction // English default
    };
    
    /// <summary>
    /// Build zodiacInfluence string based on language
    /// Format: "{birthYearZodiac} native in {yearlyYearZodiac} year → {taishuiRelationship}"
    /// </summary>
    private string BuildZodiacInfluence(string birthYearZodiac, string yearlyYearZodiac, string taishuiRelationship,
        string language)
    {
        // Parse taishui to extract Chinese, Pinyin, and English parts
        // Example input: "相害 (Xiang Hai - Harm)"
        var taishuiParts = ParseTaishuiRelationship(taishuiRelationship);
        
        if (language == "zh" || language == "zh-tw")
        {
            // Chinese: "木蛇生人遇木龙年 → 相害"
            var birthZodiacChinese = TranslateZodiacWithElementToChinese(birthYearZodiac);
            var yearlyZodiacChinese = TranslateZodiacWithElementToChinese(yearlyYearZodiac);
            return $"{birthZodiacChinese}生人遇{yearlyZodiacChinese}年 → {taishuiParts.chinese}";
        }
        else if (language == "es")
        {
            // Spanish: "Serpiente de Madera nativo en año del Dragón de Madera → Daño (相害 Xiang Hai)"
            var birthZodiacSpanish = TranslateZodiacWithElementToSpanish(birthYearZodiac);
            var yearlyZodiacSpanish = TranslateZodiacWithElementToSpanish(yearlyYearZodiac);
            return
                $"{birthZodiacSpanish} nativo en año del {yearlyZodiacSpanish} → {taishuiParts.spanish} ({taishuiParts.chinese} {taishuiParts.pinyin})";
        }
        else
        {
            // English: "Wood Snake native in Wood Dragon year → Harm (相害 Xiang Hai)"
            return
                $"{birthYearZodiac} native in {yearlyYearZodiac} year → {taishuiParts.english} ({taishuiParts.chinese} {taishuiParts.pinyin})";
        }
    }
    
    /// <summary>
    /// Build path title for daily predictions with localized template
    /// Example: "John's Path Today - A Courageous Path" (en) or "王凯文今日之路 - 勇敢之路" (zh)
    /// </summary>
    private string BuildPathTitle(string displayName, string pathType, string language)
    {
        if (language == "zh" || language == "zh-tw")
        {
            return $"{displayName}今日之路 - {pathType}之路";
        }
        else if (language == "es")
        {
            return $"Camino de {displayName} Hoy - Un Camino {pathType}";
        }
        else
        {
            return $"{displayName}'s Path Today - A {pathType} Path";
        }
    }
    
    /// <summary>
    /// Build archetype string for lifetime predictions with localized template
    /// Example: "Sun in Cancer - The Nurturing Protector" (en) or "巨蟹座太阳 - 心灵守护者" (zh)
    /// Removes duplicate articles ("The", "El", etc.) if LLM already included them
    /// </summary>
    private string BuildArchetypeString(string celestialBody, string zodiacSign, string archetypeName, string language)
    {
        // Clean archetype name: remove leading articles if LLM already added them
        var cleanArchName = archetypeName.Trim();
        
        if (language == "zh" || language == "zh-tw")
        {
            var bodyName = celestialBody switch
            {
                "Sun" => "太阳",
                "Moon" => "月亮",
                "Rising" => "上升",
                _ => celestialBody
            };
            return $"{zodiacSign}{bodyName} - {cleanArchName}";
        }
        else if (language == "es")
        {
            var bodyName = celestialBody switch
            {
                "Sun" => "Sol",
                "Moon" => "Luna",
                "Rising" => "Ascendente",
                _ => celestialBody
            };
            // Remove "El " or "el " if already present
            if (cleanArchName.StartsWith("El ", StringComparison.OrdinalIgnoreCase))
            {
                cleanArchName = cleanArchName.Substring(3).Trim();
            }
            return $"{bodyName} en {zodiacSign} - El {cleanArchName}";
        }
        else
        {
            // Remove "The " or "the " if already present
            if (cleanArchName.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
            {
                cleanArchName = cleanArchName.Substring(4).Trim();
            }
            return $"{celestialBody} in {zodiacSign} - The {cleanArchName}";
        }
    }
    
    /// <summary>
    /// Parse taishui relationship string to extract Chinese, Pinyin, and English
    /// Example: "相害 (Xiang Hai - Harm)" → (chinese: "相害", pinyin: "Xiang Hai", english: "Harm")
    /// </summary>
    private (string chinese, string pinyin, string english, string spanish) ParseTaishuiRelationship(string taishui)
    {
        var match = Regex.Match(taishui, @"^(.*?)\s*\((.*?)\s*-\s*(.*?)\)$");
        if (match.Success)
        {
            var chinese = match.Groups[1].Value.Trim();
            var pinyin = match.Groups[2].Value.Trim();
            var english = match.Groups[3].Value.Trim();
            var spanish = TranslateTaishuiToSpanish(english);
            return (chinese, pinyin, english, spanish);
        }
        
        // Fallback if parsing fails
        return (taishui, "", taishui, taishui);
    }
    
    /// <summary>
    /// Translate "Element + Zodiac" format to Chinese
    /// Example: "Wood Snake" → "木蛇"
    /// </summary>
    private string TranslateZodiacWithElementToChinese(string zodiacWithElement)
    {
        var parts = zodiacWithElement.Split(' ', 2);
        if (parts.Length != 2) return zodiacWithElement;
        
        var element = parts[0];
        var zodiac = parts[1];
        
        var elementChinese = element switch
        {
            "Wood" => "木",
            "Fire" => "火",
            "Earth" => "土",
            "Metal" => "金",
            "Water" => "水",
            _ => element
        };
        
        var zodiacChinese = zodiac switch
        {
            "Rat" => "鼠",
            "Ox" => "牛",
            "Tiger" => "虎",
            "Rabbit" => "兔",
            "Dragon" => "龙",
            "Snake" => "蛇",
            "Horse" => "马",
            "Goat" => "羊",
            "Monkey" => "猴",
            "Rooster" => "鸡",
            "Dog" => "狗",
            "Pig" => "猪",
            _ => zodiac
        };
        
        return $"{elementChinese}{zodiacChinese}";
    }
    
    /// <summary>
    /// Translate "Element + Zodiac" format to Spanish
    /// Example: "Wood Snake" → "Serpiente de Madera"
    /// </summary>
    private string TranslateZodiacWithElementToSpanish(string zodiacWithElement)
    {
        var parts = zodiacWithElement.Split(' ', 2);
        if (parts.Length != 2) return zodiacWithElement;
        
        var element = parts[0];
        var zodiac = parts[1];
        
        var elementSpanish = element switch
        {
            "Wood" => "Madera",
            "Fire" => "Fuego",
            "Earth" => "Tierra",
            "Metal" => "Metal",
            "Water" => "Agua",
            _ => element
        };
        
        var zodiacSpanish = zodiac switch
        {
            "Rat" => "Rata",
            "Ox" => "Buey",
            "Tiger" => "Tigre",
            "Rabbit" => "Conejo",
            "Dragon" => "Dragón",
            "Snake" => "Serpiente",
            "Horse" => "Caballo",
            "Goat" => "Cabra",
            "Monkey" => "Mono",
            "Rooster" => "Gallo",
            "Dog" => "Perro",
            "Pig" => "Cerdo",
            _ => zodiac
        };
        
        return $"{zodiacSpanish} de {elementSpanish}";
    }
    
    /// <summary>
    /// Translate Taishui relationship English term to Spanish
    /// </summary>
    private string TranslateTaishuiToSpanish(string english) => english switch
    {
        "Birth Year" => "Año de Nacimiento",
        "Harm" => "Daño",
        "Neutral" => "Neutral",
        "Triple Harmony" => "Triple Armonía",
        "Six Harmony" => "Seis Armonía",
        "Clash" => "Choque",
        "Break" => "Ruptura",
        _ => english
    };
    
    /// <summary>
    /// Translate taishui relationship based on language
    /// Input format: "相害 (Xiang Hai - Harm)"
    /// </summary>
    private string TranslateTaishuiRelationship(string taishui, string language)
    {
        var parts = ParseTaishuiRelationship(taishui);
        
        if (language == "zh" || language == "zh-tw")
        {
            // Chinese: only Chinese text
            return parts.chinese;
        }
        else if (language == "es")
        {
            // Spanish: "Daño (相害 Xiang Hai)"
            return $"{parts.spanish} ({parts.chinese} {parts.pinyin})";
        }
        else
        {
            // English: "Harm (相害 Xiang Hai)"
            return $"{parts.english} ({parts.chinese} {parts.pinyin})";
        }
    }
    
    /// <summary>
    /// Translate sun sign based on language
    /// </summary>
    private string TranslateSunSign(string sunSign, string language) => (sunSign, language) switch
    {
        // Chinese translations
        ("Aries", "zh" or "zh-tw") => "白羊座",
        ("Taurus", "zh" or "zh-tw") => "金牛座",
        ("Gemini", "zh" or "zh-tw") => "双子座",
        ("Cancer", "zh" or "zh-tw") => "巨蟹座",
        ("Leo", "zh" or "zh-tw") => "狮子座",
        ("Virgo", "zh" or "zh-tw") => "处女座",
        ("Libra", "zh" or "zh-tw") => "天秤座",
        ("Scorpio", "zh" or "zh-tw") => "天蝎座",
        ("Sagittarius", "zh" or "zh-tw") => "射手座",
        ("Capricorn", "zh" or "zh-tw") => "摩羯座",
        ("Aquarius", "zh" or "zh-tw") => "水瓶座",
        ("Pisces", "zh" or "zh-tw") => "双鱼座",
        
        // Spanish translations
        ("Aries", "es") => "Aries",
        ("Taurus", "es") => "Tauro",
        ("Gemini", "es") => "Géminis",
        ("Cancer", "es") => "Cáncer",
        ("Leo", "es") => "Leo",
        ("Virgo", "es") => "Virgo",
        ("Libra", "es") => "Libra",
        ("Scorpio", "es") => "Escorpio",
        ("Sagittarius", "es") => "Sagitario",
        ("Capricorn", "es") => "Capricornio",
        ("Aquarius", "es") => "Acuario",
        ("Pisces", "es") => "Piscis",
        
        // English default
        _ => sunSign
    };
    
    /// <summary>
    /// Translate Chinese zodiac with element based on language
    /// Input: "Wood Pig"
    /// </summary>
    private string TranslateChineseZodiacAnimal(string zodiacWithElement, string language)
    {
        if (language == "zh" || language == "zh-tw")
        {
            return TranslateZodiacWithElementToChinese(zodiacWithElement);
        }
        else if (language == "es")
        {
            return TranslateZodiacWithElementToSpanish(zodiacWithElement);
        }
        else
        {
            return zodiacWithElement; // English
        }
    }
    
    /// <summary>
    /// Translate cycle age range based on language
    /// Input: "Age 20-29 (1990-1999)" or "Age -10--1 (2015-2024)"
    /// </summary>
    private string TranslateCycleAgeRange(string ageRange, string language)
    {
        // Extract numbers using regex: support negative ages like "Age -10--1 (2015-2024)"
        var match = Regex.Match(ageRange, @"Age (-?\d+)-(-?\d+) \((\d+)-(\d+)\)");
        if (!match.Success) return ageRange;
        
        var startAge = match.Groups[1].Value;
        var endAge = match.Groups[2].Value;
        var startYear = match.Groups[3].Value;
        var endYear = match.Groups[4].Value;
        
        if (language == "zh" || language == "zh-tw")
        {
            return $"{startAge}-{endAge}岁 ({startYear}-{endYear})";
        }
        else if (language == "es")
        {
            return $"Edad {startAge}-{endAge} ({startYear}-{endYear})";
        }
        else
        {
            return ageRange; // English
        }
    }
    
    /// <summary>
    /// Translate cycle period based on language
    /// Input: "甲子 (Jiǎzǐ) · Wood Rat"
    /// </summary>
    private string TranslateCyclePeriod(string period, string language)
    {
        // Extract parts: "甲子 (Jiǎzǐ) · Wood Rat"
        var parts = period.Split(" · ", 2);
        if (parts.Length != 2) return period;
        
        var stemsBranch = parts[0]; // "甲子 (Jiǎzǐ)"
        var zodiacWithElement = parts[1]; // "Wood Rat"
        
        // For Chinese languages, remove pinyin; keep it for other languages
        if (language == "zh" || language == "zh-tw")
        {
            // Remove pinyin from stems-branch: "甲子 (Jiǎzǐ)" -> "甲子"
            var pinyinStartIndex = stemsBranch.IndexOf(" (");
            if (pinyinStartIndex > 0)
            {
                stemsBranch = stemsBranch.Substring(0, pinyinStartIndex);
            }
        }
        
        var translatedZodiac = TranslateChineseZodiacAnimal(zodiacWithElement, language);
        
        return $"{stemsBranch} · {translatedZodiac}";
    }
    
    private string TranslateZodiac(string zodiac, string language) => (zodiac, language) switch
    {
        ("Rat", "zh-tw" or "zh") => "鼠",
        ("Ox", "zh-tw" or "zh") => "牛",
        ("Tiger", "zh-tw" or "zh") => "虎",
        ("Rabbit", "zh-tw" or "zh") => "兔",
        ("Dragon", "zh-tw" or "zh") => "龍",
        ("Snake", "zh-tw" or "zh") => "蛇",
        ("Horse", "zh-tw" or "zh") => "馬",
        ("Goat", "zh-tw" or "zh") => "羊",
        ("Monkey", "zh-tw" or "zh") => "猴",
        ("Rooster", "zh-tw" or "zh") => "雞",
        ("Dog", "zh-tw" or "zh") => "狗",
        ("Pig", "zh-tw" or "zh") => "豬",
        ("Rat", "es") => "Rata",
        ("Ox", "es") => "Buey",
        ("Tiger", "es") => "Tigre",
        ("Rabbit", "es") => "Conejo",
        ("Dragon", "es") => "Dragón",
        ("Snake", "es") => "Serpiente",
        ("Horse", "es") => "Caballo",
        ("Goat", "es") => "Cabra",
        ("Monkey", "es") => "Mono",
        ("Rooster", "es") => "Gallo",
        ("Dog", "es") => "Perro",
        ("Pig", "es") => "Cerdo",
        _ => zodiac // English default
    };
    
    /// <summary>
    /// Translate Chinese Zodiac title (e.g., "The Pig") to different languages
    /// </summary>
    private string TranslateZodiacTitle(string birthYearAnimal, string language)
    {
        // Extract animal name (e.g., "Wood Pig" -> "Pig")
        var animalName = birthYearAnimal.Split(' ').Last();
        
        return language switch
        {
            "zh-tw" or "zh" => animalName switch
            {
                "Rat" => "鼠",
                "Ox" => "牛",
                "Tiger" => "虎",
                "Rabbit" => "兔",
                "Dragon" => "龍",
                "Snake" => "蛇",
                "Horse" => "馬",
                "Goat" => "羊",
                "Monkey" => "猴",
                "Rooster" => "雞",
                "Dog" => "狗",
                "Pig" => "豬",
                _ => animalName
            },
            "es" => animalName switch
            {
                "Rat" => "La Rata",
                "Ox" => "El Buey",
                "Tiger" => "El Tigre",
                "Rabbit" => "El Conejo",
                "Dragon" => "El Dragón",
                "Snake" => "La Serpiente",
                "Horse" => "El Caballo",
                "Goat" => "La Cabra",
                "Monkey" => "El Mono",
                "Rooster" => "El Gallo",
                "Dog" => "El Perro",
                "Pig" => "El Cerdo",
                _ => $"El {animalName}"
            },
            _ => $"The {animalName}" // English default
        };
    }
    
    #region Daily Reminder Management
    
    /// <summary>
    /// Orleans reminder callback - triggered daily at UTC 00:00
    /// </summary>
    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName != DEFAULT_DAILY_REMINDER_NAME)
            return;
            
        try
        {
            _logger.LogInformation($"[Lumen][DailyReminder] {State.UserId} Reminder triggered at {DateTime.UtcNow}");
            
            // Check if reminder TargetId matches current version
            var currentReminderTargetId = _options?.ReminderTargetId ?? CURRENT_REMINDER_TARGET_ID;
            if (State.DailyReminderTargetId != currentReminderTargetId)
            {
                _logger.LogInformation(
                    $"[Lumen][DailyReminder] {State.UserId} TargetId mismatch (State: {State.DailyReminderTargetId}, Current: {currentReminderTargetId}), unregistering old reminder");
                await UnregisterDailyReminderAsync();
                return; // User will re-register with new logic when active
            }
            
            // Check if this is a Daily prediction grain
            if (State.Type != PredictionType.Daily)
            {
                _logger.LogWarning(
                    $"[Lumen][DailyReminder] {State.UserId} Reminder triggered on non-Daily grain (Type: {State.Type}), unregistering");
                await UnregisterDailyReminderAsync();
                return;
            }
            
            // Check activity: if user hasn't been active in 3 days, stop reminder
            var daysSinceActive = (DateTime.UtcNow - State.LastActiveDate).TotalDays;
            if (daysSinceActive > 3)
            {
                _logger.LogInformation(
                    $"[Lumen][DailyReminder] {State.UserId} User inactive for {daysSinceActive:F1} days, stopping reminder");
                await UnregisterDailyReminderAsync();
                return;
            }
            
            // Get user's timezone to check if it's a new day in user's local time
            var profileGrainId = CommonHelper.StringToGuid(State.UserId);
            var profileGAgent = _clusterClient.GetGrain<ILumenUserProfileGAgent>(profileGrainId);
            var profileResult = await profileGAgent.GetRawStateAsync();
            var userTimeZoneId = profileResult?.CurrentTimeZone ?? "UTC";
            
            // Calculate today in user's local timezone
            DateOnly userToday;
            try
            {
                var userTimeZone = TimeZoneInfo.FindSystemTimeZoneById(userTimeZoneId);
                var userNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, userTimeZone);
                userToday = DateOnly.FromDateTime(userNow);
            }
            catch (TimeZoneNotFoundException)
            {
                _logger.LogWarning($"[Lumen][DailyReminder] {State.UserId} Invalid timezone: {userTimeZoneId}, using UTC");
                userToday = DateOnly.FromDateTime(DateTime.UtcNow);
            }
            
            // Check if already generated today (avoid duplicate generation)
            if (State.LastGeneratedDate == userToday)
            {
                _logger.LogInformation(
                    $"[Lumen][DailyReminder] {State.UserId} Daily prediction already generated today ({userToday} in {userTimeZoneId}), skipping");
                return;
            }
            
            // Get user profile with language setting
            var userProfileGrainId = CommonHelper.StringToGuid(State.UserId);
            var userProfileGAgent = _clusterClient.GetGrain<ILumenUserProfileGAgent>(userProfileGrainId);
            var fullProfileResult = await userProfileGAgent.GetUserProfileAsync(userProfileGrainId, "en"); // Use en for API call
            
            if (fullProfileResult == null || !fullProfileResult.Success || fullProfileResult.UserProfile == null)
            {
                _logger.LogWarning(
                    $"[Lumen][DailyReminder] {State.UserId} User profile not found, cannot generate daily prediction");
                return;
            }
            
            // Use user's current language from profile
            var targetLanguage = fullProfileResult.UserProfile.CurrentLanguage ?? "en";
            
            _logger.LogInformation(
                $"[Lumen][DailyReminder] {State.UserId} Generating daily prediction for {userToday} (TimeZone: {userTimeZoneId}, Language: {targetLanguage})");
            
            // Convert LumenUserProfileDto to LumenUserDto
            var userDto = new LumenUserDto
            {
                UserId = fullProfileResult.UserProfile.UserId,
                FirstName = fullProfileResult.UserProfile.FullName.Split(' ').FirstOrDefault() ?? "",
                LastName = fullProfileResult.UserProfile.FullName.Contains(' ')
                    ? string.Join(" ", fullProfileResult.UserProfile.FullName.Split(' ').Skip(1))
                    : "",
                Gender = fullProfileResult.UserProfile.Gender,
                BirthDate = fullProfileResult.UserProfile.BirthDate,
                BirthTime = fullProfileResult.UserProfile.BirthTime,
                BirthCity = fullProfileResult.UserProfile.BirthCity,
                LatLong = fullProfileResult.UserProfile.LatLong,
                CalendarType = fullProfileResult.UserProfile.CalendarType,
                CreatedAt = fullProfileResult.UserProfile.CreatedAt,
                CurrentResidence = fullProfileResult.UserProfile.CurrentResidence,
                UpdatedAt = fullProfileResult.UserProfile.UpdatedAt,
                Occupation = fullProfileResult.UserProfile.Occupation
            };
            
            // Trigger generation (this will update LastGeneratedDate)
            _ = GeneratePredictionInBackgroundAsync(userDto, userToday, PredictionType.Daily, targetLanguage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[Lumen][DailyReminder] {State.UserId} Error in daily reminder execution");
        }
    }
    
    /// <summary>
    /// Register daily reminder (called when user becomes active)
    /// Uses user's timezone to calculate reminder time (default: 00:01 in user's local time)
    /// </summary>
    private async Task RegisterDailyReminderAsync()
    {
        // Only register for Daily predictions
        if (State.Type != PredictionType.Daily)
            return;
        
        // Check if daily auto-generation is enabled in options
        var enableAutoGeneration = _options?.EnableDailyAutoGeneration ?? false;
        if (!enableAutoGeneration)
        {
            _logger.LogDebug($"[Lumen][DailyReminder] {State.UserId} Daily auto-generation is disabled in options, skipping reminder registration");
            return;
        }
            
        // Check if already registered
        var existingReminder = await this.GetReminder(DEFAULT_DAILY_REMINDER_NAME);
        if (existingReminder != null)
        {
            _logger.LogDebug($"[Lumen][DailyReminder] {State.UserId} Reminder already registered");
            return;
        }
        
        // Get user's timezone from profile
        var profileGrainId = CommonHelper.StringToGuid(State.UserId);
        var profileGAgent = _clusterClient.GetGrain<ILumenUserProfileGAgent>(profileGrainId);
        var profileResult = await profileGAgent.GetRawStateAsync();
        var userTimeZoneId = profileResult?.CurrentTimeZone ?? "UTC";
        
        // Record current TargetId for version control
        var currentReminderTargetId = _options?.ReminderTargetId ?? CURRENT_REMINDER_TARGET_ID;
        State.DailyReminderTargetId = currentReminderTargetId;
        
        // Calculate next trigger time in user's timezone (00:01 local time)
        var (dueTime, nextTriggerUtc) = CalculateNextReminderTime(userTimeZoneId);
        
        await this.RegisterOrUpdateReminder(
            DEFAULT_DAILY_REMINDER_NAME,
            dueTime,
            TimeSpan.FromHours(24)
        );
        
        State.IsDailyReminderEnabled = true;
        _logger.LogInformation(
            $"[Lumen][DailyReminder] {State.UserId} Reminder registered with TargetId: {State.DailyReminderTargetId}, " +
            $"TimeZone: {userTimeZoneId}, next execution at {nextTriggerUtc:yyyy-MM-dd HH:mm:ss} UTC (00:01 local time)");
    }
    
    /// <summary>
    /// Calculate next reminder trigger time based on user's timezone
    /// Returns: (dueTime, nextTriggerUtc)
    /// </summary>
    private (TimeSpan dueTime, DateTime nextTriggerUtc) CalculateNextReminderTime(string timeZoneId)
    {
        try
        {
            var userTimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var nowUtc = DateTime.UtcNow;
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, userTimeZone);
            
            // Calculate next 00:01 in user's local time
            DateTime nextLocal0001;
            if (nowLocal.TimeOfDay < new TimeSpan(0, 1, 0)) // Before 00:01 today
            {
                nextLocal0001 = nowLocal.Date.AddMinutes(1); // Today at 00:01
            }
            else // After 00:01 today
            {
                nextLocal0001 = nowLocal.Date.AddDays(1).AddMinutes(1); // Tomorrow at 00:01
            }
            
            // Convert back to UTC
            var nextTriggerUtc = TimeZoneInfo.ConvertTimeToUtc(nextLocal0001, userTimeZone);
            var dueTime = nextTriggerUtc - nowUtc;
            
            // Ensure dueTime is positive
            if (dueTime <= TimeSpan.Zero)
            {
                dueTime = TimeSpan.FromMinutes(1); // Fallback: trigger in 1 minute
            }
            
            return (dueTime, nextTriggerUtc);
        }
        catch (TimeZoneNotFoundException ex)
        {
            _logger.LogWarning(ex, "[Lumen][DailyReminder] Invalid timezone ID: {TimeZoneId}, falling back to UTC", timeZoneId);
            
            // Fallback to UTC 00:01
            var nowUtc = DateTime.UtcNow;
            var nextMidnightUtc = nowUtc.Date.AddDays(1).AddMinutes(1); // Tomorrow 00:01 UTC
            var dueTime = nextMidnightUtc - nowUtc;
            
            return (dueTime, nextMidnightUtc);
        }
    }
    
    /// <summary>
    /// Update daily reminder with new timezone (triggered when user updates timezone)
    /// </summary>
    public async Task UpdateTimeZoneReminderAsync(string timeZoneId)
    {
        try
        {
            _logger.LogInformation($"[Lumen][DailyReminder] {State.UserId} Updating timezone to: {timeZoneId}");
            
            // Only update for Daily predictions
            if (State.Type != PredictionType.Daily)
            {
                _logger.LogDebug($"[Lumen][DailyReminder] {State.UserId} Not a Daily grain (Type: {State.Type}), skipping reminder update");
                return;
            }
            
            // Check if daily auto-generation is enabled
            var enableAutoGeneration = _options?.EnableDailyAutoGeneration ?? false;
            if (!enableAutoGeneration)
            {
                _logger.LogDebug($"[Lumen][DailyReminder] {State.UserId} Daily auto-generation is disabled, skipping reminder update");
                return;
            }
            
            // Unregister existing reminder
            await UnregisterDailyReminderAsync();
            
            // Re-register with new timezone
            await RegisterDailyReminderAsync();
            
            _logger.LogInformation($"[Lumen][DailyReminder] {State.UserId} Reminder updated successfully for timezone: {timeZoneId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[Lumen][DailyReminder] {State.UserId} Error updating timezone reminder");
        }
    }
    
    /// <summary>
    /// Unregister daily reminder (called when user becomes inactive or manually disabled)
    /// </summary>
    private async Task UnregisterDailyReminderAsync()
    {
        var existingReminder = await this.GetReminder(DEFAULT_DAILY_REMINDER_NAME);
        if (existingReminder != null)
        {
            await this.UnregisterReminder(existingReminder);
            _logger.LogInformation($"[Lumen][DailyReminder] {State.UserId} Reminder unregistered");
        }
        
        State.IsDailyReminderEnabled = false;
    }
    
    /// <summary>
    /// Update user activity and ensure reminder is registered
    /// </summary>
    private async Task UpdateActivityAndEnsureReminderAsync()
    {
        // Only for Daily predictions
        if (State.Type != PredictionType.Daily)
            return;
            
        var now = DateTime.UtcNow;
        var wasInactive = (now - State.LastActiveDate).TotalDays > 3;
        
        State.LastActiveDate = now;
        
        // If user was inactive and is now active again, register reminder
        if (wasInactive || !State.IsDailyReminderEnabled)
        {
            await RegisterDailyReminderAsync();
        }
    }
    
    /// <summary>
    /// Clear current prediction data (for user deletion or profile update)
    /// This will trigger regeneration on next access
    /// </summary>
    public async Task ClearCurrentPredictionAsync()
    {
        try
        {
            _logger.LogDebug(
                "[LumenPredictionGAgent][ClearCurrentPredictionAsync] Clearing prediction data for: {GrainId}",
                this.GetPrimaryKey());

            // Raise event to clear prediction state
            RaiseEvent(new PredictionClearedEvent
            {
                ClearedAt = DateTime.UtcNow
            });

            // Confirm events to persist state changes
            await ConfirmEvents();

            _logger.LogInformation(
                "[LumenPredictionGAgent][ClearCurrentPredictionAsync] Prediction data cleared successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenPredictionGAgent][ClearCurrentPredictionAsync] Error clearing prediction data");
            throw;
        }
    }
    
    /// <summary>
    /// Get all backend-calculated values for a user
    /// Returns a dictionary of calculated astrological and zodiac data
    /// </summary>
    public async Task<Dictionary<string, string>> GetCalculatedValuesAsync(LumenUserDto userInfo,
        string userLanguage = "en")
    {
        try
        {
            _logger.LogInformation(
                $"[LumenPredictionGAgent][GetCalculatedValuesAsync] Calculating values for user {userInfo.UserId}, language: {userLanguage}");
            
            var results = new Dictionary<string, string>();
            
            // Calculate current date/time values
            var currentYear = DateTime.UtcNow.Year;
            var birthYear = userInfo.BirthDate.Year;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            
            // TIMEZONE HANDLING
            // IMPORTANT: Chinese Four Pillars (BaZi) MUST use LOCAL time, not UTC!
            // Use LOCAL birth date and time (do NOT convert to UTC for BaZi)
            var calcBirthDate = userInfo.BirthDate;
            var calcBirthTime = userInfo.BirthTime;

            // For reference: log timezone info if available
            if (!string.IsNullOrWhiteSpace(userInfo.LatLong))
            {
                try
                {
                    var parts = userInfo.LatLong.Split(',', StringSplitOptions.TrimEntries);
                    if (parts.Length == 2 && 
                        double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) && 
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
                    {
                        var localDateTime = userInfo.BirthDate.ToDateTime(userInfo.BirthTime ?? TimeOnly.MinValue);
                        var (utcDateTime, offset, tzId) = LumenTimezoneHelper.GetUtcTimeFromLocal(localDateTime, lat, lon);
                        
                        _logger.LogInformation($"[LumenPredictionGAgent][GetCalculatedValuesAsync] Using LOCAL time for BaZi: {localDateTime} [{tzId}, UTC{offset}], UTC would be: {utcDateTime}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[LumenPredictionGAgent][GetCalculatedValuesAsync] Failed to get timezone info");
                }
            }
            
            // ========== WESTERN ASTROLOGY ==========
            string sunSign = LumenCalculator.CalculateZodiacSign(calcBirthDate);
            results["sunSign_name"] = TranslateSunSign(sunSign, userLanguage);
            results["sunSign_enum"] = ((int)LumenCalculator.ParseZodiacSignEnum(sunSign)).ToString();
            
            // Calculate Moon and Rising signs if birth time and location are available
            string? moonSign = null;
            string? risingSign = null;
            
            // Diagnostic logging
            _logger.LogInformation(
                $"[LumenPredictionGAgent][GetCalculatedValuesAsync] Moon/Rising calculation check - BirthTime: {userInfo.BirthTime}, BirthTime.HasValue: {userInfo.BirthTime.HasValue}, LatLong: '{userInfo.LatLong}', LatLong IsNullOrWhiteSpace: {string.IsNullOrWhiteSpace(userInfo.LatLong)}");
            
            if (userInfo.BirthTime.HasValue && !string.IsNullOrWhiteSpace(userInfo.LatLong))
            {
                try
                {
                    var parts = userInfo.LatLong.Split(',', StringSplitOptions.TrimEntries);
                    _logger.LogInformation(
                        $"[LumenPredictionGAgent][GetCalculatedValuesAsync] Parsing LatLong - Parts count: {parts.Length}, Part[0]: '{parts.ElementAtOrDefault(0)}', Part[1]: '{parts.ElementAtOrDefault(1)}'");
                    
                    if (parts.Length == 2 && 
                        double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture,
                            out double latitude) &&
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture,
                            out double longitude))
                    {
                        _logger.LogInformation(
                            $"[LumenPredictionGAgent][GetCalculatedValuesAsync] Starting Western Astrology calculation at ({latitude}, {longitude}) using Corrected UTC: {calcBirthDate} {calcBirthTime}");
                        var (_, calculatedMoonSign, calculatedRisingSign) = CalculateSigns(
                            calcBirthDate,
                            calcBirthTime.Value,
                            latitude,
                            longitude);
                        
                        moonSign = calculatedMoonSign;
                        risingSign = calculatedRisingSign;
                        
                        _logger.LogInformation(
                            $"[LumenPredictionGAgent][GetCalculatedValuesAsync] Calculated Moon: {moonSign}, Rising: {risingSign}");
                    }
                    else
                    {
                        _logger.LogWarning(
                            $"[LumenPredictionGAgent][GetCalculatedValuesAsync] Invalid latlong format or parse failed: '{userInfo.LatLong}'");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        $"[LumenPredictionGAgent][GetCalculatedValuesAsync] Failed to calculate Moon/Rising signs");
                }
            }
            else
            {
                _logger.LogInformation(
                    $"[LumenPredictionGAgent][GetCalculatedValuesAsync] Skipping Moon/Rising calculation - BirthTime or LatLong not provided");
            }
            
            // Use sunSign as fallback if moon/rising not calculated
            moonSign = moonSign ?? sunSign;
            risingSign = risingSign ?? sunSign;
            
            results["moonSign_name"] = TranslateSunSign(moonSign, userLanguage);
            results["risingSign_name"] = TranslateSunSign(risingSign, userLanguage);
            
            // ========== CHINESE ASTROLOGY ==========
            var birthYearZodiac = LumenCalculator.GetChineseZodiacWithElement(birthYear);
            var birthYearAnimal = LumenCalculator.CalculateChineseZodiac(birthYear);
            var birthYearElement = LumenCalculator.CalculateChineseElement(birthYear);
            
            results["chineseZodiac_animal"] = TranslateChineseZodiacAnimal(birthYearZodiac, userLanguage);
            results["chineseZodiac_enum"] = ((int)LumenCalculator.ParseChineseZodiacEnum(birthYearAnimal)).ToString();
            results["chineseZodiac_title"] = TranslateZodiacTitle(birthYearAnimal, userLanguage);
            results["birthYear_zodiac"] = TranslateChineseZodiacAnimal(birthYearZodiac, userLanguage);
            results["birthYear_animal"] = TranslateZodiacTitle(birthYearAnimal, userLanguage);
            results["birthYear_element"] = TranslateElement(birthYearElement, userLanguage);
            
            // Birth Year Stems
            var birthYearStems = LumenCalculator.CalculateStemsAndBranches(birthYear);
            results["birthYear_stems"] = birthYearStems;
            
            // Current Year Zodiac
            var currentYearZodiac = LumenCalculator.GetChineseZodiacWithElement(currentYear);
            var currentYearAnimal = LumenCalculator.CalculateChineseZodiac(currentYear);
            var currentYearElement = LumenCalculator.CalculateChineseElement(currentYear);
            
            results["currentYear"] = currentYear.ToString();
            results["currentYear_zodiac"] = TranslateChineseZodiacAnimal(currentYearZodiac, userLanguage);
            results["currentYear_animal"] = TranslateZodiacTitle(currentYearAnimal, userLanguage);
            results["currentYear_element"] = TranslateElement(currentYearElement, userLanguage);
            
            // Current Year Stems (using birth year to match BaZi year pillar)
            var birthYearStemsComponents = LumenCalculator.GetStemsAndBranchesComponents(birthYear);
            results["currentYear_stems"] = LumenCalculator.CalculateStemsAndBranches(birthYear);
            results["currentYear_stemChinese"] = birthYearStemsComponents.stemChinese;
            results["currentYear_stemPinyin"] = birthYearStemsComponents.stemPinyin;
            results["currentYear_branchChinese"] = birthYearStemsComponents.branchChinese;
            results["currentYear_branchPinyin"] = birthYearStemsComponents.branchPinyin;
            
            // Add chineseAstrology_ prefixed fields (matching prediction response format)
            results["chineseAstrology_currentYear"] = TranslateChineseZodiacAnimal(birthYearZodiac, userLanguage);
            results["chineseAstrology_currentYearStem"] = birthYearStemsComponents.stemChinese;
            results["chineseAstrology_currentYearStemPinyin"] = birthYearStemsComponents.stemPinyin;
            results["chineseAstrology_currentYearBranch"] = birthYearStemsComponents.branchChinese;
            results["chineseAstrology_currentYearBranchPinyin"] = birthYearStemsComponents.branchPinyin;
            
            // Taishui Relationship
            var taishuiRelationship = LumenCalculator.CalculateTaishuiRelationship(birthYear, currentYear);
            results["taishui_relationship"] = taishuiRelationship;
            results["taishui_translated"] = TranslateTaishuiRelationship(taishuiRelationship, userLanguage);
            
            // Zodiac Influence
            results["zodiacInfluence"] =
                BuildZodiacInfluence(birthYearZodiac, currentYearZodiac, taishuiRelationship, userLanguage);
            
            // ========== LIFE CYCLES ==========
            var currentAge = LumenCalculator.CalculateAge(userInfo.BirthDate);
            results["currentAge"] = currentAge.ToString();
            
            // 10-year Cycles
            var pastCycle = LumenCalculator.CalculateTenYearCycle(birthYear, -1);
            var currentCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 0);
            var futureCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 1);
            
            results["pastCycle_ageRange"] = TranslateCycleAgeRange(pastCycle.AgeRange, userLanguage);
            results["pastCycle_period"] = TranslateCyclePeriod(pastCycle.Period, userLanguage);
            results["currentCycle_ageRange"] = TranslateCycleAgeRange(currentCycle.AgeRange, userLanguage);
            results["currentCycle_period"] = TranslateCyclePeriod(currentCycle.Period, userLanguage);
            results["futureCycle_ageRange"] = TranslateCycleAgeRange(futureCycle.AgeRange, userLanguage);
            results["futureCycle_period"] = TranslateCyclePeriod(futureCycle.Period, userLanguage);
            
            // Current Phase (for Lifetime)
            var currentPhase = CalculateCurrentPhase(userInfo.BirthDate);
            results["currentPhase"] = currentPhase.ToString();
            
            // ========== FOUR PILLARS (BA ZI) ==========
            var fourPillars = LumenCalculator.CalculateFourPillars(calcBirthDate, calcBirthTime);
            // Use same detailed structure as Lifetime prediction
            InjectFourPillarsData(results, fourPillars, userLanguage);
            
            // ========== LUCKY NUMBER (NUMEROLOGY) ==========
            // Calculate using backend service (no LLM involved)
            var luckyNumberResult = Services.LuckyNumberService.CalculateLuckyNumber(
                userInfo.BirthDate, 
                today, 
                userLanguage);
            
            results["luckyAlignments_luckyNumber_number"] = luckyNumberResult.NumberWord;
            results["luckyAlignments_luckyNumber_digit"] = luckyNumberResult.Digit.ToString();
            results["luckyAlignments_luckyNumber_description"] = luckyNumberResult.Description;
            results["luckyAlignments_luckyNumber_calculation"] = luckyNumberResult.CalculationFormula;
            
            _logger.LogInformation(
                $"[LumenPredictionGAgent][GetCalculatedValuesAsync] Successfully calculated {results.Count} values for user {userInfo.UserId}");
            
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                $"[LumenPredictionGAgent][GetCalculatedValuesAsync] Error calculating values for user {userInfo.UserId}");
            throw;
        }
    }
    
    #endregion
    
    #region Western Astrology Calculation
    
    // Zodiac sign names in order
    private static readonly string[] ZodiacSigns = new[]
    {
        "Aries", "Taurus", "Gemini", "Cancer", "Leo", "Virgo",
        "Libra", "Scorpio", "Sagittarius", "Capricorn", "Aquarius", "Pisces"
    };
    
    /// <summary>
    /// Calculate all three signs: Sun, Moon, and Rising using Swiss Ephemeris
    /// </summary>
    private (string sunSign, string moonSign, string risingSign) CalculateSigns(
        DateOnly birthDate,
        TimeOnly birthTime,
        double latitude,
        double longitude)
    {
        try
        {
            _logger.LogInformation(
                $"[LumenPredictionGAgent][WesternAstrology] Calculating signs for coordinates ({latitude}, {longitude})");
            
            // Create SwissEph instance for this calculation
            using var swissEph = new SwissEph();
            
            // Step 1: Convert to Julian Day
            var birthDateTime = birthDate.ToDateTime(birthTime);
            double julianDay = ToJulianDay(swissEph, birthDateTime);
            
            // Step 2: Calculate Sun Sign
            string sunSign = CalculateSunSign(swissEph, julianDay);
            
            // Step 3: Calculate Moon Sign
            string moonSign = CalculateMoonSign(swissEph, julianDay);
            
            // Step 4: Calculate Rising Sign (Ascendant)
            string risingSign = CalculateRisingSign(swissEph, julianDay, latitude, longitude);
            
            _logger.LogInformation(
                $"[LumenPredictionGAgent][WesternAstrology] Results - Sun: {sunSign}, Moon: {moonSign}, Rising: {risingSign}");
            
            return (sunSign, moonSign, risingSign);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                $"[LumenPredictionGAgent][WesternAstrology] Failed to calculate signs for coordinates ({latitude}, {longitude})");
            // Fallback: at least return Sun sign based on date
            string sunSign = CalculateSunSignSimple(birthDate);
            return (sunSign, sunSign, sunSign); // Use Sun sign as fallback for all
        }
    }
    
    /// <summary>
    /// Calculate Sun Sign using Swiss Ephemeris
    /// </summary>
    private string CalculateSunSign(SwissEph swissEph, double julianDay)
    {
        try
        {
            double[] positions = new double[6];
            string errorMsg = null;
            
            int result = swissEph.swe_calc_ut(
                julianDay,
                SwissEph.SE_SUN,
                SwissEph.SEFLG_SWIEPH,
                positions,
                ref errorMsg);
            
            if (result < 0)
            {
                _logger.LogWarning(
                    $"[LumenPredictionGAgent][WesternAstrology] Swiss Ephemeris Sun calculation failed: {errorMsg}");
                return "Aries"; // Fallback
            }
            
            // positions[0] is longitude in degrees (0-360)
            // Each zodiac sign is 30 degrees
            int signIndex = (int)(positions[0] / 30.0);
            _logger.LogInformation(
                $"[LumenPredictionGAgent][WesternAstrology] Sun position: {positions[0]}° -> {ZodiacSigns[signIndex % 12]}");
            return ZodiacSigns[signIndex % 12];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenPredictionGAgent][WesternAstrology] Sun sign calculation error");
            return "Aries";
        }
    }
    
    /// <summary>
    /// Calculate Moon Sign using Swiss Ephemeris
    /// </summary>
    private string CalculateMoonSign(SwissEph swissEph, double julianDay)
    {
        try
        {
            double[] positions = new double[6];
            string errorMsg = null;
            
            int result = swissEph.swe_calc_ut(
                julianDay,
                SwissEph.SE_MOON,
                SwissEph.SEFLG_SWIEPH,
                positions,
                ref errorMsg);
            
            if (result < 0)
            {
                _logger.LogWarning(
                    $"[LumenPredictionGAgent][WesternAstrology] Swiss Ephemeris Moon calculation failed: {errorMsg}");
                return "Aries"; // Fallback
            }
            
            int signIndex = (int)(positions[0] / 30.0);
            _logger.LogInformation(
                $"[LumenPredictionGAgent][WesternAstrology] Moon position: {positions[0]}° -> {ZodiacSigns[signIndex % 12]}");
            return ZodiacSigns[signIndex % 12];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenPredictionGAgent][WesternAstrology] Moon sign calculation error");
            return "Aries";
        }
    }
    
    /// <summary>
    /// Calculate Rising Sign (Ascendant) using Swiss Ephemeris
    /// </summary>
    private string CalculateRisingSign(SwissEph swissEph, double julianDay, double latitude, double longitude)
    {
        try
        {
            double[] cusps = new double[13];
            double[] ascmc = new double[10];
            
            int result = swissEph.swe_houses(
                julianDay,
                latitude,
                longitude,
                'P', // Placidus house system (most common)
                cusps,
                ascmc);
            
            if (result < 0)
            {
                _logger.LogWarning(
                    $"[LumenPredictionGAgent][WesternAstrology] Swiss Ephemeris Ascendant calculation failed");
                return "Aries"; // Fallback
            }
            
            // ascmc[0] is the Ascendant (Rising Sign) longitude
            int signIndex = (int)(ascmc[0] / 30.0);
            _logger.LogInformation(
                $"[LumenPredictionGAgent][WesternAstrology] Rising position: {ascmc[0]}° -> {ZodiacSigns[signIndex % 12]}");
            return ZodiacSigns[signIndex % 12];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenPredictionGAgent][WesternAstrology] Rising sign calculation error");
            return "Aries";
        }
    }
    
    /// <summary>
    /// Convert DateTime to Julian Day Number (for Swiss Ephemeris)
    /// Input dateTime should already be in UTC from timezone correction
    /// </summary>
    private double ToJulianDay(SwissEph swissEph, DateTime dateTime)
    {
        // Specify as UTC kind (do NOT call ToUniversalTime as it would double-convert)
        // The input is already UTC from calcBirthDate + calcBirthTime
        dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        
        int year = dateTime.Year;
        int month = dateTime.Month;
        int day = dateTime.Day;
        double hour = dateTime.Hour + dateTime.Minute / 60.0 + dateTime.Second / 3600.0;
        
        double julianDay = swissEph.swe_julday(year, month, day, hour, SwissEph.SE_GREG_CAL);
        _logger.LogInformation(
            $"[LumenPredictionGAgent][WesternAstrology] Julian Day: {julianDay} for {dateTime:yyyy-MM-dd HH:mm:ss} UTC");
        return julianDay;
    }
    
    /// <summary>
    /// Simple Sun sign calculation based on date only (fallback method)
    /// </summary>
    private string CalculateSunSignSimple(DateOnly birthDate)
    {
        int month = birthDate.Month;
        int day = birthDate.Day;
        
        return (month, day) switch
        {
            (3, >= 21) or (4, <= 19) => "Aries",
            (4, >= 20) or (5, <= 20) => "Taurus",
            (5, >= 21) or (6, <= 20) => "Gemini",
            (6, >= 21) or (7, <= 22) => "Cancer",
            (7, >= 23) or (8, <= 22) => "Leo",
            (8, >= 23) or (9, <= 22) => "Virgo",
            (9, >= 23) or (10, <= 22) => "Libra",
            (10, >= 23) or (11, <= 21) => "Scorpio",
            (11, >= 22) or (12, <= 21) => "Sagittarius",
            (12, >= 22) or (1, <= 19) => "Capricorn",
            (1, >= 20) or (2, <= 18) => "Aquarius",
            (2, >= 19) or (3, <= 20) => "Pisces",
            _ => "Aries"
        };
    }
    
    #endregion
    
    #region Language Switch Translation
    
    /// <summary>
    /// Trigger translation for this prediction to target language (triggered by language switch)
    /// </summary>
    public async Task TriggerTranslationAsync(LumenUserDto userInfo, string targetLanguage)
    {
        try
        {
            _logger.LogInformation("[LumenPredictionGAgent][TriggerTranslationAsync] Triggering translation - User: {UserId}, Type: {Type}, Language: {Language}", 
                userInfo.UserId, State.Type, targetLanguage);

            // Check if prediction exists
            if (State.PredictionId == Guid.Empty || State.MultilingualResults == null || State.MultilingualResults.Count == 0)
            {
                _logger.LogWarning("[LumenPredictionGAgent][TriggerTranslationAsync] No prediction exists to translate");
                return;
            }

            // Check if target language already exists
            if (State.MultilingualResults.ContainsKey(targetLanguage))
            {
                _logger.LogInformation("[LumenPredictionGAgent][TriggerTranslationAsync] Target language '{Language}' already exists, skipping", 
                    targetLanguage);
                return;
            }

            // Find source language (prefer English, fallback to any available)
            var sourceLanguage = State.MultilingualResults.ContainsKey("en") 
                ? "en" 
                : State.MultilingualResults.Keys.FirstOrDefault();
            
            if (sourceLanguage == null)
            {
                _logger.LogWarning("[LumenPredictionGAgent][TriggerTranslationAsync] No source language available");
                return;
            }

            var sourceContent = State.MultilingualResults[sourceLanguage];
            
            // Trigger on-demand translation (async, fire-and-forget)
            TriggerOnDemandTranslationAsync(userInfo, State.PredictionDate, State.Type, sourceLanguage, sourceContent, targetLanguage);
            
            _logger.LogInformation("[LumenPredictionGAgent][TriggerTranslationAsync] Translation triggered successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenPredictionGAgent][TriggerTranslationAsync] Error triggering translation");
        }
    }
    
    #endregion
    
    #region Lifetime Batched Generation
    
    /// <summary>
    /// Generate Lifetime prediction using parallel batched LLM calls (reduces refusal rate)
    /// </summary>
    private async Task<(Dictionary<string, string>? Results, Dictionary<string, Dictionary<string, string>>? MultilingualResults)> 
        GenerateLifetimeBatchedAsync(
            LumenUserDto userInfo, 
            DateOnly predictionDate,
            string targetLanguage,
            string? moonSign,
            string? risingSign,
            LifetimeBatchContext context)
    {
        var totalStopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "[LumenPredictionGAgent][GenerateLifetimeBatched] Starting parallel batch generation for user {UserId}, Language: {Language}",
            userInfo.UserId, targetLanguage);

        try
        {
            // Create batch tasks for parallel execution
            var batchTasks = new Dictionary<LifetimeBatchType, Task<Dictionary<string, string>?>>
            {
                [LifetimeBatchType.WesternAstrology] = GenerateLifetimeBatchAsync(
                    userInfo, predictionDate, targetLanguage, moonSign, risingSign, 
                    LifetimeBatchType.WesternAstrology, context),
                    
                [LifetimeBatchType.ChineseAstrology] = GenerateLifetimeBatchAsync(
                    userInfo, predictionDate, targetLanguage, moonSign, risingSign,
                    LifetimeBatchType.ChineseAstrology, context),
                    
                [LifetimeBatchType.LifeTraits] = GenerateLifetimeBatchAsync(
                    userInfo, predictionDate, targetLanguage, moonSign, risingSign,
                    LifetimeBatchType.LifeTraits, context),
                    
                [LifetimeBatchType.TimelinePlot] = GenerateLifetimeBatchAsync(
                    userInfo, predictionDate, targetLanguage, moonSign, risingSign,
                    LifetimeBatchType.TimelinePlot, context)
            };

            // Wait for all batches to complete
            await Task.WhenAll(batchTasks.Values);
            
            totalStopwatch.Stop();
            _logger.LogInformation(
                "[PERF][Lumen][Batched] {UserId} All_Batches: {ElapsedMs}ms",
                userInfo.UserId, totalStopwatch.ElapsedMilliseconds);

            // Merge results from all batches
            var mergedResults = new Dictionary<string, string>();
            var successCount = 0;
            var failedBatches = new List<LifetimeBatchType>();

            foreach (var (batchType, task) in batchTasks)
            {
                var batchResult = await task;
                if (batchResult != null && batchResult.Count > 0)
                {
                    foreach (var (key, value) in batchResult)
                    {
                        mergedResults[key] = value;
                    }
                    successCount++;
                    _logger.LogDebug(
                        "[LumenPredictionGAgent][GenerateLifetimeBatched] Batch {BatchType} returned {Count} fields",
                        batchType, batchResult.Count);
                }
                else
                {
                    failedBatches.Add(batchType);
                    _logger.LogWarning(
                        "[LumenPredictionGAgent][GenerateLifetimeBatched] Batch {BatchType} failed or returned empty",
                        batchType);
                }
            }

            // Check if enough batches succeeded (at least 3 of 4)
            if (successCount < 3)
            {
                _logger.LogError(
                    "[LumenPredictionGAgent][GenerateLifetimeBatched] Too many batches failed ({FailedCount}/4): {FailedBatches}",
                    failedBatches.Count, string.Join(", ", failedBatches));
                return (null, null);
            }

            _logger.LogInformation(
                "[LumenPredictionGAgent][GenerateLifetimeBatched] Successfully merged {FieldCount} fields from {SuccessCount}/4 batches",
                mergedResults.Count, successCount);

            // Build multilingual results
            var multilingualResults = new Dictionary<string, Dictionary<string, string>>
            {
                [targetLanguage] = mergedResults
            };

            return (mergedResults, multilingualResults);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[LumenPredictionGAgent][GenerateLifetimeBatched] Error during batch generation for user {UserId}",
                userInfo.UserId);
            return (null, null);
        }
    }

    /// <summary>
    /// Generate a single batch of Lifetime prediction fields
    /// </summary>
    private async Task<Dictionary<string, string>?> GenerateLifetimeBatchAsync(
        LumenUserDto userInfo,
        DateOnly predictionDate,
        string targetLanguage,
        string? moonSign,
        string? risingSign,
        LifetimeBatchType batchType,
        LifetimeBatchContext context)
    {
        var batchStopwatch = Stopwatch.StartNew();
        var batchName = batchType.ToString();
        
        try
        {
            // Build batch-specific prompt
            var prompt = BuildLifetimeBatchPrompt(userInfo, predictionDate, targetLanguage, moonSign, risingSign, batchType, context);
            var promptTokens = TokenHelper.EstimateTokenCount(prompt);
            
            _logger.LogDebug(
                "[LumenPredictionGAgent][GenerateLifetimeBatch] {BatchType} prompt: {Length} chars, ~{Tokens} tokens",
                batchType, prompt.Length, promptTokens);

            // Use unique grain key for each batch to enable true parallelism
            var batchGrainKey = CommonHelper.StringToGuid($"{userInfo.UserId}_lifetime_batch_{(int)batchType}");
            var godChat = _clusterClient.GetGrain<IGodChat>(batchGrainKey);
            var chatId = Guid.NewGuid().ToString();

            var settings = new ExecutionPromptSettings
            {
                Temperature = "0.7"
            };

            // Build system prompt for this batch
            var systemPrompt = BuildLifetimeBatchSystemPrompt(targetLanguage, batchType);

            // Make LLM call
            var llmStopwatch = Stopwatch.StartNew();
            var response = await godChat.ChatWithoutHistoryAsync(
                CommonHelper.StringToGuid(userInfo.UserId),
                systemPrompt,
                prompt,
                chatId,
                settings,
                true,
                "LUMEN");
            llmStopwatch.Stop();

            if (response == null || !response.Any())
            {
                _logger.LogWarning(
                    "[LumenPredictionGAgent][GenerateLifetimeBatch] {BatchType} - No response from LLM",
                    batchType);
                return null;
            }

            var aiResponse = response[0].Content;
            _logger.LogInformation(
                "[PERF][Lumen][Batch] {UserId} {BatchType}: {ElapsedMs}ms, {ResponseLength} chars",
                userInfo.UserId, batchType, llmStopwatch.ElapsedMilliseconds, aiResponse.Length);

            // Parse TSV response
            var parsedResults = ParseTsvResponse(aiResponse);
            
            if (parsedResults == null || parsedResults.Count == 0)
            {
                _logger.LogWarning(
                    "[LumenPredictionGAgent][GenerateLifetimeBatch] {BatchType} - Failed to parse response",
                    batchType);
                return null;
            }

            batchStopwatch.Stop();
            _logger.LogDebug(
                "[LumenPredictionGAgent][GenerateLifetimeBatch] {BatchType} completed: {FieldCount} fields in {ElapsedMs}ms",
                batchType, parsedResults.Count, batchStopwatch.ElapsedMilliseconds);

            return parsedResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[LumenPredictionGAgent][GenerateLifetimeBatch] {BatchType} failed for user {UserId}",
                batchType, userInfo.UserId);
            return null;
        }
    }

    /// <summary>
    /// Build system prompt for a Lifetime batch
    /// </summary>
    private string BuildLifetimeBatchSystemPrompt(string targetLanguage, LifetimeBatchType batchType)
    {
        var languageMap = new Dictionary<string, string>
        {
            { "en", "English" },
            { "zh-tw", "繁體中文" },
            { "zh", "简体中文" },
            { "es", "Español" }
        };
        var languageName = languageMap.GetValueOrDefault(targetLanguage, "English");

        var batchFocus = batchType switch
        {
            LifetimeBatchType.WesternAstrology => "Western Astrology archetypes, zodiac insights, and combined essence",
            LifetimeBatchType.ChineseAstrology => "Chinese Astrology, Four Pillars (BaZi), and zodiac cycles",
            LifetimeBatchType.LifeTraits => "Personal strengths, challenges, and destiny paths",
            LifetimeBatchType.TimelinePlot => "Life timeline, 10-year luck cycles, and narrative plot",
            _ => "astrological content"
        };

        return $@"You are a creative astrology guide creating {batchFocus} content.

===== LANGUAGE REQUIREMENT (CRITICAL) =====
Target Language: {languageName}

RULES:
1. Write ALL field values in {languageName} ONLY
2. Field names remain in English (lowercase with underscores)
3. Do NOT mix languages in field values
4. If {languageName} is not English, translate ALL descriptive text
============================================

CONTENT STYLE:
- Be SPECIFIC and DESCRIPTIVE - avoid vague, mystical language
- Provide ACTIONABLE insights users can apply
- Create PERSONALIZED content that addresses the user directly
- Write naturally with quality over strict length requirements
- Focus on empowerment, awareness, and personal growth

FORMAT:
- Return raw TSV (Tab-Separated Values)
- Use ACTUAL TAB CHARACTER between field name and value
- NO JSON, NO markdown, NO extra text
- Start immediately with the first field";
    }

    /// <summary>
    /// Build prompt for a specific Lifetime batch
    /// </summary>
    private string BuildLifetimeBatchPrompt(
        LumenUserDto userInfo,
        DateOnly predictionDate,
        string targetLanguage,
        string? moonSign,
        string? risingSign,
        LifetimeBatchType batchType,
        LifetimeBatchContext ctx)
    {
        return batchType switch
        {
            LifetimeBatchType.WesternAstrology => BuildWesternAstrologyBatchPrompt(userInfo, targetLanguage, moonSign, risingSign, ctx),
            LifetimeBatchType.ChineseAstrology => BuildChineseAstrologyBatchPrompt(userInfo, targetLanguage, ctx),
            LifetimeBatchType.LifeTraits => BuildLifeTraitsBatchPrompt(userInfo, targetLanguage, ctx),
            LifetimeBatchType.TimelinePlot => BuildTimelinePlotBatchPrompt(userInfo, targetLanguage, ctx),
            _ => throw new ArgumentException($"Unknown batch type: {batchType}")
        };
    }

    /// <summary>
    /// Batch 1: Western Astrology - Sun/Moon/Rising archetypes, combined essence, zodiac whisper
    /// </summary>
    private string BuildWesternAstrologyBatchPrompt(
        LumenUserDto userInfo,
        string targetLanguage,
        string? moonSign,
        string? risingSign,
        LifetimeBatchContext ctx)
    {
        var sunSign = ctx.SunSign;
        moonSign ??= sunSign;
        risingSign ??= sunSign;
        
        var sunSignTranslated = TranslateSunSign(sunSign, targetLanguage);
        var moonSignTranslated = TranslateSunSign(moonSign, targetLanguage);
        var risingSignTranslated = TranslateSunSign(risingSign, targetLanguage);

        var desc_combined = targetLanguage switch
        {
            "zh" => "描述你的太阳、月亮、上升星座如何共同塑造你独特的人格。深入分析这三重能量如何在你的思维、情感和外在表现中相互作用。(60-80字)",
            "zh-tw" => "描述你的太陽、月亮、上升星座如何共同塑造你獨特的人格。深入分析這三重能量如何在你的思維、情感和外在表現中相互作用。(60-80字)",
            "es" => "Describe cómo tu Sol, Luna y Ascendente trabajan juntos para crear tu personalidad única. Analiza cómo estas tres energías interactúan en tu pensamiento, emociones y expresión externa. (60-80 palabras)",
            _ => "Describe how your Sun, Moon, and Rising signs work together to create your unique personality. Analyze how these three energies interact in your thinking, emotions, and external expression. (60-80 words)"
        };

        var desc_whisper = targetLanguage switch
        {
            "zh" => $"以'{ctx.BirthYearAnimalTranslated}'开头，给出针对用户的具体建议或洞见。要具体、有针对性，避免模糊的神秘语言。(40-60字)",
            "zh-tw" => $"以'{ctx.BirthYearAnimalTranslated}'開頭，給出針對用戶的具體建議或洞見。要具體、有針對性，避免模糊的神秘語言。(40-60字)",
            "es" => $"Comienza con '{ctx.BirthYearAnimalTranslated}' y ofrece consejos específicos o perspectivas para el usuario. Sé específico y directo, evita lenguaje místico vago. (40-60 palabras)",
            _ => $"Start with '{ctx.BirthYearAnimalTranslated}' and offer specific advice or insights for the user. Be specific and targeted, avoid vague mystical language. (40-60 words)"
        };

        return $@"Generate Western Astrology content for lifetime prediction.

USER CONTEXT:
- Sun Sign: {sunSignTranslated}
- Moon Sign: {moonSignTranslated}  
- Rising Sign: {risingSignTranslated}
- Chinese Zodiac: {ctx.BirthYearAnimalTranslated} ({ctx.BirthYearElement})

REQUIRED FIELDS (TSV format - field name TAB value):

sun_tag	[2-4 word tagline capturing the essence of {sunSignTranslated}]
sun_arch_name	[Archetype name for {sunSignTranslated}, e.g. ""The Pioneer"", ""The Nurturer""]
sun_desc	[35-50 words describing how {sunSignTranslated} shapes thinking patterns, decision-making, and core identity]
moon_arch_name	[Archetype name for {moonSignTranslated}]
moon_desc	[35-50 words describing how {moonSignTranslated} influences emotional responses, inner needs, and intuition]
rising_arch_name	[Archetype name for {risingSignTranslated}]
rising_desc	[35-50 words describing how {risingSignTranslated} affects first impressions, social approach, and outer persona]
combined_essence	{desc_combined}
whisper	{desc_whisper}

Start output now:";
    }

    /// <summary>
    /// Batch 2: Chinese Astrology - Four Pillars, Chinese traits, zodiac cycle
    /// </summary>
    private string BuildChineseAstrologyBatchPrompt(
        LumenUserDto userInfo,
        string targetLanguage,
        LifetimeBatchContext ctx)
    {
        var cycleIntro = targetLanguage switch
        {
            "zh" => $"以\"你的生肖是{ctx.BirthYearAnimalTranslated}…\"开头，描述你的20年象征周期如何影响人生阶段",
            "zh-tw" => $"以\"你的生肖是{ctx.BirthYearAnimalTranslated}…\"開頭，描述你的20年象徵週期如何影響人生階段",
            "es" => $"Comienza con \"Tu zodiaco chino es {ctx.BirthYearAnimalTranslated}...\" y describe cómo tu ciclo simbólico de 20 años afecta las etapas de vida",
            _ => $"Start with \"Your Chinese Zodiac is {ctx.BirthYearAnimalTranslated}...\" and describe how your 20-year symbolic cycle affects life stages"
        };

        return $@"Generate Chinese Astrology content for lifetime prediction.

USER CONTEXT:
- Birth Year: {ctx.BirthYear} ({ctx.BirthYearZodiac})
- Chinese Zodiac Animal: {ctx.BirthYearAnimalTranslated}
- Element: {ctx.BirthYearElement}
- Birth Year Stems: {ctx.BirthYearStems}

REQUIRED FIELDS (TSV format):

pillars_id	[Personalized BaZi identity statement, 20-30 words]
pillars_detail	[Detailed explanation of the Four Pillars influence, 40-60 words]
cn_trait1	[Core trait from Chinese zodiac, 10-15 words]
cn_trait2	[Second key trait, 10-15 words]
cn_trait3	[Third key trait, 10-15 words]
cn_trait4	[Fourth key trait, 10-15 words]
cn_essence	[Essential quality related to {ctx.BirthYearElement} element, 25-35 words]
cycle_year_range	[20-year range, format: YYYY-YYYY]
cycle_name_zh	[Simplified Chinese name for cycle theme]
cycle_name	[Cycle theme name in target language]
cycle_intro	{cycleIntro} (40-60 words)
cycle_pt1	[First key point of this zodiac cycle, 15-20 words]
cycle_pt2	[Second key point, 15-20 words]
cycle_pt3	[Third key point, 15-20 words]
cycle_pt4	[Fourth key point, 15-20 words]

Start output now:";
    }

    /// <summary>
    /// Batch 3: Life Traits - Strengths, challenges, destiny paths
    /// </summary>
    private string BuildLifeTraitsBatchPrompt(
        LumenUserDto userInfo,
        string targetLanguage,
        LifetimeBatchContext ctx)
    {
        return $@"Generate Life Traits content for lifetime prediction.

USER CONTEXT:
- Sun Sign: {ctx.SunSignTranslated}
- Chinese Zodiac: {ctx.BirthYearAnimalTranslated} ({ctx.BirthYearElement})
- Current Age: {ctx.CurrentAge}

REQUIRED FIELDS (TSV format):

str_intro	[Introduction to user's natural strengths based on astrological profile, 25-35 words]
str1_title	[First strength title, 3-5 words]
str1_desc	[Detailed description with specific examples of how this strength manifests, 35-50 words]
str2_title	[Second strength title, 3-5 words]
str2_desc	[Detailed description with specific examples, 35-50 words]
str3_title	[Third strength title, 3-5 words]
str3_desc	[Detailed description with specific examples, 35-50 words]
chal_intro	[Introduction to growth areas and challenges, 25-35 words]
chal1_title	[First challenge title, 3-5 words]
chal1_desc	[Description with actionable advice on how to address this challenge, 35-50 words]
chal2_title	[Second challenge title, 3-5 words]
chal2_desc	[Description with actionable advice, 35-50 words]
chal3_title	[Third challenge title, 3-5 words]
chal3_desc	[Description with actionable advice, 35-50 words]
destiny_intro	[Introduction to destiny paths and life purpose, 25-35 words]
path1_title	[First destiny path title, 3-5 words]
path1_desc	[Description of this path with guidance on how to pursue it, 35-50 words]
path2_title	[Second destiny path title, 3-5 words]
path2_desc	[Description with guidance, 35-50 words]
path3_title	[Third destiny path title, 3-5 words]
path3_desc	[Description with guidance, 35-50 words]

Start output now:";
    }

    /// <summary>
    /// Batch 4: Timeline & Plot - 10-year luck cycles, life plot narrative, mantras
    /// Enhanced with more descriptive and actionable content
    /// </summary>
    private string BuildTimelinePlotBatchPrompt(
        LumenUserDto userInfo,
        string targetLanguage,
        LifetimeBatchContext ctx)
    {
        var tenYearDesc = targetLanguage switch
        {
            "zh" => "每个周期描述要具体：说明这个时期的主要挑战、机遇和用户可以采取的具体行动。避免笼统的描述。",
            "zh-tw" => "每個週期描述要具體：說明這個時期的主要挑戰、機遇和用戶可以採取的具體行動。避免籠統的描述。",
            "es" => "Cada descripción de ciclo debe ser específica: explica los principales desafíos, oportunidades y acciones concretas que el usuario puede tomar. Evita descripciones genéricas.",
            _ => "Each cycle description should be specific: explain the main challenges, opportunities, and concrete actions the user can take. Avoid generic descriptions."
        };

        var plotTitleDesc = targetLanguage switch
        {
            "zh" => "标题必须包含\"你的\"来直接称呼用户，例如\"你的英雄之旅\"、\"你的蜕变史诗\"",
            "zh-tw" => "標題必須包含\"你的\"來直接稱呼用戶，例如\"你的英雄之旅\"、\"你的蛻變史詩\"",
            "es" => "El título debe incluir \"Tu\" para dirigirse directamente al usuario, por ejemplo \"Tu Viaje Heroico\", \"Tu Épica de Transformación\"",
            _ => "Title MUST include \"Your\" to address the user directly, e.g. \"Your Hero's Journey\", \"Your Epic of Transformation\""
        };

        return $@"Generate Timeline & Life Plot content for lifetime prediction.

USER CONTEXT:
- Birth Year: {ctx.BirthYear}
- Current Age: {ctx.CurrentAge}
- Sun Sign: {ctx.SunSignTranslated}
- Chinese Zodiac: {ctx.BirthYearAnimalTranslated}
- Past Cycle: {ctx.PastCycleAgeRange}
- Current Cycle: {ctx.CurrentCycleAgeRange}  
- Future Cycle: {ctx.FutureCycleAgeRange}

IMPORTANT - 10-YEAR LUCK CYCLES:
{tenYearDesc}

IMPORTANT - LIFE PLOT TITLE:
{plotTitleDesc}

REQUIRED FIELDS (TSV format):

ten_intro	[Introduction to the 10-year luck cycle concept and its significance for the user, 30-40 words]
past_summary	[Theme/title for past cycle ({ctx.PastCycleAgeRange}), 5-8 words]
past_detail	[DETAILED description of past cycle: what challenges were faced, lessons learned, and how they shaped the user. Include specific advice on how to apply these lessons. 60-80 words]
curr_summary	[Theme/title for current cycle ({ctx.CurrentCycleAgeRange}), 5-8 words]
curr_detail	[DETAILED description of current cycle: main opportunities, challenges to watch for, and 2-3 SPECIFIC ACTIONABLE STEPS the user should take during this period. 60-80 words]
future_summary	[Theme/title for future cycle ({ctx.FutureCycleAgeRange}), 5-8 words]
future_detail	[DETAILED description of future cycle: what to expect, how to prepare, and specific actions to take NOW to maximize this future period. 60-80 words]
plot_title	[{plotTitleDesc}, 4-6 words total]
plot_chapter	[The current chapter name in user's life story, describing where they are NOW, 4-6 words]
plot_pt1	[First key plot point: a defining characteristic or theme in user's life narrative, 15-25 words]
plot_pt2	[Second key plot point with specific details, 15-25 words]
plot_pt3	[Third key plot point with specific details, 15-25 words]
plot_pt4	[Fourth key plot point with specific details, 15-25 words]
act1_title	[First life act title (e.g. ""The Awakening""), 2-4 words]
act1_desc	[Description of first life act with directions for the user, 30-45 words]
act2_title	[Second life act title, 2-4 words]
act2_desc	[Description with directions for the user, 30-45 words]
act3_title	[Third life act title, 2-4 words]
act3_desc	[Description with directions for the user, 30-45 words]
act4_title	[Fourth life act title, 2-4 words]
act4_desc	[Description with directions for the user, 30-45 words]
mantra_title	[Personal mantra title, 2-4 words]
mantra_pt1	[First mantra line - an affirmation for the user, 10-15 words]
mantra_pt2	[Second mantra line, 10-15 words]
mantra_pt3	[Third mantra line, 10-15 words]

Start output now:";
    }

    /// <summary>
    /// Context data for Lifetime batch generation (pre-calculated values shared across batches)
    /// </summary>
    public class LifetimeBatchContext
    {
        public string SunSign { get; set; } = string.Empty;
        public string SunSignTranslated { get; set; } = string.Empty;
        public int BirthYear { get; set; }
        public string BirthYearZodiac { get; set; } = string.Empty;
        public string BirthYearAnimal { get; set; } = string.Empty;
        public string BirthYearAnimalTranslated { get; set; } = string.Empty;
        public string BirthYearElement { get; set; } = string.Empty;
        public string BirthYearStems { get; set; } = string.Empty;
        public int CurrentAge { get; set; }
        public string PastCycleAgeRange { get; set; } = string.Empty;
        public string CurrentCycleAgeRange { get; set; } = string.Empty;
        public string FutureCycleAgeRange { get; set; } = string.Empty;
        public string CurrentYearStemsFormatted { get; set; } = string.Empty;
    }

    /// <summary>
    /// Create batch context with pre-calculated values
    /// </summary>
    private LifetimeBatchContext CreateLifetimeBatchContext(
        LumenUserDto userInfo, 
        DateOnly predictionDate,
        string targetLanguage)
    {
        var birthYear = userInfo.BirthDate.Year;
        var currentYear = predictionDate.Year;
        var sunSign = LumenCalculator.CalculateZodiacSign(userInfo.BirthDate);
        var birthYearZodiac = LumenCalculator.GetChineseZodiacWithElement(birthYear);
        var birthYearAnimal = LumenCalculator.CalculateChineseZodiac(birthYear);
        var birthYearElement = LumenCalculator.CalculateChineseElement(birthYear);
        
        var pastCycle = LumenCalculator.CalculateTenYearCycle(birthYear, -1);
        var currentCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 0);
        var futureCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 1);

        return new LifetimeBatchContext
        {
            SunSign = sunSign,
            SunSignTranslated = TranslateSunSign(sunSign, targetLanguage),
            BirthYear = birthYear,
            BirthYearZodiac = birthYearZodiac,
            BirthYearAnimal = birthYearAnimal,
            BirthYearAnimalTranslated = TranslateChineseZodiacAnimal(birthYearZodiac, targetLanguage),
            BirthYearElement = birthYearElement,
            BirthYearStems = LumenCalculator.CalculateStemsAndBranches(birthYear),
            CurrentAge = LumenCalculator.CalculateAge(userInfo.BirthDate),
            PastCycleAgeRange = pastCycle.AgeRange,
            CurrentCycleAgeRange = currentCycle.AgeRange,
            FutureCycleAgeRange = futureCycle.AgeRange,
            CurrentYearStemsFormatted = LumenCalculator.CalculateStemsAndBranches(currentYear)
        };
    }

    #endregion
}