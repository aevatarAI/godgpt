using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Helpers;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Aevatar.Application.Grains.ChatManager.UserQuota;

public interface IUserQuotaGrain : IGrainWithStringKey
{
    Task<bool> InitializeCreditsAsync();
    Task<CreditsInfoDto> GetCreditsAsync();
    Task SetShownCreditsToastAsync(bool hasShownInitialCreditsToast);
    Task<bool> IsSubscribedAsync();
    Task<SubscriptionInfoDto> GetSubscriptionAsync();
    Task<SubscriptionInfoDto> GetAndSetSubscriptionAsync();
    Task UpdateSubscriptionAsync(string planType, DateTime endDate);
    Task UpdateSubscriptionAsync(SubscriptionInfoDto subscriptionInfoDto);
    Task CancelSubscriptionAsync();
    Task<ExecuteActionResultDto> IsActionAllowedAsync(string actionType = "conversation");
    Task<ExecuteActionResultDto> ExecuteActionAsync(string sessionId, string chatManagerGuid,
        string actionType = "conversation");
    Task ResetRateLimitsAsync(string actionType = "conversation");
    Task ClearAllAsync();
    
    // Enhanced methods for internal Ultimate support (backward compatible)
    Task<bool> HasUnlimitedAccessAsync(); // Keep this as it's useful for rate limiting checks
}

public class UserQuotaGrain : Grain<UserQuotaState>, IUserQuotaGrain
{
    private readonly ILogger<UserQuotaGrain> _logger;
    private readonly IOptionsMonitor<CreditsOptions> _creditsOptions;
    private readonly IOptionsMonitor<RateLimitOptions> _rateLimiterOptions;

