using System.Text;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Twitter.Dtos;
using Aevatar.Application.Grains.Twitter.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using GodGPT.GAgents.Twitter;
using Json.Schema.Generation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Aevatar.Application.Grains.Twitter;

public interface ITwitterAuthGAgent : IGAgent
{
    Task<(string CodeVerifier, string CodeChallenge)> GeneratePkcePlainAsync();
    /// <summary>
    /// Generate PKCE code verifier and challenge
    /// </summary>
    Task<(string CodeVerifier, string CodeChallenge)> GeneratePkceAsync();

    /// <summary>
    /// Verify OAuth2 authorization code and bind Twitter account
    /// </summary>
    Task<TwitterAuthResultDto> VerifyAuthCodeAsync(string platform, string code, string redirectUri);

    /// <summary>
    /// Get Twitter binding status
    /// </summary>
    [Orleans.Concurrency.ReadOnly]
    Task<TwitterBindStatusDto> GetBindStatusAsync();

    /// <summary>
    /// Get OAuth2 authorization parameters
    /// </summary>
    Task<TwitterAuthParamsDto> GetAuthParamsAsync();
}

/// <summary>
/// Twitter OAuth2 authentication grain
/// </summary>
[Description("Twitter OAuth2 authentication agent")]
[GAgent]
public class TwitterAuthGAgent : GAgentBase<TwitterAuthState, TwitterAuthLogEvent, EventBase, ConfigurationBase>, ITwitterAuthGAgent
{
    private readonly ILogger<TwitterAuthGAgent> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<TwitterAuthOptions> _authOptions;

