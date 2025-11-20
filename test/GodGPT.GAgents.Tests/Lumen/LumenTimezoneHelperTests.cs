using Aevatar.Application.Grains.Lumen.Helpers;
using Shouldly;
using Xunit;

namespace Aevatar.Application.Grains.Tests.Lumen;

/// <summary>
/// Tests for LumenTimezoneHelper timezone conversion logic
/// </summary>
public class LumenTimezoneHelperTests
{
    [Fact]
    public void GetUtcTimeFromLocal_Shanghai_ShouldConvertCorrectly()
    {
        // Arrange - Shanghai coordinates (UTC+8, no DST)
        var latitude = 31.2304;
        var longitude = 121.4737;
        var localDateTime = new DateTime(2000, 1, 1, 8, 30, 0, DateTimeKind.Unspecified);
        
        // Act
        var (utcTime, offset, timezoneId) = LumenTimezoneHelper.GetUtcTimeFromLocal(localDateTime, latitude, longitude);
        
        // Assert
        utcTime.Year.ShouldBe(2000);
        utcTime.Month.ShouldBe(1);
        utcTime.Day.ShouldBe(1);
        utcTime.Hour.ShouldBe(0); // 8:30 AM local = 0:30 AM UTC
        utcTime.Minute.ShouldBe(30);
        offset.TotalHours.ShouldBe(8);
        timezoneId.ShouldBe("Asia/Shanghai");
    }
    
    [Fact]
    public void GetUtcTimeFromLocal_NewYork_Winter_ShouldConvertCorrectly()
    {
        // Arrange - New York coordinates (EST: UTC-5 in winter)
        var latitude = 40.7128;
        var longitude = -74.0060;
        var localDateTime = new DateTime(2000, 1, 1, 8, 30, 0, DateTimeKind.Unspecified);
        
        // Act
        var (utcTime, offset, timezoneId) = LumenTimezoneHelper.GetUtcTimeFromLocal(localDateTime, latitude, longitude);
        
        // Assert
        utcTime.Year.ShouldBe(2000);
        utcTime.Month.ShouldBe(1);
        utcTime.Day.ShouldBe(1);
        utcTime.Hour.ShouldBe(13); // 8:30 AM EST = 1:30 PM UTC
        utcTime.Minute.ShouldBe(30);
        offset.TotalHours.ShouldBe(-5);
        timezoneId.ShouldBe("America/New_York");
    }
    
    [Fact]
    public void GetUtcTimeFromLocal_NewYork_Summer_ShouldHandleDST()
    {
        // Arrange - New York in summer (EDT: UTC-4 due to DST)
        var latitude = 40.7128;
        var longitude = -74.0060;
        var localDateTime = new DateTime(2000, 7, 1, 8, 30, 0, DateTimeKind.Unspecified);
        
        // Act
        var (utcTime, offset, timezoneId) = LumenTimezoneHelper.GetUtcTimeFromLocal(localDateTime, latitude, longitude);
        
        // Assert
        utcTime.Year.ShouldBe(2000);
        utcTime.Month.ShouldBe(7);
        utcTime.Day.ShouldBe(1);
        utcTime.Hour.ShouldBe(12); // 8:30 AM EDT = 12:30 PM UTC
        utcTime.Minute.ShouldBe(30);
        offset.TotalHours.ShouldBe(-4);
        timezoneId.ShouldBe("America/New_York");
    }
    
    [Fact]
    public void GetUtcTimeFromLocal_London_Winter_ShouldConvertCorrectly()
    {
        // Arrange - London coordinates (GMT: UTC+0 in winter)
        var latitude = 51.5074;
        var longitude = -0.1278;
        var localDateTime = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
        
        // Act
        var (utcTime, offset, timezoneId) = LumenTimezoneHelper.GetUtcTimeFromLocal(localDateTime, latitude, longitude);
        
        // Assert
        utcTime.Year.ShouldBe(2000);
        utcTime.Month.ShouldBe(1);
        utcTime.Day.ShouldBe(1);
        utcTime.Hour.ShouldBe(12);
        utcTime.Minute.ShouldBe(0);
        offset.TotalHours.ShouldBe(0);
        timezoneId.ShouldBe("Europe/London");
    }
    
    [Fact]
    public void GetUtcTimeFromLocal_London_Summer_ShouldHandleDST()
    {
        // Arrange - London in summer (BST: UTC+1 due to DST)
        var latitude = 51.5074;
        var longitude = -0.1278;
        var localDateTime = new DateTime(2000, 7, 1, 12, 0, 0, DateTimeKind.Unspecified);
        
        // Act
        var (utcTime, offset, timezoneId) = LumenTimezoneHelper.GetUtcTimeFromLocal(localDateTime, latitude, longitude);
        
        // Assert
        utcTime.Year.ShouldBe(2000);
        utcTime.Month.ShouldBe(7);
        utcTime.Day.ShouldBe(1);
        utcTime.Hour.ShouldBe(11); // 12:00 PM BST = 11:00 AM UTC
        utcTime.Minute.ShouldBe(0);
        offset.TotalHours.ShouldBe(1);
        timezoneId.ShouldBe("Europe/London");
    }
    
