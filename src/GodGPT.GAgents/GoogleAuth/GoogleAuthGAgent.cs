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
    Task<GoogleAuthResultDto> VerifyAuthCodeAsync(string platform, string code, string redirectUri);

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
    public async Task<GoogleAuthResultDto> VerifyAuthCodeAsync(string platform, string code, string redirectUri)
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
            var tokenResult = await ExchangeAuthCodeAsync(platform, code, redirectUri);
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

            // Get redirect URI from platform config or fallback to default
            var redirectUriResult = GetPlatformRedirectUri(platform);

            return new GoogleAuthResultDto
            {
                Success = true,
                GoogleUserId = userInfo.GoogleUserId,
                Email = userInfo.Email,
                DisplayName = userInfo.DisplayName,
                BindStatus = State.IsBound,
                RedirectUri = redirectUriResult
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

            // Build query parameters with defaults and validation
            var queryParams = BuildCalendarQueryParameters(query);
            var calendarId = string.IsNullOrEmpty(query.CalendarId) ? "primary" : query.CalendarId;
            
            var url = $"{_authOptions.CurrentValue.CalendarApiEndpoint}/calendars/{calendarId}/events?{queryParams}";

            _logger.LogInformation("[GoogleAuthGAgent][QueryCalendarEventsAsync] Querying Google Calendar events: {Url}", url);

            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
                var error = await response.Content.ReadAsStringAsync();
                var localizedMessage = _localizationService.GetLocalizedException(ExceptionMessageKeys.FailedGetUserInfo, language);

                _logger.LogError("[GoogleAuthGAgent][QueryCalendarEventsAsync] Calendar events query failed: {Error}", error);
                return new GoogleCalendarListDto { Success = false, Error = localizedMessage };
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
                        Updated = item.Updated
                    });
                }
            }

            _logger.LogInformation("[GoogleAuthGAgent][QueryCalendarEventsAsync] Successfully retrieved {Count} calendar events", events.Count);

            return new GoogleCalendarListDto
            {
                Success = true,
                Events = events,
                NextPageToken = calendarResponse?.NextPageToken
            };
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

    private async Task<TokenResultDto> ExchangeAuthCodeAsync(string platform, string code, string redirectUri)
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

            var response = await client.PostAsync(_authOptions.CurrentValue.TokenEndpoint, new FormUrlEncodedContent(formData));
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[GoogleAuthGAgent][ExchangeAuthCodeAsync] Token exchange failed for platform {Platform}: Status={StatusCode}, Error={Error}", 
                    platform, response.StatusCode, responseContent);
                return new TokenResultDto { Success = false, Error = "Failed to exchange authorization code" };
            }

            var tokenResponse = JsonConvert.DeserializeObject<GoogleTokenResponseDto>(responseContent);

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
    /// Get redirect URI for platform
    /// </summary>
    private string GetPlatformRedirectUri(string platform)
    {
        if (string.IsNullOrEmpty(platform))
        {
            return string.Empty;
        }

        var platformLower = platform.ToLowerInvariant();

        // Get redirect URI from platform config
        var platformConfig = GetPlatformConfig(platformLower);
        if (!string.IsNullOrEmpty(platformConfig?.RedirectUri))
        {
            return platformConfig.RedirectUri;
        }

        _logger.LogWarning("[GoogleAuthGAgent][GetPlatformRedirectUri] RedirectUri not configured for platform: {Platform}", platform);
        return string.Empty;
    }
    
    /// <summary>
    /// Build query parameters string for Google Calendar API
    /// </summary>
    private string BuildCalendarQueryParameters(GoogleCalendarQueryDto query)
    {
        var parameters = new List<string>();

        // Time range parameters
        var startTime = query.StartTime ?? DateTime.UtcNow;
        var endTime = query.EndTime ?? startTime.AddDays(_authOptions.CurrentValue.DefaultCalendarQueryRangeDays);
        
        parameters.Add($"timeMin={startTime:yyyy-MM-ddTHH:mm:ssZ}");
        parameters.Add($"timeMax={endTime:yyyy-MM-ddTHH:mm:ssZ}");

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
}
