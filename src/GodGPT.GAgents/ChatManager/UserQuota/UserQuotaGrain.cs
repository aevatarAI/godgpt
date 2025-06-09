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
    /// Gets the active subscription based on priority: Ultimate > Standard (unfrozen)
    /// </summary>
    private async Task<SubscriptionInfoDto> GetActiveSubscriptionAsync()
    {
        var now = DateTime.UtcNow;
        
        // Priority 1: Ultimate subscription
        if (IsSubscriptionActive(State.UltimateSubscription, now))
        {
            _logger.LogDebug("[UserQuotaGrain][GetActiveSubscriptionAsync] User {UserId} has active Ultimate subscription", 
                this.GetPrimaryKeyString());
            return ConvertToDto(State.UltimateSubscription);
        }
        
        // Priority 2: Standard subscription (considering freeze state)
        if (IsSubscriptionActive(State.StandardSubscription, now))
        {
            // Check if standard subscription should be unfrozen
            await CheckAndUnfreezeStandardSubscriptionAsync();
            
            _logger.LogDebug("[UserQuotaGrain][GetActiveSubscriptionAsync] User {UserId} has active Standard subscription", 
                this.GetPrimaryKeyString());
            return ConvertToDto(State.StandardSubscription);
        }
        
        _logger.LogDebug("[UserQuotaGrain][GetActiveSubscriptionAsync] User {UserId} has no active subscription", 
            this.GetPrimaryKeyString());
        return new SubscriptionInfoDto { IsActive = false, PlanType = PlanType.None };
    }

    /// <summary>
    /// Gets complete dual subscription status
    /// </summary>
    private async Task<DualSubscriptionStatusDto> GetDualSubscriptionStatusAsync()
    {
        var activeSubscription = await GetActiveSubscriptionAsync();
        var ultimateActive = IsSubscriptionActive(State.UltimateSubscription, DateTime.UtcNow);
        var standardActive = IsSubscriptionActive(State.StandardSubscription, DateTime.UtcNow);
        
        return new DualSubscriptionStatusDto
        {
            ActiveSubscription = activeSubscription,
            StandardSubscription = ConvertToDto(State.StandardSubscription),
            UltimateSubscription = ConvertToDto(State.UltimateSubscription),
            IsStandardFrozen = State.StandardSubscriptionFrozenAt.HasValue,
            FrozenAt = State.StandardSubscriptionFrozenAt,
            AccumulatedFrozenTime = State.AccumulatedFrozenTime,
            HasUnlimitedAccess = ultimateActive
        };
    }

    /// <summary>
    /// Updates standard subscription
    /// </summary>
    private async Task UpdateStandardSubscriptionAsync(SubscriptionInfoDto subscriptionInfoDto)
    {
        _logger.LogInformation("[UserQuotaGrain][UpdateStandardSubscriptionAsync] Updating standard subscription for user {UserId}", 
            this.GetPrimaryKeyString());
        
        UpdateSubscriptionInfo(State.StandardSubscription, subscriptionInfoDto);
        
        // If Ultimate is active, freeze the standard subscription
        if (IsSubscriptionActive(State.UltimateSubscription, DateTime.UtcNow))
        {
            await FreezeStandardSubscriptionAsync();
        }
        
        await WriteStateAsync();
    }

    /// <summary>
    /// Updates Ultimate subscription
    /// </summary>
    private async Task UpdateUltimateSubscriptionAsync(SubscriptionInfoDto subscriptionInfoDto)
    {
        _logger.LogInformation("[UserQuotaGrain][UpdateUltimateSubscriptionAsync] Updating Ultimate subscription for user {UserId}", 
            this.GetPrimaryKeyString());
        
        // If activating Ultimate and Standard is active, accumulate Standard remaining time
        if (subscriptionInfoDto.IsActive && State.StandardSubscription.IsActive)
        {
            var now = DateTime.UtcNow;
            var standardRemainingTime = State.StandardSubscription.EndDate - now;
            
            // Only accumulate if Standard has remaining time
            if (standardRemainingTime.TotalSeconds > 0)
            {
                // Add Standard remaining time to Ultimate end date
                subscriptionInfoDto.EndDate = subscriptionInfoDto.EndDate.Add(standardRemainingTime);
                
                // Deactivate Standard subscription as its time has been accumulated
                State.StandardSubscription.IsActive = false;
                
                _logger.LogInformation("[UserQuotaGrain][UpdateUltimateSubscriptionAsync] Accumulated Standard remaining time {RemainingTime} into Ultimate subscription for user {UserId}", 
                    standardRemainingTime, this.GetPrimaryKeyString());
            }
        }
        
        UpdateSubscriptionInfo(State.UltimateSubscription, subscriptionInfoDto);
        
        // Reset rate limits for unlimited access
        if (subscriptionInfoDto.IsActive)
        {
            await ResetRateLimitsAsync(); // Ultimate gets unlimited access
        }
        
        await WriteStateAsync();
    }

    /// <summary>
    /// Cancels Ultimate subscription and converts remaining time back to Standard
    /// </summary>
    private async Task CancelUltimateSubscriptionAsync()
    {
        if (!IsSubscriptionActive(State.UltimateSubscription, DateTime.UtcNow))
        {
            _logger.LogWarning("[UserQuotaGrain][CancelUltimateSubscriptionAsync] No active Ultimate subscription to cancel for user {UserId}", 
                this.GetPrimaryKeyString());
            return;
        }

        var now = DateTime.UtcNow;
        var ultimateRemainingTime = State.UltimateSubscription.EndDate - now;
        
        // Use refund logic pattern: subtract purchased days from subscription
        var ultimateDays = SubscriptionHelper.GetDaysForPlanType(State.UltimateSubscription.PlanType);
        
        // Calculate how much time to refund
        if (ultimateRemainingTime.TotalSeconds > 0)
        {
            // Subtract the Ultimate plan duration (this simulates the refund)
            State.UltimateSubscription.EndDate = State.UltimateSubscription.EndDate.AddDays(-ultimateDays);
            
            // If EndDate becomes past, deactivate the subscription
            if (State.UltimateSubscription.EndDate <= now)
            {
                State.UltimateSubscription.IsActive = false;
                State.UltimateSubscription.Status = PaymentStatus.Cancelled;
                
                // Restore Standard subscription with remaining time (like refund pattern)
                if (ultimateRemainingTime.TotalDays > 0)
                {
                    State.StandardSubscription.IsActive = true;
                    State.StandardSubscription.PlanType = PlanType.Month; // Default to Month plan
                    State.StandardSubscription.StartDate = now;
                    State.StandardSubscription.EndDate = now.Add(ultimateRemainingTime);
                    State.StandardSubscription.Status = PaymentStatus.Completed;
                    
                    _logger.LogInformation("[UserQuotaGrain][CancelUltimateSubscriptionAsync] Restored Standard subscription with remaining time {RemainingTime} for user {UserId}", 
                        ultimateRemainingTime, this.GetPrimaryKeyString());
                }
            }
            
            _logger.LogInformation("[UserQuotaGrain][CancelUltimateSubscriptionAsync] Processed Ultimate subscription cancellation for user {UserId}, reduced by {Days} days", 
                this.GetPrimaryKeyString(), ultimateDays);
        }
        else
        {
            // No remaining time, just cancel
            State.UltimateSubscription.IsActive = false;
            State.UltimateSubscription.Status = PaymentStatus.Cancelled;
        }
        
        _logger.LogInformation("[UserQuotaGrain][CancelUltimateSubscriptionAsync] Cancelled Ultimate subscription for user {UserId}", 
            this.GetPrimaryKeyString());
            
        await WriteStateAsync();
    }

    /// <summary>
    /// Freezes standard subscription when Ultimate becomes active
    /// This method is kept for backward compatibility but should not be used in new logic
    /// </summary>
    [Obsolete("Use time accumulation logic in UpdateUltimateSubscriptionAsync instead")]
    private async Task FreezeStandardSubscriptionAsync()
    {
        if (State.StandardSubscription.IsActive && !State.StandardSubscriptionFrozenAt.HasValue)
        {
            State.StandardSubscriptionFrozenAt = DateTime.UtcNow;
            _logger.LogInformation("[UserQuotaGrain][FreezeStandardSubscriptionAsync] Standard subscription frozen at {FrozenTime} for user {UserId}", 
                State.StandardSubscriptionFrozenAt, this.GetPrimaryKeyString());
            await WriteStateAsync();
        }
    }

    /// <summary>
    /// Unfreezes standard subscription when Ultimate expires
    /// This method is kept for backward compatibility but should not be used in new logic
    /// </summary>
    [Obsolete("Use time accumulation logic in UpdateUltimateSubscriptionAsync instead")]
    private async Task UnfreezeStandardSubscriptionAsync()
    {
        if (State.StandardSubscriptionFrozenAt.HasValue)
        {
            var frozenDuration = DateTime.UtcNow - State.StandardSubscriptionFrozenAt.Value;
            State.AccumulatedFrozenTime += frozenDuration;
            
            // Extend standard subscription by frozen duration
            State.StandardSubscription.EndDate = State.StandardSubscription.EndDate.Add(frozenDuration);
            
            // Reset freeze state
            State.StandardSubscriptionFrozenAt = null;
            
            _logger.LogInformation("[UserQuotaGrain][UnfreezeStandardSubscriptionAsync] Standard subscription unfrozen for user {UserId}, extended by {Duration}", 
                this.GetPrimaryKeyString(), frozenDuration);
            await WriteStateAsync();
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
            var standardRemainingTime = State.StandardSubscription.EndDate - now;
            var standardDays = SubscriptionHelper.GetDaysForPlanType(State.StandardSubscription.PlanType);
            
            // Apply refund logic pattern for Standard subscription
            if (standardRemainingTime.TotalSeconds > 0)
            {
                // Subtract the plan duration (simulates refund)
                State.StandardSubscription.EndDate = State.StandardSubscription.EndDate.AddDays(-standardDays);
                
                // If EndDate becomes past, deactivate
                if (State.StandardSubscription.EndDate <= now)
                {
                    State.StandardSubscription.IsActive = false;
                    State.StandardSubscription.Status = PaymentStatus.Cancelled;
                }
                
                _logger.LogInformation("[UserQuotaGrain][CancelSubscriptionAsync] Processed Standard subscription cancellation for user {UserId}, reduced by {Days} days", 
                    this.GetPrimaryKeyString(), standardDays);
            }
            else
            {
                State.StandardSubscription.IsActive = false;
                State.StandardSubscription.Status = PaymentStatus.Cancelled;
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

    private async Task CheckAndUnfreezeStandardSubscriptionAsync()
    {
        // If Ultimate is no longer active and standard is frozen, unfreeze it
        if (!IsSubscriptionActive(State.UltimateSubscription, DateTime.UtcNow) && 
            State.StandardSubscriptionFrozenAt.HasValue)
        {
            await UnfreezeStandardSubscriptionAsync();
        }
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