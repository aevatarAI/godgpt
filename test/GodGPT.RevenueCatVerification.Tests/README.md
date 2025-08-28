# GodGPT RevenueCat Verification Tests

This test project provides comprehensive system tests for Google Pay verification through RevenueCat integration.

## Overview

This test suite addresses issues with RevenueCat response parsing in the Google Pay verify interface, ensuring correct JSON processing and API integration.

## Test Scenarios

### 1. JSON Response Parsing Tests (`RevenueCatJsonParsingTests.cs`)

Tests the correct processing of RevenueCat JSON responses:

- **Sample JSON Response**: Tests parsing of the provided sample JSON response:
```json
{
  "request_date": "2025-08-19T02:05:37Z",
  "request_date_ms": 1755569137650,
  "subscriber": {
    "subscriptions": {
      "premium_weekly_test1": {
        "store_transaction_id": "GPA.3327-7042-0698-86706",
        "store": "play_store",
        "purchase_date": "2025-08-19T02:02:00Z",
        "expires_date": "2025-08-19T02:07:00Z",
        "is_sandbox": true,
        "price": {
          "amount": 48.0,
          "currency": "HKD"
        }
      }
    }
  }
}
```

- **Transaction Mapping**: Verifies correct mapping to internal `RevenueCatTransaction` format
- **Error Handling**: Tests graceful handling of malformed or incomplete JSON
- **Edge Cases**: Tests empty subscriptions and missing optional fields

### 2. API Integration Tests (`RevenueCatApiIntegrationTests.cs`)

Tests the correct handling of mock API call results:

- **Valid Response Processing**: Tests successful transaction verification flow with simulated data
- **Multiple Subscriptions**: Tests finding the correct transaction among multiple subscriptions
- **API Error Handling**: Tests handling of API error responses (404, authentication errors)
- **URL Formatting**: Verifies correct API URL construction
- **Transaction Matching**: Tests scenarios where no matching transaction is found

### 3. Real API Integration Tests (`RevenueCatRealApiIntegrationTests.cs`)

Tests actual HTTP calls to RevenueCat API using configuration:

```bash
# Equivalent to this curl command using configured Bearer token:
curl -H "Authorization: Bearer goog_xxx" \
     "https://api.revenuecat.com/v1/subscribers/ebb5d7d5-3ae7-39dd-21c1-3a1a4f097144?transaction_id=GPA.3327-7042-0698-86706"
```

- **Configuration-based Testing**: Reads RevenueCat API key from `GooglePayOptions` configuration
- **Real HTTP Calls**: Performs actual API calls to RevenueCat servers
- **System Integration**: Tests the complete system using real configuration and HTTP infrastructure
- **Automatic Skipping**: Tests are automatically skipped if no valid Bearer token is configured

## Project Structure

```
GodGPT.RevenueCatVerification.Tests/
├── GodGPT.RevenueCatVerification.Tests.csproj  # Project file with dependencies
├── GlobalUsings.cs                              # Global using statements
├── appsettings.json                             # Test configuration
├── appsettings.Development.json                 # Development-specific config
├── RevenueCatVerificationTestBase.cs            # Base class for all tests
├── RevenueCatVerificationTestModule.cs          # ABP test module configuration
├── RevenueCatJsonParsingTests.cs                # JSON parsing test scenarios
├── RevenueCatApiIntegrationTests.cs             # API integration test scenarios
└── README.md                                    # This documentation
```

## Dependencies

- **Testing Framework**: xUnit with Visual Studio test runner
- **Mocking**: Moq for HTTP client mocking and dependency injection
- **Base Classes**: Inherits from `AevatarOrleansTestBase` for Orleans integration
- **JSON Parsing**: Newtonsoft.Json for deserialization
- **HTTP Client**: Microsoft.Extensions.Http for HTTP client factory

## Configuration

The test project uses the following configuration:

- **Target Framework**: .NET 9.0
- **Package Name**: `com.aevatar.godgpt.test`
- **RevenueCat API**: `https://api.revenuecat.com/v1`
- **Test Product**: `premium_weekly_test1` (48.0 HKD, weekly subscription)

## Running Tests

```bash
# Run all tests in the project
dotnet test GodGPT.RevenueCatVerification.Tests

# Run with verbose output
dotnet test GodGPT.RevenueCatVerification.Tests --logger "console;verbosity=detailed"

# Run specific test class
dotnet test --filter "ClassName=RevenueCatJsonParsingTests"
dotnet test --filter "ClassName=RevenueCatApiIntegrationTests"
dotnet test --filter "ClassName=RevenueCatRealApiIntegrationTests"

# Run specific test method
dotnet test --filter "TestParseRevenueCatJsonResponse_ShouldCorrectlyParseProvidedSample"
```

## Configuration for Real API Tests

To run the real API integration tests, you need to configure a valid RevenueCat Bearer token:

### Option 1: Update appsettings.Development.json
```json
{
  "GooglePay": {
    "RevenueCatApiKey": "goog_your_actual_bearer_token_here",
    "RevenueCatBaseUrl": "https://api.revenuecat.com/v1"
  }
}
```

### Option 2: Create appsettings.Local.json (recommended for local testing)
```json
{
  "GooglePay": {
    "RevenueCatApiKey": "goog_your_actual_bearer_token_here"
  }
}
```

### Test Behavior
- **Default Token**: Tests are skipped if using the default test token `goog_test_api_key_for_testing`
- **Missing Token**: Tests are skipped if no token is configured
- **Invalid Format**: Tests are skipped if token doesn't start with `goog_`
- **Valid Token**: Tests perform real HTTP calls to RevenueCat API

## Key Test Methods

### JSON Parsing Tests
- `TestParseRevenueCatJsonResponse_ShouldCorrectlyParseProvidedSample()`: Validates parsing of the exact sample JSON
- `TestRevenueCatTransactionMapping_ShouldCorrectlyMapToInternalFormat()`: Tests transaction mapping logic
- `TestRevenueCatJsonParsing_WithMissingFields_ShouldHandleGracefully()`: Tests robustness with incomplete data

### API Integration Tests  
- `TestGooglePlayTransactionVerification_WithValidRevenueCatResponse_ShouldSucceed()`: Tests successful verification flow
- `TestGooglePlayTransactionVerification_WithMultipleSubscriptions_ShouldFindCorrectOne()`: Tests transaction matching
- `TestGooglePlayTransactionVerification_WithApiError_ShouldHandleGracefully()`: Tests error handling

## Integration with Existing Code

This test project validates the logic used in:
- `UserBillingGAgent.QueryRevenueCatForTransactionAsync()`: RevenueCat API integration
- `RevenueCatSubscriberResponse` deserialization: JSON parsing
- `RevenueCatTransaction` mapping: Internal data model conversion

## Troubleshooting

If tests fail, check:
1. JSON response format matches expected RevenueCat API structure
2. Transaction ID matching logic in `QueryRevenueCatForTransactionAsync`
3. Data model mapping between `RevenueCatSubscription` and `RevenueCatTransaction`
4. HTTP client configuration and authorization headers