    public UserQuotaGrain(ILogger<UserQuotaGrain> logger, IOptionsMonitor<CreditsOptions> creditsOptions,
        IOptionsMonitor<RateLimitOptions> rateLimiterOptions)
    {
        _logger = logger;
        _creditsOptions = creditsOptions;
        _rateLimiterOptions = rateLimiterOptions;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await ReadStateAsync();
        await base.OnActivateAsync(cancellationToken);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        await WriteStateAsync();
        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    public async Task<bool> InitializeCreditsAsync()
    {
        if (State.HasInitialCredits)
        {
            return true;
        }

        var initialCredits = _creditsOptions.CurrentValue.InitialCreditsAmount;
        
        State.Credits = initialCredits;
        State.HasInitialCredits = true;
        
        await WriteStateAsync();
        
        _logger.LogDebug("[UserQuotaGrain][InitializeCreditsAsync] User {UserId} received {Credits} initial credits.", this.GetPrimaryKeyString(), initialCredits);
        return true;
    }

    public async Task<CreditsInfoDto> GetCreditsAsync()
    { 
        await InitializeCreditsAsync();
        var creditsInfoDto = new CreditsInfoDto
        {
            IsInitialized = State.HasInitialCredits,
            Credits = State.Credits,
            ShouldShowToast = State.HasShownInitialCreditsToast
        };
        if (State.HasInitialCredits && !State.HasShownInitialCreditsToast)
        {
            creditsInfoDto.ShouldShowToast = true;
        }
        else
        {
            creditsInfoDto.ShouldShowToast = false;
        }

        return creditsInfoDto;
    }

    public async Task SetShownCreditsToastAsync(bool hasShownInitialCreditsToast)
    {
        State.HasShownInitialCreditsToast = hasShownInitialCreditsToast;
        await WriteStateAsync();
    }

    #region Internal Dual Subscription Support (Private Implementation)

    /// <summary>
    /// Gets the active subscription based on priority: Ultimate > Standard
    /// </summary>
    private async Task<SubscriptionInfoDto> GetActiveSubscriptionAsync()
    {
        var now = DateTime.UtcNow;
        
        // Check Ultimate subscription first (higher priority)
        if (IsSubscriptionActive(State.UltimateSubscription, now))
        {
            return ConvertToDto(State.UltimateSubscription);
        }
        
        // Check Standard subscription (legacy field)
        if (IsSubscriptionActive(State.Subscription, now))
        {
            return ConvertToDto(State.Subscription);
        }
        
        // No active subscription
        return new SubscriptionInfoDto { IsActive = false };
    }

    /// <summary>
    /// Gets complete dual subscription status
    /// </summary>
    private async Task<DualSubscriptionStatusDto> GetDualSubscriptionStatusAsync()
    {
        var now = DateTime.UtcNow;
        var ultimateActive = IsSubscriptionActive(State.UltimateSubscription, now);
        var standardActive = IsSubscriptionActive(State.Subscription, now);
        
        return new DualSubscriptionStatusDto
        {
            UltimateSubscription = ConvertToDto(State.UltimateSubscription),
            StandardSubscription = ConvertToDto(State.Subscription),
            UltimateActive = ultimateActive,
            StandardActive = standardActive
        };
    }

    /// <summary>
    /// Updates standard subscription - only affects Standard data
    /// </summary>
    private async Task UpdateStandardSubscriptionAsync(SubscriptionInfoDto subscriptionInfoDto)
    {
        _logger.LogInformation("[UserQuotaGrain][UpdateStandardSubscriptionAsync] Updating standard subscription for user {UserId}",
            this.GetPrimaryKeyString());

        // Standard subscription rule: only affects Standard data, regardless of Ultimate status
        UpdateSubscriptionInfo(State.Subscription, subscriptionInfoDto);
        
        _logger.LogInformation("[UserQuotaGrain][UpdateStandardSubscriptionAsync] Updated Standard subscription, no impact on Ultimate for user {UserId}",
            this.GetPrimaryKeyString());
    }

    /// <summary>
    /// Updates Ultimate subscription with time extension mechanism
    /// </summary>
    private async Task UpdateUltimateSubscriptionAsync(SubscriptionInfoDto subscriptionInfoDto)
    {
        _logger.LogInformation("[UserQuotaGrain][UpdateUltimateSubscriptionAsync] Updating ultimate subscription for user {UserId}",
            this.GetPrimaryKeyString());

        var now = DateTime.UtcNow;
        var ultimateDays = SubscriptionHelper.GetDaysForPlanType(subscriptionInfoDto.PlanType);
        
        // Ultimate subscription rule: extend Standard subscription time when Ultimate is purchased and Standard is still active
        if (State.Subscription.IsActive && State.Subscription.EndDate > now)
        {
            State.Subscription.EndDate = State.Subscription.EndDate.AddDays(ultimateDays);
            
            _logger.LogInformation("[UserQuotaGrain][UpdateUltimateSubscriptionAsync] Extended Standard subscription by {Days} days due to Ultimate purchase for user {UserId}",
                ultimateDays, this.GetPrimaryKeyString());
        }
        
        // Update Ultimate subscription using standard logic
        UpdateSubscriptionInfo(State.UltimateSubscription, subscriptionInfoDto);
    }

    /// <summary>
    /// Cancels Ultimate subscription - affects both Ultimate and Standard
    /// </summary>
    private async Task CancelUltimateSubscriptionAsync()
    {
        var now = DateTime.UtcNow;
        var ultimateRemainingTime = State.UltimateSubscription.EndDate - now;
        var ultimateDays = SubscriptionHelper.GetDaysForPlanType(State.UltimateSubscription.PlanType);
        
        // Ultimate cancellation rule: reduce Ultimate time and also reduce Standard time if Standard is still active
        if (ultimateRemainingTime.TotalSeconds > 0)
        {
            // Reduce Ultimate subscription time
            State.UltimateSubscription.EndDate = State.UltimateSubscription.EndDate.AddDays(-ultimateDays);
            
            // If Standard is still active, also reduce its time accordingly
            if (State.Subscription.IsActive && State.Subscription.EndDate > now)
            {
                State.Subscription.EndDate = State.Subscription.EndDate.AddDays(-ultimateDays);
                
                _logger.LogInformation("[UserQuotaGrain][CancelUltimateSubscriptionAsync] Reduced Standard subscription by {Days} days due to Ultimate cancellation for user {UserId}",
                    ultimateDays, this.GetPrimaryKeyString());
                
                // If Standard time becomes expired, set to cancelled status
                if (State.Subscription.EndDate <= now)
                {
                    State.Subscription.IsActive = false;
                    State.Subscription.Status = PaymentStatus.Cancelled;
                    
                    _logger.LogInformation("[UserQuotaGrain][CancelUltimateSubscriptionAsync] Standard subscription expired due to Ultimate cancellation for user {UserId}",
                        this.GetPrimaryKeyString());
                }
            }
            
            // Check if Ultimate subscription is fully cancelled
            if (State.UltimateSubscription.EndDate <= now)
            {
                State.UltimateSubscription.IsActive = false;
                State.UltimateSubscription.Status = PaymentStatus.Cancelled;
                
                _logger.LogInformation("[UserQuotaGrain][CancelUltimateSubscriptionAsync] Ultimate subscription fully cancelled for user {UserId}",
                    this.GetPrimaryKeyString());
            }
            else
            {
                _logger.LogInformation("[UserQuotaGrain][CancelUltimateSubscriptionAsync] Processed Ultimate subscription cancellation for user {UserId}, reduced by {Days} days, new end date: {EndDate}", 
                    this.GetPrimaryKeyString(), ultimateDays, State.UltimateSubscription.EndDate);
            }
        }
        else
        {
            State.UltimateSubscription.IsActive = false;
            State.UltimateSubscription.Status = PaymentStatus.Cancelled;
            
            _logger.LogInformation("[UserQuotaGrain][CancelUltimateSubscriptionAsync] Ultimate subscription expired and cancelled for user {UserId}",
                this.GetPrimaryKeyString());
        }
    }

    #endregion

    #region Public API Methods (Unified Interface)

    /// <summary>
    /// Checks if user has unlimited access (Ultimate subscription active)
    /// </summary>
    public async Task<bool> HasUnlimitedAccessAsync()
    {
        var activeSubscription = await GetActiveSubscriptionAsync();
        return activeSubscription.IsActive && SubscriptionHelper.IsUltimateSubscription(activeSubscription.PlanType);
    }

    #endregion

    #region Legacy Compatibility Methods

    public async Task<bool> IsSubscribedAsync()
    {
        var activeSubscription = await GetActiveSubscriptionAsync();
        var isSubscribed = activeSubscription.IsActive;
        
        // Handle expiration cleanup for legacy compatibility
        if (!isSubscribed)
        {
            if (State.Subscription.IsActive)
            {
                _logger.LogDebug("[UserQuotaGrain][IsSubscribedAsync] Legacy subscription expired for user {UserId}", 
                    this.GetPrimaryKeyString());
                State.Subscription.IsActive = false;
                await WriteStateAsync();
            }
            
            // Clear rate limits on expiration
            if (State.RateLimits.ContainsKey("conversation"))
            {
                State.RateLimits.Remove("conversation");
            }
        }
        
        _logger.LogDebug("[UserQuotaGrain][IsSubscribedAsync] User {UserId} subscription status: {IsSubscribed}", 
            this.GetPrimaryKeyString(), isSubscribed);
        
        return isSubscribed;
    }

    public Task<SubscriptionInfoDto> GetSubscriptionAsync()
    {
        // Return the active subscription (Ultimate takes priority over Standard)
        return GetActiveSubscriptionAsync();
    }

    public async Task<SubscriptionInfoDto> GetAndSetSubscriptionAsync()
    {
        await IsSubscribedAsync();
        return await GetActiveSubscriptionAsync();
    }

    public async Task UpdateSubscriptionAsync(string planType, DateTime endDate)
    {
        if (!Enum.TryParse<PlanType>(planType, true, out var parsedPlanType))
        {
            _logger.LogWarning("[UserQuotaGrain][UpdateSubscriptionAsync] Invalid plan type: {PlanType} for user {UserId}", planType, this.GetPrimaryKeyString());
            throw new ArgumentException($"Invalid plan type: {planType}", nameof(planType));
        }
        
        var subscriptionDto = new SubscriptionInfoDto
        {
            PlanType = parsedPlanType,
            IsActive = true,
            StartDate = DateTime.UtcNow,
            EndDate = endDate,
            Status = PaymentStatus.Completed,
            SubscriptionIds = new List<string>(),
            InvoiceIds = new List<string>()
        };
        
        // Route to appropriate subscription based on plan type
        if (SubscriptionHelper.IsUltimateSubscription(parsedPlanType))
        {
            await UpdateUltimateSubscriptionAsync(subscriptionDto);
        }
        else
        {
            await UpdateStandardSubscriptionAsync(subscriptionDto);
        }
        
        _logger.LogInformation("[UserQuotaGrain][UpdateSubscriptionAsync] Updated subscription for user {UserId}: Plan={PlanType}, EndDate={EndDate}", 
            this.GetPrimaryKeyString(), planType, endDate);
    }

    public async Task UpdateSubscriptionAsync(SubscriptionInfoDto subscriptionInfoDto)
    {
        _logger.LogInformation("[UserQuotaGrain][UpdateSubscriptionAsync] Updating subscription for user {UserId}: Data={Data}", 
            this.GetPrimaryKeyString(), JsonConvert.SerializeObject(subscriptionInfoDto));
        
        // Smart routing based on plan type - internal Ultimate logic, unified external interface
        if (SubscriptionHelper.IsUltimateSubscription(subscriptionInfoDto.PlanType))
        {
            await UpdateUltimateSubscriptionAsync(subscriptionInfoDto);
        }
        else
        {
            await UpdateStandardSubscriptionAsync(subscriptionInfoDto);
        }
        
        // Update legacy subscription for backward compatibility
        UpdateSubscriptionInfo(State.Subscription, subscriptionInfoDto);
        await WriteStateAsync();
    }

    public async Task CancelSubscriptionAsync()
    {
        // Smart cancellation - determine what subscription is active and cancel accordingly
        var activeSubscription = await GetActiveSubscriptionAsync();
        
        if (!activeSubscription.IsActive)
        {
            _logger.LogInformation("[UserQuotaGrain][CancelSubscriptionAsync] User {UserId} has no active subscription to cancel", 
                this.GetPrimaryKeyString());
            
            // Legacy compatibility
            State.Subscription.IsActive = false;
            State.Subscription.Status = PaymentStatus.Cancelled;
            await WriteStateAsync();
            return;
        }
        
        // Route to appropriate cancellation logic based on active subscription type
        if (SubscriptionHelper.IsUltimateSubscription(activeSubscription.PlanType))
        {
            await CancelUltimateSubscriptionAsync();
        }
        else
        {
            // Handle Standard subscription cancellation
            var now = DateTime.UtcNow;
            var standardRemainingTime = State.Subscription.EndDate - now;
            var standardDays = SubscriptionHelper.GetDaysForPlanType(State.Subscription.PlanType);
            
            // Apply refund logic pattern for Standard subscription
            if (standardRemainingTime.TotalSeconds > 0)
            {
                // Subtract the plan duration (simulates refund)
                State.Subscription.EndDate = State.Subscription.EndDate.AddDays(-standardDays);
                
                // If EndDate becomes past, deactivate
                if (State.Subscription.EndDate <= now)
                {
                    State.Subscription.IsActive = false;
                    State.Subscription.Status = PaymentStatus.Cancelled;
                }
                
                _logger.LogInformation("[UserQuotaGrain][CancelSubscriptionAsync] Processed Standard subscription cancellation for user {UserId}, reduced by {Days} days", 
                    this.GetPrimaryKeyString(), standardDays);
            }
            else
            {
                State.Subscription.IsActive = false;
                State.Subscription.Status = PaymentStatus.Cancelled;
            }
            
            _logger.LogInformation("[UserQuotaGrain][CancelSubscriptionAsync] Cancelled Standard subscription for user {UserId}", 
                this.GetPrimaryKeyString());
        }
        
        // Legacy compatibility
        State.Subscription.IsActive = false;
        State.Subscription.Status = PaymentStatus.Cancelled;
        
        await WriteStateAsync();
    }

    #endregion

    #region Rate Limiting with Ultimate Support

    public async Task<ExecuteActionResultDto> IsActionAllowedAsync(string actionType = "conversation")
    {
        // Check for unlimited access first
        if (await HasUnlimitedAccessAsync())
        {
            _logger.LogDebug("[UserQuotaGrain][IsActionAllowedAsync] User {UserId} has unlimited access (Ultimate subscription)", 
                this.GetPrimaryKeyString());
            return new ExecuteActionResultDto { Success = true };
        }
        
        // Apply standard rate limiting and credits logic
        return await ApplyStandardRateLimitingAsync(actionType);
    }

    public async Task<ExecuteActionResultDto> ExecuteActionAsync(string sessionId, string chatManagerGuid,
        string actionType = "conversation")
    {
        // Check for unlimited access first
        if (await HasUnlimitedAccessAsync())
        {
            _logger.LogDebug("[UserQuotaGrain][ExecuteActionAsync] User {UserId} executing action with unlimited access", 
                this.GetPrimaryKeyString());
            return new ExecuteActionResultDto { Success = true };
        }
        
        // Apply standard execution logic with rate limiting and credits
        return await ExecuteStandardActionAsync(sessionId, chatManagerGuid, actionType);
    }

    private async Task<ExecuteActionResultDto> ApplyStandardRateLimitingAsync(string actionType)
    {
        var now = DateTime.UtcNow;
        var isSubscribed = await IsSubscribedAsync();
        
        // Check credits for non-subscribers
        if (!isSubscribed)
        {
            var requiredCredits = _creditsOptions.CurrentValue.CreditsPerConversation;
            var credits = (await GetCreditsAsync()).Credits;
            var isAllowed = credits >= requiredCredits;
            
            _logger.LogDebug("[UserQuotaGrain][ApplyStandardRateLimitingAsync] Credits check for user {UserId}: allowed={IsAllowed}, credits={Credits}, required={Required}", 
                this.GetPrimaryKeyString(), isAllowed, credits, requiredCredits);
                
            if (!isAllowed)
            {
                return new ExecuteActionResultDto
                {
                    Code = ExecuteActionStatus.InsufficientCredits,
                    Message = "You've run out of credits."
                };
            }
        }
        
        // Apply rate limiting
        var maxTokens = isSubscribed 
            ? _rateLimiterOptions.CurrentValue.SubscribedUserMaxRequests 
            : _rateLimiterOptions.CurrentValue.UserMaxRequests;
        var timeWindow = isSubscribed 
            ? _rateLimiterOptions.CurrentValue.SubscribedUserTimeWindowSeconds 
            : _rateLimiterOptions.CurrentValue.UserTimeWindowSeconds;
            
        if (!State.RateLimits.TryGetValue(actionType, out var rateLimitInfo))
        {
            rateLimitInfo = new RateLimitInfo { Count = maxTokens, LastTime = now };
            State.RateLimits[actionType] = rateLimitInfo;
            _logger.LogDebug("[UserQuotaGrain][ApplyStandardRateLimitingAsync] Created new rate limit for user {UserId}, action {ActionType}, max tokens {MaxTokens}", 
                this.GetPrimaryKeyString(), actionType, maxTokens);
        }
        else
        {
            var timeElapsed = now - rateLimitInfo.LastTime;
            var elapsedSeconds = timeElapsed.TotalSeconds;
            var refillRate = (double)maxTokens / timeWindow;
            var tokensToAdd = (int)(elapsedSeconds * refillRate);
            
            if (tokensToAdd > 0)
            {
                rateLimitInfo.Count = Math.Min(maxTokens, rateLimitInfo.Count + tokensToAdd);
                rateLimitInfo.LastTime = now;
                
                _logger.LogDebug("[UserQuotaGrain][ApplyStandardRateLimitingAsync] Refreshed tokens for user {UserId}, action {ActionType}, added {TokensAdded}, current {CurrentTokens}", 
                    this.GetPrimaryKeyString(), actionType, tokensToAdd, rateLimitInfo.Count);
            }
        }
        
        if (rateLimitInfo.Count <= 0)
        {
            _logger.LogWarning("[UserQuotaGrain][ApplyStandardRateLimitingAsync] Rate limit exceeded for user {UserId}, action {ActionType}", 
                this.GetPrimaryKeyString(), actionType);
            return new ExecuteActionResultDto
            {
                Code = 20002,
                Message = $"Message limit reached ({maxTokens} in {timeWindow / 3600} hours). Please try again later."
            };
        }
        
        return new ExecuteActionResultDto { Success = true };
    }

    private async Task<ExecuteActionResultDto> ExecuteStandardActionAsync(string sessionId, string chatManagerGuid, string actionType)
    {
        var now = DateTime.UtcNow;
        var isSubscribed = await IsSubscribedAsync();
        var maxTokens = isSubscribed 
            ? _rateLimiterOptions.CurrentValue.SubscribedUserMaxRequests 
            : _rateLimiterOptions.CurrentValue.UserMaxRequests;
        var timeWindow = isSubscribed 
            ? _rateLimiterOptions.CurrentValue.SubscribedUserTimeWindowSeconds 
            : _rateLimiterOptions.CurrentValue.UserTimeWindowSeconds;
            
        _logger.LogDebug("[UserQuotaGrain][ExecuteStandardActionAsync] sessionId={SessionId} chatManagerGuid={ChatManagerGuid} config: maxTokens={MaxTokens}, timeWindow={TimeWindow}, isSubscribed={IsSubscribed}, now(UTC)={Now}", 
            sessionId, chatManagerGuid, maxTokens, timeWindow, isSubscribed, now);
            
        // Initialize or update rate limit info
        if (!State.RateLimits.TryGetValue(actionType, out var rateLimitInfo))
        {
            rateLimitInfo = new RateLimitInfo { Count = maxTokens, LastTime = now };
            State.RateLimits[actionType] = rateLimitInfo;
            _logger.LogDebug("[UserQuotaGrain][ExecuteStandardActionAsync] sessionId={SessionId} chatManagerGuid={ChatManagerGuid} INIT RateLimitInfo: count={Count}, lastTime(UTC)={LastTime}", 
                sessionId, chatManagerGuid, rateLimitInfo.Count, rateLimitInfo.LastTime);
        }
        else
        {
            var timeElapsed = now - rateLimitInfo.LastTime;
            var elapsedSeconds = timeElapsed.TotalSeconds;
            var refillRate = (double)maxTokens / timeWindow;
            var tokensToAdd = (int)(elapsedSeconds * refillRate);
            
            if (tokensToAdd > 0)
            {
                rateLimitInfo.Count = Math.Min(maxTokens, rateLimitInfo.Count + tokensToAdd);
                rateLimitInfo.LastTime = now;
                _logger.LogDebug("[UserQuotaGrain][ExecuteStandardActionAsync] sessionId={SessionId} chatManagerGuid={ChatManagerGuid} REFILL: tokensToAdd={TokensToAdd}, newCount={Count}, now(UTC)={Now}", 
                    sessionId, chatManagerGuid, tokensToAdd, rateLimitInfo.Count, now);
            }
        }
        
        // Check credits for non-subscribers
        if (!isSubscribed)
        {
            var requiredCredits = _creditsOptions.CurrentValue.CreditsPerConversation;
            var credits = (await GetCreditsAsync()).Credits;
            var isAllowed = credits >= requiredCredits;
            
            _logger.LogDebug("[UserQuotaGrain][ExecuteStandardActionAsync] sessionId={SessionId} chatManagerGuid={ChatManagerGuid} CREDITS: allowed={IsAllowed}, credits={Credits}, required={RequiredCredits}, now(UTC)={Now}", 
                sessionId, chatManagerGuid, isAllowed, credits, requiredCredits, now);
                
            if (!isAllowed)
            {
                return new ExecuteActionResultDto
                {
                    Code = ExecuteActionStatus.InsufficientCredits,
                    Message = "You've run out of credits."
                };
            }
        }
        
        // Check rate limit
        var oldValue = State.RateLimits[actionType].Count;
        if (oldValue <= 0)
        {
            _logger.LogWarning("[UserQuotaGrain][ExecuteStandardActionAsync] sessionId={SessionId} chatManagerGuid={ChatManagerGuid} RATE LIMITED (oldValue): count={Count}, now(UTC)={Now}", 
                sessionId, chatManagerGuid, oldValue, now);
            return new ExecuteActionResultDto
            {
                Code = ExecuteActionStatus.RateLimitExceeded,
                Message = "Message limit reached. Please try again later."
            };
        }

        // Execute action - deduct credits and tokens
        if (!isSubscribed)
        {
            State.Credits -= _creditsOptions.CurrentValue.CreditsPerConversation;
        }
        State.RateLimits[actionType].Count = State.RateLimits[actionType].Count - 1;
        await WriteStateAsync();
        
        _logger.LogDebug("[UserQuotaGrain][ExecuteStandardActionAsync] sessionId={SessionId} chatManagerGuid={ChatManagerGuid} AFTER decrement: count={Count}, now(UTC)={Now}", 
            sessionId, chatManagerGuid, State.RateLimits[actionType].Count, now);
        
        return new ExecuteActionResultDto { Success = true };
    }

    #endregion

    #region Utility Methods

    public async Task ResetRateLimitsAsync(string actionType = "conversation")
    {
        if (State.RateLimits.ContainsKey(actionType))
        {
            State.RateLimits.Remove(actionType);
        }
        
        await WriteStateAsync();
    }

    public async Task ClearAllAsync()
    {
        State = new UserQuotaState();
        await WriteStateAsync();
    }

    private bool IsSubscriptionActive(SubscriptionInfo subscription, DateTime now)
    {
        return subscription.IsActive && 
               subscription.StartDate <= now &&
               subscription.EndDate > now;
    }

    private SubscriptionInfoDto ConvertToDto(SubscriptionInfo subscription)
    {
        return new SubscriptionInfoDto
        {
            IsActive = subscription.IsActive,
            PlanType = subscription.PlanType,
            Status = subscription.Status,
            StartDate = subscription.StartDate,
            EndDate = subscription.EndDate,
            SubscriptionIds = subscription.SubscriptionIds,
            InvoiceIds = subscription.InvoiceIds
        };
    }

    private void UpdateSubscriptionInfo(SubscriptionInfo subscription, SubscriptionInfoDto dto)
    {
        subscription.PlanType = dto.PlanType;
        subscription.IsActive = dto.IsActive;
        subscription.StartDate = dto.StartDate;
        subscription.EndDate = dto.EndDate;
        subscription.Status = dto.Status;
        subscription.SubscriptionIds = dto.SubscriptionIds ?? new List<string>();
        subscription.InvoiceIds = dto.InvoiceIds ?? new List<string>();
    }

    #endregion
}