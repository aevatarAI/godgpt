using System;
using System.Collections.Generic;
using Aevatar.Application.Grains.Lumen.Dtos;
using Aevatar.Application.Grains.Lumen.Helpers;

namespace Aevatar.Application.Grains.Lumen;

/// <summary>
/// Lumen calculation utilities for accurate astrological calculations
/// </summary>
public static partial class LumenCalculator
{
    #region Western Zodiac
    
    /// <summary>
    /// Calculate Western zodiac sign based on birth date
    /// </summary>
    public static string CalculateZodiacSign(DateOnly birthDate)
    {
        var month = birthDate.Month;
        var day = birthDate.Day;
        
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
            _ => "Pisces"
        };
    }
    
    #endregion
    
    #region Chinese Zodiac
    
    private static readonly string[] ChineseAnimals = 
    { 
        "Rat", "Ox", "Tiger", "Rabbit", "Dragon", "Snake", 
        "Horse", "Goat", "Monkey", "Rooster", "Dog", "Pig" 
    };
    
    /// <summary>
    /// Calculate Chinese zodiac animal based on birth year (Gregorian)
    /// NOTE: This is a simplified method. For accurate calculation, use CalculateFourPillars().YearPillar
    /// </summary>
    [Obsolete("Use CalculateFourPillars() for accurate zodiac calculation based on solar terms")]
    public static string CalculateChineseZodiac(int birthYear)
    {
        // 1900 is Rat year (子鼠)
        var index = (birthYear - 1900) % 12;
        if (index < 0) index += 12;
        
        return ChineseAnimals[index];
    }
    
    /// <summary>
    /// Calculate Chinese zodiac animal from birth date (accurate method using solar terms)
    /// </summary>
    public static string CalculateChineseZodiacAccurate(DateOnly birthDate, TimeOnly? birthTime = null)
    {
        var fourPillars = CalculateFourPillars(birthDate, birthTime);
        return fourPillars.YearPillar.BranchZodiac;
    }
    
    #endregion
    
    #region Chinese Element (Five Elements)
    
    /// <summary>
    /// Calculate Chinese element (Wu Xing) based on birth year using Nayin system
    /// Returns simplified element name only (Metal/Wood/Water/Fire/Earth)
    /// NOTE: This is a simplified method. For accurate calculation, use CalculateFourPillars().YearPillar.Element
    /// </summary>
    [Obsolete("Use CalculateFourPillars() for accurate element calculation based on solar terms")]
    public static string CalculateChineseElement(int birthYear)
    {
        // Nayin 60-year cycle elements (simplified to 5 basic elements)
        // Each element appears in pairs in the 60-cycle
        var elements = new[]
        {
            "Metal", "Metal", "Fire", "Fire", "Wood", "Wood", "Earth", "Earth", "Metal", "Metal",
            "Fire", "Fire", "Water", "Water", "Earth", "Earth", "Metal", "Metal", "Wood", "Wood",
            "Water", "Water", "Earth", "Earth", "Fire", "Fire", "Wood", "Wood", "Water", "Water",
            "Metal", "Metal", "Fire", "Fire", "Wood", "Wood", "Earth", "Earth", "Metal", "Metal",
            "Fire", "Fire", "Water", "Water", "Earth", "Earth", "Metal", "Metal", "Wood", "Wood",
            "Water", "Water", "Earth", "Earth", "Fire", "Fire", "Wood", "Wood", "Water", "Water"
        };
        
        var position = (birthYear - 4) % 60;
        if (position < 0) position += 60;
        
        return elements[position];
    }
    
    /// <summary>
    /// Get Chinese zodiac with element (e.g., "Fire Dragon")
    /// NOTE: This is a simplified method. For accurate calculation, use CalculateFourPillars()
    /// </summary>
    [Obsolete("Use GetChineseZodiacWithElementAccurate() for accurate calculation based on solar terms")]
    public static string GetChineseZodiacWithElement(int birthYear)
    {
        var element = CalculateChineseElement(birthYear);
        var animal = CalculateChineseZodiac(birthYear);
        return $"{element} {animal}";
    }
    
    /// <summary>
    /// Get Chinese zodiac with element using accurate solar term calculation
    /// </summary>
    public static string GetChineseZodiacWithElementAccurate(DateOnly birthDate, TimeOnly? birthTime = null)
    {
        var fourPillars = CalculateFourPillars(birthDate, birthTime);
        var element = fourPillars.YearPillar.Element;
        var animal = fourPillars.YearPillar.BranchZodiac;
        return $"{element} {animal}";
    }
    
    #endregion
    
    #region Heavenly Stems and Earthly Branches (天干地支)
    
    private static readonly string[] HeavenlyStems = 
    { 
        "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸" 
    };
    
    private static readonly string[] HeavenlyStemsPinyin = 
    { 
        "Jia", "Yi", "Bing", "Ding", "Wu", "Ji", "Geng", "Xin", "Ren", "Gui" 
    };
    
    private static readonly string[] EarthlyBranches = 
    { 
        "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥" 
    };
    
    private static readonly string[] EarthlyBranchesPinyin = 
    { 
        "Zi", "Chou", "Yin", "Mao", "Chen", "Si", "Wu", "Wei", "Shen", "You", "Xu", "Hai" 
    };
    
    /// <summary>
    /// Calculate Heavenly Stems and Earthly Branches (天干地支) for a given year
    /// NOTE: This is a simplified method. For accurate calculation, use CalculateFourPillars().YearPillar
    /// </summary>
    /// <returns>Format: "乙 巳 Yi Si"</returns>
    [Obsolete("Use CalculateFourPillars() for accurate stems/branches calculation based on solar terms")]
    public static string CalculateStemsAndBranches(int year)
    {
        // Year 4 AD is 甲子 (Jia Zi), the start of the 60-year cycle
        var stemIndex = (year - 4) % 10;
        var branchIndex = (year - 4) % 12;
        
        if (stemIndex < 0) stemIndex += 10;
        if (branchIndex < 0) branchIndex += 12;
        
        return $"{HeavenlyStems[stemIndex]} {EarthlyBranches[branchIndex]} {HeavenlyStemsPinyin[stemIndex]} {EarthlyBranchesPinyin[branchIndex]}";
    }
    
    /// <summary>
    /// Calculate Stems and Branches for a year and return structured data
    /// NOTE: This is a simplified method. For accurate calculation, use CalculateFourPillars().YearPillar
    /// </summary>
    [Obsolete("Use CalculateFourPillars() for accurate stems/branches calculation based on solar terms")]
    public static (string stemChinese, string stemPinyin, string branchChinese, string branchPinyin) GetStemsAndBranchesComponents(int year)
    {
        // Year 4 AD is 甲子 (Jia Zi), the start of the 60-year cycle
        var stemIndex = (year - 4) % 10;
        var branchIndex = (year - 4) % 12;
        
        if (stemIndex < 0) stemIndex += 10;
        if (branchIndex < 0) branchIndex += 12;
        
        return (
            stemChinese: HeavenlyStems[stemIndex],
            stemPinyin: HeavenlyStemsPinyin[stemIndex],
            branchChinese: EarthlyBranches[branchIndex],
            branchPinyin: EarthlyBranchesPinyin[branchIndex]
        );
    }
    
    /// <summary>
    /// Get Stems and Branches components from birth date (accurate method using solar terms)
    /// </summary>
    public static (string stemChinese, string stemPinyin, string branchChinese, string branchPinyin) GetStemsAndBranchesComponentsAccurate(DateOnly birthDate, TimeOnly? birthTime = null)
    {
        var fourPillars = CalculateFourPillars(birthDate, birthTime);
        return (
            stemChinese: fourPillars.YearPillar.StemChinese,
            stemPinyin: fourPillars.YearPillar.StemPinyin,
            branchChinese: fourPillars.YearPillar.BranchChinese,
            branchPinyin: fourPillars.YearPillar.BranchPinyin
        );
    }
    
    #endregion
    
    #region Taishui Relationship (太岁关系)
    
    /// <summary>
    /// Calculate Taishui (Tai Sui) relationship between birth year and current year
    /// </summary>
    public static string CalculateTaishuiRelationship(int birthYear, int currentYear)
    {
        var birthAnimal = (birthYear - 1900) % 12;
        var currentAnimal = (currentYear - 1900) % 12;
        
        if (birthAnimal < 0) birthAnimal += 12;
        if (currentAnimal < 0) currentAnimal += 12;
        
        var diff = (currentAnimal - birthAnimal + 12) % 12;
        
        return diff switch
        {
            0 => "本命年 (Ben Ming Nian - Birth Year)",
            1 => "相害 (Xiang Hai - Harm)",
            2 => "平和 (Ping He - Neutral)",
            3 => "三合 (San He - Triple Harmony)",
            4 => "平和 (Ping He - Neutral)",
            5 => "六合 (Liu He - Six Harmony)",
            6 => "相冲 (Xiang Chong - Clash)",
            7 => "相害 (Xiang Hai - Harm)",
            8 => "平和 (Ping He - Neutral)",
            9 => "三合 (San He - Triple Harmony)",
            10 => "平和 (Ping He - Neutral)",
            11 => "相破 (Xiang Po - Break)",
            _ => "平和 (Ping He - Neutral)"
        };
    }
    
    #endregion
    
    #region Age Calculation
    
    /// <summary>
    /// Calculate current age based on birth date
    /// </summary>
    public static int CalculateAge(DateOnly birthDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var age = today.Year - birthDate.Year;
        
        if (today < birthDate.AddYears(age))
        {
            age--;
        }
        
        return age;
    }
    
    /// <summary>
    /// Calculate 10-year cycle information (大运)
    /// NOTE: This is a simplified method. For accurate calculation, use CalculateTenYearCycleAccurate()
    /// </summary>
    [Obsolete("Use CalculateTenYearCycleAccurate() for accurate cycle calculation based on solar terms")]
    public static (string AgeRange, string Period) CalculateTenYearCycle(int birthYear, int cycleOffset)
    {
        var currentYear = DateTime.UtcNow.Year;
        var currentAge = currentYear - birthYear;
        
        // Calculate the cycle's start age (aligned to 10-year boundaries)
        var cycleStartAge = ((currentAge / 10) + cycleOffset) * 10;
        var cycleEndAge = cycleStartAge + 9;
        var cycleStartYear = birthYear + cycleStartAge;
        var cycleEndYear = birthYear + cycleEndAge;
        
        var stems = CalculateStemsAndBranches(cycleStartYear);
        var zodiac = GetChineseZodiacWithElement(cycleStartYear);
        
        return (
            AgeRange: $"Age {cycleStartAge}-{cycleEndAge} ({cycleStartYear}-{cycleEndYear})",
            Period: $"{stems} · {zodiac}"
        );
    }
    
    /// <summary>
    /// Calculate 10-year cycle information (大运) using accurate solar term calculation
    /// </summary>
    public static (string AgeRange, string Period) CalculateTenYearCycleAccurate(DateOnly birthDate, TimeOnly? birthTime, int cycleOffset)
    {
        // Get accurate birth year from Four Pillars
        var birthYearPillar = CalculateFourPillars(birthDate, birthTime).YearPillar;
        var birthYearGanZhi = $"{birthYearPillar.StemChinese}{birthYearPillar.BranchChinese}";
        
        // Calculate solar age (based on Four Pillars year, not Gregorian year)
        var currentYear = DateTime.UtcNow.Year;
        var currentAge = currentYear - birthDate.Year;
        
        // Calculate the cycle's start age (aligned to 10-year boundaries)
        var cycleStartAge = ((currentAge / 10) + cycleOffset) * 10;
        var cycleEndAge = cycleStartAge + 9;
        var cycleStartYear = birthDate.Year + cycleStartAge;
        var cycleEndYear = birthDate.Year + cycleEndAge;
        
        // Use accurate Four Pillars for cycle start year
        var cycleStartDate = new DateOnly(cycleStartYear, birthDate.Month, birthDate.Day);
        var cycleStartPillars = CalculateFourPillars(cycleStartDate, birthTime);
        
        var stems = $"{cycleStartPillars.YearPillar.StemChinese} {cycleStartPillars.YearPillar.BranchChinese} " +
                   $"{cycleStartPillars.YearPillar.StemPinyin} {cycleStartPillars.YearPillar.BranchPinyin}";
        var zodiac = $"{cycleStartPillars.YearPillar.Element} {cycleStartPillars.YearPillar.BranchZodiac}";
        
        return (
            AgeRange: $"Age {cycleStartAge}-{cycleEndAge} ({cycleStartYear}-{cycleEndYear})",
            Period: $"{stems} · {zodiac}"
        );
    }
    
    #endregion
    
    #region Display Name
    
    /// <summary>
    /// Convert full name to display name based on user language
    /// - For English/Spanish: take first word (space-separated)
    /// - For Chinese/Japanese: take full name
    /// </summary>
    public static string GetDisplayName(string fullName, string userLanguage)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return string.Empty;
        }
        
        var language = userLanguage?.ToLower() ?? "en";
        
        // For English and Spanish, use first name only (first word before space)
        if (language == "en" || language == "es")
        {
            var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : fullName;
        }
        
        // For Chinese, Japanese, and other languages, use full name
        return fullName.Trim();
    }
    
    #endregion
    
    #region Four Pillars (Ba Zi / 八字)
    
    /// <summary>
    /// Calculate complete Four Pillars (Year, Month, Day, Hour) using accurate solar term calculation
    /// </summary>
    public static FourPillarsInfo CalculateFourPillars(DateOnly birthDate, TimeOnly? birthTime = null)
    {
        // Convert to UTC DateTime for accurate solar term calculation
        var utcDateTime = birthTime.HasValue 
            ? new DateTime(birthDate.Year, birthDate.Month, birthDate.Day, 
                          birthTime.Value.Hour, birthTime.Value.Minute, birthTime.Value.Second, DateTimeKind.Utc)
            : new DateTime(birthDate.Year, birthDate.Month, birthDate.Day, 0, 0, 0, DateTimeKind.Utc);
        
        // Get solar term month (0-11) for accurate month pillar calculation
        var solarMonth = SolarTermCalculator.GetSolarTermMonth(utcDateTime);
        
        // Determine year for year pillar calculation
        // Year changes at 立春 (Start of Spring), not at Jan 1
        // If in Jan/Feb but before 立春 (solarMonth 11 = 丑月, which is the last month of previous year)
        var year = birthDate.Year;
        if (birthDate.Month <= 2)
        {
            // Check if we're before 立春
            // If solarMonth is 11, we're in the last month of the previous solar year
            if (solarMonth == 11)
            {
                year = birthDate.Year - 1;
            }
        }
        
        // Calculate Year Pillar
        var yearPillar = CalculateYearPillar(year);
        
        // Calculate Month Pillar (based on year stem and solar month)
        var monthPillar = CalculateMonthPillar(year, solarMonth);
        
        // Calculate Day Pillar
        var dayPillar = CalculateDayPillar(birthDate);
        
        // Calculate Hour Pillar (if birth time is provided)
        PillarInfo? hourPillar = null;
        if (birthTime.HasValue)
        {
            hourPillar = CalculateHourPillar(dayPillar.StemIndex, birthTime.Value.Hour);
        }
        
        return new FourPillarsInfo
        {
            YearPillar = yearPillar,
            MonthPillar = monthPillar,
            DayPillar = dayPillar,
            HourPillar = hourPillar
        };
    }
    
    private static PillarInfo CalculateYearPillar(int year)
    {
        var stemIndex = (year - 4) % 10;
        var branchIndex = (year - 4) % 12;
        
        if (stemIndex < 0) stemIndex += 10;
        if (branchIndex < 0) branchIndex += 12;
        
        return new PillarInfo(stemIndex, branchIndex);
    }
    
    private static PillarInfo CalculateMonthPillar(int year, int solarMonth)
    {
        // Month pillar calculation based on solar terms (accurate method)
        // Year stem determines the starting point for month stems
        // 年上起月法：甲己之年丙作首（甲年和己年从丙寅开始）
        var yearStemIndex = (year - 4) % 10;
        if (yearStemIndex < 0) yearStemIndex += 10;
        
        // Calculate month stem based on year stem
        // 甲己(0,5)→丙(2), 乙庚(1,6)→戊(4), 丙辛(2,7)→庚(6), 丁壬(3,8)→壬(8), 戊癸(4,9)→甲(0)
        int monthStemStart = yearStemIndex switch
        {
            0 or 5 => 2,  // 甲己 starts from 丙寅
            1 or 6 => 4,  // 乙庚 starts from 戊寅
            2 or 7 => 6,  // 丙辛 starts from 庚寅
            3 or 8 => 8,  // 丁壬 starts from 壬寅
            4 or 9 => 0,  // 戊癸 starts from 甲寅
            _ => 0
        };
        
        // Solar month (0-11) corresponds to earthly branches starting from 寅(2)
        // solarMonth 0 (立春 to 惊蛰) = 寅月 (branch index 2)
        // solarMonth 1 (惊蛰 to 清明) = 卯月 (branch index 3)
        // ... and so on
        var monthBranchIndex = (solarMonth + 2) % 12;
        
        // Calculate month stem: start + solar month
        var monthStemIndex = (monthStemStart + solarMonth) % 10;
        
        return new PillarInfo(monthStemIndex, monthBranchIndex);
    }
    
    private static PillarInfo CalculateDayPillar(DateOnly date)
    {
        // Day pillar calculation using accurate base date
        // Reference: 2000-01-01 (Gregorian) = 戊午 (Wu Wu) - stem 4, branch 6
        // This is a well-established reference point in Chinese astrology
        var referenceDate = new DateOnly(2000, 1, 1);
        var referenceStem = 4;   // 戊
        var referenceBranch = 6; // 午
        
        var daysDiff = date.DayNumber - referenceDate.DayNumber;
        
        // Calculate stem and branch indices
        var stemIndex = (referenceStem + daysDiff) % 10;
        var branchIndex = (referenceBranch + daysDiff) % 12;
        
        // Handle negative values (for dates before 2000-01-01)
        if (stemIndex < 0) stemIndex += 10;
        if (branchIndex < 0) branchIndex += 12;
        
        return new PillarInfo(stemIndex, branchIndex);
    }
    
    private static PillarInfo CalculateHourPillar(int dayStemIndex, int hour)
    {
        // Hour pillar calculation:
        // Day stem determines the starting point for hour stems
        // 日上起时法：甲己之日起甲子（甲日和己日从甲子时开始）
        int hourStemStart = dayStemIndex switch
        {
            0 or 5 => 0,  // 甲己 starts from 甲
            1 or 6 => 2,  // 乙庚 starts from 丙
            2 or 7 => 4,  // 丙辛 starts from 戊
            3 or 8 => 6,  // 丁壬 starts from 庚
            4 or 9 => 8,  // 戊癸 starts from 壬
            _ => 0
        };
        
        // Hour to branch mapping (12 double-hours in Chinese system)
        // 23-01: 子(0), 01-03: 丑(1), 03-05: 寅(2), 05-07: 卯(3), etc.
        var hourBranchIndex = ((hour + 1) / 2) % 12;
        var hourStemIndex = (hourStemStart + hourBranchIndex) % 10;
        
        return new PillarInfo(hourStemIndex, hourBranchIndex);
    }
    
    #endregion
}

