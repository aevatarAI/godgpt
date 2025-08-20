namespace Aevatar.Application.Grains.Common.Constants;

/// <summary>
/// Proxy initialization status
/// </summary>
public enum ProxyInitStatus
{
    /// <summary>
    /// Not initialized
    /// </summary>
    NotInitialized = 0,
    
    /// <summary>
    /// Initializing
    /// </summary>
    Initializing = 1,
    
    /// <summary>
    /// Initialized
    /// </summary>
    Initialized = 2
}