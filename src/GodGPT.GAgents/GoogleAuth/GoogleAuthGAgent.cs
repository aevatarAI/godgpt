using System.Net.Http.Headers;
using System.Text;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Common.Service;
using Aevatar.Application.Grains.GoogleAuth.Dtos;
using Aevatar.Application.Grains.GoogleAuth.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Json.Schema.Generation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Aevatar.Application.Grains.GoogleAuth;

/// <summary>
/// Google OAuth2 authentication GAgent interface
/// </summary>
public interface IGoogleAuthGAgent : IGAgent
{
    /// <summary>
    /// Verify OAuth2 authorization code and bind Google account
    /// </summary>
    /// <param name="platform">Platform name (web, ios, android, etc.)</param>
    /// <param name="code">Authorization code from Google</param>
    /// <param name="redirectUri">Redirect URI used in authorization</param>
    Task<GoogleAuthResultDto> VerifyAuthCodeAsync(string platform, string code, string redirectUri, string codeVerifier);

    /// <summary>
    /// Get Google binding status
    /// </summary>
    [Orleans.Concurrency.ReadOnly]
    Task<GoogleBindStatusDto> GetBindStatusAsync();

    /// <summary>
    /// Get OAuth2 authorization parameters for specified platform
    /// </summary>
    /// <param name="platform">Platform name (web, ios, android, etc.)</param>
    [Orleans.Concurrency.ReadOnly]
    Task<GoogleAuthParamsDto> GetAuthParamsAsync(string platform);

    /// <summary>
    /// Refresh access token if needed
    /// </summary>
    Task<bool> RefreshTokenIfNeededAsync();

    /// <summary>
    /// Enable Google Calendar sync
    /// </summary>
    Task<bool> EnableCalendarSyncAsync();

    /// <summary>
    /// Disable Google Calendar sync
    /// </summary>
    Task<bool> DisableCalendarSyncAsync();

    /// <summary>
    /// Query Google Calendar events with advanced parameters
    /// </summary>
    [Orleans.Concurrency.ReadOnly]
    Task<GoogleCalendarListDto> QueryCalendarEventsAsync(GoogleCalendarQueryDto query);

    /// <summary>
    /// Handle calendar change notification
    /// </summary>
    Task HandleCalendarChangeNotificationAsync(string resourceId, string channelId, string resourceUri);
    
    /// <summary>
    /// Query Google Tasks with advanced parameters
    /// </summary>
    [Orleans.Concurrency.ReadOnly]
    Task<GoogleTasksListDto> QueryTasksAsync(GoogleTasksQueryDto query);

    /// <summary>
    /// Unbind Google account
    /// </summary>
    Task<bool> UnbindAccountAsync();
}

/// <summary>
/// Google OAuth2 authentication GAgent
/// </summary>
[Description("Google OAuth2 authentication agent")]
[GAgent]
public class GoogleAuthGAgent : GAgentBase<GoogleAuthState, GoogleAuthLogEvent, EventBase, ConfigurationBase>, IGoogleAuthGAgent
{
    private readonly ILogger<GoogleAuthGAgent> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<GoogleAuthOptions> _authOptions;
    private readonly ILocalizationService _localizationService;

