using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using GodGPT.GAgents.DailyPush.Options;
using GodGPT.GAgents.DailyPush.SEvents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Orleans.Providers;

namespace GodGPT.GAgents.DailyPush;

// Note: Using TokenResponse from FirebaseService.cs

/// <summary>
/// Global JWT Provider GAgent - singleton for entire system (Grain ID: 0)
/// Manages JWT creation, caching, and global push token deduplication
/// Eliminates concurrency issues and resource waste from per-user JWT creation
/// </summary>
[StorageProvider(ProviderName = "PubSubStore")]
[LogConsistencyProvider(ProviderName = "LogStorage")]
[GAgent(nameof(GlobalJwtProviderGAgent))]
public class GlobalJwtProviderGAgent : GAgentBase<GlobalJwtProviderState, DailyPushLogEvent>, IGlobalJwtProviderGAgent
{
    private readonly ILogger<GlobalJwtProviderGAgent> _logger;
    private readonly IOptionsMonitor<DailyPushOptions> _options;
    
    // Global pushToken daily push tracking to prevent same-day duplicates across users
    private static readonly ConcurrentDictionary<string, DateOnly> _lastPushDates = new();
    private static int _cleanupCounter = 0;
    
    // JWT caching and creation control
    private string? _cachedJwtToken;
    private DateTime _tokenExpiry = DateTime.MinValue;
    private volatile Task<string?>? _tokenCreationTask;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    
    // Error handling and retry control
    private DateTime _lastFailureTime = DateTime.MinValue;
    private int _consecutiveFailures = 0;
    private const int MAX_CONSECUTIVE_FAILURES = 3;
    private static readonly TimeSpan FAILURE_COOLDOWN = TimeSpan.FromMinutes(5);

