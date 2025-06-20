using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Application.Grains.Common.Services;

/// <summary>
/// System authentication service implementation
/// </summary>
public class SystemAuthenticationService : ISystemAuthenticationService
{
    private readonly IOptionsMonitor<SystemAuthenticationOptions> _options;
    private readonly ILogger<SystemAuthenticationService> _logger;

    public SystemAuthenticationService(
        IOptionsMonitor<SystemAuthenticationOptions> options,
        ILogger<SystemAuthenticationService> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Get private key for the specified system from AppSettings
    /// </summary>
    /// <param name="systemId">System identifier</param>
    /// <returns>Private key in PEM format</returns>
    public Task<string> GetPrivateKeyAsync(string systemId)
    {
        try
        {
            var systems = _options.CurrentValue.Systems;
            if (!systems.TryGetValue(systemId, out var systemConfig))
            {
                _logger.LogWarning("[SystemAuthenticationService][GetPrivateKeyAsync] System configuration not found for {SystemId}", systemId);
                return Task.FromResult<string>(null);
            }

            if (string.IsNullOrEmpty(systemConfig.PrivateKey))
            {
                _logger.LogWarning("[SystemAuthenticationService][GetPrivateKeyAsync] Private key not found for system {SystemId}", systemId);
                return Task.FromResult<string>(null);
            }

            _logger.LogDebug("[SystemAuthenticationService][GetPrivateKeyAsync] Successfully retrieved private key for system {SystemId}", systemId);
            return Task.FromResult(systemConfig.PrivateKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SystemAuthenticationService][GetPrivateKeyAsync] Error retrieving private key for system {SystemId}, Error: {ErrorMessage}", 
                systemId, ex.Message);
            return Task.FromResult<string>(null);
        }
    }

    /// <summary>
    /// Get public key for the specified system from AppSettings
    /// </summary>
    /// <param name="systemId">System identifier</param>
    /// <returns>Public key in PEM format</returns>
    public Task<string> GetPublicKeyAsync(string systemId)
    {
        try
        {
            var systems = _options.CurrentValue.Systems;
            if (!systems.TryGetValue(systemId, out var systemConfig))
            {
                _logger.LogWarning("[SystemAuthenticationService][GetPublicKeyAsync] System configuration not found for {SystemId}", systemId);
                return Task.FromResult<string>(null);
            }

            if (string.IsNullOrEmpty(systemConfig.PublicKey))
            {
                _logger.LogWarning("[SystemAuthenticationService][GetPublicKeyAsync] Public key not found for system {SystemId}", systemId);
                return Task.FromResult<string>(null);
            }

            _logger.LogDebug("[SystemAuthenticationService][GetPublicKeyAsync] Successfully retrieved public key for system {SystemId}", systemId);
            return Task.FromResult(systemConfig.PublicKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SystemAuthenticationService][GetPublicKeyAsync] Error retrieving public key for system {SystemId}, Error: {ErrorMessage}", 
                systemId, ex.Message);
            return Task.FromResult<string>(null);
        }
    }
} 