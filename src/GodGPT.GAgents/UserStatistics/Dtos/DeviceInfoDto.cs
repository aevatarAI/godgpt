namespace GodGPT.GAgents.UserStatistics.Dtos;

/// <summary>
/// Data transfer object for device information
/// </summary>
[GenerateSerializer]
public class DeviceInfoDto
{
    /// <summary>
    /// Device model information
    /// </summary>
    [Id(0)] public string? DeviceModel { get; set; }
    
    /// <summary>
    /// Operating system version
    /// </summary>
    [Id(1)] public string? OSVersion { get; set; }
    
    /// <summary>
    /// App version when rating was made
    /// </summary>
    [Id(2)] public string? AppVersion { get; set; }
    
    /// <summary>
    /// Raw device information string from frontend
    /// </summary>
    [Id(3)] public string? RawDeviceInfo { get; set; }
}
