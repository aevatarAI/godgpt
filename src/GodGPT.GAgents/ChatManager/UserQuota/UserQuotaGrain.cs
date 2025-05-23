using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Common.Constants;
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
    Task UpdateSubscriptionAsync(string planType, DateTime endDate);
    Task UpdateSubscriptionAsync(SubscriptionInfoDto subscriptionInfoDto);
    Task CancelSubscriptionAsync();
    Task<ExecuteActionResultDto> IsActionAllowedAsync(string actionType = "conversation");

    Task<ExecuteActionResultDto> ExecuteActionAsync(string sessionId, string chatManagerGuid,
        string actionType = "conversation");
    Task ResetRateLimitsAsync(string actionType = "conversation");
    Task ClearAllAsync();
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

    public async Task<bool> IsSubscribedAsync()
    {
        var now = DateTime.UtcNow;
        var isSubscribed = State.Subscription.IsActive && 
                           State.Subscription.StartDate <= now &&
                           State.Subscription.EndDate > now;

        if (!isSubscribed && State.Subscription.IsActive)
        {
            _logger.LogDebug("[UserQuotaGrain][IsSubscribedAsync] Subscription for user {UserId} expired. Start: {StartDate}, End: {EndDate}, Now: {Now}", 
                this.GetPrimaryKeyString(), State.Subscription.StartDate, State.Subscription.EndDate, now);

            if (State.RateLimits.ContainsKey("conversation"))
            {
                State.RateLimits.Remove("conversation");
            }
            
            State.Subscription.IsActive = false;
            
            await WriteStateAsync();
        }
        
        _logger.LogDebug("[UserQuotaGrain][IsSubscribedAsync] User {UserId} subscription status: {IsSubscribed}", 
            this.GetPrimaryKeyString(), isSubscribed);
        
        return isSubscribed;
    }

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

    public Task<SubscriptionInfoDto> GetSubscriptionAsync()
    {
        _logger.LogDebug("[UserQuotaGrain][GetSubscriptionAsync] Getting subscription info for user {UserId}", this.GetPrimaryKeyString());
        return Task.FromResult(new SubscriptionInfoDto
        {
            IsActive = State.Subscription.IsActive,
            PlanType = State.Subscription.PlanType,
            Status = State.Subscription.Status,
            StartDate = State.Subscription.StartDate,
            EndDate = State.Subscription.EndDate,
            SubscriptionIds = State.Subscription.SubscriptionIds,
            InvoiceIds = State.Subscription.InvoiceIds
        });
    }

    public async Task UpdateSubscriptionAsync(string planType, DateTime endDate)
    {
        if (!Enum.TryParse<PlanType>(planType, true, out var parsedPlanType))
        {
            _logger.LogWarning("[UserQuotaGrain][UpdateSubscriptionAsync] Invalid plan type: {PlanType} for user {UserId}", planType, this.GetPrimaryKeyString());
            throw new ArgumentException($"Invalid plan type: {planType}", nameof(planType));
        }
        
        State.Subscription.PlanType = parsedPlanType;
        State.Subscription.IsActive = true;
        State.Subscription.StartDate = DateTime.UtcNow;
        State.Subscription.EndDate = endDate;
        State.Subscription.Status = PaymentStatus.Completed;
        
        await WriteStateAsync();
        
        _logger.LogInformation("[UserQuotaGrain][UpdateSubscriptionAsync] Updated subscription for user {UserId}: Plan={PlanType}, EndDate={EndDate}", 
            this.GetPrimaryKeyString(), planType, endDate);
    }

    public async Task UpdateSubscriptionAsync(SubscriptionInfoDto subscriptionInfoDto)
    {
        _logger.LogInformation("[UserQuotaGrain][UpdateSubscriptionAsync] Updated subscription for user {UserId}: Data={PlanType}", 
            this.GetPrimaryKeyString(), JsonConvert.SerializeObject(subscriptionInfoDto));
        State.Subscription.PlanType = subscriptionInfoDto.PlanType;
        State.Subscription.IsActive = subscriptionInfoDto.IsActive;
        State.Subscription.StartDate = subscriptionInfoDto.StartDate;
        State.Subscription.EndDate = subscriptionInfoDto.EndDate;
        State.Subscription.Status = subscriptionInfoDto.Status;
        State.Subscription.SubscriptionIds = subscriptionInfoDto.SubscriptionIds;
        State.Subscription.InvoiceIds = subscriptionInfoDto.InvoiceIds;
        await WriteStateAsync();
    }

    public async Task CancelSubscriptionAsync()
    {
        if (!State.Subscription.IsActive)
        {
            _logger.LogInformation("[UserQuotaGrain][CancelSubscriptionAsync] User {UserId} has no active subscription to cancel.", this.GetPrimaryKeyString());
            return;
        }
        
        State.Subscription.IsActive = false;
        State.Subscription.Status = PaymentStatus.Cancelled;
        
        await WriteStateAsync();
        
        _logger.LogInformation("[UserQuotaGrain][CancelSubscriptionAsync] Cancelled subscription for user {UserId}", this.GetPrimaryKeyString());
    }

    public async Task<ExecuteActionResultDto> IsActionAllowedAsync(string actionType = "conversation")
    {
        var now = DateTime.UtcNow;
        
        var isSubscribed = await IsSubscribedAsync();
        
        if (!isSubscribed)
        {
            
            var requiredCredits = _creditsOptions.CurrentValue.CreditsPerConversation;
            var credits = (await GetCreditsAsync()).Credits;
            var isAllowed = credits >= requiredCredits;
            _logger.LogDebug("[UserQuotaGrain][IsActionAllowedAsync] Action {ActionType} {IsAllowed} for user {UserId}. Credits: {Credits}, Required: {Required}", 
                actionType, isAllowed ? "allowed" : "denied", this.GetPrimaryKeyString(), State.Credits, requiredCredits);
            if (!isAllowed)
            {
                return new ExecuteActionResultDto
                {
                    Code = ExecuteActionStatus.InsufficientCredits,
                    Message = "You've run out of credits."
                };
            }
        }
        
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
            _logger.LogDebug("[UserQuotaGrain][IsActionAllowedAsync] Created new rate limit for user {UserId}, action {ActionType}, max tokens {MaxTokens}", 
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
                
                _logger.LogDebug("[UserQuotaGrain][IsActionAllowedAsync] Refreshed tokens for user {UserId}, action {ActionType}, added {TokensAdded}, current {CurrentTokens}", 
                    this.GetPrimaryKeyString(), actionType, tokensToAdd, rateLimitInfo.Count);
            }
        }
        
        if (rateLimitInfo.Count <= 0)
        {
            _logger.LogWarning("[UserQuotaGrain][IsActionAllowedAsync] Rate limit exceeded for user {UserId}, action {ActionType}", 
                this.GetPrimaryKeyString(), actionType);
            return new ExecuteActionResultDto
            {
                Code = 20002,
                Message = $"Message limit reached ({maxTokens} in {timeWindow / 3600} hours). Please try again later."
            };
        }
        
        return new ExecuteActionResultDto
        {
            Success = true
        };
    }

    public async Task<ExecuteActionResultDto> ExecuteActionAsync(string sessionId, string chatManagerGuid,
        string actionType = "conversation")
    {
        var now = DateTime.UtcNow;
        var isSubscribed = await IsSubscribedAsync();
        var maxTokens = isSubscribed 
            ? _rateLimiterOptions.CurrentValue.SubscribedUserMaxRequests 
            : _rateLimiterOptions.CurrentValue.UserMaxRequests;
        var timeWindow = isSubscribed 
            ? _rateLimiterOptions.CurrentValue.SubscribedUserTimeWindowSeconds 
            : _rateLimiterOptions.CurrentValue.UserTimeWindowSeconds;
        _logger.LogDebug("[UserQuotaGrain][ExecuteActionAsync] sessionId={SessionId} chatManagerGuid={ChatManagerGuid} config: maxTokens={MaxTokens}, timeWindow={TimeWindow}, isSubscribed={IsSubscribed}, now(UTC)={Now}", sessionId, chatManagerGuid, maxTokens, timeWindow, isSubscribed, now);
        if (!State.RateLimits.TryGetValue(actionType, out var rateLimitInfo))
        {
            rateLimitInfo = new RateLimitInfo { Count = maxTokens, LastTime = now };
            State.RateLimits[actionType] = rateLimitInfo;
            _logger.LogDebug("[UserQuotaGrain][ExecuteActionAsync] sessionId={SessionId} chatManagerGuid={ChatManagerGuid} INIT RateLimitInfo: count={Count}, lastTime(UTC)={LastTime}", sessionId, chatManagerGuid, rateLimitInfo.Count, rateLimitInfo.LastTime);
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
                _logger.LogDebug("[UserQuotaGrain][ExecuteActionAsync] sessionId={SessionId} chatManagerGuid={ChatManagerGuid} REFILL: tokensToAdd={TokensToAdd}, newCount={Count}, now(UTC)={Now}", sessionId, chatManagerGuid, tokensToAdd, rateLimitInfo.Count, now);
            }
        }
        // Step 1: Record the initial value
        int oldValue = State.RateLimits[actionType].Count;
        // Step 2: Check if can decrement
        if (oldValue <= 0)
        {
            _logger.LogWarning("[UserQuotaGrain][ExecuteActionAsync] sessionId={SessionId} chatManagerGuid={ChatManagerGuid} RATE LIMITED (oldValue): count={Count}, now(UTC)={Now}", sessionId, chatManagerGuid, oldValue, now);
            return new ExecuteActionResultDto
            {
                Code = ExecuteActionStatus.RateLimitExceeded,
                Message = $"Message limit reached ({maxTokens} in {timeWindow / 3600} hours). Please try again later."
            };
        }
        // Step 3: Get latest value and decrement
        await ReadStateAsync();
        int latestValue = State.RateLimits[actionType].Count;
        if (latestValue <= 0)
        {
            _logger.LogWarning("[UserQuotaGrain][ExecuteActionAsync] sessionId={SessionId} chatManagerGuid={ChatManagerGuid} RATE LIMITED (latestValue): count={Count}, now(UTC)={Now}", sessionId, chatManagerGuid, latestValue, now);
            return new ExecuteActionResultDto
            {
                Code = ExecuteActionStatus.RateLimitExceeded,
                Message = $"Message limit reached ({maxTokens} in {timeWindow / 3600} hours). Please try again later."
            };
        }
        State.RateLimits[actionType].Count = latestValue - 1;
        await WriteStateAsync();
        _logger.LogDebug("[UserQuotaGrain][ExecuteActionAsync] sessionId={SessionId} chatManagerGuid={ChatManagerGuid} AFTER first decrement: count={Count}, now(UTC)={Now}", sessionId, chatManagerGuid, State.RateLimits[actionType].Count, now);
        // Step 4: Business logic (credits, etc.)
        if (!isSubscribed)
        {
            var requiredCredits = _creditsOptions.CurrentValue.CreditsPerConversation;
            var credits = (await GetCreditsAsync()).Credits;
            var isAllowed = credits >= requiredCredits;
            _logger.LogDebug("[UserQuotaGrain][ExecuteActionAsync] sessionId={SessionId} chatManagerGuid={ChatManagerGuid} CREDITS: allowed={IsAllowed}, credits={Credits}, required={RequiredCredits}, now(UTC)={Now}", sessionId, chatManagerGuid, isAllowed, credits, requiredCredits, now);
            if (!isAllowed)
            {
                return new ExecuteActionResultDto
                {
                    Code = ExecuteActionStatus.InsufficientCredits,
                    Message = "You've run out of credits."
                };
            }
            State.Credits -= requiredCredits;
        }
        // Step 5: Final check and fallback decrement
        await ReadStateAsync();
        int finalValue = State.RateLimits[actionType].Count;
        if (finalValue >= oldValue)
        {
            State.RateLimits[actionType].Count = finalValue - 1;
            await WriteStateAsync();
            _logger.LogDebug("[UserQuotaGrain][ExecuteActionAsync] sessionId={SessionId} chatManagerGuid={ChatManagerGuid} AFTER fallback decrement: count={Count}, now(UTC)={Now}", sessionId, chatManagerGuid, State.RateLimits[actionType].Count, now);
        }
        return new ExecuteActionResultDto
        {
            Success = true
        };
    }
}