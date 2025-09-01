using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using GodGPT.GAgents.DailyPush.Options;
using GodGPT.GAgents.DailyPush.SEvents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Orleans.Providers;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Firebase access token provider GAgent implementation
/// Provides JWT token management for individual ChatManager without concurrency conflicts
/// </summary>
[StorageProvider(ProviderName = "PubSubStore")]
[LogConsistencyProvider(ProviderName = "LogStorage")]
[GAgent(nameof(FirebaseTokenProviderGAgent))]
public class FirebaseTokenProviderGAgent : GAgentBase<FirebaseTokenProviderGAgentState, DailyPushLogEvent>, 
    IFirebaseTokenProviderGAgent
{
    private readonly ILogger<FirebaseTokenProviderGAgent> _logger;
    private readonly IConfiguration _configuration;
    private readonly IOptionsMonitor<DailyPushOptions> _options;
    private readonly HttpClient _httpClient;
    private ServiceAccountInfo? _serviceAccount;

    public FirebaseTokenProviderGAgent(
        ILogger<FirebaseTokenProviderGAgent> logger,
        IConfiguration configuration,
        IOptionsMonitor<DailyPushOptions> options,
        HttpClient httpClient)
    {
        _logger = logger;
        _configuration = configuration;
        _options = options;
        _httpClient = httpClient;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Firebase token provider for ChatManager {this.GetPrimaryKeyLong()}");
    }

    protected override async Task OnGAgentActivateAsync(CancellationToken cancellationToken)
    {
        var chatManagerId = this.GetPrimaryKeyLong();
        _logger.LogInformation("FirebaseTokenProviderGAgent activated for ChatManager {ChatManagerId}", chatManagerId);

        // Initialize service account configuration
        await InitializeServiceAccountAsync();

        // Log activation event
        RaiseEvent(new TokenProviderActivationEventLog 
        { 
            ChatManagerId = chatManagerId 
        });

        State.ActivationTime = DateTime.UtcNow;
    }

    private async Task InitializeServiceAccountAsync()
    {
        try
        {
            // Load service account from file first, then fallback to configuration
            var fileAccount = LoadServiceAccountFromFile(new[] { _options.CurrentValue.FilePaths.FirebaseKeyPath });
            var configAccount = fileAccount == null ? LoadServiceAccountFromConfiguration(_configuration) : null;

            _serviceAccount = fileAccount ?? configAccount;

            if (_serviceAccount != null)
            {
                _logger.LogInformation("Firebase service account loaded successfully for ChatManager {ChatManagerId}", 
                    this.GetPrimaryKeyLong());
            }
            else
            {
                _logger.LogWarning("Firebase service account not configured for ChatManager {ChatManagerId}. Token provider will not be functional.", 
                    this.GetPrimaryKeyLong());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing service account for ChatManager {ChatManagerId}", 
                this.GetPrimaryKeyLong());
            State.RecordFailure($"Service account initialization failed: {ex.Message}");
        }
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        State.IncrementRequests();

        // Check if cached token is still valid
        if (State.IsTokenValid())
        {
            _logger.LogDebug("Using cached access token for ChatManager {ChatManagerId}", this.GetPrimaryKeyLong());
            return State.CachedAccessToken;
        }

        // Create new token
        return await CreateAccessTokenAsync();
    }

    public async Task<bool> IsTokenValidAsync()
    {
        return State.IsTokenValid();
    }

    public async Task ClearTokenCacheAsync()
    {
        _logger.LogInformation("Clearing token cache for ChatManager {ChatManagerId}", this.GetPrimaryKeyLong());
        
        State.ClearToken();

        RaiseEvent(new TokenCacheClearedEventLog 
        { 
            Reason = "Manual cache clear" 
        });
    }

    public async Task<TokenProviderStatus> GetStatusAsync()
    {
        return new TokenProviderStatus
        {
            IsReady = _serviceAccount != null,
            HasCachedToken = !string.IsNullOrEmpty(State.CachedAccessToken),
            TokenExpiry = State.TokenExpiry != DateTime.MinValue ? State.TokenExpiry : null,
            TotalRequests = State.TotalRequests,
            SuccessfulCreations = State.SuccessfulCreations,
            FailedAttempts = State.FailedAttempts,
            LastSuccessTime = State.LastSuccessTime,
            LastError = State.LastError
        };
    }

    private async Task<string?> CreateAccessTokenAsync()
    {
        const int maxRetries = 3;
        const int retryDelayMs = 1000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                if (_serviceAccount == null)
                {
                    var error = "Service account not configured";
                    _logger.LogError("{Error} for ChatManager {ChatManagerId} on attempt {Attempt}/{MaxRetries}", 
                        error, this.GetPrimaryKeyLong(), attempt, maxRetries);
                    
                    State.RecordFailure(error);
                    
                    RaiseEvent(new TokenCreationFailureEventLog 
                    { 
                        ErrorMessage = error,
                        ErrorType = "Configuration",
                        AttemptNumber = attempt 
                    });
                    
                    return null;
                }

                // Create JWT for OAuth 2.0 flow
                var now = DateTimeOffset.UtcNow;
                var expiry = now.AddHours(24); // 24 hours validity

                var claims = new Dictionary<string, object>
                {
                    ["iss"] = _serviceAccount.ClientEmail,
                    ["scope"] = "https://www.googleapis.com/auth/firebase.messaging",
                    ["aud"] = "https://oauth2.googleapis.com/token",
                    ["iat"] = now.ToUnixTimeSeconds(),
                    ["exp"] = expiry.ToUnixTimeSeconds()
                };

                // Create JWT
                var jwt = CreateJwt(claims, _serviceAccount.PrivateKey);
                if (string.IsNullOrEmpty(jwt))
                {
                    var error = "Failed to create JWT";
                    _logger.LogError("{Error} for ChatManager {ChatManagerId} on attempt {Attempt}/{MaxRetries}", 
                        error, this.GetPrimaryKeyLong(), attempt, maxRetries);
                    
                    State.RecordFailure(error);
                    
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(retryDelayMs * attempt);
                        continue;
                    }
                    return null;
                }

                // Exchange JWT for access token
                var tokenRequest = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
                    new KeyValuePair<string, string>("assertion", jwt)
                });

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var response = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", tokenRequest, cts.Token);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent);
                    if (tokenResponse?.AccessToken != null)
                    {
                        var tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60); // 1 min buffer
                        State.UpdateToken(tokenResponse.AccessToken, tokenExpiry);

                        _logger.LogInformation("Successfully created access token for ChatManager {ChatManagerId} on attempt {Attempt}", 
                            this.GetPrimaryKeyLong(), attempt);

                        RaiseEvent(new TokenCreationSuccessEventLog 
                        { 
                            TokenExpiry = tokenExpiry,
                            AttemptNumber = attempt 
                        });

                        return tokenResponse.AccessToken;
                    }
                }

                var errorMsg = $"HTTP {response.StatusCode}: {responseContent}";
                _logger.LogWarning("Failed to obtain access token for ChatManager {ChatManagerId} on attempt {Attempt}/{MaxRetries}: {Error}",
                    this.GetPrimaryKeyLong(), attempt, maxRetries, errorMsg);

                State.RecordFailure(errorMsg);

                RaiseEvent(new TokenCreationFailureEventLog 
                { 
                    ErrorMessage = errorMsg,
                    ErrorType = "HttpError",
                    AttemptNumber = attempt 
                });

                if (attempt < maxRetries)
                {
                    var delay = retryDelayMs * attempt; // Exponential backoff
                    _logger.LogInformation("Retrying access token request in {DelayMs}ms for ChatManager {ChatManagerId}...", 
                        delay, this.GetPrimaryKeyLong());
                    await Task.Delay(delay);
                    continue;
                }

                return null;
            }
            catch (OperationCanceledException)
            {
                var error = "Request timeout";
                _logger.LogWarning("Access token request timed out for ChatManager {ChatManagerId} on attempt {Attempt}/{MaxRetries}", 
                    this.GetPrimaryKeyLong(), attempt, maxRetries);
                
                State.RecordFailure(error);

                RaiseEvent(new TokenCreationFailureEventLog 
                { 
                    ErrorMessage = error,
                    ErrorType = "Timeout",
                    AttemptNumber = attempt 
                });

                if (attempt < maxRetries)
                {
                    var delay = retryDelayMs * attempt;
                    await Task.Delay(delay);
                    continue;
                }
                return null;
            }
            catch (Exception ex)
            {
                var error = $"Unexpected error: {ex.Message}";
                _logger.LogError(ex, "Error obtaining access token for ChatManager {ChatManagerId} on attempt {Attempt}/{MaxRetries}", 
                    this.GetPrimaryKeyLong(), attempt, maxRetries);
                
                State.RecordFailure(error);

                RaiseEvent(new TokenCreationFailureEventLog 
                { 
                    ErrorMessage = error,
                    ErrorType = ex.GetType().Name,
                    AttemptNumber = attempt 
                });

                if (attempt < maxRetries)
                {
                    var delay = retryDelayMs * attempt;
                    await Task.Delay(delay);
                    continue;
                }
                return null;
            }
        }

        return null; // All retries failed
    }

    private string? CreateJwt(Dictionary<string, object> claims, string privateKeyPem)
    {
        RSA? rsa = null;
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

            // Create RSA instance and keep it alive until JWT is fully serialized
            rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);

            // Create JWT payload
            var payload = new JwtPayload();
            foreach (var claim in claims)
            {
                payload[claim.Key] = claim.Value;
            }

            // Create security key and credentials - RSA must remain valid
            var key = new RsaSecurityKey(rsa);
            var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);

            // Create header with credentials
            var header = new JwtHeader(credentials);
            
            // Create token and serialize immediately
            var token = new JwtSecurityToken(header, payload);
            var handler = new JwtSecurityTokenHandler();

            // Serialize JWT - this is where RSA must still be valid
            var jwtString = handler.WriteToken(token);
            
            return jwtString;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating JWT for ChatManager {ChatManagerId}: {ErrorMessage}", 
                this.GetPrimaryKeyLong(), ex.Message);
            return null;
        }
        finally
        {
            // Always dispose RSA in finally block to prevent leaks
            rsa?.Dispose();
        }
    }

    #region Service Account Loading (copied from FirebaseService)

    private ServiceAccountInfo? LoadServiceAccountFromFile(IEnumerable<string>? filePaths)
    {
        if (filePaths == null) return null;

        foreach (var filePath in filePaths)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    var account = JsonSerializer.Deserialize<ServiceAccountInfo>(json);
                    if (account?.ProjectId != null && account.PrivateKey != null && account.ClientEmail != null)
                    {
                        _logger.LogInformation("Service account loaded from file: {FilePath}", filePath);
                        return account;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load service account from file: {FilePath}", filePath);
            }
        }

        return null;
    }

    private ServiceAccountInfo? LoadServiceAccountFromConfiguration(IConfiguration configuration)
    {
        try
        {
            var projectId = configuration["Firebase:ProjectId"];
            var privateKey = configuration["Firebase:PrivateKey"];
            var clientEmail = configuration["Firebase:ClientEmail"];

            if (!string.IsNullOrEmpty(projectId) && !string.IsNullOrEmpty(privateKey) && !string.IsNullOrEmpty(clientEmail))
            {
                _logger.LogInformation("Service account loaded from configuration");
                return new ServiceAccountInfo
                {
                    ProjectId = projectId,
                    PrivateKey = privateKey,
                    ClientEmail = clientEmail
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load service account from configuration");
        }

        return null;
    }

    #endregion
}


