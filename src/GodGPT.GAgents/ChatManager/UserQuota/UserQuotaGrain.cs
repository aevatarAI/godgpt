using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.Common.Helpers;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using ActionType = Aevatar.Application.Grains.Common.Constants.ActionType;

namespace Aevatar.Application.Grains.ChatManager.UserQuota;

using Orleans;

[Obsolete]
public interface IUserQuotaGrain : IGrainWithStringKey
{
    Task<UserQuotaState> GetUserQuotaStateAsync();
}

[Obsolete]
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

    public Task<UserQuotaState> GetUserQuotaStateAsync()
    {
        return Task.FromResult(State);
    }
}