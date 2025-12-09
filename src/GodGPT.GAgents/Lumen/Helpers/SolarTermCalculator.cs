using System;

namespace Aevatar.Application.Grains.Lumen.Helpers;

/// <summary>
/// Solar Term (节气) Calculator for accurate Four Pillars calculation
/// </summary>
public static class SolarTermCalculator
{
    private static readonly string[] SolarTermNames = 
    {
        "立春", "雨水", "惊蛰", "春分", "清明", "谷雨",
        "立夏", "小满", "芒种", "夏至", "小暑", "大暑",
        "立秋", "处暑", "白露", "秋分", "寒露", "霜降",
        "立冬", "小雪", "大雪", "冬至", "小寒", "大寒"
    };

    /// <summary>
    /// Calculate solar term month index (0-11) based on date and time
    /// Month boundaries are determined by Major Solar Terms (节):
    /// 立春, 惊蛰, 清明, 立夏, 芒种, 小暑, 立秋, 白露, 寒露, 立冬, 大雪, 小寒
    /// </summary>
    public static int GetSolarTermMonth(DateTime utcDateTime)
    {
        var year = utcDateTime.Year;
        var month = utcDateTime.Month;
        var day = utcDateTime.Day;
        var hour = utcDateTime.Hour;
        var minute = utcDateTime.Minute;

        // Approximate solar term dates for each month
        // These are the starting dates of each solar month (节)
        var lichun = GetLiChunDateTime(year);        // 立春 - Month 0 (寅月)
        var jingzhe = GetJingZheDateTime(year);      // 惊蛰 - Month 1 (卯月)
        var qingming = GetQingMingDateTime(year);    // 清明 - Month 2 (辰月)
        var lixia = GetLiXiaDateTime(year);          // 立夏 - Month 3 (巳月)
        var mangzhong = GetMangZhongDateTime(year);  // 芒种 - Month 4 (午月)
        var xiaoshu = GetXiaoShuDateTime(year);      // 小暑 - Month 5 (未月)
        var liqiu = GetLiQiuDateTime(year);          // 立秋 - Month 6 (申月)
        var bailu = GetBaiLuDateTime(year);          // 白露 - Month 7 (酉月)
        var hanlu = GetHanLuDateTime(year);          // 寒露 - Month 8 (戌月)
        var lidong = GetLiDongDateTime(year);        // 立冬 - Month 9 (亥月)
        var daxue = GetDaXueDateTime(year);          // 大雪 - Month 10 (子月)
        var xiaohan = GetXiaoHanDateTime(year);      // 小寒 - Month 11 (丑月)

        // Check if we need to look at previous year's 小寒
        if (utcDateTime < lichun)
        {
            var prevXiaohan = GetXiaoHanDateTime(year - 1);
            if (utcDateTime >= prevXiaohan)
            {
                return 11; // 丑月 (previous year's last month)
            }
            
            // If before previous year's 小寒, use 大雪 of previous year
            var prevDaxue = GetDaXueDateTime(year - 1);
            if (utcDateTime >= prevDaxue)
            {
                return 10; // 子月
            }
        }

        // Determine month based on solar terms
        if (utcDateTime >= xiaohan) return 11; // 丑月
        if (utcDateTime >= daxue) return 10;   // 子月
        if (utcDateTime >= lidong) return 9;   // 亥月
        if (utcDateTime >= hanlu) return 8;    // 戌月
        if (utcDateTime >= bailu) return 7;    // 酉月
        if (utcDateTime >= liqiu) return 6;    // 申月
        if (utcDateTime >= xiaoshu) return 5;  // 未月
        if (utcDateTime >= mangzhong) return 4; // 午月
        if (utcDateTime >= lixia) return 3;    // 巳月
        if (utcDateTime >= qingming) return 2; // 辰月
        if (utcDateTime >= jingzhe) return 1;  // 卯月
        if (utcDateTime >= lichun) return 0;   // 寅月

        // Should not reach here, but fallback to previous year's last month
        return 11;
    }

    /// <summary>
    /// Calculate approximate solar term datetime using astronomical formula
    /// Based on the mean solar term calculation
    /// </summary>
    private static DateTime GetLiChunDateTime(int year)
    {
        // Try to get from JSON data first
        var fromData = SolarTermData.GetSolarTerm(year, "lichun");
        if (fromData.HasValue)
        {
            return fromData.Value;
        }
        
        // Fallback to approximation
        return ApproximateLiChun(year);
    }
    
    private static DateTime ApproximateLiChun(int year)
    {
        // Simplified calculation for years without precise data
        // 立春 formula: (year - 2000) * 0.2422 + 3.87
        var dayOfMonth = (year - 2000) * 0.2422 + 3.87;
        
        // Clamp to reasonable range
        dayOfMonth = Math.Clamp(dayOfMonth, 3, 5);
        
        var day = (int)Math.Floor(dayOfMonth);
        var hour = (dayOfMonth - day) * 24;
        
        // Ensure hour is within valid range (0-23)
        hour = Math.Clamp(hour, 0, 23);
        
        return new DateTime(year, 2, day, (int)hour, 0, 0, DateTimeKind.Utc);
    }

