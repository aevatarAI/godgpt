using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Helpers;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.UserQuota.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Aevatar.Core.Placement;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Aevatar.Application.Grains.UserQuota;

public interface IUserQuotaGAgent : IGAgent
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
        ActionType actionType = ActionType.Conversation);
    Task<ExecuteActionResultDto> ExecuteVoiceActionAsync(string sessionId, string chatManagerGuid);

    Task<ExecuteActionResultDto> CanUploadImageAsync();

    Task ResetRateLimitsAsync(string actionType = "conversation");

    Task ClearAllAsync();

    // New method to support App Store subscriptions
    Task UpdateQuotaAsync(string productId, DateTime expiresDate);
    Task ResetQuotaAsync();
    Task<GrainResultDto<int>> UpdateCreditsAsync(string operatorUserId, int creditsChange);
    Task<GrainResultDto<List<SubscriptionInfoDto>>> UpdateSubscriptionAsync(string operatorUserId, PlanType planType,
        bool ultimate = false);
    Task AddCreditsAsync(int credits);
    Task<bool> RedeemInitialRewardAsync(string userId, DateTime dateTime);
    Task<UserQuotaGAgentState> GetUserQuotaStateAsync();
}

[GAgent(nameof(UserQuotaGAgent))]
[SiloNamePatternPlacement("User")]

public class UserQuotaGAgent : GAgentBase<UserQuotaGAgentState, UserQuotaLogEvent>, IUserQuotaGAgent
{
    private readonly ILogger<UserQuotaGAgent> _logger;
    private readonly IOptionsMonitor<CreditsOptions> _creditsOptions;
    private readonly IOptionsMonitor<RateLimitOptions> _rateLimiterOptions;

    public UserQuotaGAgent(ILogger<UserQuotaGAgent> logger, IOptionsMonitor<CreditsOptions> creditsOptions,
        IOptionsMonitor<RateLimitOptions> rateLimiterOptions)
    {
        _logger = logger;
        _creditsOptions = creditsOptions;
        _rateLimiterOptions = rateLimiterOptions;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("User Quota Management GAgent");
    }

    public async Task<bool> InitializeCreditsAsync()
    {
        if (State.HasInitialCredits)
        {
            return true;
        }

        var initialCredits = _creditsOptions.CurrentValue.InitialCreditsAmount;

        RaiseEvent(new InitializeCreditsLogEvent
        {
            InitialCredits = initialCredits
        });
        await ConfirmEvents();

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
        RaiseEvent(new SetShownCreditsToastLogEvent
        {
            HasShownInitialCreditsToast = hasShownInitialCreditsToast
        });
        await ConfirmEvents();
    }

    #region Legacy Compatibility Methods

    public async Task<bool> IsSubscribedAsync(bool ultimate = false)
    {
        var subscriptionInfo = (ultimate ? State.UltimateSubscription : State.Subscription) ?? new SubscriptionInfo();

        var now = DateTime.UtcNow;
        var isSubscribed = subscriptionInfo.IsActive &&
                           subscriptionInfo.StartDate <= now &&
                           subscriptionInfo.EndDate > now;

        if (subscriptionInfo.IsActive && subscriptionInfo.EndDate <= now)
        {
            _logger.LogDebug(
                "[UserQuotaGrain][IsSubscribedAsync] Subscription for user {UserId} expired. Start: {StartDate}, End: {EndDate}, Now: {Now}, Ultimate: {Ultimate}",
                this.GetPrimaryKey().ToString(), subscriptionInfo.StartDate, subscriptionInfo.EndDate, now, ultimate);

            var subscriptionDto = new SubscriptionInfoDto
            {
                IsActive = false,
                PlanType = subscriptionInfo.PlanType,
                Status = subscriptionInfo.Status,
                StartDate = subscriptionInfo.StartDate,
                EndDate = subscriptionInfo.EndDate,
                SubscriptionIds = subscriptionInfo.SubscriptionIds,
                InvoiceIds = subscriptionInfo.InvoiceIds
            };

            RaiseEvent(new UpdateSubscriptionLogEvent
            {
                SubscriptionInfo = subscriptionDto,
                IsUltimate = ultimate
            });
            await ConfirmEvents();

            if (State.RateLimits.ContainsKey("conversation"))
            {
                RaiseEvent(new ClearRateLimitLogEvent
                {
                    ActionType = "conversation"
                });
                await ConfirmEvents();
            }
        }

        _logger.LogDebug("[UserQuotaGrain][IsSubscribedAsync] User {UserId} subscription status: {IsSubscribed}",
            this.GetPrimaryKey().ToString(), isSubscribed);

        return isSubscribed;
    }

