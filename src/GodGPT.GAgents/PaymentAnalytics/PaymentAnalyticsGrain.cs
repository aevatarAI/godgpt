using System.Net.Http.Headers;
using System.Text.Json;
using Aevatar.Application.Grains.PaymentAnalytics.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.PaymentAnalytics;

/// <summary>
/// Payment analytics service implementation
/// Responsible for reporting payment success events to Google Analytics 4
/// </summary>
[StatelessWorker]
[Reentrant]
public class PaymentAnalyticsGrain : Grain, IPaymentAnalyticsGrain
{
    private readonly ILogger<PaymentAnalyticsGrain> _logger;
    private readonly IOptionsMonitor<GoogleAnalyticsOptions> _options;
    private readonly HttpClient _httpClient;
    
    // Google Analytics 4 Measurement Protocol endpoints
    private const string GA4_COLLECT_ENDPOINT = "/mp/collect";
    private const string GA4_DEBUG_ENDPOINT = "/debug/mp/collect";
    
    public PaymentAnalyticsGrain(
        ILogger<PaymentAnalyticsGrain> logger,
        IOptionsMonitor<GoogleAnalyticsOptions> options,
        HttpClient httpClient)
    {
        _logger = logger;
        _options = options;
        _httpClient = httpClient;
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("PaymentAnalyticsGrain activated for key: {GrainKey}", this.GetPrimaryKeyString());
        
        // Configure HttpClient - remove default headers as we'll set them per request
        _httpClient.DefaultRequestHeaders.Clear();
        
        return base.OnActivateAsync(cancellationToken);
    }

    public async Task<PaymentAnalyticsResultDto> ReportPaymentSuccessAsync()
    {
        try
        {
            var currentOptions = _options.CurrentValue;
            
            if (!currentOptions.EnableAnalytics)
            {
                _logger.LogDebug("Analytics reporting is disabled in configuration");
                return new PaymentAnalyticsResultDto
                {
                    IsSuccess = false,
                    ErrorMessage = "Analytics reporting is disabled"
                };
            }

            if (string.IsNullOrWhiteSpace(currentOptions.MeasurementId) || 
                string.IsNullOrWhiteSpace(currentOptions.ApiSecret))
            {
                _logger.LogError("Google Analytics configuration is incomplete. MeasurementId and ApiSecret are required");
                return new PaymentAnalyticsResultDto
                {
                    IsSuccess = false,
                    ErrorMessage = "Google Analytics configuration is incomplete"
                };
            }

            _logger.LogDebug("Reporting payment success event to Google Analytics");

            var eventPayload = CreateGA4PaymentSuccessPayload();
            var url = BuildGA4ApiUrl(currentOptions.ApiEndpoint, currentOptions.MeasurementId, currentOptions.ApiSecret);
            
            _logger.LogInformation("PaymentAnalyticsGrain reporting payment success to: {Url}", url);
            
            var result = await SendEventToGA4Async(url, eventPayload, currentOptions.TimeoutSeconds);
            
            if (result.IsSuccess)
            {
                _logger.LogInformation("[PaymentAnalytics] Successfully reported payment success event");
            }
            else
            {
                _logger.LogWarning("[PaymentAnalytics] Failed to report payment success: {ErrorMessage}", result.ErrorMessage);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reporting payment success event");
            return new PaymentAnalyticsResultDto
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }

    #region Helper Methods

    /// <summary>
    /// Create Google Analytics 4 payload for payment success event
    /// </summary>
    private object CreateGA4PaymentSuccessPayload()
    {
        var clientId = $"payment_analytics_{DateTime.UtcNow.Ticks}";
        var sessionId = Guid.NewGuid().ToString();
        
        return new
        {
            client_id = clientId,
            events = new[]
            {
                new
                {
                    name = "payment_success",
                    @params = new
                    {
                        session_id = sessionId,
                        engagement_time_msec = 100
                    }
                }
            }
        };
    }

    /// <summary>
    /// Build Google Analytics 4 API URL with parameters
    /// </summary>
    private static string BuildGA4ApiUrl(string baseEndpoint, string measurementId, string apiSecret)
    {
        return $"{baseEndpoint}?measurement_id={Uri.EscapeDataString(measurementId)}&api_secret={Uri.EscapeDataString(apiSecret)}";
    }

    /// <summary>
    /// Send event to Google Analytics 4 API
    /// </summary>
    private async Task<PaymentAnalyticsResultDto> SendEventToGA4Async(string url, object payload, int timeoutSeconds)
    {
        try
        {
            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
            
            _logger.LogDebug("Sending GA4 event payload: {Payload}", jsonPayload);
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            var response = await _httpClient.PostAsync(url, content, cts.Token);
            
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("GA4 API response successful: {StatusCode}", response.StatusCode);
                return new PaymentAnalyticsResultDto
                {
                    IsSuccess = true,
                    StatusCode = (int)response.StatusCode
                };
            }
            else
            {
                _logger.LogError("GA4 API error: StatusCode={StatusCode}, Content={Content}", 
                    response.StatusCode, responseContent);
                return new PaymentAnalyticsResultDto
                {
                    IsSuccess = false,
                    ErrorMessage = $"GA4 API error {response.StatusCode}: {responseContent}",
                    StatusCode = (int)response.StatusCode
                };
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed when sending to GA4");
            return new PaymentAnalyticsResultDto
            {
                IsSuccess = false,
                ErrorMessage = $"HTTP request failed: {ex.Message}"
            };
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timeout when sending to GA4");
            return new PaymentAnalyticsResultDto
            {
                IsSuccess = false,
                ErrorMessage = "Request timeout"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error when sending to GA4");
            return new PaymentAnalyticsResultDto
            {
                IsSuccess = false,
                ErrorMessage = $"Unexpected error: {ex.Message}"
            };
        }
    }

    #endregion
}