    public TwitterAuthGAgent(
        ILogger<TwitterAuthGAgent> logger,
        IHttpClientFactory httpClientFactory, IOptionsMonitor<TwitterAuthOptions> authOptions)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _authOptions = authOptions;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Twitter OAuth2 Authentication GAgent");
    }
    
    /// <summary>
    /// Generate PKCE code verifier and challenge
    /// </summary>
    public async Task<(string CodeVerifier, string CodeChallenge)> GeneratePkcePlainAsync()
    {
        // Generate code verifier
        var codeVerifier = "challenge";
        // Generate code challenge
        var codeChallenge = codeVerifier;
        // Store code verifier in state
        RaiseEvent(new SetCodeVerifierLogEvent
        {
            CodeVerifier = codeVerifier
        });
        await ConfirmEvents();
        return (codeVerifier, codeChallenge);
    }


    /// <summary>
    /// Generate PKCE code verifier and challenge
    /// </summary>
    public async Task<(string CodeVerifier, string CodeChallenge)> GeneratePkceAsync()
    {
        // Generate code verifier
        var codeVerifier = GenerateCodeVerifier();

        // Generate code challenge
        var codeChallenge = GenerateCodeChallenge(codeVerifier);

        // Store code verifier in state
        RaiseEvent(new SetCodeVerifierLogEvent
        {
            CodeVerifier = codeVerifier
        });
        await ConfirmEvents();

        return (codeVerifier, codeChallenge);
    }

    /// <summary>
    /// Verify OAuth2 authorization code and bind Twitter account
    /// </summary>
    public async Task<TwitterAuthResultDto> VerifyAuthCodeAsync(string platform, string code, string redirectUri)
    {
        try
        {
            _logger.LogInformation("Starting OAuth verification with code: {CodeLength} chars, State.CodeVerifier: {VerifierLength} chars",
                code?.Length ?? 0, State.CodeVerifier?.Length ?? 0);
            _logger.LogInformation("Current timestamp: {Timestamp}", DateTime.UtcNow);
            // Exchange authorization code for access token
            var tokenResult = await ExchangeAuthCodeAsync(code, redirectUri);
            if (!tokenResult.Success)
            {
                _logger.LogError("Failed to exchange auth code for token: {Error}", tokenResult.Error);
                return new TwitterAuthResultDto { Success = false, Error = tokenResult.Error };
            }

            // Get Twitter user info
            var userInfo = await GetTwitterUserInfoAsync(tokenResult.AccessToken);
            if (!userInfo.Success)
            {
                _logger.LogError("Failed to get Twitter user info: {Error}", userInfo.Error);
                return new TwitterAuthResultDto { Success = false, Error = userInfo.Error };
            }

            // Update auth state
            RaiseEvent(new TwitterAccountBoundLogEvent
            {
                UserId = this.GetPrimaryKey().ToString(),
                TwitterId = userInfo.TwitterId,
                Username = userInfo.Username,
                AccessToken = tokenResult.AccessToken,
                RefreshToken = tokenResult.Scope,
                TokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenResult.ExpiresIn),
                ProfileImageUrl = userInfo.ProfileImageUrl
            });
            await ConfirmEvents();

            // Bind Twitter identity
            var bindingGrain = GrainFactory.GetGrain<ITwitterIdentityBindingGAgent>(CommonHelper.StringToGuid(userInfo.TwitterId));
            var bindingResult = await bindingGrain.CreateOrUpdateBindingAsync(userInfo.TwitterId, this.GetPrimaryKey(), userInfo.Username, userInfo.ProfileImageUrl);
            if (!bindingResult.Success)
            {
                _logger.LogError("Failed to bind Twitter identity: {Error}", bindingResult.Error);
                return new TwitterAuthResultDto { Success = false, Error = bindingResult.Error };
            }

            return new TwitterAuthResultDto
            {
                Success = true,
                TwitterId = userInfo.TwitterId,
                Username = userInfo.Username,
                BindStatus = State.IsBound,
                ProfileImageUrl = bindingResult.ProfileImageUrl,
                RedirectUri = _authOptions.CurrentValue.PostLoginRedirectUrls.GetValueOrDefault(platform, string.Empty)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying auth code");
            return new TwitterAuthResultDto { Success = false, Error = "Internal server error" };
        }
    }

    /// <summary>
    /// Get Twitter binding status
    /// </summary>
    public Task<TwitterBindStatusDto> GetBindStatusAsync()
    {
        return Task.FromResult(new TwitterBindStatusDto
        {
            IsBound = State.IsBound,
            TwitterId = State.TwitterUserId,
            Username = State.Username,
            ProfileImageUrl = State.ProfileImageUrl
        });
    }

    /// <summary>
    /// Get OAuth2 authorization parameters
    /// </summary>
    public async Task<TwitterAuthParamsDto> GetAuthParamsAsync()
    {
        // Generate PKCE code verifier and challenge
        var (_, codeChallenge) = await GeneratePkcePlainAsync();

        return new TwitterAuthParamsDto
        {
            ClientId = _authOptions.CurrentValue.ClientId,
            GrantType = "authorization_code",
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = "plain", // Using plain method as per current implementation
            ResponseType = "code",
            Scope = string.Join(" ", _authOptions.CurrentValue.Scopes),
            State = this.GetPrimaryKey().ToString() // Generate a random state for CSRF protection
        };
    }

    private async Task<TokenResultDto> ExchangeAuthCodeAsync(string code, string redirectUri)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();

            // Add client authentication
            var auth = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_authOptions.CurrentValue.ClientId}:{_authOptions.CurrentValue.ClientSecret}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);

            // Prepare form data
            var formData = new Dictionary<string, string>
            {
                { "code", code },
                { "grant_type", "authorization_code" },
                { "redirect_uri", redirectUri },
                // Use the same value as code_challenge since method=plain
                { "code_verifier", "challenge" }
            };
            
            // Send token request
            var response = await client.PostAsync(_authOptions.CurrentValue.TokenEndpoint, new FormUrlEncodedContent(formData));
            var responseContent = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Token exchange failed: {Error}", responseContent);
                return new TokenResultDto { Success = false, Error = "Failed to exchange authorization code" };
            }

            // Parse response
            var tokenResponse = JsonConvert.DeserializeObject<TwitterTokenResponseDto>(responseContent);

            return new TokenResultDto
            {
                Success = true,
                AccessToken = tokenResponse.AccessToken,
                TokenType = tokenResponse.TokenType,
                ExpiresIn = tokenResponse.ExpiresIn,
                Scope = tokenResponse.Scope
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exchanging authorization code");
            return new TokenResultDto { Success = false, Error = "Internal server error" };
        }
    }

    private async Task<TwitterUserInfoDto> GetTwitterUserInfoAsync(string accessToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            // Send user info request
            var response = await client.GetAsync(_authOptions.CurrentValue.UserInfoEndpoint);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("User info request failed: {Error}", error);
                return new TwitterUserInfoDto { Success = false, Error = "Failed to get user information" };
            }

            // Parse response
            var content = await response.Content.ReadAsStringAsync();
            var userInfo = JsonConvert.DeserializeObject<TwitterUserInfoResponseDto>(content);

            return new TwitterUserInfoDto
            {
                Success = true,
                TwitterId = userInfo?.Data?.Id,
                Username = userInfo?.Data?.Username,
                Name = userInfo?.Data.Name,
                ProfileImageUrl = userInfo?.Data.ProfileImageUrl
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user information");
            return new TwitterUserInfoDto { Success = false, Error = "Internal server error" };
        }
    }

    protected sealed override void GAgentTransitionState(TwitterAuthState state,
        StateLogEventBase<TwitterAuthLogEvent> @event)
    {
        switch (@event)
        {
            case SetCodeVerifierLogEvent setCodeVerifier:
                State.CodeVerifier = setCodeVerifier.CodeVerifier;
                break;
            case TwitterAccountBoundLogEvent accountBound:
                State.UserId = accountBound.UserId;
                State.TwitterUserId = accountBound.TwitterId;
                State.Username = accountBound.Username;
                State.IsBound = true;
                State.AccessToken = accountBound.AccessToken;
                State.RefreshToken = accountBound.RefreshToken;
                State.TokenExpiresAt = accountBound.TokenExpiresAt;
                State.ProfileImageUrl = accountBound.ProfileImageUrl;
                break;
        }
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        using (var sha256 = SHA256.Create())
        {
            var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
            return Convert.ToBase64String(challengeBytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}