    private static DateTime GetJingZheDateTime(int year)
    {
        var fromData = SolarTermData.GetSolarTerm(year, "jingzhe");
        return fromData ?? ApproximateSolarTerm(year, 3, 5, 0.5698 + 0.2422 * (year - 2000));
    }

    private static DateTime GetQingMingDateTime(int year)
    {
        var fromData = SolarTermData.GetSolarTerm(year, "qingming");
        return fromData ?? ApproximateSolarTerm(year, 4, 4, 0.2422 * (year - 2000) + 4.81);
    }

    private static DateTime GetLiXiaDateTime(int year)
    {
        var fromData = SolarTermData.GetSolarTerm(year, "lixia");
        return fromData ?? ApproximateSolarTerm(year, 5, 5, 0.2422 * (year - 2000) + 5.52);
    }

    private static DateTime GetMangZhongDateTime(int year)
    {
        var fromData = SolarTermData.GetSolarTerm(year, "mangzhong");
        return fromData ?? ApproximateSolarTerm(year, 6, 5, 0.2422 * (year - 2000) + 5.678);
    }

    private static DateTime GetXiaoShuDateTime(int year)
    {
        var fromData = SolarTermData.GetSolarTerm(year, "xiaoshu");
        return fromData ?? ApproximateSolarTerm(year, 7, 6, 0.2422 * (year - 2000) + 7.108);
    }

    private static DateTime GetLiQiuDateTime(int year)
    {
        var fromData = SolarTermData.GetSolarTerm(year, "liqiu");
        return fromData ?? ApproximateSolarTerm(year, 8, 7, 0.2422 * (year - 2000) + 7.5);
    }

    private static DateTime GetBaiLuDateTime(int year)
    {
        var fromData = SolarTermData.GetSolarTerm(year, "bailu");
        return fromData ?? ApproximateSolarTerm(year, 9, 7, 0.2422 * (year - 2000) + 7.646);
    }

    private static DateTime GetHanLuDateTime(int year)
    {
        var fromData = SolarTermData.GetSolarTerm(year, "hanlu");
        return fromData ?? ApproximateSolarTerm(year, 10, 8, 0.2422 * (year - 2000) + 8.318);
    }

    private static DateTime GetLiDongDateTime(int year)
    {
        var fromData = SolarTermData.GetSolarTerm(year, "lidong");
        return fromData ?? ApproximateSolarTerm(year, 11, 7, 0.2422 * (year - 2000) + 7.438);
    }

    private static DateTime GetDaXueDateTime(int year)
    {
        var fromData = SolarTermData.GetSolarTerm(year, "daxue");
        return fromData ?? ApproximateSolarTerm(year, 12, 6, 0.2422 * (year - 2000) + 7.18);
    }

    private static DateTime GetXiaoHanDateTime(int year)
    {
        // 小寒 belongs to next year
        var fromData = SolarTermData.GetSolarTerm(year + 1, "xiaohan");
        return fromData ?? ApproximateSolarTerm(year + 1, 1, 5, 0.2422 * ((year + 1) - 2000) + 5.4055);
    }
    
    private static DateTime ApproximateSolarTerm(int year, int month, int baseDay, double dayOfMonth)
    {
        // Clamp dayOfMonth to reasonable range first
        dayOfMonth = Math.Clamp(dayOfMonth, baseDay - 1, baseDay + 2);
        
        var day = (int)Math.Floor(dayOfMonth);
        var hour = (dayOfMonth - day) * 24;
        
        // Ensure hour is within valid range (0-23)
        hour = Math.Clamp(hour, 0, 23);
        
        return new DateTime(year, month, day, (int)hour, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>
    /// Calculate solar term offset in minutes using simplified astronomical formula
    /// This provides approximate dates (accurate to within a few hours)
    /// </summary>
    private static double CalculateSolarTermOffset(int year, int termIndex)
    {
        // Simplified calculation based on year offset from 2000
        var yearOffset = year - 2000;
        
        // Each year, solar terms shift slightly due to leap years and orbital variations
        // Approximate: ~6 hours shift every 4 years (leap year cycle)
        var leapYearAdjustment = (yearOffset / 4.0) * 360; // minutes
        
        // Century-scale adjustment (very small)
        var centuryAdjustment = (yearOffset / 100.0) * 30; // minutes
        
        // Combine adjustments
        var totalOffset = leapYearAdjustment + centuryAdjustment;
        
        // Add small random variation based on term index to simulate orbital eccentricity
        var termVariation = Math.Sin(termIndex * Math.PI / 12) * 60; // +/- 1 hour
        
        return totalOffset + termVariation;
    }

    /// <summary>
    /// Get the solar term name by index
    /// </summary>
    public static string GetSolarTermName(int index)
    {
        if (index < 0 || index >= SolarTermNames.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Solar term index must be between 0 and 23");
        }
        
        return SolarTermNames[index];
    }
}