/// <summary>
/// Information for a single pillar (stem + branch combination)
/// </summary>
public class PillarInfo
{
    public int StemIndex { get; }
    public int BranchIndex { get; }
    
    // Chinese characters
    public string StemChinese { get; }
    public string BranchChinese { get; }
    
    // Pinyin
    public string StemPinyin { get; }
    public string BranchPinyin { get; }
    
    // Stem Attributes
    public string YinYang { get; }  // Yang or Yin (for Stem)
    public string Element { get; }  // Wood, Fire, Earth, Metal, Water (for Stem)
    public string Direction { get; }  // e.g., "East 1" (for Stem)
    
    // Branch Attributes
    public string BranchYinYang { get; }  // Yang or Yin (for Branch)
    public string BranchElement { get; }  // Wood, Fire, Earth, Metal, Water (for Branch)
    public string BranchDirection { get; }  // e.g., "North 1" (for Branch)
    public string BranchZodiac { get; }  // Zodiac animal (e.g., "Rat", "Ox")
    
    private static readonly string[] HeavenlyStems = { "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸" };
    private static readonly string[] HeavenlyStemsPinyin = { "Jia", "Yi", "Bing", "Ding", "Wu", "Ji", "Geng", "Xin", "Ren", "Gui" };
    private static readonly string[] EarthlyBranches = { "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥" };
    private static readonly string[] EarthlyBranchesPinyin = { "Zi", "Chou", "Yin", "Mao", "Chen", "Si", "Wu", "Wei", "Shen", "You", "Xu", "Hai" };
    
