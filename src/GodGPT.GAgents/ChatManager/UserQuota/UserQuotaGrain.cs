using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.Common.Helpers;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Aevatar.Application.Grains.ChatManager.UserQuota;

using Orleans;

public interface IUserQuotaGrain : IGrainWithStringKey
{
    Task<bool> InitializeCreditsAsync();
    Task<CreditsInfoDto> GetCreditsAsync();
    Task SetShownCreditsToastAsync(bool hasShownInitialCreditsToast);
    Task<bool> IsSubscribedAsync(bool ultimate = false);
    Task<SubscriptionInfoDto> GetSubscriptionAsync(bool ultimate = false);
    Task<SubscriptionInfoDto> GetAndSetSubscriptionAsync(bool ultimate = false);
    Task UpdateSubscriptionAsync(SubscriptionInfoDto subscriptionInfoDto, bool ultimate = false);
    Task CancelSubscriptionAsync();

    Task<ExecuteActionResultDto> ExecuteActionAsync(string sessionId, string chatManagerGuid,
        string actionType = "conversation");

    Task ResetRateLimitsAsync(string actionType = "conversation");

    Task ClearAllAsync();

    // New method to support App Store subscriptions
    Task UpdateQuotaAsync(string productId, DateTime expiresDate);
    Task ResetQuotaAsync();
    Task<GrainResultDto<int>> UpdateCreditsAsync(string operatorUserId, int creditsChange);
    Task AddCreditsAsync(int credits);
    Task<bool> RedeemInitialRewardAsync(string userId, DateTime dateTime);
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

        _logger.LogDebug("[UserQuotaGrain][InitializeCreditsAsync] User {UserId} received {Credits} initial credits.",
            this.GetPrimaryKeyString(), initialCredits);
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

    #region Legacy Compatibility Methods

