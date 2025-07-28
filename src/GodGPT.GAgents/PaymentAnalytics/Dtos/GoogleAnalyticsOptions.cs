namespace Aevatar.Application.Grains.PaymentAnalytics.Dtos;

/// <summary>
/// Google Analytics 4 configuration options for payment reporting
/// </summary>
public class GoogleAnalyticsOptions
{
    public bool EnableAnalytics { get; set; } = true;
    public string MeasurementId { get; set; } = string.Empty; // G-XXXXXXXXXX
    public string ApiSecret { get; set; } = string.Empty; // Generated in GA4 Admin
    public string ApiEndpoint { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 5;
    public int RetryCount { get; set; } = 3;
    public int ApiCallDelayMs { get; set; } = 50;
    
}