    // Stem attributes: YinYang, Element, Direction
    private static readonly (string yinYang, string element, string direction)[] StemAttributes = {
        ("Yang", "Wood", "East 1"),     // 甲
        ("Yin", "Wood", "East 2"),      // 乙
        ("Yang", "Fire", "South 1"),    // 丙
        ("Yin", "Fire", "South 2"),     // 丁
        ("Yang", "Earth", "Centre"),    // 戊
        ("Yin", "Earth", "Centre"),     // 己
        ("Yang", "Metal", "West 1"),    // 庚
        ("Yin", "Metal", "West 2"),     // 辛
        ("Yang", "Water", "North 1"),   // 壬
        ("Yin", "Water", "North 2")     // 癸
    };
    
    // Branch attributes: YinYang, Element (from zodiac animal)
    private static readonly (string yinYang, string element, string direction)[] BranchAttributes = {
        ("Yang", "Water", "North 1"),       // 子 Rat
        ("Yin", "Earth", "Centre"),         // 丑 Ox
        ("Yang", "Wood", "East 1"),         // 寅 Tiger
        ("Yin", "Wood", "East 2"),          // 卯 Rabbit
        ("Yang", "Earth", "Centre"),        // 辰 Dragon
        ("Yin", "Fire", "South 2"),         // 巳 Snake
        ("Yang", "Fire", "South 1"),        // 午 Horse
        ("Yin", "Earth", "Centre"),         // 未 Goat
        ("Yang", "Metal", "West 1"),        // 申 Monkey
        ("Yin", "Metal", "West 2"),         // 酉 Rooster
        ("Yang", "Earth", "Centre"),        // 戌 Dog
        ("Yin", "Water", "North 2")         // 亥 Pig
    };
    