    public async Task ResetRateLimitsAsync(string actionType = "conversation")
    {
        RaiseEvent(new ClearRateLimitLogEvent
        {
            ActionType = actionType
        });
        await ConfirmEvents();
    }

    public async Task ClearAllAsync()
    {
        _logger.LogInformation("IUserQuotaGrain ClearAllAsync before GrainId={A} CanReceiveInviteReward={B}",
            this.GrainContext.GrainId, State.CanReceiveInviteReward);

        RaiseEvent(new ClearAllLogEvent
        {
            CanReceiveInviteReward = State.CanReceiveInviteReward
        });

        _logger.LogInformation("IUserQuotaGrain ClearAllAsync before GrainId={A} CanReceiveInviteReward={B}",
            this.GrainContext.GrainId, State.CanReceiveInviteReward);
        await ConfirmEvents();
    }

    public async Task<SubscriptionInfoDto> GetSubscriptionAsync(bool ultimate = false)
    {
        _logger.LogDebug(
            "[UserQuotaGrain][GetSubscriptionAsync] Getting subscription info for user {UserId}, ultimate={Ultimate}",
            this.GetPrimaryKey().ToString(), ultimate);
        var subscriptionInfo = ultimate ? State.UltimateSubscription : State.Subscription;

        if (subscriptionInfo == null)
        {
            RaiseEvent(new UpdateSubscriptionLogEvent
            {
                SubscriptionInfo = new SubscriptionInfoDto(),
                IsUltimate = ultimate
            });
            await ConfirmEvents();

            subscriptionInfo = ultimate ? State.UltimateSubscription : State.Subscription;
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

        RaiseEvent(new UpdateSubscriptionLogEvent
        {
            SubscriptionInfo = subscriptionInfoDto,
            IsUltimate = ultimate
        });
        await ConfirmEvents();
    }

    public async Task CancelSubscriptionAsync()
    {
        var premiumSubscription = State.Subscription;
        if (premiumSubscription != null && premiumSubscription.IsActive)
        {
            _logger.LogInformation("[UserQuotaGrain][CancelSubscriptionAsync] cancel premium subscription {0}",
                this.GetPrimaryKey().ToString());

            RaiseEvent(new CancelSubscriptionLogEvent { IsUltimate = false });
            await ConfirmEvents();
        }

        var ultimateSubscription = State.UltimateSubscription;
        if (ultimateSubscription!= null && ultimateSubscription.IsActive)
        {
            _logger.LogInformation("[UserQuotaGrain][CancelSubscriptionAsync] cancel ultimate subscription {0}",
                this.GetPrimaryKey().ToString());

            RaiseEvent(new CancelSubscriptionLogEvent { IsUltimate = true });
            await ConfirmEvents();
        }
    }

    #endregion

    #region Rate Limiting with Ultimate Support

    public async Task<ExecuteActionResultDto> ExecuteActionAsync(string sessionId, string chatManagerGuid,
        ActionType actionType = ActionType.Conversation)
    {
        if (actionType == ActionType.ImageConversation)
        {
            // For non-subscribed users, check daily limit
            var today = DateTime.UtcNow.Date;
            var dailyInfo = State.DailyImageConversation;
            
            // Check if it's a new day, reset count if so
            if (dailyInfo.LastConversationTime.Date != today)
            {
                dailyInfo.LastConversationTime = DateTime.UtcNow;
                dailyInfo.Count = 1;
            }
            else
            {
                // Increment daily count and update last conversation time
                dailyInfo.Count++;
                dailyInfo.LastConversationTime = DateTime.UtcNow;
            }
            
            // Check if user is subscribed (subscribers have no daily limit)
            if (!await IsSubscribedAsync(true) && !await IsSubscribedAsync(false) && dailyInfo.Count > 1)
            {
                _logger.LogDebug(
                    "[UserQuotaGAgent][ExecuteActionAsync] userId={chatManagerGuid} sessionId={SessionId} Daily image conversation limit exceeded for non-subscriber. Count={Count}",
                    chatManagerGuid, sessionId, dailyInfo.Count);
                    
                return new ExecuteActionResultDto
                {
                    Code = ExecuteActionStatus.RateLimitExceeded,
                    Message = "Daily upload limit reached. Upgrade to premium to continue."
                };
            }

            RaiseEvent(new UpdateDailyImageConversationLogEvent
            {
                DailyImageConversation = dailyInfo
            });
            //await ConfirmEvents();

            _logger.LogDebug(
                "[UserQuotaGAgent][ExecuteActionAsync] userId={chatManagerGuid} sessionId={SessionId} Image conversation allowed. New count={Count}",
                chatManagerGuid, sessionId, dailyInfo.Count);
            
            return await ExecuteStandardActionAsync(sessionId, chatManagerGuid, ActionType.Conversation);
        }
        // Apply standard execution logic with rate limiting and credits
        return await ExecuteStandardActionAsync(sessionId, chatManagerGuid, actionType);
    }
    public async Task<ExecuteActionResultDto> ExecuteVoiceActionAsync(string sessionId, string chatManagerGuid)
    {
        // Apply voice-specific execution logic with voice rate limiting and credits
        return await ExecuteStandardActionAsync(sessionId, chatManagerGuid, ActionType.VoiceConversation);
    }

    public async Task<ExecuteActionResultDto> CanUploadImageAsync()
    {
        // Check if user is subscribed (subscribers have no daily limit)
        if (await IsSubscribedAsync(true) || await IsSubscribedAsync(false))
        {
            return new ExecuteActionResultDto
            {
                Success = true
            };
        }

        // For non-subscribed users, check daily limit
        var today = DateTime.UtcNow.Date;
        var dailyInfo = State.DailyImageConversation;

        // Check if it's a new day (if so, user can upload)
        if (dailyInfo.LastConversationTime.Date != today)
        {
            return new ExecuteActionResultDto
            {
                Success = true
            };
        }

        // Check if daily limit exceeded (non-subscribers can only use once per day)
        if (dailyInfo.Count >= 1)
        {
            _logger.LogDebug(
                "[UserQuotaGAgent][CanUploadImageAsync] UserId={UserId} Daily image upload limit exceeded for non-subscriber. Count={Count}",
                this.GetPrimaryKeyString(), dailyInfo.Count);
                
            return new ExecuteActionResultDto
            {
                Success = false,
                Code = ExecuteActionStatus.RateLimitExceeded,
                Message = "Daily upload limit reached. Upgrade to premium to continue."
            };
        }

        // User can still upload image today
        return new ExecuteActionResultDto
        {
            Success = true
        };
    }

    private async Task<ExecuteActionResultDto> ExecuteStandardActionAsync(string sessionId, string chatManagerGuid,
        ActionType actionTypeEnum)
    {
        var now = DateTime.UtcNow;
        var isVoiceMessage = actionTypeEnum == ActionType.VoiceConversation;
        var actionType = actionTypeEnum.ToString().ToLowerInvariant();

        // Ultimate users have unlimited access
        if (await IsSubscribedAsync(true))
        {
            return new ExecuteActionResultDto
            {
                Success = true
            };
        }

        var isSubscribed = await IsSubscribedAsync(false);
        var maxTokens = isSubscribed
            ? (isVoiceMessage ? _rateLimiterOptions.CurrentValue.VoiceSubscribedUserMaxRequests : _rateLimiterOptions.CurrentValue.SubscribedUserMaxRequests)
            : (isVoiceMessage ? _rateLimiterOptions.CurrentValue.VoiceUserMaxRequests : _rateLimiterOptions.CurrentValue.UserMaxRequests);
        var timeWindow = isSubscribed
            ? (isVoiceMessage ? _rateLimiterOptions.CurrentValue.VoiceSubscribedUserTimeWindowSeconds : _rateLimiterOptions.CurrentValue.SubscribedUserTimeWindowSeconds)
            : (isVoiceMessage ? _rateLimiterOptions.CurrentValue.VoiceUserTimeWindowSeconds : _rateLimiterOptions.CurrentValue.UserTimeWindowSeconds);

        _logger.LogDebug(
            "[UserQuotaGrain][ExecuteStandardActionAsync] {MessageType} sessionId={SessionId} chatManagerGuid={ChatManagerGuid} config: maxTokens={MaxTokens}, timeWindow={TimeWindow}, isSubscribed={IsSubscribed}, now(UTC)={Now}",
            actionType, sessionId, chatManagerGuid, maxTokens, timeWindow, isSubscribed, now);

        // Initialize or update rate limit info
        if (!State.RateLimits.TryGetValue(actionType, out var rateLimitInfo))
        {
            rateLimitInfo = new RateLimitInfo { Count = maxTokens, LastTime = now };
            RaiseEvent(new UpdateRateLimitLogEvent
            {
                ActionType = actionType,
                RateLimitInfo = rateLimitInfo
            });
            //await ConfirmEvents();

            _logger.LogDebug(
                "[UserQuotaGrain][ExecuteStandardActionAsync] {MessageType} sessionId={SessionId} chatManagerGuid={ChatManagerGuid} INIT RateLimitInfo: count={Count}, lastTime(UTC)={LastTime}",
                actionType, sessionId, chatManagerGuid, rateLimitInfo.Count, rateLimitInfo.LastTime);
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

                RaiseEvent(new UpdateRateLimitLogEvent
                {
                    ActionType = actionType,
                    RateLimitInfo = rateLimitInfo
                });
               // await ConfirmEvents();

                _logger.LogDebug(
                    "[UserQuotaGrain][ExecuteStandardActionAsync] {MessageType} sessionId={SessionId} chatManagerGuid={ChatManagerGuid} REFILL: tokensToAdd={TokensToAdd}, newCount={Count}, now(UTC)={Now}",
                    actionType, sessionId, chatManagerGuid, tokensToAdd, rateLimitInfo.Count, now);
            }
        }

        // Check credits for non-subscribers
        if (!isSubscribed)
        {
            var requiredCredits = _creditsOptions.CurrentValue.CreditsPerConversation;
            var credits = (await GetCreditsAsync()).Credits;
            var isAllowed = credits >= requiredCredits;

            _logger.LogDebug(
                "[UserQuotaGrain][ExecuteStandardActionAsync] {MessageType} sessionId={SessionId} chatManagerGuid={ChatManagerGuid} CREDITS: allowed={IsAllowed}, credits={Credits}, required={RequiredCredits}, now(UTC)={Now}",
                actionType, sessionId, chatManagerGuid, isAllowed, credits, requiredCredits, now);

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
                "[UserQuotaGrain][ExecuteStandardActionAsync] {MessageType} sessionId={SessionId} chatManagerGuid={ChatManagerGuid} RATE LIMITED: count={Count}, now(UTC)={Now}",
                actionType, sessionId, chatManagerGuid, oldValue, now);
            return new ExecuteActionResultDto
            {
                Code = ExecuteActionStatus.RateLimitExceeded,
                Message = isVoiceMessage ? "Voice message limit reached. Please try again later." : "Message limit reached. Please try again later."
            };
        }

