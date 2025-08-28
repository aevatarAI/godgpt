using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http;
using System.Text;
using System.Threading;
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
    
    // ✅ Global pushToken daily push tracking to prevent same-day duplicates across timezones
    private static readonly ConcurrentDictionary<string, DateOnly> _lastPushDates = new();
    private static readonly TimeSpan _shortTermCooldown = TimeSpan.FromMinutes(10); // Prevent rapid duplicates
    private static readonly ConcurrentDictionary<string, DateTime> _lastPushTimes = new();
    private static int _cleanupCounter = 0;
    
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
            
            // ✅ Dual-layer deduplication: short-term + same-day prevention
            var now = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(now);
            
            // Periodically cleanup old records (every 100 calls)
            if (Interlocked.Increment(ref _cleanupCounter) % 100 == 0)
            {
                CleanupOldRecords();
            }
            
            // ✅ Layer 1: Check if already pushed today (UTC date) - Skip for test pushes
            bool isTestPush = title.Contains("🧪") || title.Contains("test") || title.Contains("Test") || title.Contains("TEST");
            
            if (!isTestPush && _lastPushDates.TryGetValue(pushToken, out var lastPushDate) && lastPushDate == today)
            {
                _logger.LogInformation("📅 PushToken {TokenPrefix} already received daily push on {Date}, skipping duplicate", 
                    pushToken.Substring(0, Math.Min(8, pushToken.Length)) + "...", 
                    today.ToString("yyyy-MM-dd"));
                return false;
            }
            
            if (isTestPush)
            {
                _logger.LogDebug("🧪 Test push detected, skipping daily date check for token {TokenPrefix}", 
                    pushToken.Substring(0, Math.Min(8, pushToken.Length)) + "...");
            }
            
            // ✅ Layer 2: Short-term cooldown check (prevent rapid fire)
            var canSendTime = _lastPushTimes.AddOrUpdate(
                pushToken,
                now, // If key doesn't exist, add with current time
                (key, existingTime) =>
                {
                    var timeSinceLastPush = now - existingTime;
                    if (timeSinceLastPush < _shortTermCooldown)
                    {
                        // Still in short-term cooldown - return existing time (no update)
                        return existingTime;
                    }
                    // Short-term cooldown expired - update to current time
                    return now;
                });
            
            // Check short-term cooldown
            if (canSendTime != now)
            {
                var timeSinceLastPush = now - canSendTime;
                var remainingCooldown = _shortTermCooldown - timeSinceLastPush;
                _logger.LogInformation("⏱️ PushToken {TokenPrefix} in short-term cooldown, skipping push. Last push: {TimeSince} ago, cooldown remaining: {Remaining}", 
                    pushToken.Substring(0, Math.Min(8, pushToken.Length)) + "...", 
                    timeSinceLastPush.ToString(@"mm\:ss"), 
                    remainingCooldown.ToString(@"mm\:ss"));
                return false;
            }
            
            // Use FCM API v1 if configured
            bool success;
            if (_serviceAccount != null && !string.IsNullOrEmpty(_projectId))
            {
                success = await SendPushNotificationV1Async(pushToken, title, content, data);
            }
            else
            {
                // No credentials configured - simulation mode
                _logger.LogWarning("Firebase credentials not configured, using simulation mode");
                success = await SimulatePushAsync(pushToken, title, content);
            }
            
            // ✅ Record successful daily push date to prevent same-day duplicates (skip for test pushes)
            if (success)
            {
                if (!isTestPush)
                {
                    _lastPushDates.AddOrUpdate(pushToken, today, (key, oldDate) => today);
                    _logger.LogDebug("📱 Daily push completed successfully for token {TokenPrefix} at {PushTime}, date recorded: {Date}", 
                        pushToken.Substring(0, Math.Min(8, pushToken.Length)) + "...", now.ToString("HH:mm:ss"), today.ToString("yyyy-MM-dd"));
                }
                else
                {
                    _logger.LogDebug("🧪 Test push completed successfully for token {TokenPrefix} at {PushTime}, date not recorded", 
                        pushToken.Substring(0, Math.Min(8, pushToken.Length)) + "...", now.ToString("HH:mm:ss"));
                }
            }
            else
            {
                _logger.LogDebug("❌ Push failed for token {TokenPrefix} at {PushTime}, date not recorded", 
                    pushToken.Substring(0, Math.Min(8, pushToken.Length)) + "...", now.ToString("HH:mm:ss"));
            }
            
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Unexpected error sending push notification to {pushToken}");
            return false;
        }
    }
    
    /// <summary>
    /// Clean up old push records to prevent memory leaks
    /// Called periodically (every 100 requests)
    /// </summary>
    private static void CleanupOldRecords()
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        
        // Clean up old date records (older than 3 days)
        var dateCutoff = today.AddDays(-3);
        var dateKeysToRemove = _lastPushDates
            .Where(kvp => kvp.Value < dateCutoff)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var key in dateKeysToRemove)
        {
            _lastPushDates.TryRemove(key, out _);
        }
        
        // Clean up old time records (older than short-term cooldown)
        var timeCutoff = now.Subtract(_shortTermCooldown);
        var timeKeysToRemove = _lastPushTimes
            .Where(kvp => kvp.Value < timeCutoff)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var key in timeKeysToRemove)
        {
            _lastPushTimes.TryRemove(key, out _);
        }
        
        var totalCleaned = dateKeysToRemove.Count + timeKeysToRemove.Count;
        if (totalCleaned > 0)
        {
            // Note: Can't use _logger here as this is a static method
            Console.WriteLine($"🧹 Cleaned up {dateKeysToRemove.Count} old date records and {timeKeysToRemove.Count} old time records from push tracking cache");
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
            
            _logger.LogDebug("FCM v1 payload: {JsonPayload}", jsonPayload);
            
            var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            
            using var request = new HttpRequestMessage(HttpMethod.Post, fcmV1Endpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = httpContent;
            
            _logger.LogDebug("Sending FCM v1 push notification request to token: {TokenPrefix}...", 
                pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken);
            
            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            // Always log FCM response at Information level for debugging purposes
            _logger.LogInformation("🔥 FCM v1 response - Status: {StatusCode}, Token: {TokenPrefix}..., Title: '{Title}', Content: {ResponseContent}", 
                response.StatusCode, pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, title, responseContent);
            
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
                        
                        _logger.LogError("🚨 FCM returned error in successful response - Code: {ErrorCode}, Message: {ErrorMessage}, Token: {TokenPrefix}..., Title: {Title}", 
                            errorCode, errorMessage, pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, title);
                        
                        // Handle specific FCM errors
                        if (errorCode == "UNREGISTERED" || errorCode == "INVALID_ARGUMENT")
                        {
                            _logger.LogWarning("❌ Push token is invalid and should be removed from database - Token: {TokenPrefix}..., ErrorCode: {ErrorCode}", 
                                pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, errorCode);
                        }
                        
                        return false;
                    }
                    
                    // Check if response contains message name (success indicator)
                    if (root.TryGetProperty("name", out var nameElement))
                    {
                        var messageName = nameElement.GetString();
                        _logger.LogInformation("✅ Push notification sent successfully via FCM v1 - Message: {MessageName}, Token: {TokenPrefix}..., Title: {Title}", 
                            messageName, pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, title);
                        return true;
                    }
                    
                    // Unexpected response format
                    _logger.LogWarning("⚠️ FCM returned 200 but unexpected response format: {ResponseContent}, Token: {TokenPrefix}..., Title: {Title}", 
                        responseContent, pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, title);
                    return false;
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to parse FCM response JSON: {ResponseContent}", responseContent);
                    // Assume success if we can't parse the response but got 200
                    _logger.LogInformation("✅ Push notification sent successfully via FCM v1 (assumed from 200 status) - Token: {TokenPrefix}..., Title: {Title}", 
                        pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, title);
                    return true;
                }
            }
            else
            {
                _logger.LogError("💥 FCM v1 request failed with status {StatusCode}: {ResponseContent}, Token: {TokenPrefix}..., Title: {Title}", 
                    response.StatusCode, responseContent, pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, title);
                
                // Handle specific FCM v1 errors
                if (responseContent.Contains("UNREGISTERED") || responseContent.Contains("INVALID_ARGUMENT"))
                {
                    _logger.LogWarning("❌ Token is invalid and should be removed from database - Token: {TokenPrefix}..., Status: {StatusCode}", 
                        pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, response.StatusCode);
                }
                
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Error sending push notification via FCM API v1 - Token: {TokenPrefix}..., Title: {Title}", 
                pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, title);
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
    /// Send push notifications to multiple devices using FCM sendEach (single HTTP request)
    /// This is the recommended approach for multiple messages in FCM API v1
    /// </summary>
    public async Task<BatchPushResult> SendEachAsync(
        List<PushMessage> messages)
    {
        var results = new BatchPushResult();
        
        if (messages == null || !messages.Any())
        {
            _logger.LogWarning("No messages provided for batch send");
            return results;
        }
        
        // ✅ Global pushToken deduplication - prevent cross-timezone duplicates
        var originalCount = messages.Count;
        var deduplicatedMessages = messages
            .Where(m => !string.IsNullOrEmpty(m.Token))
            .GroupBy(m => m.Token)
            .Select(g => g.First()) // Keep first message for each pushToken
            .ToList();
            
        var duplicateCount = originalCount - deduplicatedMessages.Count;
        if (duplicateCount > 0)
        {
            _logger.LogInformation("🌍 Global pushToken deduplication: {OriginalCount} → {DeduplicatedCount} messages (removed {DuplicateCount} cross-timezone duplicates)", 
                originalCount, deduplicatedMessages.Count, duplicateCount);
        }
        
        // Use deduplicated messages for actual sending
        messages = deduplicatedMessages;
        
        try
        {
            // If no FCM credentials, fall back to simulation
            if (_serviceAccount == null || string.IsNullOrEmpty(_projectId))
            {
                _logger.LogInformation("🧪 FCM credentials not configured, using simulation mode for {Count} messages", messages.Count);
                return await SimulateBatchPushAsync(messages);
            }
            
            // Use FCM API v1 batch send
            return await SendEachV1Async(messages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SendEachAsync for {Count} messages", messages.Count);
            results.FailureCount = messages.Count;
            results.FailedTokens = messages.Select(m => m.Token).ToList();
            return results;
        }
    }
    
    /// <summary>
    /// Send multiple messages using FCM API v1 (single HTTP request)
    /// </summary>
    private async Task<BatchPushResult> SendEachV1Async(List<PushMessage> messages)
    {
        var results = new BatchPushResult();
        
        try
        {
            var accessToken = await GetAccessTokenAsync();
            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogError("Failed to obtain access token for FCM batch send");
                results.FailureCount = messages.Count;
                results.FailedTokens = messages.Select(m => m.Token).ToList();
                return results;
            }
            
            // FCM API v1 doesn't have a native "sendEach" endpoint like the legacy API
            // So we'll use concurrent requests with proper rate limiting
            const int batchSize = 10; // FCM recommended batch size
            const int delayMs = 100;  // Reduced delay since we're using proper batching
            
            // Log batch push start with device details
            var tokenPreviews = messages.Take(5).Select(m => 
                m.Token.Length > 10 ? m.Token.Substring(0, 10) + "..." : m.Token).ToList();
            var remainingCount = Math.Max(0, messages.Count - 5);
            var tokenPreviewText = string.Join(", ", tokenPreviews) + 
                (remainingCount > 0 ? $" (+{remainingCount} more)" : "");
                
            _logger.LogInformation("🚀 Starting FCM batch push to {TotalDevices} devices: [{TokenPreviews}]", 
                messages.Count, tokenPreviewText);
            _logger.LogInformation("📋 Batch configuration: BatchSize={BatchSize}, DelayMs={DelayMs}, TotalBatches={TotalBatches}", 
                batchSize, delayMs, (messages.Count + batchSize - 1) / batchSize);
            
            for (int i = 0; i < messages.Count; i += batchSize)
            {
                var batch = messages.Skip(i).Take(batchSize).ToList();
                _logger.LogDebug("📦 Processing batch {BatchNumber}/{TotalBatches} with {BatchSize} messages", 
                    (i / batchSize) + 1, (messages.Count + batchSize - 1) / batchSize, batch.Count);
                
                var batchTasks = batch.Select(async (message, index) =>
                {
                    var tokenPrefix = message.Token.Length > 10 ? message.Token.Substring(0, 10) + "..." : message.Token;
                    
                    _logger.LogDebug("📱 Processing token {TokenIndex}/{BatchSize}: {TokenPrefix} - Title: '{Title}'", 
                        index + 1, batch.Count, tokenPrefix, message.Title);
                    
                    // ✅ Use SendPushNotificationAsync to apply global date-based deduplication
                    var success = await SendPushNotificationAsync(
                        message.Token, 
                        message.Title, 
                        message.Content, 
                        message.Data);
                    
                    if (success)
                    {
                        results.SuccessCount++;
                        _logger.LogInformation("✅ Push SUCCESS for token {TokenPrefix} - Title: '{Title}'", 
                            tokenPrefix, message.Title);
                    }
                    else
                    {
                        results.FailureCount++;
                        results.FailedTokens.Add(message.Token);
                        _logger.LogWarning("❌ Push FAILED for token {TokenPrefix} - Title: '{Title}' (Check detailed error above)", 
                            tokenPrefix, message.Title);
                    }
                }).ToArray();
                
                await Task.WhenAll(batchTasks);
                
                // Add small delay between batches to respect FCM rate limits
                if (i + batchSize < messages.Count)
                {
                    await Task.Delay(delayMs);
                }
            }
            
            // Enhanced completion logging with detailed statistics
            var successRate = results.SuccessCount + results.FailureCount > 0 ? 
                (double)results.SuccessCount / (results.SuccessCount + results.FailureCount) * 100 : 0;
                
            if (results.FailureCount == 0)
            {
                _logger.LogInformation("🎉 FCM batch push completed successfully: {SuccessCount}/{TotalCount} devices (100% success rate)", 
                    results.SuccessCount, results.SuccessCount + results.FailureCount);
            }
            else
            {
                // Show failed token previews for debugging
                var failedTokenPreviews = results.FailedTokens.Take(3).Select(token => 
                    token.Length > 10 ? token.Substring(0, 10) + "..." : token).ToList();
                var remainingFailures = Math.Max(0, results.FailedTokens.Count - 3);
                var failedTokenText = string.Join(", ", failedTokenPreviews) + 
                    (remainingFailures > 0 ? $" (+{remainingFailures} more)" : "");
                
                _logger.LogWarning("⚠️ FCM batch push completed with failures: {SuccessCount}/{TotalCount} successful ({SuccessRate:F1}% success rate)", 
                    results.SuccessCount, results.SuccessCount + results.FailureCount, successRate);
                _logger.LogWarning("💔 Failed tokens: [{FailedTokens}] - Check detailed errors above", failedTokenText);
            }
                
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SendEachV1Async");
            results.FailureCount = messages.Count;
            results.FailedTokens = messages.Select(m => m.Token).ToList();
            return results;
        }
    }
    
    /// <summary>
    /// Simulate batch push for development/testing
    /// </summary>
    private async Task<BatchPushResult> SimulateBatchPushAsync(List<PushMessage> messages)
    {
        var results = new BatchPushResult();
        
        foreach (var message in messages)
        {
            var success = await SimulatePushAsync(message.Token, message.Title, message.Content);
            if (success)
            {
                results.SuccessCount++;
            }
            else
            {
                results.FailureCount++;
                results.FailedTokens.Add(message.Token);
            }
        }
        
        return results;
    }
    
    /// <summary>
    /// Send push notifications to multiple devices (batch) - Legacy method
    /// </summary>
    [Obsolete("Use SendEachAsync instead for better performance")]
    public async Task<BatchPushResult> SendBatchPushNotificationAsync(
        List<string> pushTokens,
        string title,
        string content,
        Dictionary<string, object>? data = null)
    {
        var messages = pushTokens.Select(token => new PushMessage
        {
            Token = token,
            Title = title,
            Content = content,
            Data = data
        }).ToList();
        
        return await SendEachAsync(messages);
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
/// Push message for batch sending
/// </summary>
public class PushMessage
{
    public string Token { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public Dictionary<string, object>? Data { get; set; }
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
            result["type"] = ((int)DailyPushConstants.PushType.DailyPush).ToString(); // Default to enum value 1
        }
        
        return result;
    }
}
