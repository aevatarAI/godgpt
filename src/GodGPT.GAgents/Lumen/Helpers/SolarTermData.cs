using System;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Options;

namespace Aevatar.Application.Grains.Lumen.Helpers;

/// <summary>
/// Solar term data loader and cache
/// Loads precise solar term data from JSON file (1900-2100)
/// Thread-safe singleton: data is loaded once on first access and cached for the application lifetime
/// </summary>
public static class SolarTermData
{
    // Default fallback path if options are not configured
    private const string DefaultDataFilePath = "/app/lumen/solar-terms-full.json";
    
    // Lazy<T> ensures thread-safe, single initialization - loaded once on first access
    private static readonly Lazy<Dictionary<int, YearSolarTerms>> _data = 
        new(() => LoadData(), LazyThreadSafetyMode.ExecutionAndPublication);
    
    // Static configuration holder (set by DI during startup)
    private static string? _configuredFilePath;
    
    /// <summary>
    /// Configure file path from DI options (called during startup)
    /// </summary>
    public static void Configure(string? filePath)
    {
        _configuredFilePath = filePath;
    }
    
    /// <summary>
    /// Get cached solar term data for all years (thread-safe)
    /// </summary>
    public static Dictionary<int, YearSolarTerms> Data => _data.Value;
    
    /// <summary>
    /// Get solar term DateTime for specific year and term
    /// </summary>
    public static DateTime? GetSolarTerm(int year, string termName)
    {
        if (!Data.TryGetValue(year, out var yearData))
        {
            return null;
        }
        
        return termName.ToLower() switch
        {
            "lichun" or "立春" => yearData.LiChun,
            "jingzhe" or "惊蛰" => yearData.JingZhe,
            "qingming" or "清明" => yearData.QingMing,
            "lixia" or "立夏" => yearData.LiXia,
            "mangzhong" or "芒种" => yearData.MangZhong,
            "xiaoshu" or "小暑" => yearData.XiaoShu,
            "liqiu" or "立秋" => yearData.LiQiu,
            "bailu" or "白露" => yearData.BaiLu,
            "hanlu" or "寒露" => yearData.HanLu,
            "lidong" or "立冬" => yearData.LiDong,
            "daxue" or "大雪" => yearData.DaXue,
            "xiaohan" or "小寒" => yearData.XiaoHan,
            _ => null
        };
    }
    
    private static Dictionary<int, YearSolarTerms> LoadData()
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Use configured path from options, fallback to default
            var filePath = !string.IsNullOrWhiteSpace(_configuredFilePath) 
                ? _configuredFilePath 
                : DefaultDataFilePath;
            
            Console.WriteLine($"[SolarTermData] Loading solar term data from: {filePath}");
            
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[SolarTermData] ⚠️ WARNING: Solar term data file not found at {filePath}. Using fallback calculation (less accurate).");
                return new Dictionary<int, YearSolarTerms>();
            }
            
            var json = File.ReadAllText(filePath);
            var result = ParseJsonData(json);
            
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            Console.WriteLine($"[SolarTermData] ✅ Loaded {result.Count} years of solar term data in {elapsed:F2}ms (cached for application lifetime)");
            
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SolarTermData] ❌ Error loading solar term data: {ex.Message}");
            Console.WriteLine($"[SolarTermData] Stack trace: {ex.StackTrace}");
            return new Dictionary<int, YearSolarTerms>();
        }
    }
    
    private static Dictionary<int, YearSolarTerms> ParseJsonData(string json)
    {
        var result = new Dictionary<int, YearSolarTerms>();
        
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            
            // New compact format: direct year->terms mapping (no "solar_terms" wrapper)
            foreach (var yearProp in root.EnumerateObject())
            {
                if (int.TryParse(yearProp.Name, out var year))
                {
                    var yearData = ParseYearData(year, yearProp.Value);
                    if (yearData != null)
                    {
                        result[year] = yearData;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SolarTermData] Error parsing JSON: {ex.Message}");
        }
        
        return result;
    }
    
    private static YearSolarTerms? ParseYearData(int year, JsonElement yearElement)
    {
        try
        {
            return new YearSolarTerms
            {
                LiChun = ParseCompactDateTime(year, yearElement, "lichun"),
                JingZhe = ParseCompactDateTime(year, yearElement, "jingzhe"),
                QingMing = ParseCompactDateTime(year, yearElement, "qingming"),
                LiXia = ParseCompactDateTime(year, yearElement, "lixia"),
                MangZhong = ParseCompactDateTime(year, yearElement, "mangzhong"),
                XiaoShu = ParseCompactDateTime(year, yearElement, "xiaoshu"),
                LiQiu = ParseCompactDateTime(year, yearElement, "liqiu"),
                BaiLu = ParseCompactDateTime(year, yearElement, "bailu"),
                HanLu = ParseCompactDateTime(year, yearElement, "hanlu"),
                LiDong = ParseCompactDateTime(year, yearElement, "lidong"),
                DaXue = ParseCompactDateTime(year, yearElement, "daxue"),
                XiaoHan = ParseCompactDateTime(year, yearElement, "xiaohan")
            };
        }
        catch
        {
            return null;
        }
    }
    
    private static DateTime? ParseCompactDateTime(int year, JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            var timeStr = prop.GetString();
            if (!string.IsNullOrEmpty(timeStr))
            {
                try
                {
                    // Parse compact format: "MM-DD HH:MM:SS"
                    var parts = timeStr.Split(' ');
                    if (parts.Length == 2)
                    {
                        var dateParts = parts[0].Split('-');
                        var timeParts = parts[1].Split(':');
                        
                        if (dateParts.Length == 2 && timeParts.Length == 3)
                        {
                            var month = int.Parse(dateParts[0]);
                            var day = int.Parse(dateParts[1]);
                            var hour = int.Parse(timeParts[0]);
                            var minute = int.Parse(timeParts[1]);
                            var second = int.Parse(timeParts[2]);
                            
                            // Special case: xiaohan (小寒) is in January of next year
                            var actualYear = propertyName == "xiaohan" && month == 1 ? year + 1 : year;
                            
                            return new DateTime(actualYear, month, day, hour, minute, second, DateTimeKind.Utc);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SolarTermData] Error parsing {propertyName} for year {year}: {ex.Message}");
                }
            }
        }
        return null;
    }
}

/// <summary>
/// Solar terms for a specific year
/// </summary>
public class YearSolarTerms
{
    public DateTime? LiChun { get; set; }      // 立春
    public DateTime? JingZhe { get; set; }     // 惊蛰
    public DateTime? QingMing { get; set; }    // 清明
    public DateTime? LiXia { get; set; }       // 立夏
    public DateTime? MangZhong { get; set; }   // 芒种
    public DateTime? XiaoShu { get; set; }     // 小暑
    public DateTime? LiQiu { get; set; }       // 立秋
    public DateTime? BaiLu { get; set; }       // 白露
    public DateTime? HanLu { get; set; }       // 寒露
    public DateTime? LiDong { get; set; }      // 立冬
    public DateTime? DaXue { get; set; }       // 大雪
    public DateTime? XiaoHan { get; set; }     // 小寒 (belongs to next year)
}

