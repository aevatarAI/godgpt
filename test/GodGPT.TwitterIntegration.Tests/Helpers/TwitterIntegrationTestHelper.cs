using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Aevatar.Application.Grains.TwitterInteraction.Dtos;

namespace GodGPT.TwitterIntegration.Tests.Helpers;

/// <summary>
/// Twitter configuration for testing
/// </summary>
public class TwitterTestConfig
{
    public string BearerToken { get; set; } = string.Empty;
    public string MonitorHandle { get; set; } = string.Empty;
    public int BatchFetchSize { get; set; } = 25;
    public int MonitoringIntervalMinutes { get; set; } = 30;
    public int DataRetentionDays { get; set; } = 5;
}

/// <summary>
/// Helper class for Twitter integration tests
/// </summary>
public class TwitterIntegrationTestHelper
{
    private readonly ILogger<TwitterIntegrationTestHelper> _logger;
    private readonly IConfiguration _configuration;
    private readonly TwitterTestConfig _twitterConfig;

    public TwitterIntegrationTestHelper(
        ILogger<TwitterIntegrationTestHelper> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _twitterConfig = LoadTwitterConfiguration();
    }

    /// <summary>
    /// Load Twitter configuration from appsettings
    /// </summary>
    private TwitterTestConfig LoadTwitterConfiguration()
    {
        var config = new TwitterTestConfig();
        
        // Load from TwitterReward section (not TwitterRewardOptions)
        var twitterSection = _configuration.GetSection("TwitterReward");
        if (twitterSection.Exists())
        {
            config.BearerToken = twitterSection["BearerToken"] ?? string.Empty;
            config.MonitorHandle = twitterSection["MonitorHandle"] ?? "@godgpt_";
            config.BatchFetchSize = twitterSection.GetValue<int>("BatchFetchSize", 100);
            config.MonitoringIntervalMinutes = twitterSection.GetValue<int>("MonitoringIntervalMinutes", 30);
            config.DataRetentionDays = twitterSection.GetValue<int>("DataRetentionDays", 5);
        }

        return config;
    }

    /// <summary>
    /// Get Twitter configuration
    /// </summary>
    public TwitterTestConfig GetTwitterConfiguration()
    {
        return _twitterConfig;
    }

    /// <summary>
    /// Validate Twitter API configuration
    /// </summary>
    public Task<bool> ValidateTwitterConfigurationAsync()
    {
        try
        {
            _logger.LogInformation("Validating Twitter configuration...");
            
            if (string.IsNullOrWhiteSpace(_twitterConfig.BearerToken))
            {
                _logger.LogWarning("Twitter Bearer Token not configured");
                return Task.FromResult(false);
            }

            if (string.IsNullOrWhiteSpace(_twitterConfig.MonitorHandle))
            {
                _logger.LogWarning("Monitor Handle not configured");
                return Task.FromResult(false);
            }

            if (_twitterConfig.BatchFetchSize <= 0)
            {
                _logger.LogWarning("BatchFetchSize configuration is invalid: {BatchFetchSize}", _twitterConfig.BatchFetchSize);
                return Task.FromResult(false);
            }

            _logger.LogInformation("Twitter configuration validation passed:");
            _logger.LogInformation("- Monitor Handle: {MonitorHandle}", _twitterConfig.MonitorHandle);
            _logger.LogInformation("- Batch Fetch Size: {BatchFetchSize}", _twitterConfig.BatchFetchSize);
            _logger.LogInformation("- Bearer Token: {HasToken}", !string.IsNullOrWhiteSpace(_twitterConfig.BearerToken) ? "Configured" : "Not configured");

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Twitter configuration validation failed");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Log test configuration for debugging
    /// </summary>
    public void LogTestConfiguration()
    {
        _logger.LogInformation("=== Twitter Integration Test Configuration ===");
        _logger.LogInformation("Monitor Handle: {MonitorHandle}", _twitterConfig.MonitorHandle);
        _logger.LogInformation("Batch Fetch Size: {BatchFetchSize}", _twitterConfig.BatchFetchSize);
        _logger.LogInformation("Monitoring Interval: {MonitoringIntervalMinutes} minutes", _twitterConfig.MonitoringIntervalMinutes);
        _logger.LogInformation("Data Retention: {DataRetentionDays} days", _twitterConfig.DataRetentionDays);
        _logger.LogInformation("Bearer Token Configured: {HasToken}", !string.IsNullOrWhiteSpace(_twitterConfig.BearerToken));
        _logger.LogInformation("==============================================");
    }
} 