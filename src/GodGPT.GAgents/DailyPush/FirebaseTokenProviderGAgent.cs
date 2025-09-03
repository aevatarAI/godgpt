using Aevatar.Core;
using Aevatar.Core.Abstractions;
using GodGPT.GAgents.DailyPush.Options;
using GodGPT.GAgents.DailyPush.SEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Providers;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Firebase access token provider GAgent implementation
/// Provides local token caching for individual ChatManager, delegates JWT creation to FirebaseService
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

        // Get FirebaseService through ServiceProvider and delegate token creation
        var firebaseService = ServiceProvider.GetService(typeof(FirebaseService)) as FirebaseService;
        if (firebaseService == null)
        {
            var error = "FirebaseService not available through ServiceProvider";
            _logger.LogError("{Error} for ChatManager {ChatManagerId}", error, this.GetPrimaryKeyLong());
            State.RecordFailure(error);
            return null;
        }

        try
        {
            // Delegate to FirebaseService for token creation (with global caching and proper concurrency control)
            var token = await firebaseService.GetAccessTokenAsync();
            
            if (!string.IsNullOrEmpty(token))
            {
                // Cache token locally with reasonable expiry
                var tokenExpiry = DateTime.UtcNow.AddHours(1); // Shorter than FirebaseService to ensure freshness
                State.UpdateToken(token, tokenExpiry);
                
                _logger.LogInformation("Successfully obtained access token from FirebaseService for ChatManager {ChatManagerId}", 
                    this.GetPrimaryKeyLong());
                
                RaiseEvent(new TokenCreationSuccessEventLog 
                { 
                    TokenExpiry = tokenExpiry,
                    AttemptNumber = 1 
                });
                
                return token;
            }
            else
            {
                var error = "FirebaseService returned null token";
                _logger.LogError("{Error} for ChatManager {ChatManagerId}", error, this.GetPrimaryKeyLong());
                State.RecordFailure(error);
                return null;
            }
        }
        catch (Exception ex)
        {
            var error = $"Error getting token from FirebaseService: {ex.Message}";
            _logger.LogError(ex, "{Error} for ChatManager {ChatManagerId}", error, this.GetPrimaryKeyLong());
            State.RecordFailure(error);
            
            RaiseEvent(new TokenCreationFailureEventLog 
            { 
                ErrorMessage = error,
                ErrorType = "FirebaseServiceError",
                AttemptNumber = 1 
            });
            
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
            IsReady = true, // Always ready since we delegate to FirebaseService
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
