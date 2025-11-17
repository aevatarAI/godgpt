using Microsoft.Extensions.Logging;
using SwissEphNet;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace Aevatar.Application.Grains.Lumen;

/// <summary>
/// Western astrology calculator using Swiss Ephemeris
/// Calculates Sun, Moon, and Rising signs based on birth date, time, and location
/// </summary>
public class WesternAstrologyCalculator
{
    private readonly ILogger<WesternAstrologyCalculator> _logger;
    private readonly SwissEph _swissEph;
    
    // Zodiac sign names in order
    private static readonly string[] ZodiacSigns = new[]
    {
        "Aries", "Taurus", "Gemini", "Cancer", "Leo", "Virgo",
        "Libra", "Scorpio", "Sagittarius", "Capricorn", "Aquarius", "Pisces"
    };
    
    // City coordinates dictionary (major cities worldwide)
    private static readonly Dictionary<string, (double latitude, double longitude)> CityCoordinates = new()
    {
        // Asia
        ["beijing"] = (39.9042, 116.4074),
        ["shanghai"] = (31.2304, 121.4737),
        ["tokyo"] = (35.6762, 139.6503),
        ["hong kong"] = (22.3193, 114.1694),
        ["singapore"] = (1.3521, 103.8198),
        ["seoul"] = (37.5665, 126.9780),
        ["bangkok"] = (13.7563, 100.5018),
        ["dubai"] = (25.2048, 55.2708),
        ["mumbai"] = (19.0760, 72.8777),
        ["delhi"] = (28.6139, 77.2090),
        // North America
        ["new york"] = (40.7128, -74.0060),
        ["los angeles"] = (34.0522, -118.2437),
        ["chicago"] = (41.8781, -87.6298),
        ["san francisco"] = (37.7749, -122.4194),
        ["toronto"] = (43.6532, -79.3832),
        ["vancouver"] = (49.2827, -123.1207),
        ["mexico city"] = (19.4326, -99.1332),
        // Europe
        ["london"] = (51.5074, -0.1278),
        ["paris"] = (48.8566, 2.3522),
        ["berlin"] = (52.5200, 13.4050),
        ["rome"] = (41.9028, 12.4964),
        ["madrid"] = (40.4168, -3.7038),
        ["moscow"] = (55.7558, 37.6173),
        ["amsterdam"] = (52.3676, 4.9041),
        // Oceania
        ["sydney"] = (-33.8688, 151.2093),
        ["melbourne"] = (-37.8136, 144.9631),
        ["auckland"] = (-36.8485, 174.7633),
        // South America
        ["sao paulo"] = (-23.5505, -46.6333),
        ["rio de janeiro"] = (-22.9068, -43.1729),
        ["buenos aires"] = (-34.6037, -58.3816),
        // Africa
        ["cairo"] = (30.0444, 31.2357),
        ["johannesburg"] = (-26.2041, 28.0473),
        ["lagos"] = (6.5244, 3.3792)
    };
    
    public WesternAstrologyCalculator(
        ILogger<WesternAstrologyCalculator> logger)
    {
        _logger = logger;
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
    /// Get city coordinates from dictionary
    /// Returns (latitude, longitude) in decimal degrees
    /// </summary>
    private Task<(double latitude, double longitude)> GetCityCoordinatesAsync(string cityName)
    {
        try
        {
            var normalizedCity = cityName?.ToLowerInvariant().Trim() ?? "";
            
            if (CityCoordinates.TryGetValue(normalizedCity, out var coordinates))
            {
                _logger.LogInformation($"[WesternAstrology] Found coordinates for {cityName}: ({coordinates.latitude}, {coordinates.longitude})");
                return Task.FromResult(coordinates);
            }
            
            // Try partial match
            foreach (var (key, value) in CityCoordinates)
            {
                if (normalizedCity.Contains(key) || key.Contains(normalizedCity))
                {
                    _logger.LogInformation($"[WesternAstrology] Partial match for {cityName} -> {key}: ({value.latitude}, {value.longitude})");
                    return Task.FromResult(value);
                }
            }
            
            _logger.LogWarning($"[WesternAstrology] City {cityName} not found in dictionary, using Beijing as default");
            // Fallback: Beijing coordinates
            return Task.FromResult((39.9042, 116.4074));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"[WesternAstrology] Failed to get coordinates for {cityName}, using default");
            return Task.FromResult((39.9042, 116.4074));
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