        // Execute action - deduct credits and tokens
        if (!isSubscribed)
        {
            RaiseEvent(new UpdateCreditsLogEvent
            {
                NewCredits = State.Credits - _creditsOptions.CurrentValue.CreditsPerConversation
            });
           // await ConfirmEvents();
        }

        var updatedRateLimitInfo = State.RateLimits[actionType];
        updatedRateLimitInfo.Count--;
        RaiseEvent(new UpdateRateLimitLogEvent
        {
            ActionType = actionType,
            RateLimitInfo = updatedRateLimitInfo
        });
       // await ConfirmEvents();

        _logger.LogDebug(
            "[UserQuotaGrain][ExecuteStandardActionAsync] {MessageType} sessionId={SessionId} chatManagerGuid={ChatManagerGuid} AFTER decrement: count={Count}, now(UTC)={Now}",
            actionType, sessionId, chatManagerGuid, State.RateLimits[actionType].Count, now);

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

        var subscriptionDto = new SubscriptionInfoDto
        {
            PlanType = planType,
            IsActive = true,
            StartDate = DateTime.UtcNow,
            EndDate = expiresDate,
            Status = PaymentStatus.Completed,
            SubscriptionIds = State.Subscription.SubscriptionIds,
            InvoiceIds = State.Subscription.InvoiceIds
        };