    // Branch zodiac animals (must match EarthlyBranches order)
    private static readonly string[] BranchZodiacs = { 
        "Rat",      // 子 (index 0)
        "Ox",       // 丑 (index 1)
        "Tiger",    // 寅 (index 2)
        "Rabbit",   // 卯 (index 3)
        "Dragon",   // 辰 (index 4)
        "Snake",    // 巳 (index 5)
        "Horse",    // 午 (index 6)
        "Goat",     // 未 (index 7)
        "Monkey",   // 申 (index 8)
        "Rooster",  // 酉 (index 9)
        "Dog",      // 戌 (index 10)
        "Pig"       // 亥 (index 11)
    };
    
    public PillarInfo(int stemIndex, int branchIndex)
    {
        StemIndex = stemIndex;
        BranchIndex = branchIndex;
        
        StemChinese = HeavenlyStems[stemIndex];
        BranchChinese = EarthlyBranches[branchIndex];
        
        StemPinyin = HeavenlyStemsPinyin[stemIndex];
        BranchPinyin = EarthlyBranchesPinyin[branchIndex];
        
        // Stem attributes
        var stemAttr = StemAttributes[stemIndex];
        YinYang = stemAttr.yinYang;
        Element = stemAttr.element;
        Direction = stemAttr.direction;
        
        // Branch attributes
        var branchAttr = BranchAttributes[branchIndex];
        BranchYinYang = branchAttr.yinYang;
        BranchElement = branchAttr.element;
        BranchDirection = branchAttr.direction;
        BranchZodiac = BranchZodiacs[branchIndex];
    }
    
