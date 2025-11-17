using Microsoft.Extensions.Logging;
using SwissEphNet;
using System;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;

namespace GodGPT.GAgents.Lumen;

/// <summary>
/// Western astrology calculator using Swiss Ephemeris
/// Calculates Sun, Moon, and Rising signs based on birth date, time, and location
/// </summary>
public class WesternAstrologyCalculator
{
    private readonly ILogger<WesternAstrologyCalculator> _logger;
    private readonly SwissEph _swissEph;
    private readonly IGodChat _godChat; // For LLM-based coordinate lookup
    
    // Zodiac sign names in order
    private static readonly string[] ZodiacSigns = new[]
    {
        "Aries", "Taurus", "Gemini", "Cancer", "Leo", "Virgo",
        "Libra", "Scorpio", "Sagittarius", "Capricorn", "Aquarius", "Pisces"
    };
    
    public WesternAstrologyCalculator(
        ILogger<WesternAstrologyCalculator> logger,
        IGodChat godChat)
    {
        _logger = logger;
        _godChat = godChat;
        _swissEph = new SwissEph();
    }
    
    /// <summary>
    /// Calculate all three signs: Sun, Moon, and Rising
    /// </summary>
    public async Task<(string sunSign, string moonSign, string risingSign)> CalculateSignsAsync(
        DateOnly birthDate,
        TimeOnly birthTime,
        string birthCity)
    {
        try
        {
            // Step 1: Get coordinates from LLM
            var (latitude, longitude) = await GetCityCoordinatesAsync(birthCity);
            
            _logger.LogInformation($"[WesternAstrology] Calculating signs for {birthCity} ({latitude}, {longitude})");
            
            // Step 2: Convert to Julian Day
            var birthDateTime = birthDate.ToDateTime(birthTime);
            double julianDay = ToJulianDay(birthDateTime);
            
            // Step 3: Calculate Sun Sign
            string sunSign = CalculateSunSign(julianDay);
            
            // Step 4: Calculate Moon Sign
            string moonSign = CalculateMoonSign(julianDay);
            
            // Step 5: Calculate Rising Sign (Ascendant)
            string risingSign = CalculateRisingSign(julianDay, latitude, longitude);
            
            _logger.LogInformation($"[WesternAstrology] Results - Sun: {sunSign}, Moon: {moonSign}, Rising: {risingSign}");
            
            return (sunSign, moonSign, risingSign);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[WesternAstrology] Failed to calculate signs for {birthCity}");
            // Fallback: at least return Sun sign based on date
            string sunSign = CalculateSunSignSimple(birthDate);
            return (sunSign, sunSign, sunSign); // Use Sun sign as fallback for all
        }
    }
    
    /// <summary>
    /// Get city coordinates using LLM
    /// Returns (latitude, longitude) in decimal degrees
    /// </summary>
    private async Task<(double latitude, double longitude)> GetCityCoordinatesAsync(string cityName)
    {
        try
        {
            string prompt = $@"Return ONLY the geographic coordinates of ""{cityName}"" in JSON format.
Output format (no other text):
{{""latitude"": 39.9042, ""longitude"": 116.4074}}

City: {cityName}
JSON:";

            var response = await _godChat.AskAsync(prompt, AgentEnum.LUMEN);
            
            // Parse JSON response
            var json = JsonDocument.Parse(response);
            double latitude = json.RootElement.GetProperty("latitude").GetDouble();
            double longitude = json.RootElement.GetProperty("longitude").GetDouble();
            
            _logger.LogInformation($"[WesternAstrology] LLM returned coordinates for {cityName}: ({latitude}, {longitude})");
            
            return (latitude, longitude);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"[WesternAstrology] Failed to get coordinates from LLM for {cityName}, using default");
            // Fallback: Beijing coordinates
            return (39.9042, 116.4074);
        }
    }
    
    /// <summary>
    /// Calculate Sun Sign using Swiss Ephemeris
    /// </summary>
    private string CalculateSunSign(double julianDay)
    {
        try
        {
            double[] positions = new double[6];
            string errorMsg = null;
            
            int result = _swissEph.swe_calc_ut(
                julianDay,
                SwissEph.SE_SUN,
                SwissEph.SEFLG_SWIEPH,
                positions,
                ref errorMsg);
            
            if (result < 0)
            {
                _logger.LogWarning($"[WesternAstrology] Swiss Ephemeris Sun calculation failed: {errorMsg}");
                return "Aries"; // Fallback
            }
            
            // positions[0] is longitude in degrees (0-360)
            // Each zodiac sign is 30 degrees
            int signIndex = (int)(positions[0] / 30.0);
            return ZodiacSigns[signIndex % 12];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WesternAstrology] Sun sign calculation error");
            return "Aries";
        }
    }
    
    /// <summary>
    /// Calculate Moon Sign using Swiss Ephemeris
    /// </summary>
    private string CalculateMoonSign(double julianDay)
    {
        try
        {
            double[] positions = new double[6];
            string errorMsg = null;
            
            int result = _swissEph.swe_calc_ut(
                julianDay,
                SwissEph.SE_MOON,
                SwissEph.SEFLG_SWIEPH,
                positions,
                ref errorMsg);
            
            if (result < 0)
            {
                _logger.LogWarning($"[WesternAstrology] Swiss Ephemeris Moon calculation failed: {errorMsg}");
                return "Aries"; // Fallback
            }
            
            int signIndex = (int)(positions[0] / 30.0);
            return ZodiacSigns[signIndex % 12];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WesternAstrology] Moon sign calculation error");
            return "Aries";
        }
    }
    
    /// <summary>
    /// Calculate Rising Sign (Ascendant) using Swiss Ephemeris
    /// </summary>
    private string CalculateRisingSign(double julianDay, double latitude, double longitude)
    {
        try
        {
            double[] cusps = new double[13];
            double[] ascmc = new double[10];
            string errorMsg = null;
            
            int result = _swissEph.swe_houses(
                julianDay,
                latitude,
                longitude,
                'P', // Placidus house system (most common)
                cusps,
                ascmc);
            
            if (result < 0)
            {
                _logger.LogWarning($"[WesternAstrology] Swiss Ephemeris Ascendant calculation failed");
                return "Aries"; // Fallback
            }
            
            // ascmc[0] is the Ascendant (Rising Sign) longitude
            int signIndex = (int)(ascmc[0] / 30.0);
            return ZodiacSigns[signIndex % 12];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WesternAstrology] Rising sign calculation error");
            return "Aries";
        }
    }
    
    /// <summary>
    /// Convert DateTime to Julian Day Number (for Swiss Ephemeris)
    /// </summary>
    private double ToJulianDay(DateTime dateTime)
    {
        // Convert to UTC for astronomical calculations
        dateTime = dateTime.ToUniversalTime();
        
        int year = dateTime.Year;
        int month = dateTime.Month;
        int day = dateTime.Day;
        double hour = dateTime.Hour + dateTime.Minute / 60.0 + dateTime.Second / 3600.0;
        
        return _swissEph.swe_julday(year, month, day, hour, SwissEph.SE_GREG_CAL);
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
}