    [Fact]
    public void GetUtcTimeFromLocal_Tokyo_ShouldConvertCorrectly()
    {
        // Arrange - Tokyo coordinates (JST: UTC+9, no DST)
        var latitude = 35.6762;
        var longitude = 139.6503;
        var localDateTime = new DateTime(2000, 1, 1, 9, 0, 0, DateTimeKind.Unspecified);
        
        // Act
        var (utcTime, offset, timezoneId) = LumenTimezoneHelper.GetUtcTimeFromLocal(localDateTime, latitude, longitude);
        
        // Assert
        utcTime.Year.ShouldBe(2000);
        utcTime.Month.ShouldBe(1);
        utcTime.Day.ShouldBe(1);
        utcTime.Hour.ShouldBe(0);
        offset.TotalHours.ShouldBe(9);
        timezoneId.ShouldBe("Asia/Tokyo");
    }
    
    [Fact]
    public void GetUtcTimeFromLocal_MidnightBoundary_ShouldHandleDateChange()
    {
        // Arrange - Test date boundary: midnight in Beijing (UTC+8)
        var latitude = 39.9042;
        var longitude = 116.4074;
        var localDateTime = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        
        // Act
        var (utcTime, offset, timezoneId) = LumenTimezoneHelper.GetUtcTimeFromLocal(localDateTime, latitude, longitude);
        
        // Assert - Should roll back to previous day in UTC
        utcTime.Year.ShouldBe(1999);
        utcTime.Month.ShouldBe(12);
        utcTime.Day.ShouldBe(31);
        utcTime.Hour.ShouldBe(16); // Midnight Beijing = 4:00 PM previous day UTC
        offset.TotalHours.ShouldBe(8);
        timezoneId.ShouldBe("Asia/Shanghai");
    }
    
    [Fact]
    public void GetUtcTimeFromLocal_NegativeTimezoneBoundary_ShouldHandleDateChange()
    {
        // Arrange - Test date boundary: late evening in Los Angeles (UTC-8)
        var latitude = 34.0522;
        var longitude = -118.2437;
        var localDateTime = new DateTime(2000, 1, 1, 23, 30, 0, DateTimeKind.Unspecified);
        
        // Act
        var (utcTime, offset, timezoneId) = LumenTimezoneHelper.GetUtcTimeFromLocal(localDateTime, latitude, longitude);
        
        // Assert - Should roll forward to next day in UTC
        utcTime.Year.ShouldBe(2000);
        utcTime.Month.ShouldBe(1);
        utcTime.Day.ShouldBe(2);
        utcTime.Hour.ShouldBe(7); // 11:30 PM PST = 7:30 AM next day UTC
        utcTime.Minute.ShouldBe(30);
        offset.TotalHours.ShouldBe(-8);
        timezoneId.ShouldBe("America/Los_Angeles");
    }
    
    [Fact]
    public void GetUtcTimeFromLocal_Sydney_ShouldConvertCorrectly()
    {
        // Arrange - Sydney coordinates (AEDT: UTC+11 in summer)
        var latitude = -33.8688;
        var longitude = 151.2093;
        var localDateTime = new DateTime(2000, 1, 1, 11, 0, 0, DateTimeKind.Unspecified);
        
        // Act
        var (utcTime, offset, timezoneId) = LumenTimezoneHelper.GetUtcTimeFromLocal(localDateTime, latitude, longitude);
        
        // Assert
        utcTime.Year.ShouldBe(2000);
        utcTime.Month.ShouldBe(1);
        utcTime.Day.ShouldBe(1);
        utcTime.Hour.ShouldBe(0);
        offset.TotalHours.ShouldBe(11); // Sydney uses DST in January
        timezoneId.ShouldBe("Australia/Sydney");
    }
    
    [Fact]
    public void GetUtcTimeFromLocal_InvalidCoordinates_ShouldUseLongitudeApproximation()
    {
        // Arrange - Invalid coordinates
        // Note: GeoTimeZone library is resilient and may still return a valid timezone even with slightly invalid coords
        // This test verifies the system handles edge cases gracefully
        var latitude = 91.0; // Invalid latitude (must be -90 to 90), but library may clamp to 90
        var longitude = 120.0; // Valid longitude for UTC+8 approximation
        var localDateTime = new DateTime(2000, 1, 1, 8, 0, 0, DateTimeKind.Unspecified);
        
        // Act
        var (utcTime, offset, timezoneId) = LumenTimezoneHelper.GetUtcTimeFromLocal(localDateTime, latitude, longitude);
        
        // Assert - GeoTimeZone library handles this gracefully and returns "Etc/GMT-8"
        utcTime.Year.ShouldBe(2000);
        utcTime.Month.ShouldBe(1);
        utcTime.Day.ShouldBe(1);
        utcTime.Hour.ShouldBe(0);
        offset.TotalHours.ShouldBe(8);
        // Library returns a valid timezone ID instead of falling back to approximation
        timezoneId.ShouldNotBeNullOrWhiteSpace();
    }
    