        RaiseEvent(new UpdateSubscriptionLogEvent
        {
            SubscriptionInfo = subscriptionDto,
            IsUltimate = false
        });
        await ConfirmEvents();

        // Reset rate limits
        await ResetRateLimitsAsync();
    }

    public async Task ResetQuotaAsync()
    {
        _logger.LogInformation("[UserQuotaGrain][ResetQuotaAsync] Resetting quota for user {UserId}",
            this.GetPrimaryKeyString());

        var subscriptionDto = new SubscriptionInfoDto
        {
            IsActive = false,
            PlanType = State.Subscription.PlanType,
            Status = PaymentStatus.None,
            StartDate = State.Subscription.StartDate,
            EndDate = State.Subscription.EndDate,
            SubscriptionIds = State.Subscription.SubscriptionIds,
            InvoiceIds = State.Subscription.InvoiceIds
        };

        RaiseEvent(new UpdateSubscriptionLogEvent
        {
            SubscriptionInfo = subscriptionDto,
            IsUltimate = false
        });
        await ConfirmEvents();

        // Reset rate limits
        await ResetRateLimitsAsync();
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
                this.GetPrimaryKey().ToString(), operatorUserId);

            return new GrainResultDto<int>
            {
                Success = false,
                Message = "Unauthorized: User does not have permission to update credits",
                Data = State.Credits
            };
        }

        var oldCredits = State.Credits;
        var newCredits = State.Credits + creditsChange;
        if (newCredits < 0)
        {
            newCredits = 0;
        }

        RaiseEvent(new UpdateCreditsLogEvent
        {
            NewCredits = newCredits
        });
        await ConfirmEvents();

        _logger.LogInformation(
            "[UserQuotaGrain][UpdateCreditsAsync] Credits updated for user {UserId} by operator {OperatorId}: {OldCredits} -> {NewCredits} (change: {Change})",
            this.GetPrimaryKey().ToString(), operatorUserId, oldCredits, State.Credits, creditsChange);

        return new GrainResultDto<int>
        {
            Success = true,
            Message = $"Credits successfully updated by {creditsChange}",
            Data = State.Credits
        };
    }

    public async Task<GrainResultDto<List<SubscriptionInfoDto>>> UpdateSubscriptionAsync(string operatorUserId, PlanType planType, bool ultimate = false)
    {
        if (!IsUserAuthorizedToUpdateCredits(operatorUserId))
        {
            _logger.LogWarning(
                "[UserQuotaGrain][UpdateSubscriptionAsync] Unauthorized attempt to update subscription for user {UserId} by operator {OperatorId}",
                this.GetPrimaryKey().ToString(), operatorUserId);

            return new GrainResultDto<List<SubscriptionInfoDto>>
            {
                Success = false,
                Message = "Unauthorized: User does not have permission to update subscription",
                Data = new List<SubscriptionInfoDto>()
            };
        }

        var oldSubscriptionInfoDto = await GetSubscriptionAsync(ultimate);
        if (await IsSubscribedAsync(ultimate))
        {
            var subscriptionInfoDto = await GetSubscriptionAsync(ultimate);
            if (SubscriptionHelper.IsUpgradeOrSameLevel(subscriptionInfoDto.PlanType, planType))
            {
                subscriptionInfoDto.PlanType = planType;
            }
            subscriptionInfoDto.EndDate =
                SubscriptionHelper.GetSubscriptionEndDate(planType, subscriptionInfoDto.EndDate);

            await UpdateSubscriptionAsync(subscriptionInfoDto, ultimate);
            _logger.LogWarning("[UserQuotaGrain][UpdateSubscriptionAsync] true, Update subscription for user {UserId} by operator {OperatorId}", 
                this.GetPrimaryKey().ToString(), operatorUserId);
        }
        else
        {
            var startDate = DateTime.UtcNow;
            var subscriptionInfoDto = new SubscriptionInfoDto
            {
                IsActive = true,
                PlanType = planType,
                Status = PaymentStatus.Completed,
                StartDate = startDate,
                EndDate = SubscriptionHelper.GetSubscriptionEndDate(planType, startDate),
                SubscriptionIds = null,
                InvoiceIds = null
            };
            await UpdateSubscriptionAsync(subscriptionInfoDto, ultimate);
            
            _logger.LogWarning("[UserQuotaGrain][UpdateSubscriptionAsync] false, Update subscription for user {UserId} by operator {OperatorId}", 
                this.GetPrimaryKey().ToString(), operatorUserId);
        }
        
        if (ultimate && await IsSubscribedAsync(false))
        {
            var premiumSubscription = await GetSubscriptionAsync(false);
            premiumSubscription.StartDate =
                SubscriptionHelper.GetSubscriptionEndDate(planType, premiumSubscription.StartDate);
            premiumSubscription.EndDate =
                SubscriptionHelper.GetSubscriptionEndDate(planType, premiumSubscription.EndDate);
            await UpdateSubscriptionAsync(premiumSubscription, false);
            _logger.LogWarning("[UserQuotaGrain][UpdateSubscriptionAsync] premium, Update subscription for user {UserId} by operator {OperatorId}", 
                this.GetPrimaryKey().ToString(), operatorUserId);
            
        }
        await ConfirmEvents();

        var currentSubscriptionInfoDto = await GetSubscriptionAsync(ultimate);
        return new GrainResultDto<List<SubscriptionInfoDto>>
        {
            Data = new List<SubscriptionInfoDto>()
            {
                oldSubscriptionInfoDto, currentSubscriptionInfoDto
            }
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
        RaiseEvent(new UpdateCreditsLogEvent
        {
            NewCredits = oldCredits + credits
        });
        await ConfirmEvents();

        _logger.LogInformation(
            "[UserQuotaGrain][AddCreditsAsync] Credits updated: {OldCredits} -> {NewCredits} (added: {Added})",
            oldCredits, State.Credits, credits);
    }

    public async Task<bool> RedeemInitialRewardAsync(string userId, DateTime dateTime)
    {
        if (!State.CanReceiveInviteReward)
        {
            _logger.LogWarning($"User {userId} cannot receive invite reward, CanReceiveInviteReward is false");
            return false;
        }

        if ((DateTime.UtcNow - dateTime).TotalHours > 72)
        {
            _logger.LogWarning(
                $"User {userId} invite reward redemption window expired. now={DateTime.UtcNow} checkIime={dateTime}");
            RaiseEvent(new UpdateCanReceiveInviteRewardLogEvent { CanReceiveInviteReward = false });
            await ConfirmEvents();
            return false;
        }

        // This is where the business logic for granting the initial reward (e.g., a 7-day trial) would go.
        // For now, we just mark that the reward has been redeemed.
        if (await IsSubscribedAsync(false))
        {
            //TODO
            _logger.LogWarning($"User {userId} cannot receive invite,reward,IsSubscribedAsync is true.");
            RaiseEvent(new UpdateCanReceiveInviteRewardLogEvent { CanReceiveInviteReward = false });
            await ConfirmEvents();
            return false;
        }

        _logger.LogWarning($"User {userId} receive invite,reward begin");
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

        RaiseEvent(new UpdateCanReceiveInviteRewardLogEvent { CanReceiveInviteReward = false });
        await ConfirmEvents();
        _logger.LogWarning($"User {userId} receive invite,reward end");
        return true;
    }

    protected sealed override void GAgentTransitionState(UserQuotaGAgentState state,
        StateLogEventBase<UserQuotaLogEvent> @event)
    {
        switch (@event)
        {
            case MarkInitializedLogEvent:
                state.UserId = this.GetPrimaryKey().ToString();
                state.IsInitializedFromGrain = true;
                break;

            case InitializeFromGrainLogEvent initializeFromGrain:
                state.UserId = this.GetPrimaryKey().ToString();
                state.Credits = initializeFromGrain.Credits;
                state.HasInitialCredits = initializeFromGrain.HasInitialCredits;
                state.HasShownInitialCreditsToast = initializeFromGrain.HasShownInitialCreditsToast;
                state.Subscription = initializeFromGrain.Subscription;
                state.RateLimits = initializeFromGrain.RateLimits;
                state.UltimateSubscription = initializeFromGrain.UltimateSubscription;
                state.CreatedAt = initializeFromGrain.CreatedAt;
                state.CanReceiveInviteReward = initializeFromGrain.CanReceiveInviteReward;
                state.IsInitializedFromGrain = true;
                break;

            case InitializeCreditsLogEvent initializeCredits:
                state.Credits = initializeCredits.InitialCredits;
                state.HasInitialCredits = true;
                break;

            case SetShownCreditsToastLogEvent setShownCreditsToast:
                state.HasShownInitialCreditsToast = setShownCreditsToast.HasShownInitialCreditsToast;
                break;

            case UpdateRateLimitLogEvent updateRateLimit:
                state.RateLimits[updateRateLimit.ActionType] = updateRateLimit.RateLimitInfo;
                break;

            case ClearRateLimitLogEvent clearRateLimit:
                if (state.RateLimits.ContainsKey(clearRateLimit.ActionType))
                {
                    state.RateLimits.Remove(clearRateLimit.ActionType);
                }

                break;

            case UpdateSubscriptionLogEvent updateSubscription:
                var subscription = updateSubscription.IsUltimate ? state.UltimateSubscription : state.Subscription;
                if (subscription == null)
                {
                    subscription = new SubscriptionInfo();
                    if (updateSubscription.IsUltimate)
                    {
                        state.UltimateSubscription = subscription;
                    }
                    else
                    {
                        state.Subscription = subscription;
                    }
                }

                subscription.IsActive = updateSubscription.SubscriptionInfo.IsActive;
                subscription.PlanType = updateSubscription.SubscriptionInfo.PlanType;
                subscription.Status = updateSubscription.SubscriptionInfo.Status;
                subscription.StartDate = updateSubscription.SubscriptionInfo.StartDate;
                subscription.EndDate = updateSubscription.SubscriptionInfo.EndDate;
                subscription.SubscriptionIds = updateSubscription.SubscriptionInfo.SubscriptionIds;
                subscription.InvoiceIds = updateSubscription.SubscriptionInfo.InvoiceIds;
                break;

            case CancelSubscriptionLogEvent cancelSubscription:
                var sub = cancelSubscription.IsUltimate ? state.UltimateSubscription : state.Subscription;
                if (sub != null)
                {
                    sub.IsActive = false;
                    sub.PlanType = PlanType.None;
                    sub.StartDate = default;
                    sub.EndDate = default;
                    sub.Status = PaymentStatus.None;
                }

                break;

            case UpdateCreditsLogEvent updateCredits:
                state.Credits = updateCredits.NewCredits;
                break;

            case UpdateCanReceiveInviteRewardLogEvent updateCanReceiveInviteReward:
                state.CanReceiveInviteReward = updateCanReceiveInviteReward.CanReceiveInviteReward;
                break;
            
            case UpdateDailyImageConversationLogEvent updateDailyImageConversation:
                State.DailyImageConversation = updateDailyImageConversation.DailyImageConversation;
                break;
                
            case ClearAllLogEvent clearAll:
                var canReceiveInviteReward = state.CanReceiveInviteReward;
                state.Credits = 0;
                state.HasInitialCredits = false;
                state.HasShownInitialCreditsToast = false;
                state.Subscription = new SubscriptionInfo();
                state.RateLimits = new Dictionary<string, RateLimitInfo>();
                state.UltimateSubscription = new SubscriptionInfo();
                state.CreatedAt = default;
                state.CanReceiveInviteReward = canReceiveInviteReward;
                break;
        }
    }
    public Task<UserQuotaGAgentState> GetUserQuotaStateAsync()
    {
        return Task.FromResult(State);
    }
    protected override async Task OnGAgentActivateAsync(CancellationToken cancellationToken)
    {
        if (!State.IsInitializedFromGrain)
        {
            var userQuota =
                GrainFactory.GetGrain<IUserQuotaGrain>(CommonHelper.GetUserQuotaGAgentId(this.GetPrimaryKey()));
            var userQuotaState = await userQuota.GetUserQuotaStateAsync();
            if (userQuotaState != null)
            {
                _logger.LogInformation(
                    "[UserQuotaGAgent][OnGAgentActivateAsync] Initializing state from IUserQuotaGrain for user {UserId}",
                    this.GetPrimaryKey().ToString());

                RaiseEvent(new InitializeFromGrainLogEvent
                {
                    Credits = userQuotaState.Credits,
                    HasInitialCredits = userQuotaState.HasInitialCredits,
                    HasShownInitialCreditsToast = userQuotaState.HasShownInitialCreditsToast,
                    Subscription = userQuotaState.Subscription,
                    RateLimits = userQuotaState.RateLimits,
                    UltimateSubscription = userQuotaState.UltimateSubscription,
                    CreatedAt = userQuotaState.CreatedAt,
                    CanReceiveInviteReward = userQuotaState.CanReceiveInviteReward
                });
                await ConfirmEvents();

                _logger.LogDebug(
                    "[UserQuotaGAgent][OnGAgentActivateAsync] State initialized from IUserQuotaGrain for user {UserId}",
                    this.GetPrimaryKeyString());
            }
            else
            {
                _logger.LogDebug(
                    "[UserQuotaGAgent][OnGAgentActivateAsync] No state found in IUserQuotaGrain for user {UserId}, marking as initialized",
                    this.GetPrimaryKeyString());
                    
                RaiseEvent(new MarkInitializedLogEvent());
                await ConfirmEvents();
            }
        }
    }
}