    public GlobalJwtProviderGAgent(
        ILogger<GlobalJwtProviderGAgent> logger,
        IOptionsMonitor<DailyPushOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    public override async Task<string> GetDescriptionAsync()
    {
        var status = await GetStatusAsync();
        return $"Global JWT Provider - Tokens: {status.TotalTokenRequests}, Dedupe: {status.TotalDeduplicationChecks}, Prevented: {status.PreventedDuplicates}";
    }

    public async Task<string?> GetFirebaseAccessTokenAsync()
    {
        State.IncrementTokenRequests();
        
        // Check cached token first (24-hour cache)
        if (!string.IsNullOrEmpty(_cachedJwtToken) && DateTime.UtcNow < _tokenExpiry.AddMinutes(-5))
        {
            _logger.LogDebug("Using cached JWT token");
            return _cachedJwtToken;
        }

        // Check for failure cooldown period to prevent rapid retries after consecutive failures
        if (_consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
        {
            var timeSinceLastFailure = DateTime.UtcNow - _lastFailureTime;
            if (timeSinceLastFailure < FAILURE_COOLDOWN)
            {
                var remainingCooldown = FAILURE_COOLDOWN - timeSinceLastFailure;
                _logger.LogWarning(
                    "⏱️ JWT creation in cooldown period due to {FailureCount} consecutive failures. " +
                    "Remaining cooldown: {RemainingTime:mm\\:ss}. Last failure: {LastFailure}",
                    _consecutiveFailures, remainingCooldown, _lastFailureTime);
                return null;
            }
            else
            {
                // Reset failure count after cooldown period
                _logger.LogInformation("🔄 Cooldown period expired, resetting failure count from {FailureCount} to 0", _consecutiveFailures);
                _consecutiveFailures = 0;
            }
        }

        // Use task-based singleton pattern for thread-safe token creation
        var currentTask = _tokenCreationTask;
        if (currentTask != null && !currentTask.IsCompleted)
        {
            _logger.LogDebug("JWT creation already in progress, awaiting result");
            return await currentTask;
        }

        await _tokenSemaphore.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (!string.IsNullOrEmpty(_cachedJwtToken) && DateTime.UtcNow < _tokenExpiry.AddMinutes(-5))
            {
                return _cachedJwtToken;
            }

            // Check if another thread started creation
            currentTask = _tokenCreationTask;
            if (currentTask != null && !currentTask.IsCompleted)
            {
                return await currentTask;
            }

            // Start new token creation
            _tokenCreationTask = CreateJwtTokenInternalAsync();
            return await _tokenCreationTask;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    public async Task<bool> CanSendPushAsync(string pushToken, string timeZoneId, bool isRetryPush = false, bool isFirstContent = true)
    {
        if (string.IsNullOrEmpty(pushToken))
        {
            _logger.LogWarning("Push token is empty for deduplication check");
            return false;
        }

        State.IncrementDeduplicationChecks();

        // Periodically cleanup old records (every 100 calls)
        if (Interlocked.Increment(ref _cleanupCounter) % 100 == 0)
        {
            CleanupOldRecords();
        }

        // Timezone-based deduplication: each timezone can have daily pushes independently
        var dedupeKey = $"{pushToken}:{timeZoneId}";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Only check date deduplication for first content of regular daily pushes
        if (!isRetryPush && isFirstContent && _lastPushDates.TryGetValue(dedupeKey, out var lastPushDate) && lastPushDate == today)
        {
            _logger.LogInformation(
                "📅 PushToken {TokenPrefix} in timezone {TimeZone} already received daily push on {Date}, preventing duplicate",
                pushToken.Substring(0, Math.Min(8, pushToken.Length)) + "...",
                timeZoneId,
                today.ToString("yyyy-MM-dd"));
            
            State.IncrementPreventedDuplicates();
            
            // Log deduplication event for analytics
            var tokenPrefix = pushToken.Substring(0, Math.Min(8, pushToken.Length));
            RaiseEvent(new DuplicatePreventionEventLog 
            { 
                PushTokenPrefix = tokenPrefix,
                TimeZone = timeZoneId,
                PreventionDate = today.ToDateTime(TimeOnly.MinValue),
                PreventionTime = DateTime.UtcNow
            });
            
            return false;
        }

        return true;
    }

    public async Task MarkPushSentAsync(string pushToken, string timeZoneId, bool isRetryPush = false, bool isFirstContent = true)
    {
        if (string.IsNullOrEmpty(pushToken))
        {
            return;
        }

        // Record successful daily push date to prevent same-day duplicates (only for first content of regular daily pushes)
        if (!isRetryPush && isFirstContent)
        {
            var dedupeKey = $"{pushToken}:{timeZoneId}";
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            
            _lastPushDates.AddOrUpdate(dedupeKey, today, (key, oldDate) => today);
            
            _logger.LogDebug("Marked push sent: token {TokenPrefix} in timezone {TimeZone} on {Date}",
                pushToken.Substring(0, Math.Min(8, pushToken.Length)) + "...", 
                timeZoneId, 
                today.ToString("yyyy-MM-dd"));
        }

        await Task.CompletedTask;
    }

    public async Task<GlobalJwtProviderStatus> GetStatusAsync()
    {
        return new GlobalJwtProviderStatus
        {
            IsReady = !string.IsNullOrEmpty(_options?.CurrentValue?.FilePaths?.FirebaseKeyPath),
            HasCachedToken = !string.IsNullOrEmpty(_cachedJwtToken),
            TokenExpiry = _tokenExpiry != DateTime.MinValue ? _tokenExpiry : null,
            TotalTokenRequests = State.TotalTokenRequests,
            TotalDeduplicationChecks = State.TotalDeduplicationChecks,
            PreventedDuplicates = State.PreventedDuplicates,
            TrackedPushTokens = _lastPushDates.Count,
            LastTokenCreation = State.LastTokenCreation,
            LastCleanup = State.LastCleanup,
            LastError = State.LastError
        };
    }

    public async Task RefreshTokenAsync()
    {
        _logger.LogInformation("Force refreshing JWT token");
        _cachedJwtToken = null;
        _tokenExpiry = DateTime.MinValue;
        _tokenCreationTask = null;
        
        var newToken = await GetFirebaseAccessTokenAsync();
        _logger.LogInformation("JWT token force refreshed: {HasToken}", !string.IsNullOrEmpty(newToken));
    }

    /// <summary>
    /// Internal JWT creation with proper RSA lifecycle management
    /// Uses proven pattern from UserBillingGrain to avoid ObjectDisposedException
    /// </summary>
    private async Task<string?> CreateJwtTokenInternalAsync()
    {
        const int maxRetries = 3;
        const int retryDelayMs = 1000;

        try
        {
            var firebaseKeyPath = _options?.CurrentValue?.FilePaths?.FirebaseKeyPath;
            if (string.IsNullOrEmpty(firebaseKeyPath))
            {
                var error = "❌ CRITICAL: DailyPushOptions.FilePaths.FirebaseKeyPath not configured for GlobalJwtProviderGAgent";
                _logger.LogError(error);
                State.RecordError(error);
                return null;
            }

            var httpClient = ServiceProvider.GetService(typeof(HttpClient)) as HttpClient;
            if (httpClient == null)
            {
                var error = "HttpClient not available for GlobalJwtProviderGAgent";
                _logger.LogError(error);
                State.RecordError(error);
                return null;
            }

            // Load service account from Firebase key file
            var serviceAccount = LoadServiceAccountFromFile(firebaseKeyPath);
            if (serviceAccount == null)
            {
                var error = $"Failed to load service account from {firebaseKeyPath}";
                _logger.LogError(error);
                State.RecordError(error);
                return null;
            }

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Create JWT using proven RSA pattern
                    var jwt = CreateJwt(serviceAccount);
                    if (string.IsNullOrEmpty(jwt))
                    {
                        throw new InvalidOperationException("JWT creation returned null/empty");
                    }

                    // Exchange JWT for access token
                    var tokenResponse = await ExchangeJwtForAccessTokenAsync(httpClient, jwt);
                    if (tokenResponse != null && !string.IsNullOrEmpty(tokenResponse.AccessToken))
                    {
                        // Cache token with 24-hour expiry (or shorter if specified)
                        var expiryHours = Math.Min(tokenResponse.ExpiresIn / 3600, 24);
                        _tokenExpiry = DateTime.UtcNow.AddHours(expiryHours);
                        
                        _logger.LogDebug("Token calculated with {ExpiryHours}h expiry", expiryHours);
                        _cachedJwtToken = tokenResponse.AccessToken;
                        
                        State.RecordSuccessfulTokenCreation();
                        RaiseEvent(new TokenCreationSuccessEventLog 
                        { 
                            CreationTime = DateTime.UtcNow,
                            TokenExpiry = _tokenExpiry 
                        });
                        
                        // Reset failure tracking on success
                        _consecutiveFailures = 0;
                        _lastFailureTime = DateTime.MinValue;
                        
                        _logger.LogInformation("Global JWT token created successfully");
                        return tokenResponse.AccessToken;
                    }
                    else
                    {
                        var httpError = $"Firebase OAuth returned empty token on attempt {attempt}/{maxRetries}";
                        _logger.LogWarning(httpError);
                        
                        if (attempt < maxRetries)
                        {
                            await Task.Delay(retryDelayMs * attempt);
                            continue;
                        }
                        
                        State.RecordError(httpError);
                        RaiseEvent(new TokenCreationFailureEventLog 
                        { 
                            AttemptNumber = maxRetries,
                            ErrorMessage = httpError 
                        });
                        
                        // Record failure for cooldown mechanism
                        RecordTokenCreationFailure();
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    var exceptionError = $"Exception during global JWT creation attempt {attempt}: {ex.Message}";
                    _logger.LogError(ex, exceptionError);
                    
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(retryDelayMs * attempt);
                        continue;
                    }
                    
                    State.RecordError(exceptionError);
                    RaiseEvent(new TokenCreationFailureEventLog 
                    { 
                        AttemptNumber = maxRetries,
                        ErrorMessage = exceptionError 
                    });
                    
                    // Record failure for cooldown mechanism
                    RecordTokenCreationFailure();
                    return null;
                }
            }

            return null;
        }
        finally
        {
            _tokenCreationTask = null;
        }
    }

    /// <summary>
    /// Record token creation failure and clear cache to prevent invalid state
    /// </summary>
    private void RecordTokenCreationFailure()
    {
        _consecutiveFailures++;
        _lastFailureTime = DateTime.UtcNow;
        
        // Clear invalid cached state to force fresh attempt after cooldown
        _cachedJwtToken = null;
        _tokenExpiry = DateTime.MinValue;
        
        _logger.LogError(
            "JWT creation failed after {FailureCount} consecutive failures. Next attempt blocked until cooldown expires",
            _consecutiveFailures);
    }

    /// <summary>
    /// Create JWT using RSA private key - proven pattern from UserBillingGrain
    /// </summary>
    private string? CreateJwt(ServiceAccountInfo serviceAccount)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var expiry = now.AddHours(1);

            var claims = new Dictionary<string, object>
            {
                { "iss", serviceAccount.ClientEmail },
                { "scope", "https://www.googleapis.com/auth/firebase.messaging" },
                { "aud", "https://oauth2.googleapis.com/token" },
                { "iat", now.ToUnixTimeSeconds() },
                { "exp", expiry.ToUnixTimeSeconds() }
            };

            // Clean up private key format
            var privateKeyContent = serviceAccount.PrivateKey
                .Replace("-----BEGIN PRIVATE KEY-----", "")
                .Replace("-----END PRIVATE KEY-----", "")
                .Replace("\\n", "\n")
                .Replace("\n", "")
                .Replace("\r", "")
                .Trim();

            var privateKeyBytes = Convert.FromBase64String(privateKeyContent);

            // Simple and direct RSA lifecycle management - complete all JWT operations within using block
            using (var rsa = RSA.Create())
            {
                rsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);
                
                // Create security key and signing credentials
                var securityKey = new RsaSecurityKey(rsa) { KeyId = Guid.NewGuid().ToString() };
                var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

                // Create token descriptor with claims and signing credentials  
                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Claims = claims,
                    SigningCredentials = signingCredentials
                };

