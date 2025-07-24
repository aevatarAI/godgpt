# PaymentAnalyticsGrain

## Overview

PaymentAnalyticsGrain is a stateless, reentrant Orleans grain designed to report payment success events to Google Analytics 4 (GA4). It provides **idempotent transaction tracking with retry mechanism** using Google Analytics 4's built-in deduplication.

## Features

- **🔒 Idempotent Transaction Reporting**: Uses GA4's built-in `transaction_id` deduplication for perfect idempotency
- **🔄 Smart Retry Mechanism**: Configurable retry logic with intelligent error handling
- **📊 Standard Purchase Events**: Uses GA4's standard `purchase` event for automatic deduplication
- **🌐 Network-Safe**: Retry-safe design leverages GA4's server-side deduplication
- **⚡ Stateless Design**: No state management, purely functional service
- **🛡️ Error Resilient**: Handles 4xx/5xx errors differently, with smart retry policies

## Usage

### Basic Usage

```csharp
// Get the grain instance
var grain = grainFactory.GetGrain<IPaymentAnalyticsGrain>("payment-analytics");

// Report a payment success event with automatic retry and deduplication
var result = await grain.ReportPaymentSuccessAsync(
    PaymentPlatform.Stripe,
    "ORDER_12345",    // Your unique order/transaction ID
    "user_789"        // User ID
);

if (result.IsSuccess)
{
    Console.WriteLine($"Successfully reported to GA4 with status: {result.StatusCode}");
}
else
{
    Console.WriteLine($"Failed to report: {result.ErrorMessage}");
}
```

## Configuration

### appsettings.json

```json
{
  "GoogleAnalytics": {
    "EnableAnalytics": true,
    "MeasurementId": "G-XXXXXXXXXX",
    "ApiSecret": "your-api-secret-here",
    "ApiEndpoint": "https://www.google-analytics.com/mp/collect",
    "TimeoutSeconds": 10,
    "RetryCount": 3,
    "ApiCallDelayMs": 50
  }
}
```

### Configuration Options

| Option | Description | Default |
|--------|-------------|---------|
| `EnableAnalytics` | Enable/disable analytics reporting | `true` |
| `MeasurementId` | GA4 Measurement ID (G-XXXXXXXXXX) | Required |
| `ApiSecret` | GA4 Measurement Protocol API Secret | Required |
| `ApiEndpoint` | GA4 Measurement Protocol endpoint | Required |
| `TimeoutSeconds` | HTTP request timeout in seconds | `5` |
| `RetryCount` | Maximum number of retry attempts | `3` |
| `ApiCallDelayMs` | Delay between retry attempts in milliseconds | `50` |

## Retry Mechanism

### Smart Retry Logic

- **4xx Errors (Client Errors)**: No retry - immediate failure
- **5xx Errors (Server Errors)**: Retry up to `RetryCount` times
- **Network Errors**: Retry up to `RetryCount` times
- **Timeout Errors**: Retry up to `RetryCount` times

### Retry Configuration

```csharp
// Retry settings in appsettings.json
{
  "GoogleAnalytics": {
    "RetryCount": 3,        // Retry up to 3 times
    "ApiCallDelayMs": 50    // Wait 50ms between retries
  }
}
```

## Idempotency Deep Dive

### How GA4 Deduplication Works

Google Analytics 4 automatically deduplicates `purchase` events using the `transaction_id` parameter. This means:

✅ **Safe to retry**: Network failures won't create duplicate data  
✅ **Cross-session protection**: Same transaction ID from different sessions/devices = one record  
✅ **Server-side logic**: No client-side caching needed  

### Transaction ID Format

The grain creates a unique transaction ID by combining:
```
{userId}^{paymentPlatform}^{originalTransactionId}
```

Example: `user_789^Stripe^ORDER_12345`

This ensures uniqueness across users and platforms while maintaining GA4's deduplication benefits.

## Event Format

### Sent to Google Analytics 4

```json
{
  "client_id": "user_789^Stripe^ORDER_12345",
  "events": [
    {
      "name": "purchase",
      "params": {
        "transaction_id": "user_789^Stripe^ORDER_12345"
      }
    }
  ]
}
```

## Error Handling

### Common Error Scenarios

| Error Type | Behavior | Action |
|------------|----------|--------|
| Missing Configuration | Immediate failure | Check `MeasurementId` and `ApiSecret` |
| 4xx Client Error | No retry | Fix request format or credentials |
| 5xx Server Error | Retry with backoff | Temporary GA4 service issue |
| Network Timeout | Retry with backoff | Check network connectivity |
| Analytics Disabled | Immediate success | Feature flag disabled |

### Error Response Format

```csharp
public class PaymentAnalyticsResultDto
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public int StatusCode { get; set; }
}
```

## Testing

### Test Categories

1. **Basic Functionality**: Verify successful event reporting
2. **Idempotency**: Test duplicate transaction handling
3. **Error Handling**: Test invalid inputs and configuration
4. **Concurrency**: Test multiple simultaneous requests
5. **Retry Mechanism**: Verify retry logic and logging

### Running Tests

```bash
dotnet test test/GodGPT.PaymentAnalytics.Tests/
```

## Best Practices

### 1. Transaction ID Strategy
- Use your actual order/transaction numbers as the base transaction ID
- Let the grain handle uniqueness formatting
- Keep transaction IDs consistent across retries

### 2. Retry Safety
- Always use the same transaction ID for retries
- Don't implement your own retry logic - the grain handles it
- Trust GA4's deduplication mechanism

### 3. Error Handling
- Check `IsSuccess` before assuming the event was recorded
- Log `ErrorMessage` for debugging failed events
- Don't retry 4xx errors - they indicate client-side issues

### 4. Performance Optimization
- Consider batching multiple events if your application has high volume
- Use different grain keys to parallelize requests across different contexts
- Monitor retry patterns to identify systemic issues

### 5. Configuration Management
- Keep `ApiSecret` secure and rotate regularly
- Use different `MeasurementId` for development/staging/production
- Test configuration changes in non-production environments first

## Monitoring

### Key Metrics to Track

- Success/failure rates
- Retry attempt patterns
- Response time distribution
- GA4 event appearance in reports

### Logging

The grain provides detailed logging at different levels:
- **Debug**: Request details and retry attempts
- **Info**: Successful operations and key decisions
- **Warning**: Retry attempts and recoverable errors
- **Error**: Configuration issues and non-recoverable errors

### Log Examples

```
[Info] PaymentAnalyticsGrain reporting purchase event for transaction user_123^Stripe^ORDER_789
[Debug] Sending GA4 event payload (attempt 1/4): {"client_id":"user_123^Stripe^ORDER_789"...}
[Warning] GA4 API error on attempt 1: StatusCode=503, Content=Service Unavailable
[Debug] Waiting 50ms before retry attempt 2
[Info] [PaymentAnalytics] Successfully reported purchase event for transaction user_123^Stripe^ORDER_789
```

## Migration Guide

### From Custom Events to Standard Events

If you were previously using custom event names, the grain now uses GA4's standard `purchase` event:

❌ **Old**: Custom events (no deduplication)
```json
{ "name": "payment_success" }  // No automatic deduplication
```

✅ **New**: Standard purchase events (automatic deduplication)
```json
{ "name": "purchase" }  // GA4 handles deduplication automatically
```

This change ensures proper deduplication and better integration with GA4's ecommerce reports.