    /// <summary>
    /// Get formatted string with multilingual support
    /// </summary>
    public string GetFormattedString(string language = "en")
    {
        var yinYangTranslated = TranslateYinYang(YinYang, language);
        var elementTranslated = TranslateElement(Element, language);
        var directionTranslated = TranslateDirection(Direction, language);
        
        return $"{StemChinese}{BranchChinese} ({StemPinyin} {BranchPinyin}) · {yinYangTranslated} {elementTranslated} · {directionTranslated}";
    }
    
    private static string TranslateYinYang(string yinYang, string language) => language switch
    {
        "zh-tw" or "zh" => yinYang == "Yang" ? "陽" : "陰",
        "es" => yinYang == "Yang" ? "Yang" : "Yin",
        _ => yinYang  // English default
    };
    
    private static string TranslateElement(string element, string language) => (element, language) switch
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
        _ => element  // English default
    };
    
    private static string TranslateDirection(string direction, string language) => (direction, language) switch
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
        _ => direction  // English default
    };
}

public static partial class LumenCalculator
{
    #region Enum Parsers
    
    /// <summary>
    /// Parse zodiac sign name to enum
    /// </summary>
    public static ZodiacSignEnum ParseZodiacSignEnum(string zodiacSign)
    {
        if (string.IsNullOrWhiteSpace(zodiacSign)) return ZodiacSignEnum.Unknown;
        
        return zodiacSign.Trim() switch
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
    
    /// <summary>
    /// Parse Chinese zodiac animal to enum
    /// </summary>
    public static ChineseZodiacEnum ParseChineseZodiacEnum(string chineseZodiac)
    {
        if (string.IsNullOrWhiteSpace(chineseZodiac)) return ChineseZodiacEnum.Unknown;
        
        // Extract animal name (e.g., "Wood Pig" -> "Pig")
        var parts = chineseZodiac.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var animalName = parts.Length > 0 ? parts[^1] : chineseZodiac;
        
        return animalName.Trim() switch
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
    
    #endregion
}

/// <summary>
/// Complete Four Pillars information
/// </summary>
public class FourPillarsInfo
{
    public PillarInfo YearPillar { get; set; } = null!;
    public PillarInfo MonthPillar { get; set; } = null!;
    public PillarInfo DayPillar { get; set; } = null!;
    public PillarInfo? HourPillar { get; set; }  // Optional if no birth time
}