                // Create and sign token - all operations completed within RSA lifetime
                var tokenHandler = new JwtSecurityTokenHandler();
                var token = tokenHandler.CreateJwtSecurityToken(tokenDescriptor);
                
                // Return signed token string - RSA object remains valid throughout entire process
                return tokenHandler.WriteToken(token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error in global JWT creation: {ErrorMessage}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Load service account information from Firebase key file
    /// </summary>
    private ServiceAccountInfo? LoadServiceAccountFromFile(string firebaseKeyPath)
    {
        try
        {
            _logger.LogDebug("Loading Firebase key from path: {KeyPath}", firebaseKeyPath);

            if (!File.Exists(firebaseKeyPath))
            {
                _logger.LogWarning("Firebase key file not found: {KeyPath}", firebaseKeyPath);
                return null;
            }

            var jsonContent = File.ReadAllText(firebaseKeyPath);
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                _logger.LogWarning("Firebase key file is empty: {KeyPath}", firebaseKeyPath);
                return null;
            }

            var serviceAccount = JsonSerializer.Deserialize<ServiceAccountInfo>(jsonContent);
            if (serviceAccount != null)
            {
                _logger.LogDebug("Successfully loaded Firebase service account for global JWT provider");
                return serviceAccount;
            }

            _logger.LogWarning("Failed to deserialize Firebase service account from file: {KeyPath}", firebaseKeyPath);
            return null;
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "JSON parsing error loading Firebase service account from file: {KeyPath}", firebaseKeyPath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error loading Firebase service account from file: {KeyPath}", firebaseKeyPath);
            return null;
        }
    }

