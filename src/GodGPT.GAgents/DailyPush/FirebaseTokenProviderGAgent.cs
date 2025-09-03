using Aevatar.Core;
using Aevatar.Core.Abstractions;
using GodGPT.GAgents.DailyPush.Options;
using GodGPT.GAgents.DailyPush.SEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Providers;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Firebase access token provider GAgent implementation
/// Each ChatManager has its own instance - eliminates concurrency issues with JWT creation
/// </summary>
[StorageProvider(ProviderName = "PubSubStore")]
[LogConsistencyProvider(ProviderName = "LogStorage")]
[GAgent(nameof(FirebaseTokenProviderGAgent))]
public class FirebaseTokenProviderGAgent : GAgentBase<FirebaseTokenProviderGAgentState, DailyPushLogEvent>, 
    IFirebaseTokenProviderGAgent
{
    private readonly ILogger<FirebaseTokenProviderGAgent> _logger;
    private readonly IOptionsMonitor<DailyPushOptions> _options;

    public FirebaseTokenProviderGAgent(
        ILogger<FirebaseTokenProviderGAgent> logger,
        IOptionsMonitor<DailyPushOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Firebase token provider for ChatManager {this.GetPrimaryKeyLong()}");
    }

    protected override async Task OnGAgentActivateAsync(CancellationToken cancellationToken)
    {
        var chatManagerId = this.GetPrimaryKeyLong();
        _logger.LogInformation("FirebaseTokenProviderGAgent activated for ChatManager {ChatManagerId}", chatManagerId);

        // Log activation event
        RaiseEvent(new TokenProviderActivationEventLog 
        { 
            ChatManagerId = chatManagerId 
        });

        State.ActivationTime = DateTime.UtcNow;
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

        // Create token directly in this Agent instance (no shared state, no concurrency issues)
        return await CreateAccessTokenAsync();
    }

    /// <summary>
    /// Create access token directly in this Agent instance - eliminates concurrency issues
    /// Each ChatManager has its own FirebaseTokenProviderGAgent, so no shared state
    /// </summary>
    private async Task<string?> CreateAccessTokenAsync()
    {
        const int maxRetries = 3;
        const int retryDelayMs = 1000;

        // Get dependencies through ServiceProvider
        var httpClient = ServiceProvider.GetService(typeof(HttpClient)) as HttpClient;

        var firebaseKeyPath = _options?.CurrentValue?.FilePaths?.FirebaseKeyPath;
        if (string.IsNullOrEmpty(firebaseKeyPath))
        {
            var configError = "❌ CRITICAL CONFIG ERROR: DailyPushOptions.FilePaths.FirebaseKeyPath not configured - falling back to legacy with RSA issues";
            _logger.LogError("{Error} for ChatManager {ChatManagerId}", configError, this.GetPrimaryKeyLong());
            State.RecordFailure(configError);
            RaiseEvent(new TokenCreationFailureEventLog { ErrorMessage = configError, AttemptNumber = 1 });
            return null;
        }

        if (httpClient == null)
        {
            var httpError = "HttpClient not available through ServiceProvider";
            _logger.LogError("{Error} for ChatManager {ChatManagerId}", httpError, this.GetPrimaryKeyLong());
            State.RecordFailure(httpError);
            RaiseEvent(new TokenCreationFailureEventLog { ErrorMessage = httpError, AttemptNumber = 1 });
            return null;
        }

        // Load service account from Firebase key file
        var serviceAccount = LoadServiceAccountFromFile(firebaseKeyPath);

        if (serviceAccount == null)
        {
            var loadError = $"Failed to load service account from {firebaseKeyPath}";
            _logger.LogError("{Error} for ChatManager {ChatManagerId}", loadError, this.GetPrimaryKeyLong());
            State.RecordFailure(loadError);
            RaiseEvent(new TokenCreationFailureEventLog { ErrorMessage = loadError, AttemptNumber = 1 });
            return null;
        }

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Create JWT for OAuth 2.0 flow
                var now = DateTimeOffset.UtcNow;
                var expiry = now.AddHours(1); // 1 hour expiry per agent instance

                var claims = new Dictionary<string, object>
                {
                    ["iss"] = serviceAccount.ClientEmail,
                    ["scope"] = "https://www.googleapis.com/auth/firebase.messaging",
                    ["aud"] = "https://oauth2.googleapis.com/token",
                    ["iat"] = now.ToUnixTimeSeconds(),
                    ["exp"] = expiry.ToUnixTimeSeconds()
                };

                // Create JWT using same pattern as UserBillingGrain
                var jwt = CreateJwt(claims, serviceAccount.PrivateKey);
                if (string.IsNullOrEmpty(jwt))
                {
                    var error = $"Failed to create JWT on attempt {attempt}";
                    _logger.LogError("{Error} for ChatManager {ChatManagerId}", error, this.GetPrimaryKeyLong());
                    
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(retryDelayMs * attempt);
                        continue;
                    }
                    
                    State.RecordFailure(error);
                    RaiseEvent(new TokenCreationFailureEventLog { ErrorMessage = error, AttemptNumber = attempt });
                    return null;
                }

                // Exchange JWT for access token
                var tokenRequest = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
                    new KeyValuePair<string, string>("assertion", jwt)
                });

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var response = await httpClient.PostAsync("https://oauth2.googleapis.com/token", tokenRequest, cts.Token);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent);
                    if (tokenResponse?.AccessToken != null)
                    {
                        // Cache token locally with 1-hour expiry
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

                var httpError = $"Failed to obtain access token on attempt {attempt}/{maxRetries}: {response.StatusCode} - {responseContent}";
                _logger.LogWarning("{Error} for ChatManager {ChatManagerId}", httpError, this.GetPrimaryKeyLong());

                if (attempt < maxRetries)
                {
                    var delay = retryDelayMs * attempt; // Exponential backoff
                    await Task.Delay(delay);
                    continue;
                }

                State.RecordFailure(httpError);
                RaiseEvent(new TokenCreationFailureEventLog { ErrorMessage = httpError, AttemptNumber = attempt });
                return null;
            }
            catch (Exception ex)
            {
                var exceptionError = $"Exception during token creation attempt {attempt}: {ex.Message}";
                _logger.LogError(ex, "{Error} for ChatManager {ChatManagerId}", exceptionError, this.GetPrimaryKeyLong());
                
                if (attempt < maxRetries)
                {
                    await Task.Delay(retryDelayMs * attempt);
                    continue;
                }
                
                State.RecordFailure(exceptionError);
                RaiseEvent(new TokenCreationFailureEventLog { ErrorMessage = exceptionError, AttemptNumber = attempt });
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Load service account information from Firebase key file
    /// </summary>
    private ServiceAccountInfo? LoadServiceAccountFromFile(string firebaseKeyPath)
    {
        try
        {
            _logger.LogDebug("Loading Firebase key from path: {KeyPath} for ChatManager {ChatManagerId}", firebaseKeyPath, this.GetPrimaryKeyLong());

            if (!File.Exists(firebaseKeyPath))
            {
                _logger.LogWarning("Firebase key file not found: {KeyPath} for ChatManager {ChatManagerId}", firebaseKeyPath, this.GetPrimaryKeyLong());
                return null;
            }

            var jsonContent = File.ReadAllText(firebaseKeyPath);
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                _logger.LogWarning("Firebase key file is empty: {KeyPath} for ChatManager {ChatManagerId}", firebaseKeyPath, this.GetPrimaryKeyLong());
                return null;
            }

            var serviceAccount = JsonSerializer.Deserialize<ServiceAccountInfo>(jsonContent, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });

            if (serviceAccount != null)
            {
                _logger.LogDebug("Successfully loaded Firebase service account for ChatManager {ChatManagerId}", this.GetPrimaryKeyLong());
                return serviceAccount;
            }

            _logger.LogWarning("Failed to deserialize Firebase service account from file: {KeyPath} for ChatManager {ChatManagerId}", firebaseKeyPath, this.GetPrimaryKeyLong());
            return null;
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "JSON parsing error loading Firebase service account from file: {KeyPath} for ChatManager {ChatManagerId}", firebaseKeyPath, this.GetPrimaryKeyLong());
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error loading Firebase service account from file: {KeyPath} for ChatManager {ChatManagerId}", firebaseKeyPath, this.GetPrimaryKeyLong());
            return null;
        }
    }

    /// <summary>
    /// Create JWT using RSA private key - same pattern as UserBillingGrain to avoid ObjectDisposedException
    /// Each Agent instance handles its own JWT creation - no concurrency issues
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

            // Simple RSA lifecycle management - complete all JWT operations within using block
            using (var rsa = RSA.Create())
            {
                rsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);
                
                // Create security key and signing credentials
                var securityKey = new RsaSecurityKey(rsa) { KeyId = Guid.NewGuid().ToString() };
                var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

                // Create token descriptor and sign token within RSA lifetime
                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Claims = claims,
                    SigningCredentials = signingCredentials
                };

                var tokenHandler = new JwtSecurityTokenHandler();
                var token = tokenHandler.CreateJwtSecurityToken(tokenDescriptor);
                
                // Return signed token - all operations completed within RSA scope
                return tokenHandler.WriteToken(token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating JWT for ChatManager {ChatManagerId}: {ErrorMessage}", 
                this.GetPrimaryKeyLong(), ex.Message);
            return null;
        }
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
            IsReady = !string.IsNullOrEmpty(_options?.CurrentValue?.FilePaths?.FirebaseKeyPath), // Ready if firebase key path is configured
            HasCachedToken = !string.IsNullOrEmpty(State.CachedAccessToken),
            TokenExpiry = State.TokenExpiry != DateTime.MinValue ? State.TokenExpiry : null,
            TotalRequests = State.TotalRequests,
            SuccessfulCreations = State.SuccessfulCreations,
            FailedAttempts = State.FailedAttempts,
            LastSuccessTime = State.LastSuccessTime,
            LastError = State.LastError
        };
    }
}
