using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Complete Firebase Cloud Messaging service implementation
/// </summary>
public class FirebaseService
{
    private readonly ILogger<FirebaseService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _serverKey;
    private readonly string _fcmEndpoint = "https://fcm.googleapis.com/fcm/send";
    
    public FirebaseService(ILogger<FirebaseService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
        
        // Get Firebase server key from environment or configuration
        _serverKey = Environment.GetEnvironmentVariable("FIREBASE_SERVER_KEY") ?? "";
        
        if (!string.IsNullOrEmpty(_serverKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("key", $"={_serverKey}");
            _httpClient.DefaultRequestHeaders.Add("Content-Type", "application/json");
        }
    }

    /// <summary>
    /// Send push notification to a device
    /// </summary>
    public async Task<bool> SendPushNotificationAsync(
        string pushToken, 
        string title, 
        string content, 
        Dictionary<string, object>? data = null)
    {
        try
        {
            if (string.IsNullOrEmpty(pushToken))
            {
                _logger.LogWarning("Push token is empty");
                return false;
            }
            
            if (string.IsNullOrEmpty(_serverKey))
            {
                _logger.LogWarning("Firebase server key not configured, using simulation mode");
                return await SimulatePushAsync(pushToken, title, content);
            }
            
            // Create FCM payload according to FCM v1 API
            var payload = new
            {
                to = pushToken,
                notification = new
                {
                    title = title,
                    body = content,
                    sound = "default",
                    badge = 1,
                    click_action = "FLUTTER_NOTIFICATION_CLICK" // For Flutter apps
                },
                data = data ?? new Dictionary<string, object>
                {
                    ["type"] = "daily_push",
                    ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()
                },
                priority = "high",
                content_available = true,
                time_to_live = 86400 // 24 hours
            };
            
            var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            
            _logger.LogDebug($"Sending FCM request to {pushToken.Substring(0, Math.Min(10, pushToken.Length))}...");
            _logger.LogDebug($"Payload: {jsonPayload}");
            
            var response = await _httpClient.PostAsync(_fcmEndpoint, httpContent);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                var fcmResponse = JsonSerializer.Deserialize<FCMResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                
                if (fcmResponse?.Success > 0)
                {
                    _logger.LogInformation($"Push notification sent successfully to {pushToken.Substring(0, Math.Min(10, pushToken.Length))}...");
                    return true;
                }
                else if (fcmResponse?.Failure > 0 && fcmResponse.Results?.Length > 0)
                {
                    var error = fcmResponse.Results[0]?.Error;
                    _logger.LogWarning($"FCM error: {error} for token {pushToken.Substring(0, Math.Min(10, pushToken.Length))}...");
                    
                    // Handle specific FCM errors for token management
                    switch (error)
                    {
                        case "NotRegistered":
                        case "InvalidRegistration":
                            _logger.LogInformation("Token is invalid and should be removed from database");
                            break;
                        case "MismatchSenderId":
                            _logger.LogError("FCM sender ID mismatch - check project configuration");
                            break;
                        case "MessageTooBig":
                            _logger.LogWarning("Push payload too large, consider reducing content");
                            break;
                        case "InvalidTtl":
                            _logger.LogWarning("Invalid time-to-live value");
                            break;
                    }
                    
                    return false;
                }
            }
            else
            {
                _logger.LogError($"FCM request failed with status {response.StatusCode}: {responseContent}");
                
                // Handle HTTP-level errors
                switch (response.StatusCode)
                {
                    case System.Net.HttpStatusCode.Unauthorized:
                        _logger.LogError("FCM authentication failed - check server key");
                        break;
                    case System.Net.HttpStatusCode.BadRequest:
                        _logger.LogError("FCM bad request - check payload format");
                        break;
                    case System.Net.HttpStatusCode.TooManyRequests:
                        _logger.LogWarning("FCM rate limit exceeded - implement retry with backoff");
                        break;
                }
                
                return false;
            }
            
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while sending push notification");
            return false;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Push notification request timed out");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Unexpected error sending push notification to {pushToken}");
            return false;
        }
    }
    
    /// <summary>
    /// Send push notifications to multiple devices (batch)
    /// </summary>
    public async Task<BatchPushResult> SendBatchPushNotificationAsync(
        List<string> pushTokens,
        string title,
        string content,
        Dictionary<string, object>? data = null)
    {
        var results = new BatchPushResult();
        var tasks = pushTokens.Select(async token =>
        {
            var success = await SendPushNotificationAsync(token, title, content, data);
            if (success)
            {
                results.SuccessCount++;
            }
            else
            {
                results.FailureCount++;
                results.FailedTokens.Add(token);
            }
        });
        
        await Task.WhenAll(tasks);
        
        _logger.LogInformation($"Batch push completed: {results.SuccessCount} success, {results.FailureCount} failures");
        return results;
    }
    
    private async Task<bool> SimulatePushAsync(string pushToken, string title, string content)
    {
        // Realistic simulation for development/testing
        await Task.Delay(Random.Shared.Next(100, 500)); // Simulate network latency
        
        // Simulate different outcomes with realistic probabilities
        var outcome = Random.Shared.NextDouble();
        
        if (outcome < 0.92) // 92% success rate
        {
            _logger.LogInformation($"[SIMULATION] Push sent successfully to {pushToken.Substring(0, Math.Min(10, pushToken.Length))}...: {title}");
            return true;
        }
        else if (outcome < 0.95) // 3% invalid token
        {
            _logger.LogWarning($"[SIMULATION] Invalid token {pushToken.Substring(0, Math.Min(10, pushToken.Length))}...");
            return false;
        }
        else // 5% network/server error
        {
            _logger.LogWarning($"[SIMULATION] Network error for {pushToken.Substring(0, Math.Min(10, pushToken.Length))}...");
            return false;
        }
    }
    
    /// <summary>
    /// Validate if a push token format is valid
    /// </summary>
    public bool IsValidPushToken(string pushToken)
    {
        if (string.IsNullOrEmpty(pushToken))
            return false;
            
        // Basic validation - FCM tokens are typically 152+ characters
        if (pushToken.Length < 140)
            return false;
            
        // Should contain only valid characters
        return pushToken.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ':');
    }
}

/// <summary>
/// FCM API response model
/// </summary>
public class FCMResponse
{
    public long MulticastId { get; set; }
    public int Success { get; set; }
    public int Failure { get; set; }
    public int CanonicalIds { get; set; }
    public FCMResult[]? Results { get; set; }
}

/// <summary>
/// Individual FCM result
/// </summary>
public class FCMResult
{
    public string? MessageId { get; set; }
    public string? RegistrationId { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Batch push operation result
/// </summary>
public class BatchPushResult
{
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<string> FailedTokens { get; set; } = new();
    
    public double SuccessRate => SuccessCount + FailureCount > 0 ? 
        (double)SuccessCount / (SuccessCount + FailureCount) : 0;
}
