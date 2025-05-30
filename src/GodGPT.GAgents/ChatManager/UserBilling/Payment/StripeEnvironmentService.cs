using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;

namespace Aevatar.Application.Grains.ChatManager.UserBilling.Payment;

/// <summary>
/// Implementation of the Stripe environment service
/// </summary>
public class StripeEnvironmentService : IStripeEnvironmentService
{
    private readonly IOptionsMonitor<StripeOptions> _stripeOptions;
    private readonly ILogger<StripeEnvironmentService> _logger;
    private StripeEnvironment _currentEnvironment;
    
    /// <summary>
    /// Creates a new instance of StripeEnvironmentService
    /// </summary>
    /// <param name="stripeOptions">Stripe options</param>
    /// <param name="logger">Logger</param>
    public StripeEnvironmentService(
        IOptionsMonitor<StripeOptions> stripeOptions,
        ILogger<StripeEnvironmentService> logger)
    {
        _stripeOptions = stripeOptions;
        _logger = logger;
        _currentEnvironment = stripeOptions.CurrentValue.DefaultEnvironment;
        
        _logger.LogInformation("StripeEnvironmentService initialized with default environment: {Environment}", 
            _currentEnvironment);
    }
    
    /// <inheritdoc />
    public StripeEnvironment CurrentEnvironment => _currentEnvironment;
    
    /// <inheritdoc />
    public void SwitchEnvironment(StripeEnvironment environment)
    {
        _logger.LogInformation("Switching Stripe environment from {Current} to {New}", 
            _currentEnvironment, environment);
        _currentEnvironment = environment;
    }
    
    /// <inheritdoc />
    public StripeEnvironment GetEnvironmentFromMetadata(IDictionary<string, string> metadata)
    {
        var options = _stripeOptions.CurrentValue;
        if (metadata != null && 
            metadata.TryGetValue(options.EnvironmentMetadataKey, out var envValue) && 
            Enum.TryParse<StripeEnvironment>(envValue, true, out var environment))
        {
            _logger.LogDebug("Found environment in metadata: {Environment}", environment);
            return environment;
        }
        
        _logger.LogDebug("Environment not found in metadata, using default: {Default}", 
            options.DefaultEnvironment);
        return options.DefaultEnvironment;
    }
    
    /// <inheritdoc />
    public string GetSecretKeyForEnvironment(StripeEnvironment environment)
    {
        return _stripeOptions.CurrentValue.GetSecretKeyForEnvironment(environment);
    }
    
    /// <inheritdoc />
    public string GetWebhookSecretForEnvironment(StripeEnvironment environment)
    {
        return _stripeOptions.CurrentValue.GetWebhookSecretForEnvironment(environment);
    }
} 