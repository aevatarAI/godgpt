using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.Options;

namespace GodGPT.RevenueCatVerification.Tests;

/// <summary>
/// Debug tests to check configuration reading
/// </summary>
public class ConfigurationDebugTests : RevenueCatVerificationTestBase
{
    [Fact]
    public void TestConfigurationReading_ShouldShowCurrentValues()
    {
        // Arrange & Act
        var googlePayOptions = GetService<IOptionsMonitor<GooglePayOptions>>();
        var bearerToken = googlePayOptions.CurrentValue.RevenueCatApiKey;
        var baseUrl = googlePayOptions.CurrentValue.RevenueCatBaseUrl;
        
        // Debug output - force to console
        Console.WriteLine("=== Configuration Debug ===");
        Console.WriteLine($"RevenueCatApiKey: {bearerToken}");
        Console.WriteLine($"RevenueCatBaseUrl: {baseUrl}");
        Console.WriteLine($"Bearer token starts with 'goog_': {bearerToken?.StartsWith("goog_") ?? false}");
        Console.WriteLine($"Bearer token is test token: {bearerToken == "goog_test_api_key_for_testing"}");
        Console.WriteLine($"Bearer token is null or empty: {string.IsNullOrEmpty(bearerToken)}");
        
        // Assert
        Assert.NotNull(bearerToken);
        Assert.NotNull(baseUrl);
        
        Console.WriteLine("Configuration test completed successfully");
    }
}
