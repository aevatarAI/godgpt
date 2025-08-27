using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http;
using System.Text;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using GodGPT.GAgents.DailyPush.Options;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Firebase Cloud Messaging service implementation using FCM API v1
/// </summary>
public class FirebaseService
{
    private readonly ILogger<FirebaseService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ServiceAccountInfo? _serviceAccount;
    private readonly string? _projectId;
    private string? _cachedAccessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;
    
    public FirebaseService(
        ILogger<FirebaseService> logger, 
        HttpClient httpClient, 
        IConfiguration configuration, 
        IOptionsMonitor<DailyPushOptions> options)
    {
        _logger = logger;
        _httpClient = httpClient;
        
        _logger.LogDebug("🚀 Initializing Firebase service...");
        
        // Load service account from file first, then fallback to configuration
        _logger.LogDebug("🔄 Attempting to load Firebase credentials (File first, then appsettings.json fallback)");
        
        var fileAccount = LoadServiceAccountFromFile(options.CurrentValue.FilePaths);
        var configAccount = fileAccount == null ? LoadServiceAccountFromConfiguration(configuration) : null;
        
        _serviceAccount = fileAccount ?? configAccount;
        _projectId = _serviceAccount?.ProjectId ?? configuration["Firebase:ProjectId"];
        
        // Log the source of credentials
        if (fileAccount != null)
        {
            _logger.LogInformation("🎯 Firebase credentials loaded from FILE for project: {ProjectId}", _projectId);
        }
        else if (configAccount != null)
        {
            _logger.LogInformation("🎯 Firebase credentials loaded from APPSETTINGS for project: {ProjectId}", _projectId);
        }
        else if (!string.IsNullOrEmpty(_projectId))
        {
            _logger.LogWarning("⚠️ Firebase project ID found in config but no service account - limited functionality");
        }
        else
        {
            _logger.LogWarning("❌ Firebase credentials not configured - using SIMULATION MODE for push notifications");
        }
        
        if (_serviceAccount != null && !string.IsNullOrEmpty(_projectId))
        {
            _logger.LogInformation("✅ Firebase FCM API v1 configured successfully for project: {ProjectId}", _projectId);
        }
    }
    
