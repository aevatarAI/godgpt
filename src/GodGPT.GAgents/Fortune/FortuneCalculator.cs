using System;
using System.Collections.Generic;

namespace Aevatar.Application.Grains.Fortune;

/// <summary>
/// Fortune calculation utilities for accurate astrological calculations
/// </summary>
public static class FortuneCalculator
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
    /// Calculate Chinese zodiac animal based on birth year
    /// </summary>
    public static string CalculateChineseZodiac(int birthYear)
    {
        // 1900 is Rat year (子鼠)
        var index = (birthYear - 1900) % 12;
        if (index < 0) index += 12;
        
        return ChineseAnimals[index];
    }
    
    #endregion
    
    #region Chinese Element (Five Elements)
    
    /// <summary>
    /// Calculate Chinese element (Wu Xing) based on birth year using Nayin system
    /// Returns simplified element name only (Metal/Wood/Water/Fire/Earth)
    /// </summary>
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
    /// </summary>
    public static string GetChineseZodiacWithElement(int birthYear)
    {
        var element = CalculateChineseElement(birthYear);
        var animal = CalculateChineseZodiac(birthYear);
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
    /// </summary>
    /// <returns>Format: "乙 巳 Yi Si"</returns>
    public static string CalculateStemsAndBranches(int year)
    {
        // Year 4 AD is 甲子 (Jia Zi), the start of the 60-year cycle
        var stemIndex = (year - 4) % 10;
        var branchIndex = (year - 4) % 12;
        
        if (stemIndex < 0) stemIndex += 10;
        if (branchIndex < 0) branchIndex += 12;
        
        return $"{HeavenlyStems[stemIndex]} {EarthlyBranches[branchIndex]} {HeavenlyStemsPinyin[stemIndex]} {EarthlyBranchesPinyin[branchIndex]}";
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
    /// </summary>
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
}

