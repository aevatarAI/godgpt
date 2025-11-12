using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Common.Service;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;

namespace Aevatar.Application.Grains.ChatManager.UserBilling;

public interface IUserBillingGrain : IGrainWithStringKey
{
    Task<UserBillingState> GetUserBillingGrainStateAsync();
}

public class UserBillingGrain : Grain<UserBillingState>, IUserBillingGrain
{
    private readonly ILogger<UserBillingGrain> _logger;
    private readonly IOptionsMonitor<StripeOptions> _stripeOptions;
    private readonly IOptionsMonitor<ApplePayOptions> _appleOptions;
    private readonly IOptionsMonitor<GooglePayOptions> _googlePayOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGooglePayService _googlePayService;
    
    private IStripeClient _client; 
    
    public UserBillingGrain(
        ILogger<UserBillingGrain> logger, 
        IOptionsMonitor<StripeOptions> stripeOptions,
        IOptionsMonitor<ApplePayOptions> appleOptions,
        IOptionsMonitor<GooglePayOptions> googlePayOptions,
        IHttpClientFactory httpClientFactory,
        IGooglePayService googlePayService)
    {
        _logger = logger;
        _stripeOptions = stripeOptions;
        _appleOptions = appleOptions;
        _googlePayOptions = googlePayOptions;
        _httpClientFactory = httpClientFactory;
        _googlePayService = googlePayService;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        StripeConfiguration.ApiKey = _stripeOptions.CurrentValue.SecretKey;
        _client ??= new StripeClient(_stripeOptions.CurrentValue.SecretKey);
        _logger.LogDebug("[UserBillingGrain][OnActivateAsync] Activating grain for user {UserId}",
            this.GetPrimaryKeyString());
        
        await ReadStateAsync();
        await base.OnActivateAsync(cancellationToken);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[UserBillingGrain][OnDeactivateAsync] Deactivating grain for user {UserId}. Reason: {Reason}",
            this.GetPrimaryKeyString(), reason);
        await WriteStateAsync();
        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    public Task<UserBillingState> GetUserBillingGrainStateAsync()
    {
        return Task.FromResult(State);
    }
}