using System.Net.Http.Headers;
using System.Text.Json;
using Aevatar.Application.Grains.Common.Constants;
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

    public async Task<PaymentAnalyticsResultDto> ReportPaymentSuccessAsync(
        PaymentPlatform paymentPlatform,
        string transactionId, 
        string userId
        )
    {
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            return new PaymentAnalyticsResultDto
            {
                IsSuccess = false,
                ErrorMessage = "Transaction ID is required for idempotent reporting"
            };
        }

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

            _logger.LogDebug("Reporting payment success event to Google Analytics with transaction ID: {TransactionId}", transactionId);

            // Create unique transaction ID by combining user, platform and original transaction ID
            var uniqueTransactionId = userId + "^" + paymentPlatform + "^" + transactionId;
            var eventPayload = CreateGA4PurchasePayload(uniqueTransactionId, userId);
            var url = BuildGA4ApiUrl(currentOptions.ApiEndpoint, currentOptions.MeasurementId, currentOptions.ApiSecret);
            
            _logger.LogInformation("PaymentAnalyticsGrain reporting purchase event for transaction {TransactionId} to: {Url}", uniqueTransactionId, url);
            
            var result = await SendEventToGA4Async(url, eventPayload, currentOptions.TimeoutSeconds);
            
            if (result.IsSuccess)
            {
                _logger.LogInformation("[PaymentAnalytics] Successfully reported purchase event for transaction {TransactionId}", uniqueTransactionId);
            }
            else
            {
                _logger.LogWarning("[PaymentAnalytics] Failed to report purchase event for transaction {TransactionId}: {ErrorMessage}", 
                    uniqueTransactionId, result.ErrorMessage);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reporting payment success event for transaction {TransactionId}", transactionId);
            return new PaymentAnalyticsResultDto
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }

    #region Helper Methods
    

    /// <summary>
    /// Create Google Analytics 4 payload for purchase event with idempotency support
    /// Uses GA4's built-in transaction_id deduplication mechanism
    /// </summary>
    private object CreateGA4PurchasePayload(string transactionId, string userId, decimal? paymentValue = 0)
    {
        var clientId = userId;
        
        return new
        {
            client_id = clientId,
            events = new[]
            {
                new
                {
                    name = "purchase",  // Using standard purchase event for GA4 auto-deduplication
                    @params = new
                    {
                        transaction_id = transactionId,
                        currency = "USD",
                        value = paymentValue,
                        engagement_time_msec = 1000
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
    /// Send event to Google Analytics 4 API with retry mechanism
    /// </summary>
    private async Task<PaymentAnalyticsResultDto> SendEventToGA4Async(string url, object payload, int timeoutSeconds)
    {
        var options = _options.CurrentValue;
        var maxRetries = options.RetryCount;
        var delayMs = options.ApiCallDelayMs;
        
        var jsonPayload = JsonSerializer.Serialize(payload);
        
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
                
                _logger.LogDebug("Sending GA4 event payload (attempt {Attempt}/{MaxAttempts}): {Payload}", 
                    attempt + 1, maxRetries + 1, jsonPayload);
                
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                var response = await _httpClient.PostAsync(url, content, cts.Token);
                
                var responseContent = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("GA4 API response successful: {StatusCode} (attempt {Attempt})", 
                        response.StatusCode, attempt + 1);
                    return new PaymentAnalyticsResultDto
                    {
                        IsSuccess = true,
                        StatusCode = (int)response.StatusCode
                    };
                }
                else
                {
                    _logger.LogWarning("GA4 API error on attempt {Attempt}: StatusCode={StatusCode}, Content={Content}", 
                        attempt + 1, response.StatusCode, responseContent);
                    
                    // For non-retriable errors (4xx), don't retry
                    if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                    {
                        _logger.LogError("GA4 API client error (4xx): {StatusCode}. Not retrying.", response.StatusCode);
                        return new PaymentAnalyticsResultDto
                        {
                            IsSuccess = false,
                            ErrorMessage = $"GA4 API client error {response.StatusCode}: {responseContent}",
                            StatusCode = (int)response.StatusCode
                        };
                    }
                    
                    // If this is the last attempt, return the error
                    if (attempt == maxRetries)
                    {
                        return new PaymentAnalyticsResultDto
                        {
                            IsSuccess = false,
                            ErrorMessage = $"GA4 API error {response.StatusCode} after {maxRetries + 1} attempts: {responseContent}",
                            StatusCode = (int)response.StatusCode
                        };
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "HTTP request failed on attempt {Attempt}: {Message}", attempt + 1, ex.Message);
                
                if (attempt == maxRetries)
                {
                    _logger.LogError(ex, "HTTP request failed after {MaxAttempts} attempts", maxRetries + 1);
                    return new PaymentAnalyticsResultDto
                    {
                        IsSuccess = false,
                        ErrorMessage = $"HTTP request failed after {maxRetries + 1} attempts: {ex.Message}"
                    };
                }
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                _logger.LogWarning(ex, "Request timeout on attempt {Attempt}", attempt + 1);
                
                if (attempt == maxRetries)
                {
                    _logger.LogError(ex, "Request timeout after {MaxAttempts} attempts", maxRetries + 1);
                    return new PaymentAnalyticsResultDto
                    {
                        IsSuccess = false,
                        ErrorMessage = $"Request timeout after {maxRetries + 1} attempts"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error on attempt {Attempt}: {Message}", attempt + 1, ex.Message);
                return new PaymentAnalyticsResultDto
                {
                    IsSuccess = false,
                    ErrorMessage = $"Unexpected error: {ex.Message}"
                };
            }
            
            // Wait before retrying (except on the last attempt)
            if (attempt < maxRetries && delayMs > 0)
            {
                _logger.LogDebug("Waiting {DelayMs}ms before retry attempt {NextAttempt}", delayMs, attempt + 2);
                await Task.Delay(delayMs);
            }
        }
        
        // This should never be reached, but add as safety
        return new PaymentAnalyticsResultDto
        {
            IsSuccess = false,
            ErrorMessage = "Unknown error in retry logic"
        };
    }

    public async Task<PaymentAnalyticsResultDto> ReportPaymentSuccessAsync(
        PaymentPlatform paymentPlatform,
        string transactionId,
        string userId,
        PurchaseType purchaseType,
        string currency,
        decimal amount)
    {
        _logger.LogInformation(
            "Reporting {PurchaseType} payment success to Google Analytics: Platform={Platform}, TransactionId={TransactionId}, UserId={UserId}, Amount={Amount} {Currency}",
            purchaseType, paymentPlatform, transactionId, userId, amount, currency);

        // Delegate to the original method for now - could be enhanced later with additional analytics data
        var result = await ReportPaymentSuccessAsync(paymentPlatform, transactionId, userId);
        
        // Log additional analytics tracking for the specific purchase type
        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "Successfully reported {PurchaseType} payment analytics: Platform={Platform}, TransactionId={TransactionId}",
                purchaseType, paymentPlatform, transactionId);
        }
        
        return result;
    }

    #endregion
}
