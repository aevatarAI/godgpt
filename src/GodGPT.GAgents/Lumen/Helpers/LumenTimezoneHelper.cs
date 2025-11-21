using System;
using GeoTimeZone;
using TimeZoneConverter;

namespace Aevatar.Application.Grains.Lumen.Helpers;

public static class LumenTimezoneHelper
{
    /// <summary>
    /// Converts a local "wall clock" time at a specific location to UTC time, considering historical timezone rules and DST.
    /// </summary>
    /// <param name="localDateTime">The local date and time (Kind should be Unspecified)</param>
    /// <param name="latitude">Location latitude</param>
    /// <param name="longitude">Location longitude</param>
    /// <returns>Tuple containing UTC DateTime, the Offset used, and the Timezone ID</returns>
    public static (DateTime utcTime, TimeSpan offset, string timezoneId) GetUtcTimeFromLocal(DateTime localDateTime, double latitude, double longitude)
    {
        try 
        {
            // 1. Get IANA Timezone ID from coordinates
            // Result returns the IANA ID (e.g. "Asia/Shanghai")
            var timezoneId = TimeZoneLookup.GetTimeZone(latitude, longitude).Result;
            
            // 2. Convert to .NET TimeZoneInfo
            // TZConvert handles IANA vs Windows ID conversion cross-platform
            var tzInfo = TZConvert.GetTimeZoneInfo(timezoneId);
            
            // 3. Calculate Offset for the specific date
            // GetUtcOffset handles historical rules and DST if the OS/library has the data
            var offset = tzInfo.GetUtcOffset(localDateTime);
            
            // 4. Create DateTimeOffset to represent the exact moment
            var localTimeOffset = new DateTimeOffset(localDateTime, offset);
            
            return (localTimeOffset.UtcDateTime, offset, timezoneId);
        }
        catch (Exception ex)
        {
            // Fallback: If lookup fails (e.g. invalid coords), use longitude approximation
            // WARNING: This is an approximation and may not be accurate near timezone boundaries
            // Longitude-based approximation: 15 degrees = 1 hour
            var approxOffsetHours = Math.Round(longitude / 15.0);
            var approxOffset = TimeSpan.FromHours(approxOffsetHours);
            var fallbackUtc = localDateTime.Add(-approxOffset);
            
            // Log warning for debugging
            System.Diagnostics.Debug.WriteLine($"[LumenTimezoneHelper] Timezone lookup failed for ({latitude}, {longitude}): {ex.Message}. Using longitude approximation: {approxOffset}");
            
            return (fallbackUtc, approxOffset, "UTC_Approx");
        }
    }
}