    public GoogleAuthGAgent(
        ILogger<GoogleAuthGAgent> logger,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<GoogleAuthOptions> authOptions,
        ILocalizationService localizationService)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _authOptions = authOptions;
        _localizationService = localizationService;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Google OAuth2 Authentication GAgent");
    }

    /// <summary>
    /// Verify OAuth2 authorization code and bind Google account
    /// </summary>
    public async Task<GoogleAuthResultDto> VerifyAuthCodeAsync(string platform, string code, string redirectUri, string codeVerifier)
    {
        try
        {
            _logger.LogInformation("[GoogleAuthGAgent][VerifyAuthCodeAsync] Starting Google OAuth verification for platform: {Platform}, code length: {CodeLength} chars",
                platform, code?.Length ?? 0);

            // Validate platform
            if (!IsValidPlatform(platform))
            {
                _logger.LogError("[GoogleAuthGAgent][VerifyAuthCodeAsync] Invalid platform: {Platform}", platform);
                return new GoogleAuthResultDto { Success = false, Error = "Invalid platform" };
            }

            // Exchange authorization code for access token
            var tokenResult = await ExchangeAuthCodeAsync(platform, code, redirectUri, codeVerifier);
            if (!tokenResult.Success)
            {
                _logger.LogError("[GoogleAuthGAgent][VerifyAuthCodeAsync] Failed to exchange auth code for token on platform {Platform}: {Error}", platform, tokenResult.Error);
                return new GoogleAuthResultDto { Success = false, Error = tokenResult.Error };
            }

            var userInfo = ParseIdToken(tokenResult.IdToken);
            
            // Update auth state
            RaiseEvent(new GoogleAccountBoundLogEvent
            {
                UserId = this.GetPrimaryKey().ToString(),
                GoogleUserId = userInfo.GoogleUserId,
                Email = userInfo.Email,
                DisplayName = userInfo.DisplayName,
                AccessToken = tokenResult.AccessToken,
                RefreshToken = tokenResult.RefreshToken,
                TokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenResult.ExpiresIn),
                Platform = platform.ToLowerInvariant()
            });
            await ConfirmEvents();

            // Bind Google identity
            var bindingGrain = GrainFactory.GetGrain<IGoogleIdentityBindingGAgent>(CommonHelper.StringToGuid(userInfo.GoogleUserId));
            var bindingResult = await bindingGrain.CreateOrUpdateBindingAsync(userInfo.GoogleUserId, this.GetPrimaryKey(), userInfo.Email, userInfo.DisplayName);
            if (!bindingResult.Success)
            {
                _logger.LogError("[GoogleAuthGAgent][VerifyAuthCodeAsync] Failed to bind Google identity: {Error}", bindingResult.Error);
                return new GoogleAuthResultDto { Success = false, Error = bindingResult.Error };
            }

            return new GoogleAuthResultDto
            {
                Success = true,
                GoogleUserId = userInfo.GoogleUserId,
                Email = userInfo.Email,
                DisplayName = userInfo.DisplayName,
                BindStatus = State.IsBound,
                RedirectUri = redirectUri
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleAuthGAgent][VerifyAuthCodeAsync] Error verifying Google auth code for platform: {Platform}", platform);
            return new GoogleAuthResultDto { Success = false, Error = "Internal server error" };
        }
    }

    /// <summary>
    /// Get Google binding status
    /// </summary>
    public async Task<GoogleBindStatusDto> GetBindStatusAsync()
    {
        await RefreshTokenIfNeededAsync();
        
        return new GoogleBindStatusDto
        {
            IsBound = State.IsBound,
            GoogleUserId = State.GoogleUserId,
            Email = State.Email,
            DisplayName = State.DisplayName,
            CalendarSyncEnabled = State.CalendarSyncEnabled
        };
    }

    /// <summary>
    /// Get OAuth2 authorization parameters for specified platform
    /// </summary>
    public Task<GoogleAuthParamsDto> GetAuthParamsAsync(string platform)
    {
        var platformConfig = GetPlatformConfig(platform);
        if (platformConfig == null)
        {
            _logger.LogError("[GoogleAuthGAgent][GetAuthParamsAsync] Platform {Platform} configuration not found", platform);
            return Task.FromResult(new GoogleAuthParamsDto
            {
                ClientId = string.Empty,
                ResponseType = "code",
                Scope = new List<string>(),
                State = this.GetPrimaryKey().ToString(),
                RedirectUri = string.Empty,
                AccessType = "offline",
                Prompt = "consent"
            });
        }

        var scopes = platformConfig.Scopes != null && platformConfig.Scopes.Count > 0
            ? platformConfig.Scopes
            :  _authOptions.CurrentValue.Scopes;

        return Task.FromResult(new GoogleAuthParamsDto
        {
            ClientId = platformConfig.ClientId,
            ResponseType = "code",
            Scope = scopes,
            State = this.GetPrimaryKey().ToString(),
            RedirectUri = platformConfig.RedirectUri,
            AccessType = "offline",
            Prompt = "consent"
        });
    }

    /// <summary>
    /// Refresh access token if needed
    /// </summary>
    public async Task<bool> RefreshTokenIfNeededAsync()
    {
        if (!State.IsBound || string.IsNullOrEmpty(State.RefreshToken))
        {
            _logger.LogWarning("[GoogleAuthGAgent][RefreshTokenIfNeededAsync] Cannot refresh token: account not bound or no refresh token");
            return false;
        }

        var threshold = TimeSpan.FromMinutes(_authOptions.CurrentValue.TokenRefreshThresholdMinutes);
        if (State.TokenExpiresAt.HasValue && DateTime.UtcNow.Add(threshold) < State.TokenExpiresAt.Value)
        {
            _logger.LogDebug("[GoogleAuthGAgent][RefreshTokenIfNeededAsync] Token is still valid, no refresh needed");
            return true;
        }

        try
        {
            _logger.LogInformation("[GoogleAuthGAgent][RefreshTokenIfNeededAsync] Refreshing Google access token");
            var tokenResult = await RefreshAccessTokenAsync(State.RefreshToken);
            if (!tokenResult.Success)
            {
                RaiseEvent(new GoogleTokenRefreshedFailedLogEvent());
                await ConfirmEvents();
                _logger.LogError("[GoogleAuthGAgent][RefreshTokenIfNeededAsync] Failed to refresh access token: {Error}", tokenResult.Error);
                return false;
            }

            RaiseEvent(new GoogleTokenRefreshedLogEvent
            {
                AccessToken = tokenResult.AccessToken,
                TokenExpiresAt = DateTime.UtcNow.AddSeconds(tokenResult.ExpiresIn),
                RefreshToken = tokenResult.RefreshToken
            });
            await ConfirmEvents();

            _logger.LogInformation("[GoogleAuthGAgent][RefreshTokenIfNeededAsync] Successfully refreshed Google access token");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleAuthGAgent][RefreshTokenIfNeededAsync] Error refreshing Google access token");
            return false;
        }
    }

    /// <summary>
    /// Enable Google Calendar sync
    /// </summary>
    public async Task<bool> EnableCalendarSyncAsync()
    {
        if (!State.IsBound)
        {
            _logger.LogWarning("[GoogleAuthGAgent][EnableCalendarSyncAsync] Cannot enable calendar sync: Google account not bound");
            return false;
        }

        try
        {
            // Ensure token is valid
            await RefreshTokenIfNeededAsync();

            // Set up calendar watch
            var watchResult = await SetupCalendarWatchAsync();
            if (!watchResult.Success)
            {
                _logger.LogError("[GoogleAuthGAgent][EnableCalendarSyncAsync] Failed to setup calendar watch: {Error}", watchResult.Error);
                return false;
            }

            RaiseEvent(new GoogleCalendarSyncEnabledLogEvent
            {
                WatchResourceId = watchResult.ResourceId,
                WatchExpiresAt = watchResult.Expiration,
                LastSyncAt = DateTime.UtcNow
            });
            await ConfirmEvents();

            _logger.LogInformation("[GoogleAuthGAgent][EnableCalendarSyncAsync] Successfully enabled Google Calendar sync");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleAuthGAgent][EnableCalendarSyncAsync] Error enabling Google Calendar sync");
            return false;
        }
    }

    /// <summary>
    /// Disable Google Calendar sync
    /// </summary>
    public async Task<bool> DisableCalendarSyncAsync()
    {
        try
        {
            if (!string.IsNullOrEmpty(State.CalendarWatchResourceId))
            {
                await StopCalendarWatchAsync(State.CalendarWatchResourceId);
            }

            RaiseEvent(new GoogleCalendarSyncDisabledLogEvent
            {
                DisabledAt = DateTime.UtcNow
            });
            await ConfirmEvents();

            _logger.LogInformation("[GoogleAuthGAgent][DisableCalendarSyncAsync] Successfully disabled Google Calendar sync");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleAuthGAgent][DisableCalendarSyncAsync] Error disabling Google Calendar sync");
            return false;
        }
    }

    /// <summary>
    /// Query Google Calendar events with advanced parameters
    /// </summary>
    public async Task<GoogleCalendarListDto> QueryCalendarEventsAsync(GoogleCalendarQueryDto query)
    {
        if (!State.IsBound)
        {
            return new GoogleCalendarListDto
            {
                Success = false,
                Error = "Google account not bound"
            };
        }

        try
        {
            // Ensure token is valid
            await RefreshTokenIfNeededAsync();

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", State.AccessToken);

            // If CalendarId is empty, query all calendars
            if (string.IsNullOrEmpty(query.CalendarId))
            {
                return await QueryAllCalendarsEventsAsync(client, query);
            }
            else
            {
                // Query specific calendar
                return await QuerySingleCalendarEventsAsync(client, query, query.CalendarId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleAuthGAgent][QueryCalendarEventsAsync] Error querying Google Calendar events");
            return new GoogleCalendarListDto { Success = false, Error = "Internal server error" };
        }
    }

    /// <summary>
    /// Query Google Tasks with advanced parameters
    /// </summary>
    public async Task<GoogleTasksListDto> QueryTasksAsync(GoogleTasksQueryDto query)
    {
        if (!State.IsBound)
        {
            return new GoogleTasksListDto
            {
                Success = false,
                Error = "Google account not bound"
            };
        }

        try
        {
            // Ensure token is valid
            await RefreshTokenIfNeededAsync();

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", State.AccessToken);

            var allTasks = new List<GoogleTaskDto>();
            var totalTaskLists = 0;

            if (!string.IsNullOrEmpty(query.TaskListId))
            {
                // Query specific task list
                var tasks = await QueryTaskListAsync(client, query.TaskListId, query);
                allTasks.AddRange(tasks);
                totalTaskLists = 1;
            }
            else
            {
                // Query all task lists
                var taskListsUrl = $"{_authOptions.CurrentValue.TasksApiEndpoint}/users/@me/lists?maxResults={_authOptions.CurrentValue.MaxTaskListIdResultsLimit}";
                var taskListsResponse = await client.GetAsync(taskListsUrl);
                
                if (!taskListsResponse.IsSuccessStatusCode)
                {
                    var error = await taskListsResponse.Content.ReadAsStringAsync();
                    _logger.LogError("[GoogleAuthGAgent][QueryTasksAsync] Failed to get task lists: {Error}", error);
                    return new GoogleTasksListDto { Success = false, Error = "Failed to get task lists" };
                }

                var taskListsContent = await taskListsResponse.Content.ReadAsStringAsync();
                var taskListsData = JsonConvert.DeserializeObject<GoogleTaskListsResponseDto>(taskListsContent);

                if (taskListsData?.Items != null)
                {
                    totalTaskLists = taskListsData.Items.Count;
                    
                    foreach (var taskList in taskListsData.Items)
                    {
                        var tasks = await QueryTaskListAsync(client, taskList.Id, query, taskList.Title);
                        allTasks.AddRange(tasks);
                    }
                }
            }

            _logger.LogInformation("[GoogleAuthGAgent][QueryTasksAsync] Successfully queried {Count} tasks from {ListCount} task lists", 
                allTasks.Count, totalTaskLists);

            return new GoogleTasksListDto
            {
                Success = true,
                Tasks = allTasks,
                TotalTaskLists = totalTaskLists
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleAuthGAgent][QueryTasksAsync] Error querying Google Tasks");
            return new GoogleTasksListDto { Success = false, Error = "Internal server error" };
        }
    }

    /// <summary>
    /// Handle calendar change notification
    /// </summary>
    public async Task HandleCalendarChangeNotificationAsync(string resourceId, string channelId, string resourceUri)
    {
        try
        {
            _logger.LogInformation("[GoogleAuthGAgent][HandleCalendarChangeNotificationAsync] Received calendar change notification: ResourceId={ResourceId}, ChannelId={ChannelId}",
                resourceId, channelId);

            // Verify this is our watch
            if (State.CalendarWatchResourceId != resourceId)
            {
                _logger.LogWarning("[GoogleAuthGAgent][HandleCalendarChangeNotificationAsync] Received notification for unknown resource: {ResourceId}", resourceId);
                return;
            }

            // Trigger calendar sync
            await RefreshTokenIfNeededAsync();
            
            // Update last sync time
            RaiseEvent(new GoogleCalendarSyncEnabledLogEvent
            {
                WatchResourceId = State.CalendarWatchResourceId,
                WatchExpiresAt = State.CalendarWatchExpiresAt ?? DateTime.UtcNow.AddHours(24),
                LastSyncAt = DateTime.UtcNow
            });
            await ConfirmEvents();

            _logger.LogInformation("[GoogleAuthGAgent][HandleCalendarChangeNotificationAsync] Successfully processed calendar change notification");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleAuthGAgent][HandleCalendarChangeNotificationAsync] Error handling calendar change notification");
        }
    }

    /// <summary>
    /// Unbind Google account
    /// </summary>
    public async Task<bool> UnbindAccountAsync()
    {
        try
        {
            _logger.LogInformation("[GoogleAuthGAgent][UnbindAccountAsync] Starting Google account unbind process for user: {UserId}", this.GetPrimaryKey().ToString());

            // If calendar sync is enabled, disable it first
            if (State.CalendarSyncEnabled)
            {
                await DisableCalendarSyncAsync();
            }

            // Raise event to clear all authentication data
            RaiseEvent(new GoogleTokenRefreshedFailedLogEvent());
            await ConfirmEvents();

            _logger.LogInformation("[GoogleAuthGAgent][UnbindAccountAsync] Successfully unbound Google account for user: {UserId}", this.GetPrimaryKey().ToString());
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleAuthGAgent][UnbindAccountAsync] Error unbinding Google account for user: {UserId}", this.GetPrimaryKey().ToString());
            return false;
        }
    }

    private async Task<TokenResultDto> ExchangeAuthCodeAsync(string platform, string code, string redirectUri, string codeVerifier)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();

            // Get platform-specific configuration
            var platformConfig = GetPlatformConfig(platform);
            if (platformConfig == null)
            {
                _logger.LogError("[GoogleAuthGAgent][ExchangeAuthCodeAsync] Platform {Platform} configuration not found in PlatformConfigs", platform);
                return new TokenResultDto { Success = false, Error = $"Platform {platform} not configured" };
            }

            var clientId = platformConfig.ClientId;
            var clientSecret = platformConfig.ClientSecret;
            var requiresClientSecret = platformConfig.RequiresClientSecret;

            _logger.LogInformation("[GoogleAuthGAgent][ExchangeAuthCodeAsync] Exchanging auth code for platform: {Platform}, ClientId: {ClientId}, RequiresClientSecret: {RequiresClientSecret}", 
                platform, clientId?.Substring(0, Math.Min(20, clientId?.Length ?? 0)) + "...", requiresClientSecret);

            if (string.IsNullOrEmpty(clientId))
            {
                _logger.LogError("[GoogleAuthGAgent][ExchangeAuthCodeAsync] ClientId is not configured for platform: {Platform}", platform);
                return new TokenResultDto { Success = false, Error = $"ClientId not configured for platform {platform}" };
            }

            var formData = new Dictionary<string, string>
            {
                { "code", code },
                { "client_id", clientId },
                { "redirect_uri", redirectUri },
                { "grant_type", "authorization_code" }
            };

            // Only add client_secret if platform requires it (iOS doesn't need it)
            if (requiresClientSecret && !string.IsNullOrEmpty(clientSecret))
            {
                formData.Add("client_secret", clientSecret);
                _logger.LogDebug("[GoogleAuthGAgent][ExchangeAuthCodeAsync] Added client_secret for platform: {Platform}", platform);
            }
            else if (requiresClientSecret)
            {
                _logger.LogWarning("[GoogleAuthGAgent][ExchangeAuthCodeAsync] Platform {Platform} requires client_secret but it is not configured", platform);
            }
            else
            {
                _logger.LogInformation("[GoogleAuthGAgent][ExchangeAuthCodeAsync] Platform {Platform} does not require client_secret (e.g., iOS)", platform);
            }

            if (!codeVerifier.IsNullOrWhiteSpace())
            {
                
                formData.Add("code_verifier", codeVerifier);
                _logger.LogDebug("[GoogleAuthGAgent][ExchangeAuthCodeAsync] Added code_verifier for platform: {Platform}", platform);
            }

            var response = await client.PostAsync(_authOptions.CurrentValue.TokenEndpoint, new FormUrlEncodedContent(formData));
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[GoogleAuthGAgent][ExchangeAuthCodeAsync] Token exchange failed for platform {Platform}: Status={StatusCode}, Error={Error}", 
                    platform, response.StatusCode, responseContent);
                return new TokenResultDto { Success = false, Error = "Failed to exchange authorization code" };
            }

            var tokenResponse = JsonConvert.DeserializeObject<GoogleTokenResponseDto>(responseContent);
            if (tokenResponse == null)
            {
                _logger.LogError("[GoogleAuthGAgent][ExchangeAuthCodeAsync] Failed to deserialize token response for platform: {Platform}", platform);
                return new TokenResultDto { Success = false, Error = "Invalid token response" };
            }

            _logger.LogInformation("[GoogleAuthGAgent][ExchangeAuthCodeAsync] Successfully exchanged auth code for platform: {Platform}", platform);

            return new TokenResultDto
            {
                Success = true,
                AccessToken = tokenResponse.AccessToken,
                TokenType = tokenResponse.TokenType,
                ExpiresIn = tokenResponse.ExpiresIn,
                RefreshToken = tokenResponse.RefreshToken,
                IdToken = tokenResponse.IdToken
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleAuthGAgent][ExchangeAuthCodeAsync] Error exchanging authorization code for platform: {Platform}", platform);
            return new TokenResultDto { Success = false, Error = "Internal server error" };
        }
    }

    private async Task<TokenResultDto> RefreshAccessTokenAsync(string refreshToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();

            // Get platform configuration from state
            var platform = State.Platform ?? "web"; // Default to web if platform not stored
            var platformConfig = GetPlatformConfig(platform);
            
            if (platformConfig == null)
            {
                _logger.LogError("[GoogleAuthGAgent][RefreshAccessTokenAsync] Platform {Platform} configuration not found for token refresh", platform);
                return new TokenResultDto { Success = false, Error = "Platform configuration not found" };
            }

            var clientId = platformConfig.ClientId;
            var clientSecret = platformConfig.ClientSecret;
            var requiresClientSecret = platformConfig.RequiresClientSecret;

            _logger.LogInformation("[GoogleAuthGAgent][RefreshAccessTokenAsync] Refreshing token for platform: {Platform}, RequiresClientSecret: {RequiresClientSecret}", 
                platform, requiresClientSecret);

            var formData = new Dictionary<string, string>
            {
                { "refresh_token", refreshToken },
                { "client_id", clientId },
                { "grant_type", "refresh_token" }
            };

            // Only add client_secret if platform requires it
            if (requiresClientSecret && !string.IsNullOrEmpty(clientSecret))
            {
                formData.Add("client_secret", clientSecret);
            }

            var response = await client.PostAsync(_authOptions.CurrentValue.TokenEndpoint, new FormUrlEncodedContent(formData));
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[GoogleAuthGAgent][RefreshAccessTokenAsync] Token refresh failed for platform {Platform}: Status={StatusCode}, Error={Error}", 
                    platform, response.StatusCode, responseContent);
                return new TokenResultDto { Success = false, Error = "Failed to refresh access token" };
            }

            var tokenResponse = JsonConvert.DeserializeObject<GoogleTokenResponseDto>(responseContent);
            if (tokenResponse == null)
            {
                _logger.LogError("[GoogleAuthGAgent][RefreshAccessTokenAsync] Failed to deserialize token response for platform: {Platform}", platform);
                return new TokenResultDto { Success = false, Error = "Invalid token response" };
            }

            _logger.LogInformation("[GoogleAuthGAgent][RefreshAccessTokenAsync] Successfully refreshed token for platform: {Platform}", platform);

            return new TokenResultDto
            {
                Success = true,
                AccessToken = tokenResponse.AccessToken,
                TokenType = tokenResponse.TokenType,
                ExpiresIn = tokenResponse.ExpiresIn,
                RefreshToken = tokenResponse.RefreshToken
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleAuthGAgent][RefreshAccessTokenAsync] Error refreshing access token");
            return new TokenResultDto { Success = false, Error = "Internal server error" };
        }
    }

    private async Task<GoogleCalendarWatchDto> SetupCalendarWatchAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", State.AccessToken);

            var watchRequest = new
            {
                id = Guid.NewGuid().ToString(),
                type = "web_hook",
                address = $"{_authOptions.CurrentValue.WebhookBaseUrl}/api/google/calendar/notifications"
            };

            var json = JsonConvert.SerializeObject(watchRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{_authOptions.CurrentValue.CalendarApiEndpoint}/calendars/primary/events/watch", content);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[GoogleAuthGAgent][SetupCalendarWatchAsync] Calendar watch setup failed: {Error}", responseContent);
                return new GoogleCalendarWatchDto { Success = false, Error = "Failed to setup calendar watch" };
            }

            var watchResponse = JsonConvert.DeserializeObject<GoogleCalendarWatchResponseDto>(responseContent);
            if (watchResponse == null)
            {
                _logger.LogError("[GoogleAuthGAgent][SetupCalendarWatchAsync] Failed to deserialize watch response");
                return new GoogleCalendarWatchDto { Success = false, Error = "Invalid watch response" };
            }

            return new GoogleCalendarWatchDto
            {
                Success = true,
                Id = watchResponse.Id,
                Type = watchResponse.Type,
                Address = watchResponse.Address,
                Token = watchResponse.Token,
                Expiration = DateTimeOffset.FromUnixTimeMilliseconds(watchResponse.Expiration).DateTime,
                ResourceId = watchResponse.ResourceId,
                ResourceUri = watchResponse.ResourceUri
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleAuthGAgent][SetupCalendarWatchAsync] Error setting up calendar watch");
            return new GoogleCalendarWatchDto { Success = false, Error = "Internal server error" };
        }
    }

    private async Task StopCalendarWatchAsync(string resourceId)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", State.AccessToken);

            var stopRequest = new
            {
                id = resourceId,
                resourceId = resourceId
            };

            var json = JsonConvert.SerializeObject(stopRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            await client.PostAsync($"{_authOptions.CurrentValue.CalendarApiEndpoint}/channels/stop", content);
            _logger.LogInformation("[GoogleAuthGAgent][StopCalendarWatchAsync] Successfully stopped calendar watch: {ResourceId}", resourceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleAuthGAgent][StopCalendarWatchAsync] Error stopping calendar watch: {ResourceId}", resourceId);
        }
    }

    protected sealed override void GAgentTransitionState(GoogleAuthState state, StateLogEventBase<GoogleAuthLogEvent> @event)
    {
        switch (@event)
        {
            case GoogleAccountBoundLogEvent accountBound:
                state.UserId = accountBound.UserId;
                state.GoogleUserId = accountBound.GoogleUserId;
                state.Email = accountBound.Email;
                state.DisplayName = accountBound.DisplayName;
                state.IsBound = true;
                state.AccessToken = accountBound.AccessToken;
                state.RefreshToken = accountBound.RefreshToken;
                state.TokenExpiresAt = accountBound.TokenExpiresAt;
                state.Platform = accountBound.Platform;
                break;
            case GoogleTokenRefreshedLogEvent tokenRefreshed:
                state.AccessToken = tokenRefreshed.AccessToken;
                state.TokenExpiresAt = tokenRefreshed.TokenExpiresAt;
                if (!tokenRefreshed.RefreshToken.IsNullOrWhiteSpace())
                {
                    state.RefreshToken = tokenRefreshed.RefreshToken;
                }
                break;
            case GoogleTokenRefreshedFailedLogEvent tokenRefreshedFailed:
                state.IsBound = false;
                state.AccessToken = string.Empty;
                state.TokenExpiresAt = null;
                state.RefreshToken = string.Empty;
                break;
            case GoogleCalendarSyncEnabledLogEvent syncEnabled:
                state.CalendarSyncEnabled = true;
                state.CalendarWatchResourceId = syncEnabled.WatchResourceId;
                state.CalendarWatchExpiresAt = syncEnabled.WatchExpiresAt;
                state.LastCalendarSyncAt = syncEnabled.LastSyncAt;
                break;
            case GoogleCalendarSyncDisabledLogEvent syncDisabled:
                state.CalendarSyncEnabled = false;
                state.CalendarWatchResourceId = string.Empty;
                state.CalendarWatchExpiresAt = null;
                break;
        }
    }

    /// <summary>
    /// Get platform-specific configuration
    /// </summary>
    private GooglePlatformConfig? GetPlatformConfig(string platform)
    {
        if (string.IsNullOrEmpty(platform))
        {
            return null;
        }

        var platformLower = platform.ToLowerInvariant();
        return _authOptions.CurrentValue.PlatformConfigs?.GetValueOrDefault(platformLower);
    }

    /// <summary>
    /// Validate if platform is supported
    /// </summary>
    private bool IsValidPlatform(string platform)
    {
        if (string.IsNullOrEmpty(platform))
        {
            _logger.LogWarning("[GoogleAuthGAgent][IsValidPlatform] Platform is null or empty");
            return false;
        }

        var platformLower = platform.ToLowerInvariant();
        
        // Check if platform has specific config in PlatformConfigs
        if (_authOptions.CurrentValue.PlatformConfigs?.ContainsKey(platformLower) == true)
        {
            return true;
        }

        _logger.LogWarning("[GoogleAuthGAgent][IsValidPlatform] Platform {Platform} not found in PlatformConfigs", platform);
        return false;
    }

    /// <summary>
    /// Build query parameters string for Google Calendar API
    /// </summary>
    private string BuildCalendarQueryParameters(GoogleCalendarQueryDto query)
    {
        var parameters = new List<string>();

        if (query.StartTime.IsNullOrWhiteSpace())
        {
            var startTime = DateTime.UtcNow;
            parameters.Add($"timeMin={startTime:yyyy-MM-ddTHH:mm:ssZ}");
            
        }
        else
        {
            parameters.Add($"timeMin={Uri.EscapeDataString(query.StartTime)}");
        }

        if (query.EndTime.IsNullOrWhiteSpace())
        {
            var endTime = DateTime.UtcNow.AddDays(_authOptions.CurrentValue.DefaultCalendarQueryRangeDays);
            parameters.Add($"timeMax={endTime:yyyy-MM-ddTHH:mm:ssZ}");
        }
        else
        {
            parameters.Add($"timeMax={Uri.EscapeDataString(query.EndTime)}");
        }

        // Max results with validation
        var effectiveMaxResults = Math.Min(Math.Max(1, query.MaxResults), _authOptions.CurrentValue.MaxCalendarResultsLimit);
        parameters.Add($"maxResults={effectiveMaxResults}");

        // Single events
        parameters.Add($"singleEvents={query.SingleEvents.ToString().ToLower()}");

        // Order by
        if (!string.IsNullOrEmpty(query.OrderBy))
        {
            parameters.Add($"orderBy={query.OrderBy}");
        }

        // Time zone
        if (!string.IsNullOrEmpty(query.TimeZone) && query.TimeZone != "UTC")
        {
            parameters.Add($"timeZone={Uri.EscapeDataString(query.TimeZone)}");
        }

        // Show deleted
        if (query.ShowDeleted)
        {
            parameters.Add("showDeleted=true");
        }

        // Page token for pagination
        if (!string.IsNullOrEmpty(query.PageToken))
        {
            parameters.Add($"pageToken={Uri.EscapeDataString(query.PageToken)}");
        }

        // Event types - only add if specifically requested
        // According to Google docs, omitting eventTypes returns all event types by default
        if (query.EventTypes != null && query.EventTypes.Count > 0)
        {
            foreach (var eventType in query.EventTypes)
            {
                parameters.Add($"eventTypes={Uri.EscapeDataString(eventType)}");
            }
        }
        // If EventTypes is null or empty, don't add eventTypes parameter at all
        // This allows Google API to return all event types by default

        var result = string.Join("&", parameters);
        _logger.LogDebug("[GoogleAuthGAgent][BuildCalendarQueryParameters] Built calendar query parameters: {Parameters}", result);
        
        return result;
    }

    /// <summary>
    /// Build Tasks API query URL
    /// </summary>
    private string BuildTasksQueryUrl(string taskListId, int maxResults, bool showCompleted, DateTime? startTime = null, DateTime? endTime = null)
    {
        var parameters = new List<string>
        {
            $"maxResults={maxResults}",
            $"showCompleted={showCompleted.ToString().ToLower()}",
            "showDeleted=false",
            "showHidden=false"
        };

        // Add time filtering if specified
        if (startTime.HasValue)
        {
            parameters.Add($"dueMin={startTime.Value:yyyy-MM-ddTHH:mm:ssZ}");
        }
        
        if (endTime.HasValue)
        {
            parameters.Add($"dueMax={endTime.Value:yyyy-MM-ddTHH:mm:ssZ}");
        }

        var queryString = string.Join("&", parameters);
        return $"{_authOptions.CurrentValue.TasksApiEndpoint}/lists/{taskListId}/tasks?{queryString}";
    }

    /// <summary>
    /// Query tasks from a specific task list
    /// </summary>
    private async Task<List<GoogleTaskDto>> QueryTaskListAsync(HttpClient client, string taskListId, GoogleTasksQueryDto query, string taskListTitle = "")
    {
        var tasks = new List<GoogleTaskDto>();
        
        var parameters = new List<string>();
        
        // Max results with validation
        var effectiveMaxResults = Math.Min(Math.Max(1, query.MaxResults), _authOptions.CurrentValue.MaxTasksResultsLimit);
        parameters.Add($"maxResults={effectiveMaxResults}");

        // Show options
        parameters.Add($"showCompleted={query.ShowCompleted.ToString().ToLower()}");
        parameters.Add($"showDeleted={query.ShowDeleted.ToString().ToLower()}");
        parameters.Add($"showHidden={query.ShowHidden.ToString().ToLower()}");

        // Time filtering
        if (query.StartTime.HasValue)
        {
            parameters.Add($"dueMin={query.StartTime.Value:yyyy-MM-ddTHH:mm:ssZ}");
        }
        
        if (query.EndTime.HasValue)
        {
            parameters.Add($"dueMax={query.EndTime.Value:yyyy-MM-ddTHH:mm:ssZ}");
        }

        if (query.UpdatedMin.HasValue)
        {
            parameters.Add($"updatedMin={query.UpdatedMin.Value:yyyy-MM-ddTHH:mm:ssZ}");
        }

        // Page token for pagination
        if (!string.IsNullOrEmpty(query.PageToken))
        {
            parameters.Add($"pageToken={Uri.EscapeDataString(query.PageToken)}");
        }

        var queryString = string.Join("&", parameters);
        var tasksUrl = $"{_authOptions.CurrentValue.TasksApiEndpoint}/lists/{taskListId}/tasks?{queryString}";

        var tasksResponse = await client.GetAsync(tasksUrl);
        if (tasksResponse.IsSuccessStatusCode)
        {
            var tasksContent = await tasksResponse.Content.ReadAsStringAsync();
            var tasksData = JsonConvert.DeserializeObject<GoogleTasksResponseDto>(tasksContent);

            if (tasksData?.Items != null)
            {
                foreach (var task in tasksData.Items)
                {
                    var processedTask = ProcessTaskItem(task, taskListId, taskListTitle);
                    
                    // Apply additional time filtering if specified
                    if (IsTaskInTimeRange(processedTask, query.StartTime, query.EndTime))
                    {
                        tasks.Add(processedTask);
                    }
                }
            }
        }

        return tasks;
    }

    /// <summary>
    /// Process task item from API response
    /// </summary>
    private GoogleTaskDto ProcessTaskItem(GoogleTaskItemDto task, string taskListId, string taskListTitle)
    {
        return new GoogleTaskDto
        {
            Id = task.Id,
            Title = task.Title,
            Notes = task.Notes ?? string.Empty,
            Status = task.Status,
            Due = ParseGoogleDateTime(task.Due),
            Completed = ParseGoogleDateTime(task.Completed),
            Updated = ParseGoogleDateTime(task.Updated),
            Parent = task.Parent ?? string.Empty,
            Position = task.Position ?? string.Empty,
            TaskListId = taskListId,
            TaskListTitle = taskListTitle,
            IsDeleted = task.Deleted ?? false,
            IsHidden = task.Hidden ?? false
        };
    }

    /// <summary>
    /// Check if task is within specified time range
    /// </summary>
    private bool IsTaskInTimeRange(GoogleTaskDto task, DateTime? startTime, DateTime? endTime)
    {
        if (!startTime.HasValue && !endTime.HasValue)
        {
            return true;
        }

        var taskTime = task.Due ?? task.Updated ?? task.Completed;
        if (!taskTime.HasValue)
        {
            return true; // Include tasks without dates
        }

        if (startTime.HasValue && taskTime < startTime.Value)
        {
            return false;
        }

        if (endTime.HasValue && taskTime > endTime.Value)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Parse Google API date time string to DateTime
    /// </summary>
    private DateTime? ParseGoogleDateTime(string? dateTimeString)
    {
        if (string.IsNullOrEmpty(dateTimeString))
        {
            return null;
        }

        try
        {
            // Google API returns RFC 3339 format
            return DateTime.Parse(dateTimeString, null, System.Globalization.DateTimeStyles.RoundtripKind);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GoogleAuthGAgent][ParseGoogleDateTime] Failed to parse Google date time: {DateTime}", dateTimeString);
            return null;
        }
    }

    private GoogleUserInfoDto ParseIdToken(string idToken)
    {
        try
        {
            // JWT format: header.payload.signature
            var parts = idToken.Split('.');
            if (parts.Length != 3)
            {
                throw new ArgumentException("Invalid JWT format");
            }
        
            // Decode payload part (second part)
            var payloadBase64 = parts[1];
        
            // Add necessary padding
            var padding = 4 - (payloadBase64.Length % 4);
            if (padding < 4)
            {
                payloadBase64 += new string('=', padding);
            }
        
            // Replace URL-safe characters
            payloadBase64 = payloadBase64.Replace('-', '+').Replace('_', '/');
        
            // Base64 decode
            var payloadBytes = Convert.FromBase64String(payloadBase64);
            var payloadJson = Encoding.UTF8.GetString(payloadBytes);
        
            // Deserialize JSON
            var payload = JsonConvert.DeserializeObject<GoogleIdTokenPayload>(payloadJson);
            if (payload == null)
            {
                throw new ArgumentException("Failed to deserialize ID token payload");
            }
        
            return new GoogleUserInfoDto
            {
                Success = true,
                GoogleUserId = payload.Sub,        // Google User ID
                Email = payload.Email,
                DisplayName = payload.Name
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleAuthGAgent][ParseIdToken] Error parsing Google ID token");
            return new GoogleUserInfoDto { Success = false, Error = "Failed to parse ID token" };
        }
    }

    /// <summary>
    /// Query events from all user calendars
    /// </summary>
    private async Task<GoogleCalendarListDto> QueryAllCalendarsEventsAsync(HttpClient client, GoogleCalendarQueryDto query)
    {
        try
        {
            // First, get the list of all calendars
            var calendarListUrl = $"{_authOptions.CurrentValue.CalendarApiEndpoint}/users/me/calendarList?maxResults={_authOptions.CurrentValue.MaxCalendarListResultLimit}";
            
            _logger.LogInformation("[GoogleAuthGAgent][QueryAllCalendarsEventsAsync] Fetching calendar list: {Url}", calendarListUrl);

            var calendarListResponse = await client.GetAsync(calendarListUrl);
            if (!calendarListResponse.IsSuccessStatusCode)
            {
                var error = await calendarListResponse.Content.ReadAsStringAsync();
                _logger.LogError("[GoogleAuthGAgent][QueryAllCalendarsEventsAsync] Failed to get calendar list: {Error}", error);
                return new GoogleCalendarListDto { Success = false, Error = "Failed to get calendar list" };
            }

            var calendarListContent = await calendarListResponse.Content.ReadAsStringAsync();
            var calendarListData = JsonConvert.DeserializeObject<GoogleCalendarListResponseDto>(calendarListContent);

            if (calendarListData?.Items == null || calendarListData.Items.Count == 0)
            {
                _logger.LogWarning("[GoogleAuthGAgent][QueryAllCalendarsEventsAsync] No calendars found");
                return new GoogleCalendarListDto
                {
                    Success = true,
                    Events = new List<GoogleCalendarEventDto>(),
                    TotalCalendarsQueried = 0
                };
            }

            // Filter calendars to query (only selected and not hidden calendars)
            var calendarsToQuery = calendarListData.Items
                .Where(cal => cal.Selected && !cal.Hidden && 
                             (cal.AccessRole == "owner" || cal.AccessRole == "reader" || cal.AccessRole == "writer"))
                .ToList();

            _logger.LogInformation("[GoogleAuthGAgent][QueryAllCalendarsEventsAsync] Found {TotalCalendars} calendars, will query {QueryCalendars} calendars", 
                calendarListData.Items.Count, calendarsToQuery.Count);

            var allEvents = new List<GoogleCalendarEventDto>();
            var queriedCalendarIds = new List<string>();
            var failedCalendarIds = new Dictionary<string, string>();

            // Query each calendar in parallel for better performance
            var queryTasks = calendarsToQuery.Select(async calendar =>
            {
                try
                {
                    var calendarEvents = await QuerySingleCalendarEventsInternalAsync(client, query, calendar.Id, calendar.Summary);
                    return new { Calendar = calendar, Events = calendarEvents, Success = true, Error = (string?)null };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[GoogleAuthGAgent][QueryAllCalendarsEventsAsync] Error querying calendar {CalendarId}: {Error}", 
                        calendar.Id, ex.Message);
                    return new { Calendar = calendar, Events = new List<GoogleCalendarEventDto>(), Success = false, Error = (string?)ex.Message };
                }
            });

            var queryResults = await Task.WhenAll(queryTasks);

            foreach (var result in queryResults)
            {
                if (result.Success)
                {
                    allEvents.AddRange(result.Events);
                    queriedCalendarIds.Add(result.Calendar.Id);
                }
                else
                {
                    failedCalendarIds[result.Calendar.Id] = result.Error ?? "Unknown error";
                }
            }

            _logger.LogDebug("[GoogleAuthGAgent][QueryAllCalendarsEventsAsync] {UserId} Google response: {EventCount}", 
                this.GetPrimaryKey(), JsonConvert.SerializeObject(allEvents));
            
            // Sort all events by start time
            allEvents = allEvents
                .OrderBy(e => e.StartTime?.DateTime ?? DateTime.MinValue)
                .ToList();

            _logger.LogInformation("[GoogleAuthGAgent][QueryAllCalendarsEventsAsync] Successfully retrieved {EventCount} events from {SuccessCount} calendars, {FailedCount} calendars failed", 
                allEvents.Count, queriedCalendarIds.Count, failedCalendarIds.Count);
            _logger.LogDebug("[GoogleAuthGAgent][QueryAllCalendarsEventsAsync] {UserId} Order By : {EventCount}", 
                this.GetPrimaryKey(), JsonConvert.SerializeObject(allEvents));

            return new GoogleCalendarListDto
            {
                Success = true,
                Events = allEvents,
                TotalCalendarsQueried = calendarsToQuery.Count,
                QueriedCalendarIds = queriedCalendarIds,
                FailedCalendarIds = failedCalendarIds
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleAuthGAgent][QueryAllCalendarsEventsAsync] Error querying all calendars");
            return new GoogleCalendarListDto { Success = false, Error = "Internal server error" };
        }
    }

    /// <summary>
    /// Query events from a single calendar
    /// </summary>
    private async Task<GoogleCalendarListDto> QuerySingleCalendarEventsAsync(HttpClient client, GoogleCalendarQueryDto query, string calendarId)
    {
        try
        {
            var events = await QuerySingleCalendarEventsInternalAsync(client, query, calendarId, calendarId);

            return new GoogleCalendarListDto
            {
                Success = true,
                Events = events,
                TotalCalendarsQueried = 1,
                QueriedCalendarIds = new List<string> { calendarId }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleAuthGAgent][QuerySingleCalendarEventsAsync] Error querying calendar {CalendarId}", calendarId);
            return new GoogleCalendarListDto 
            { 
                Success = false, 
                Error = "Internal server error",
                FailedCalendarIds = new Dictionary<string, string> { { calendarId, ex.Message } }
            };
        }
    }

    /// <summary>
    /// Internal method to query events from a single calendar
    /// </summary>
    private async Task<List<GoogleCalendarEventDto>> QuerySingleCalendarEventsInternalAsync(HttpClient client, GoogleCalendarQueryDto query, string calendarId, string calendarName)
    {
        // Build query parameters with defaults and validation
        var queryParams = BuildCalendarQueryParameters(query);
        var url = $"{_authOptions.CurrentValue.CalendarApiEndpoint}/calendars/{Uri.EscapeDataString(calendarId)}/events?{queryParams}";

        _logger.LogDebug("[GoogleAuthGAgent][QuerySingleCalendarEventsInternalAsync] Querying calendar {CalendarId}: {Url}", calendarId, url);

        var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("[GoogleAuthGAgent][QuerySingleCalendarEventsInternalAsync] Calendar events query failed for {CalendarId}: {Error}", calendarId, error);
            throw new HttpRequestException($"Calendar query failed: {response.StatusCode}");
        }

        var content = await response.Content.ReadAsStringAsync();
        var calendarResponse = JsonConvert.DeserializeObject<GoogleCalendarEventsResponseDto>(content);

        var events = new List<GoogleCalendarEventDto>();
        if (calendarResponse?.Items != null)
        {
            foreach (var item in calendarResponse.Items)
            {
                events.Add(new GoogleCalendarEventDto
                {
                    Id = item.Id,
                    Summary = item.Summary,
                    Description = item.Description,
                    StartTime = item.Start,
                    EndTime = item.End,
                    Status = item.Status,
                    Created = item.Created,
                    Updated = item.Updated,
                    CalendarId = calendarId,
                    CalendarName = calendarName ?? calendarId
                });
            }
        }

        _logger.LogDebug("[GoogleAuthGAgent][QuerySingleCalendarEventsInternalAsync] Retrieved {Count} events from calendar {CalendarId}", 
            events.Count, calendarId);

        return events;
    }
}
