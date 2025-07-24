# PaymentAnalyticsGrain

## Overview

PaymentAnalyticsGrain is a stateless, reentrant Orleans grain designed to report payment success events to Google Analytics 4 (GA4). It provides a simple, fire-and-forget mechanism for tracking payment counts without requiring detailed payment information.

## Features

- **Simple Payment Count Reporting**: Reports unified "payment_success" events to GA4
- **Stateless Design**: No state management, purely functional service
- **Error Resilience**: Comprehensive error handling and logging
- **Configurable**: Fully configurable via appsettings.json
- **Concurrent Support**: [Reentrant] attribute supports concurrent calls

## Architecture

The grain follows Orleans architectural patterns:

- `[StatelessWorker]` - No state management required
- `[Reentrant]` - Supports concurrent calls
- Comprehensive logging and error handling
- Uses `IOptionsMonitor` for configuration
- HTTP client dependency injection

## Configuration

Add the following to your `appsettings.json`:

```json
{
  "GoogleAnalytics": {
    "EnableAnalytics": true,
    "MeasurementId": "G-XXXXXXXXXX",
    "ApiSecret": "YOUR_API_SECRET_HERE",
    "ApiEndpoint": "https://www.google-analytics.com/mp/collect",
    "TimeoutSeconds": 5
  }
}
```

### Configuration Options

- `EnableAnalytics`: Enable or disable analytics reporting
- `MeasurementId`: Your GA4 measurement ID (format: G-XXXXXXXXXX)
- `ApiSecret`: API secret generated in GA4 Admin
- `ApiEndpoint`: GA4 Measurement Protocol endpoint
- `TimeoutSeconds`: Request timeout in seconds (default: 5)

## Usage

### Basic Usage

```csharp
// Get the grain instance
var analyticsGrain = grainFactory.GetGrain<IPaymentAnalyticsGrain>("payment-analytics");

// Report a payment success event
var result = await analyticsGrain.ReportPaymentSuccessAsync();

if (result.IsSuccess)
{
    // Event reported successfully
    Console.WriteLine($"Payment success event reported to GA4. Status: {result.StatusCode}");
}
else
{
    // Handle error
    Console.WriteLine($"Failed to report event: {result.ErrorMessage}");
}
```

### Integration with Payment Processing

```csharp
// In StripeEventProcessingGrain or AppleEventProcessingGrain
public async Task<string> ParseEventAndGetUserIdAsync(string json)
{
    // ... existing payment processing logic ...
    
    if (!string.IsNullOrEmpty(userId))
    {
        // Existing logic
        
        // NEW: Report payment success (fire-and-forget)
        _ = Task.Run(async () =>
        {
            try
            {
                var analyticsGrain = GrainFactory.GetGrain<IPaymentAnalyticsGrain>("payment-analytics");
                await analyticsGrain.ReportPaymentSuccessAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to report payment analytics");
            }
        });
    }
    
    return userId;
}
```

### Concurrent Usage

```csharp
// Multiple concurrent reports are supported
var grain = grainFactory.GetGrain<IPaymentAnalyticsGrain>("payment-analytics");

var tasks = Enumerable.Range(0, 5)
    .Select(_ => grain.ReportPaymentSuccessAsync())
    .ToArray();

var results = await Task.WhenAll(tasks);

foreach (var result in results)
{
    Console.WriteLine($"Report result: {result.IsSuccess}");
}
```

## API Reference

### `ReportPaymentSuccessAsync()`

Reports a payment success event to Google Analytics.

**Returns**: `PaymentAnalyticsResultDto`
- `IsSuccess`: Whether the event was successfully reported
- `StatusCode`: HTTP status code from GA4 API
- `ErrorMessage`: Error description if unsuccessful

**Behavior**:
- Returns failure immediately if `EnableAnalytics` is false
- Validates required configuration (MeasurementId, ApiSecret)
- Uses configurable timeout for HTTP requests
- Generates unique client ID for each event

## GA4 Event Structure

The grain sends the following event structure to GA4:

```json
{
  "client_id": "payment_analytics_{timestamp}",
  "events": [
    {
      "name": "payment_success",
      "params": {
        "session_id": "{uuid}",
        "engagement_time_msec": 100
      }
    }
  ]
}
```

## Error Handling

The grain implements comprehensive error handling:

- **Configuration Validation**: Checks for required GA4 configuration
- **HTTP Timeouts**: Configurable request timeouts with proper cancellation
- **Network Errors**: Graceful handling of network failures
- **Logging**: Detailed logging for debugging and monitoring
- **Exception Safety**: All exceptions are caught and returned as error results

### Common Error Scenarios

1. **Analytics Disabled**: Returns `IsSuccess = false` with "Analytics reporting is disabled"
2. **Missing Configuration**: Returns error when MeasurementId or ApiSecret is missing
3. **Network Timeout**: Handles `TaskCanceledException` with timeout message
4. **HTTP Errors**: Returns GA4 API error status and response content

## Testing

The grain includes comprehensive integration tests:

- Basic success reporting
- Disabled analytics handling
- Multiple grain instances
- Concurrent call handling
- Performance testing

### Running Tests

```bash
cd test/GodGPT.PaymentAnalytics.Tests
dotnet test
```

### Test Configuration

Tests use mock configuration with test values:
```csharp
new GoogleAnalyticsOptions
{
    EnableAnalytics = true,
    MeasurementId = "G-TEST123456789",
    ApiSecret = "test-api-secret",
    TimeoutSeconds = 5
}
```

## Dependencies

- **Orleans**: Grain framework (`[StatelessWorker]`, `[Reentrant]`)
- **Microsoft.Extensions.Options**: Configuration management (`IOptionsMonitor`)
- **Microsoft.Extensions.Logging**: Logging infrastructure
- **System.Net.Http**: HTTP client for GA4 API calls
- **System.Text.Json**: JSON serialization

## Performance Considerations

- **Stateless**: No state overhead, can be deployed on any silo
- **Concurrent**: Multiple calls can be processed simultaneously
- **Fire-and-forget**: Should be called asynchronously to avoid blocking payment processing
- **Timeout Protection**: Configurable timeouts prevent long-running requests

## Notes

- This grain is designed to be stateless and does not store any payment information
- All reporting is fire-and-forget to avoid impacting payment processing performance
- The grain uses unique client IDs for each event to ensure proper counting in GA4
- Configuration is monitored and can be updated without restarting the application
- Namespace: `Aevatar.Application.Grains.PaymentAnalytics`
