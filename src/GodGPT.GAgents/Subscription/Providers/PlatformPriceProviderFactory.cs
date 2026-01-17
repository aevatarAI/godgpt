using System;
using System.Collections.Generic;
using System.Linq;
using Aevatar.Application.Grains.Common.Constants;

namespace Aevatar.Application.Grains.Subscription.Providers;

/// <summary>
/// Factory for getting platform-specific price providers.
/// Uses DI to collect all registered providers.
/// </summary>
public class PlatformPriceProviderFactory : IPlatformPriceProviderFactory
{
    private readonly Dictionary<PaymentPlatform, IPlatformPriceProvider> _providers;

    public PlatformPriceProviderFactory(IEnumerable<IPlatformPriceProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.Platform);
    }

    public IPlatformPriceProvider GetProvider(PaymentPlatform platform)
    {
        if (_providers.TryGetValue(platform, out var provider))
            return provider;

        throw new NotSupportedException($"No provider registered for platform: {platform}");
    }

    public bool HasProvider(PaymentPlatform platform)
    {
        return _providers.ContainsKey(platform);
    }
}