    public async Task<bool> IsSubscribedAsync(bool ultimate = false)
    {
        var subscriptionInfo = ultimate ? State.UltimateSubscription : State.Subscription;
        if (subscriptionInfo == null)
        {
            subscriptionInfo = new SubscriptionInfo();
            if (ultimate)
            {
                State.UltimateSubscription = subscriptionInfo;
            }
            else
            {
                State.Subscription = subscriptionInfo;
            }
        }

        var now = DateTime.UtcNow;
        var isSubscribed = subscriptionInfo.IsActive &&
                           subscriptionInfo.StartDate <= now &&
                           subscriptionInfo.EndDate > now;

        if (subscriptionInfo.IsActive && subscriptionInfo.EndDate <= now)
        {
            _logger.LogDebug(
                "[UserQuotaGrain][IsSubscribedAsync] Subscription for user {UserId} expired. Start: {StartDate}, End: {EndDate}, Now: {Now}, Ultimate: {Ultimate}",
                this.GetPrimaryKeyString(), subscriptionInfo.StartDate, subscriptionInfo.EndDate, now, ultimate);

            if (State.RateLimits.ContainsKey("conversation"))
            {
                State.RateLimits.Remove("conversation");
            }

            subscriptionInfo.IsActive = false;

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

    public async Task<SubscriptionInfoDto> GetSubscriptionAsync(bool ultimate = false)
    {
        _logger.LogDebug(
            "[UserQuotaGrain][GetSubscriptionAsync] Getting subscription info for user {UserId}, ultimate={Ultimate}",
            this.GetPrimaryKeyString(), ultimate);
        var subscriptionInfo = ultimate ? State.UltimateSubscription : State.Subscription;

        if (subscriptionInfo == null)
        {
            subscriptionInfo = new SubscriptionInfo();
            if (ultimate)
            {
                State.UltimateSubscription = subscriptionInfo;
            }
            else
            {
                State.Subscription = subscriptionInfo;
            }

            await WriteStateAsync();
        }

        return new SubscriptionInfoDto
        {
            IsActive = subscriptionInfo.IsActive,
            PlanType = subscriptionInfo.PlanType,
            Status = subscriptionInfo.Status,
            StartDate = subscriptionInfo.StartDate,
            EndDate = subscriptionInfo.EndDate,
            SubscriptionIds = subscriptionInfo.SubscriptionIds,
            InvoiceIds = subscriptionInfo.InvoiceIds
        };
    }

    public async Task<SubscriptionInfoDto> GetAndSetSubscriptionAsync(bool ultimate = false)
    {
        await IsSubscribedAsync(ultimate);
        return await GetSubscriptionAsync(ultimate);
    }

    public async Task UpdateSubscriptionAsync(SubscriptionInfoDto subscriptionInfoDto, bool ultimate = false)
    {
        _logger.LogInformation(
            "[UserQuotaGrain][UpdateSubscriptionAsync] Updated subscription for user {UserId}: Data={PlanType}",
            this.GetPrimaryKeyString(), JsonConvert.SerializeObject(subscriptionInfoDto));
        var subscription = ultimate ? State.UltimateSubscription : State.Subscription;
        if (subscription == null)
        {
            subscription = new SubscriptionInfo();
            if (ultimate)
            {
                State.UltimateSubscription = subscription;
            }
            else
            {
                State.Subscription = subscription;
            }
        }

        subscription.PlanType = subscriptionInfoDto.PlanType;
        subscription.IsActive = subscriptionInfoDto.IsActive;
        subscription.StartDate = subscriptionInfoDto.StartDate;
        subscription.EndDate = subscriptionInfoDto.EndDate;
        subscription.Status = subscriptionInfoDto.Status;
        subscription.SubscriptionIds = subscriptionInfoDto.SubscriptionIds;
        subscription.InvoiceIds = subscriptionInfoDto.InvoiceIds;
        await WriteStateAsync();
    }

    public async Task CancelSubscriptionAsync()
    {
        var premiumSubscription = State.Subscription;
        if (premiumSubscription.IsActive)
        {
            _logger.LogInformation("[UserQuotaGrain][CancelSubscriptionAsync] cancel premium subscription {0}",
                this.GetPrimaryKeyString());

            premiumSubscription.IsActive = false;
            premiumSubscription.PlanType = PlanType.None;
            premiumSubscription.StartDate = default;
            premiumSubscription.EndDate = default;
            premiumSubscription.Status = PaymentStatus.None;
            await WriteStateAsync();
        }

        var ultimateSubscription = State.UltimateSubscription;
        if (ultimateSubscription.IsActive)
        {
            _logger.LogInformation("[UserQuotaGrain][CancelSubscriptionAsync] cancel ultimate subscription {0}",
                this.GetPrimaryKeyString());

            ultimateSubscription.IsActive = false;
            ultimateSubscription.PlanType = PlanType.None;
            ultimateSubscription.StartDate = default;
            ultimateSubscription.EndDate = default;
            ultimateSubscription.Status = PaymentStatus.None;
            await WriteStateAsync();
        }
    }

    #endregion

    #region Rate Limiting with Ultimate Support

    public async Task<ExecuteActionResultDto> ExecuteActionAsync(string sessionId, string chatManagerGuid,
        string actionType = "conversation")
    {
        // Apply standard execution logic with rate limiting and credits
        return await ExecuteStandardActionAsync(sessionId, chatManagerGuid, actionType);
    }

    private async Task<ExecuteActionResultDto> ExecuteStandardActionAsync(string sessionId, string chatManagerGuid,
        string actionType)
    {
        var now = DateTime.UtcNow;

        if (await IsSubscribedAsync(true))
        {
            return new ExecuteActionResultDto
            {
                Success = true
            };
        }

        var isSubscribed = await IsSubscribedAsync(false);
        var maxTokens = isSubscribed
            ? _rateLimiterOptions.CurrentValue.SubscribedUserMaxRequests
            : _rateLimiterOptions.CurrentValue.UserMaxRequests;
        var timeWindow = isSubscribed
            ? _rateLimiterOptions.CurrentValue.SubscribedUserTimeWindowSeconds
            : _rateLimiterOptions.CurrentValue.UserTimeWindowSeconds;

        _logger.LogDebug(
            "[UserQuotaGrain][ExecuteStandardActionAsync] sessionId={SessionId} chatManagerGuid={ChatManagerGuid} config: maxTokens={MaxTokens}, timeWindow={TimeWindow}, isSubscribed={IsSubscribed}, now(UTC)={Now}",
            sessionId, chatManagerGuid, maxTokens, timeWindow, isSubscribed, now);

        // Initialize or update rate limit info
        if (!State.RateLimits.TryGetValue(actionType, out var rateLimitInfo))
        {
            rateLimitInfo = new RateLimitInfo { Count = maxTokens, LastTime = now };
            State.RateLimits[actionType] = rateLimitInfo;
            _logger.LogDebug(
                "[UserQuotaGrain][ExecuteStandardActionAsync] sessionId={SessionId} chatManagerGuid={ChatManagerGuid} INIT RateLimitInfo: count={Count}, lastTime(UTC)={LastTime}",
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
                _logger.LogDebug(
                    "[UserQuotaGrain][ExecuteStandardActionAsync] sessionId={SessionId} chatManagerGuid={ChatManagerGuid} REFILL: tokensToAdd={TokensToAdd}, newCount={Count}, now(UTC)={Now}",
                    sessionId, chatManagerGuid, tokensToAdd, rateLimitInfo.Count, now);
            }
        }

        // Check credits for non-subscribers
        if (!isSubscribed)
        {
            var requiredCredits = _creditsOptions.CurrentValue.CreditsPerConversation;
            var credits = (await GetCreditsAsync()).Credits;
            var isAllowed = credits >= requiredCredits;

            _logger.LogDebug(
                "[UserQuotaGrain][ExecuteStandardActionAsync] sessionId={SessionId} chatManagerGuid={ChatManagerGuid} CREDITS: allowed={IsAllowed}, credits={Credits}, required={RequiredCredits}, now(UTC)={Now}",
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
            _logger.LogWarning(
                "[UserQuotaGrain][ExecuteStandardActionAsync] sessionId={SessionId} chatManagerGuid={ChatManagerGuid} RATE LIMITED (oldValue): count={Count}, now(UTC)={Now}",
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

        _logger.LogDebug(
            "[UserQuotaGrain][ExecuteStandardActionAsync] sessionId={SessionId} chatManagerGuid={ChatManagerGuid} AFTER decrement: count={Count}, now(UTC)={Now}",
            sessionId, chatManagerGuid, State.RateLimits[actionType].Count, now);

        return new ExecuteActionResultDto { Success = true };
    }

    #endregion

    public async Task UpdateQuotaAsync(string productId, DateTime expiresDate)
    {
        _logger.LogInformation(
            "[UserQuotaGrain][UpdateQuotaAsync] Updating quota for user {UserId} with product {ProductId}, expires on {ExpiresDate}",
            this.GetPrimaryKeyString(), productId, expiresDate);

        // Determine subscription type based on product ID
        PlanType planType = DeterminePlanTypeFromProductId(productId);

        // Update subscription information
        State.Subscription.PlanType = planType;
        State.Subscription.IsActive = true;
        State.Subscription.StartDate = DateTime.UtcNow;
        State.Subscription.EndDate = expiresDate;
        State.Subscription.Status = PaymentStatus.Completed;

        // Reset rate limits
        await ResetRateLimitsAsync();

        await WriteStateAsync();
    }

    public async Task ResetQuotaAsync()
    {
        _logger.LogInformation("[UserQuotaGrain][ResetQuotaAsync] Resetting quota for user {UserId}",
            this.GetPrimaryKeyString());

        // Reset subscription status
        State.Subscription.IsActive = false;
        State.Subscription.Status = PaymentStatus.None;

        // Reset rate limits
        await ResetRateLimitsAsync();

        await WriteStateAsync();
    }

    private PlanType DeterminePlanTypeFromProductId(string productId)
    {
        // Determine subscription type based on product ID prefix or naming conventions
        // Assume product ID contains information about monthly/yearly plan
        if (productId.Contains("monthly") || productId.Contains("month"))
        {
            return PlanType.Month;
        }
        else if (productId.Contains("yearly") || productId.Contains("year"))
        {
            return PlanType.Year;
        }
        else if (productId.Contains("daily") || productId.Contains("day"))
        {
            return PlanType.Day;
        }

        // Default to monthly plan
        return PlanType.Month;
    }

    public async Task<GrainResultDto<int>> UpdateCreditsAsync(string operatorUserId, int creditsChange)
    {
        if (!IsUserAuthorizedToUpdateCredits(operatorUserId))
        {
            _logger.LogWarning(
                "[UserQuotaGrain][UpdateCreditsAsync] Unauthorized attempt to update credits for user {UserId} by operator {OperatorId}",
                this.GetPrimaryKeyString(), operatorUserId);

            return new GrainResultDto<int>
            {
                Success = false,
                Message = "Unauthorized: User does not have permission to update credits",
                Data = State.Credits
            };
        }

        var oldCredits = State.Credits;
        State.Credits += creditsChange;

        if (State.Credits < 0)
        {
            State.Credits = 0;
        }

        _logger.LogInformation(
            "[UserQuotaGrain][UpdateCreditsAsync] Credits updated for user {UserId} by operator {OperatorId}: {OldCredits} -> {NewCredits} (change: {Change})",
            this.GetPrimaryKeyString(), operatorUserId, oldCredits, State.Credits, creditsChange);

        await WriteStateAsync();

        return new GrainResultDto<int>
        {
            Success = true,
            Message = $"Credits successfully updated by {creditsChange}",
            Data = State.Credits
        };
    }

    private bool IsUserAuthorizedToUpdateCredits(string operatorUserId)
    {
        var authorizedUsers = _creditsOptions.CurrentValue.OperatorUserId;
        return authorizedUsers.Contains(operatorUserId);
    }

    public async Task AddCreditsAsync(int credits)
    {
        await InitializeCreditsAsync();
        
        if (credits < 0)
        {
            _logger.LogWarning("[UserQuotaGrain][AddCreditsAsync] Attempt to add negative credits: {Credits}", credits);
            return;
        }
        
        var oldCredits = State.Credits;
        State.Credits += credits;
        
        _logger.LogInformation(
            "[UserQuotaGrain][AddCreditsAsync] Credits updated: {OldCredits} -> {NewCredits} (added: {Added})",
            oldCredits, State.Credits, credits);
        
        await WriteStateAsync();
    }

    public async Task<bool> RedeemInitialRewardAsync(string userId, DateTime dateTime)
    {
        if (!State.CanReceiveInviteReward)
        {
            _logger.LogWarning("User {UserId} cannot receive invite reward, CanReceiveInviteReward is false",
                userId);
            return false;
        }

        //if ((DateTime.UtcNow - dateTime).TotalHours > 72)
        if ((DateTime.UtcNow - dateTime).TotalHours > 6)
        {
            _logger.LogWarning("User {UserId} invite reward redemption window expired. now={A} checkIime={B}", userId,
                DateTime.UtcNow, dateTime);
            State.CanReceiveInviteReward = false;
            await WriteStateAsync();
            return false;
        }

        // This is where the business logic for granting the initial reward (e.g., a 7-day trial) would go.
        // For now, we just mark that the reward has been redeemed.
        if (await IsSubscribedAsync(false))
        {
            //TODO
            _logger.LogWarning("User {UserId} cannot receive invite,reward,IsSubscribedAsync is true.", userId);
            State.CanReceiveInviteReward = false;
            await WriteStateAsync();
            return false;
        }

        var startDate = DateTime.UtcNow;
        await UpdateSubscriptionAsync(new SubscriptionInfoDto
        {
            IsActive = true,
            PlanType = PlanType.Week,
            Status = PaymentStatus.Completed,
            StartDate = DateTime.UtcNow,
            EndDate = SubscriptionHelper.GetSubscriptionEndDate(PlanType.Week, startDate),
            SubscriptionIds = null,
            InvoiceIds = null
        }, false);
        
        return true;
    }
}