    /// <summary>
    /// Exchange JWT for Firebase access token via OAuth API
    /// </summary>
    private async Task<TokenResponse?> ExchangeJwtForAccessTokenAsync(HttpClient httpClient, string jwt)
    {
        try
        {
            var requestContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
                new KeyValuePair<string, string>("assertion", jwt)
            });

            var response = await httpClient.PostAsync("https://oauth2.googleapis.com/token", requestContent);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent);
                _logger.LogDebug("Firebase OAuth response received");
                return tokenResponse;
            }
            else
            {
                _logger.LogError("Firebase OAuth API error: {StatusCode} - {Content}", response.StatusCode, responseContent);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during Firebase OAuth token exchange: {ErrorMessage}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Cleanup old push records to prevent memory leaks
    /// </summary>
    private void CleanupOldRecords()
    {
        try
        {
            var cutoffDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));
            var keysToRemove = _lastPushDates
                .Where(kvp => kvp.Value < cutoffDate)
                .Select(kvp => kvp.Key)
                .ToList();

            var removedCount = 0;
            foreach (var key in keysToRemove)
            {
                if (_lastPushDates.TryRemove(key, out _))
                {
                    removedCount++;
                }
            }

            if (removedCount > 0)
            {
                _logger.LogDebug("Cleaned up {RemovedCount} old push records", removedCount);
            }

            State.RecordCleanup();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during push records cleanup: {ErrorMessage}", ex.Message);
        }
    }
}