    /// <summary>
    /// Load service account information from Firebase key file
    /// </summary>
    private ServiceAccountInfo? LoadServiceAccountFromFile(FilePathsOptions filePaths)
    {
        try
        {
            var keyPath = filePaths.FirebaseKeyPath;
            
            _logger.LogDebug("🔑 Attempting to load Firebase key from configured path: {KeyPath}", keyPath);
            
            if (!File.Exists(keyPath))
            {
                _logger.LogWarning("❌ Firebase key file not found at path: {KeyPath} - will fallback to appsettings.json", keyPath);
                return null;
            }
            
            var fileInfo = new FileInfo(keyPath);
            _logger.LogInformation("📁 Found Firebase key file: {KeyPath} (Size: {FileSize} bytes, Modified: {LastModified})", 
                keyPath, fileInfo.Length, fileInfo.LastWriteTime);
            
            var jsonContent = File.ReadAllText(keyPath);
            
            // Log basic file info without exposing sensitive data
            var contentLength = jsonContent.Length;
            var hasPrivateKey = jsonContent.Contains("private_key");
            var hasProjectId = jsonContent.Contains("project_id");
            var hasClientEmail = jsonContent.Contains("client_email");
            
            _logger.LogDebug("🔍 Firebase key file analysis: Length={ContentLength} chars, HasPrivateKey={HasPrivateKey}, HasProjectId={HasProjectId}, HasClientEmail={HasClientEmail}",
                contentLength, hasPrivateKey, hasProjectId, hasClientEmail);
            
            var serviceAccount = JsonSerializer.Deserialize<ServiceAccountInfo>(jsonContent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });
            
            if (serviceAccount != null)
            {
                _logger.LogInformation("✅ Successfully loaded Firebase service account from file for project: {ProjectId}", 
                    serviceAccount.ProjectId);
                
                // Log key fields presence (without exposing values)
                _logger.LogDebug("🔑 Service account validation: ProjectId='{ProjectId}', ClientEmail='{ClientEmail}', HasPrivateKey={HasPrivateKey}",
                    serviceAccount.ProjectId, 
                    string.IsNullOrEmpty(serviceAccount.ClientEmail) ? "<EMPTY>" : serviceAccount.ClientEmail,
                    !string.IsNullOrEmpty(serviceAccount.PrivateKey));
            }
            else
            {
                _logger.LogError("❌ Failed to parse Firebase service account from file - JSON deserialization returned null");
            }
            
            return serviceAccount;
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "💥 JSON parsing error loading Firebase service account from file: {KeyPath}", 
                filePaths.FirebaseKeyPath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Critical error loading Firebase service account from file: {KeyPath}", 
                filePaths.FirebaseKeyPath);
            return null;
        }
    }
    
    /// <summary>
    /// Load service account information from configuration
    /// </summary>
    private ServiceAccountInfo? LoadServiceAccountFromConfiguration(IConfiguration configuration)
    {
        try
        {
            // Try to load from Firebase section in appsettings.json
            var firebaseSection = configuration.GetSection("Firebase");
            if (firebaseSection.Exists())
            {
                var serviceAccount = new ServiceAccountInfo
                {
                    Type = firebaseSection["Type"] ?? "service_account",
                    ProjectId = firebaseSection["ProjectId"] ?? "",
                    PrivateKeyId = firebaseSection["PrivateKeyId"] ?? "",
                    PrivateKey = firebaseSection["PrivateKey"] ?? "",
                    ClientEmail = firebaseSection["ClientEmail"] ?? "",
                    ClientId = firebaseSection["ClientId"] ?? "",
                    AuthUri = firebaseSection["AuthUri"] ?? "https://accounts.google.com/o/oauth2/auth",
                    TokenUri = firebaseSection["TokenUri"] ?? "https://oauth2.googleapis.com/token"
                };
                
                if (!string.IsNullOrEmpty(serviceAccount.ProjectId) && 
                    !string.IsNullOrEmpty(serviceAccount.PrivateKey) && 
                    !string.IsNullOrEmpty(serviceAccount.ClientEmail))
                {
                    _logger.LogDebug("Firebase service account loaded from appsettings.json");
                    return serviceAccount;
                }
            }
            
            // Fallback to environment variable (JSON string)
            var serviceAccountJson = Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT_JSON");
            if (!string.IsNullOrEmpty(serviceAccountJson))
            {
                var serviceAccount = JsonSerializer.Deserialize<ServiceAccountInfo>(serviceAccountJson);
                if (serviceAccount != null)
                {
                    _logger.LogDebug("Firebase service account loaded from environment variable");
                    return serviceAccount;
                }
            }
            
            _logger.LogWarning("Firebase service account not found in configuration");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Firebase service account from configuration");
            return null;
        }
    }

    /// <summary>
    /// Send push notification to a device using FCM API v1
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
            
            // Use FCM API v1 if configured
            if (_serviceAccount != null && !string.IsNullOrEmpty(_projectId))
            {
                return await SendPushNotificationV1Async(pushToken, title, content, data);
            }
            
            // No credentials configured - simulation mode
            _logger.LogWarning("Firebase credentials not configured, using simulation mode");
            return await SimulatePushAsync(pushToken, title, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Unexpected error sending push notification to {pushToken}");
            return false;
        }
    }
    
    /// <summary>
    /// Send push notification using FCM API v1 (recommended)
    /// </summary>
    private async Task<bool> SendPushNotificationV1Async(
        string pushToken, 
        string title, 
        string content, 
        Dictionary<string, object>? data = null)
    {
        try
        {
            var accessToken = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogError("Failed to obtain access token for FCM API v1");
                return false;
            }
            
            var fcmV1Endpoint = $"https://fcm.googleapis.com/v1/projects/{_projectId}/messages:send";
            
            // Create FCM v1 payload with proper click handling
            var dataPayload = FirebaseServiceExtensions.CreateDataPayload(data);
            var message = new
            {
                message = new
                {
                    token = pushToken,
                    notification = new
                    {
                        title = title,
                        body = content
                    },
                    data = dataPayload,
                    android = new
                    {
                        priority = "high",  // Ensure timely delivery
                        notification = new
                        {
                            sound = "default",
                            // Remove click_action to use default click behavior
                            channel_id = "daily_push_channel",  // Custom notification channel
                            // Remove tag to prevent notification replacement - each should be separate
                            notification_count = dataPayload.TryGetValue("content_index", out var contentIndex) ? 
                                int.Parse(contentIndex.ToString() ?? "1") : 1  // Unique count for each content
                        }
                    },
                    apns = new
                    {
                        headers = new
                        {
                            apns_push_type = "alert"  // Ensure it's a visible notification
                        },
                        payload = new
                        {
                            aps = new
                            {
                                sound = "default"
                                // Remove category to use default click behavior
                                // Note: No content-available or mutable-content to avoid silent push
                            }
                        }
                    }
                }
            };
            
            var jsonPayload = JsonSerializer.Serialize(message, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            
            using var request = new HttpRequestMessage(HttpMethod.Post, fcmV1Endpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = httpContent;
            
            _logger.LogDebug("Sending FCM v1 push notification request");
            
            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                // Even with 200 status, FCM might return error details in the response
                try
                {
                    using var jsonDocument = JsonDocument.Parse(responseContent);
                    var root = jsonDocument.RootElement;
                    
                    if (root.TryGetProperty("error", out var errorElement))
                    {
                        var errorCode = errorElement.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : "UNKNOWN";
                        var errorMessage = errorElement.TryGetProperty("message", out var msgElement) ? msgElement.GetString() : "Unknown error";
                        
                        _logger.LogError("FCM returned error in successful response - Code: {ErrorCode}, Message: {ErrorMessage}", 
                            errorCode, errorMessage);
                        
                        // Handle specific FCM errors
                        if (errorCode == "UNREGISTERED" || errorCode == "INVALID_ARGUMENT")
                        {
                            _logger.LogWarning("Push token is invalid and should be removed from database");
                        }
                        
                        return false;
                    }
                    
                    // Check if response contains message name (success indicator)
                    if (root.TryGetProperty("name", out var nameElement))
                    {
                        var messageName = nameElement.GetString();
                        _logger.LogInformation("Push notification sent successfully via FCM v1 - Message: {MessageName}", messageName);
                        return true;
                    }
                    
                    // Unexpected response format
                    _logger.LogWarning("FCM returned 200 but unexpected response format: {ResponseContent}", responseContent);
                    return false;
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to parse FCM response JSON: {ResponseContent}", responseContent);
                    // Assume success if we can't parse the response but got 200
                    _logger.LogInformation("Push notification sent successfully via FCM v1 (assumed from 200 status)");
                    return true;
                }
            }
            else
            {
                _logger.LogError("FCM v1 request failed with status {StatusCode}: {ResponseContent}", 
                    response.StatusCode, responseContent);
                
                // Handle specific FCM v1 errors
                if (responseContent.Contains("UNREGISTERED") || responseContent.Contains("INVALID_ARGUMENT"))
                {
                    _logger.LogWarning("Token is invalid and should be removed from database");
                }
                
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending push notification via FCM API v1");
            return false;
        }
    }
    

    
    /// <summary>
    /// Get access token for FCM API v1 using service account
    /// </summary>
    private async Task<string?> GetAccessTokenAsync()
    {
        const int maxRetries = 3;
        const int retryDelayMs = 1000;
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Check if cached token is still valid
                if (!string.IsNullOrEmpty(_cachedAccessToken) && DateTime.UtcNow < _tokenExpiry.AddMinutes(-5))
                {
                    return _cachedAccessToken;
                }
                
                if (_serviceAccount == null)
                {
                    _logger.LogError("Service account not configured on attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
                    if (attempt < maxRetries)
                    {
                        _logger.LogInformation("Retrying access token request in {DelayMs}ms...", retryDelayMs);
                        await Task.Delay(retryDelayMs);
                        continue;
                    }
                    return null;
                }
            
            // Create JWT for OAuth 2.0 flow
            var now = DateTimeOffset.UtcNow;
            var expiry = now.AddHours(1);
            
            var claims = new Dictionary<string, object>
            {
                ["iss"] = _serviceAccount.ClientEmail,
                ["scope"] = "https://www.googleapis.com/auth/firebase.messaging",
                ["aud"] = "https://oauth2.googleapis.com/token",
                ["iat"] = now.ToUnixTimeSeconds(),
                ["exp"] = expiry.ToUnixTimeSeconds()
            };
            
            // Parse private key
            var privateKeyPem = _serviceAccount.PrivateKey;
            if (string.IsNullOrEmpty(privateKeyPem))
            {
                _logger.LogError("Private key not found in service account");
                return null;
            }
            
            // Create JWT
            var jwt = CreateJwt(claims, privateKeyPem);
            if (string.IsNullOrEmpty(jwt))
            {
                _logger.LogError("Failed to create JWT");
                return null;
            }
            
                // Exchange JWT for access token
                var tokenRequest = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
                    new KeyValuePair<string, string>("assertion", jwt)
                });
                
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // 30s timeout
                var response = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", tokenRequest, cts.Token);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent);
                    if (tokenResponse?.AccessToken != null)
                    {
                        _cachedAccessToken = tokenResponse.AccessToken;
                        _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 300); // 5 min buffer
                        
                        _logger.LogInformation("✅ Successfully obtained access token for FCM API v1 on attempt {Attempt}", attempt);
                        return _cachedAccessToken;
                    }
                }
                
                _logger.LogWarning("⚠️ Failed to obtain access token on attempt {Attempt}/{MaxRetries}: {StatusCode} - {ResponseContent}", 
                    attempt, maxRetries, response.StatusCode, responseContent);
                    
                if (attempt < maxRetries)
                {
                    var delay = retryDelayMs * attempt; // Exponential backoff
                    _logger.LogInformation("Retrying access token request in {DelayMs}ms...", delay);
                    await Task.Delay(delay);
                    continue;
                }
                
                return null;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("⏰ Access token request timed out on attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
                if (attempt < maxRetries)
                {
                    var delay = retryDelayMs * attempt;
                    _logger.LogInformation("Retrying access token request in {DelayMs}ms...", delay);
                    await Task.Delay(delay);
                    continue;
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error obtaining access token for FCM API v1 on attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
                if (attempt < maxRetries)
                {
                    var delay = retryDelayMs * attempt;
                    _logger.LogInformation("Retrying access token request in {DelayMs}ms...", delay);
                    await Task.Delay(delay);
                    continue;
                }
                return null;
            }
        }
        
        return null; // All retries failed
    }
    
    /// <summary>
    /// Create JWT using RSA private key
    /// </summary>
    private string? CreateJwt(Dictionary<string, object> claims, string privateKeyPem)
    {
        try
        {
            // Clean up the private key format
            var privateKeyContent = privateKeyPem
                .Replace("-----BEGIN PRIVATE KEY-----", "")
                .Replace("-----END PRIVATE KEY-----", "")
                .Replace("\\n", "\n")
                .Replace("\n", "")
                .Replace("\r", "")
                .Trim();
            
            var privateKeyBytes = Convert.FromBase64String(privateKeyContent);
            
            using var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);
            
            var key = new RsaSecurityKey(rsa);
            var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
            
            var header = new JwtHeader(credentials);
            var payload = new JwtPayload();
            
            // Add claims to payload
            foreach (var claim in claims)
            {
                payload[claim.Key] = claim.Value;
            }
            
            var token = new JwtSecurityToken(header, payload);
            var handler = new JwtSecurityTokenHandler();
            
            return handler.WriteToken(token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating JWT");
            return null;
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
            _logger.LogInformation("[SIMULATION] Push notification sent successfully: {Title}", title);
            return true;
        }
        else if (outcome < 0.95) // 3% invalid token
        {
            _logger.LogWarning("[SIMULATION] Invalid push token detected");
            return false;
        }
        else // 5% network/server error
        {
            _logger.LogWarning("[SIMULATION] Network error during push notification");
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
/// Firebase Service Account information
/// </summary>
public class ServiceAccountInfo
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
    
    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";
    
    [JsonPropertyName("private_key_id")]
    public string PrivateKeyId { get; set; } = "";
    
    [JsonPropertyName("private_key")]
    public string PrivateKey { get; set; } = "";
    
    [JsonPropertyName("client_email")]
    public string ClientEmail { get; set; } = "";
    
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = "";
    
    [JsonPropertyName("auth_uri")]
    public string AuthUri { get; set; } = "";
    
    [JsonPropertyName("token_uri")]
    public string TokenUri { get; set; } = "";
}

/// <summary>
/// OAuth 2.0 token response
/// </summary>
public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = "";
    
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
    
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "";
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

public static class FirebaseServiceExtensions
{
    /// <summary>
    /// Creates a data payload ensuring required fields are included
    /// </summary>
    public static Dictionary<string, string> CreateDataPayload(Dictionary<string, object>? data)
    {
        var result = data?.ToDictionary(x => x.Key, x => x.Value?.ToString() ?? "") ?? new Dictionary<string, string>();
        
        // Ensure timestamp is always included
        if (!result.ContainsKey("timestamp"))
        {
            result["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        }
        
        // Ensure type is set for daily push if not specified
        if (!result.ContainsKey("type"))
        {
            result["type"] = ((int)PushType.DailyPush).ToString(); // Default to enum value 1
        }
        
        return result;
    }
}