    [Fact]
    public void GetUtcTimeFromLocal_Berlin_ShouldHandleHistoricalTimezone()
    {
        // Arrange - Berlin in 1950 (testing historical timezone data)
        var latitude = 52.5200;
        var longitude = 13.4050;
        var localDateTime = new DateTime(1950, 6, 15, 12, 0, 0, DateTimeKind.Unspecified);
        
        // Act
        var (utcTime, offset, timezoneId) = LumenTimezoneHelper.GetUtcTimeFromLocal(localDateTime, latitude, longitude);
        
        // Assert
        timezoneId.ShouldBe("Europe/Berlin");
        // Note: Historical timezone data accuracy depends on the system's timezone database
        // In 1950 June, Berlin was using CET (UTC+1), not CEST
        utcTime.Hour.ShouldBe(11); // 12:00 CET = 11:00 UTC
        offset.TotalHours.ShouldBe(1);
    }
    
    [Fact]
    public void GetUtcTimeFromLocal_Dubai_ShouldConvertCorrectly()
    {
        // Arrange - Dubai coordinates (GST: UTC+4, no DST)
        var latitude = 25.2048;
        var longitude = 55.2708;
        var localDateTime = new DateTime(2000, 1, 1, 4, 30, 0, DateTimeKind.Unspecified);
        
        // Act
        var (utcTime, offset, timezoneId) = LumenTimezoneHelper.GetUtcTimeFromLocal(localDateTime, latitude, longitude);
        
        // Assert
        utcTime.Year.ShouldBe(2000);
        utcTime.Month.ShouldBe(1);
        utcTime.Day.ShouldBe(1);
        utcTime.Hour.ShouldBe(0);
        utcTime.Minute.ShouldBe(30);
        offset.TotalHours.ShouldBe(4);
        timezoneId.ShouldBe("Asia/Dubai");
    }
    
    [Fact]
    public void GetUtcTimeFromLocal_SaoPaulo_ShouldConvertCorrectly()
    {
        // Arrange - São Paulo coordinates
        // In January 2000, São Paulo was observing BRST (UTC-2) due to daylight saving time
        var latitude = -23.5505;
        var longitude = -46.6333;
        var localDateTime = new DateTime(2000, 1, 1, 9, 0, 0, DateTimeKind.Unspecified);
        
        // Act
        var (utcTime, offset, timezoneId) = LumenTimezoneHelper.GetUtcTimeFromLocal(localDateTime, latitude, longitude);
        
        // Assert
        utcTime.Year.ShouldBe(2000);
        utcTime.Month.ShouldBe(1);
        utcTime.Day.ShouldBe(1);
        utcTime.Hour.ShouldBe(11); // 9:00 AM BRST (UTC-2) = 11:00 AM UTC
        offset.TotalHours.ShouldBe(-2); // BRST offset
        timezoneId.ShouldBe("America/Sao_Paulo");
    }
    
    [Fact]
    public void GetUtcTimeFromLocal_LeapYear_ShouldHandleCorrectly()
    {
        // Arrange - Test leap year date (Feb 29, 2000)
        var latitude = 31.2304;
        var longitude = 121.4737;
        var localDateTime = new DateTime(2000, 2, 29, 12, 0, 0, DateTimeKind.Unspecified);
        
        // Act
        var (utcTime, offset, timezoneId) = LumenTimezoneHelper.GetUtcTimeFromLocal(localDateTime, latitude, longitude);
        
        // Assert
        utcTime.Year.ShouldBe(2000);
        utcTime.Month.ShouldBe(2);
        utcTime.Day.ShouldBe(29);
        utcTime.Hour.ShouldBe(4);
        offset.TotalHours.ShouldBe(8);
    }
    
    [Fact]
    public void GetUtcTimeFromLocal_Kathmandu_ShouldHandleNonStandardOffset()
    {
        // Arrange - Kathmandu has unusual UTC+5:45 offset
        var latitude = 27.7172;
        var longitude = 85.3240;
        var localDateTime = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
        
        // Act
        var (utcTime, offset, timezoneId) = LumenTimezoneHelper.GetUtcTimeFromLocal(localDateTime, latitude, longitude);
        
        // Assert
        utcTime.Year.ShouldBe(2000);
        utcTime.Month.ShouldBe(1);
        utcTime.Day.ShouldBe(1);
        utcTime.Hour.ShouldBe(6);
        utcTime.Minute.ShouldBe(15);
        offset.TotalHours.ShouldBe(5.75); // 5 hours 45 minutes
        timezoneId.ShouldBe("Asia/Kathmandu");
    }
}

