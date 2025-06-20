namespace Aevatar.Application.Grains.Common.Services;

/// <summary>
/// Interface for system authentication service
/// </summary>
public interface ISystemAuthenticationService
{
    /// <summary>
    /// Get private key for the specified system
    /// </summary>
    /// <param name="systemId">System identifier</param>
    /// <returns>Private key in PEM format</returns>
    Task<string> GetPrivateKeyAsync(string systemId);
    
    /// <summary>
    /// Get public key for the specified system
    /// </summary>
    /// <param name="systemId">System identifier</param>
    /// <returns>Public key in PEM format</returns>
    Task<string> GetPublicKeyAsync(string systemId);
} 