namespace Aevatar.Application.Grains.Common.Options;

/// <summary>
/// Options for system authentication configuration
/// </summary>
public class SystemAuthenticationOptions
{
    /// <summary>
    /// Dictionary of system configurations, where key is system ID and value is system config
    /// </summary>
    public Dictionary<string, SystemConfig> Systems { get; set; } = new();
}

/// <summary>
/// Configuration for a specific system
/// </summary>
public class SystemConfig
{
    //PKCS8
    public string PrivateKey { get; set; }
    public string PublicKey { get; set; }
}