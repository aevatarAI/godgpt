# GodGPT Twitter Integration Tests

This module provides comprehensive integration tests for the Twitter functionality in GodGPT, specifically focusing on the `FetchTweetsManuallyAsync()` method and related Twitter monitoring capabilities.

## Overview

The integration tests validate:
- Configuration loading from `appsettings.json`
- Database storage and retrieval operations
- Twitter API integration
- Error handling and edge cases
- Performance and reliability

## Test Structure

### Core Components

1. **TwitterIntegrationTestModule** - Main test module with dependency injection setup
2. **TwitterIntegrationTestBase** - Base class for all integration tests
3. **TwitterIntegrationTestHelper** - Helper utilities for test operations
4. **TestDataManager** - Database operations and test data management

### Main Test Classes

- **TweetMonitorIntegrationTests** - Integration tests for `FetchTweetsManuallyAsync()`

## Configuration

### Required Settings

Update `appsettings.json` with your Twitter API credentials:

```json
{
  "TwitterReward": {
    "BearerToken": "YOUR_TWITTER_BEARER_TOKEN_HERE",
    "ApiKey": "YOUR_TWITTER_API_KEY_HERE",
    "ApiSecret": "YOUR_TWITTER_API_SECRET_HERE",
    "MonitorHandle": "@account",
    "ShareLinkDomain": "https://app.xxx.xx"
  }
}
```

### Database Configuration

The tests use MongoDB for data persistence:

```json
{
  "ConnectionStrings": {
    "Default": "mongodb://127.0.0.1:27017/xxx_xxx_Tests"
  }
}
```

## Running the Tests

### Prerequisites

1. **MongoDB**: Ensure MongoDB is running locally
2. **Twitter API Access**: Valid Twitter API credentials (optional for some tests)
3. **.NET 9.0**: Required runtime

### Command Line

```bash
# Run all integration tests
dotnet test test/GodGPT.TwitterIntegration.Tests/

# Run with detailed logging
dotnet test test/GodGPT.TwitterIntegration.Tests/ --logger "console;verbosity=detailed"

# Run specific test
dotnet test test/GodGPT.TwitterIntegration.Tests/ --filter "FetchTweetsManuallyAsync_WithValidConfiguration_ShouldSucceed"
```

### Development Environment

For development testing, create `appsettings.Development.json` with reduced limits:

```json
{
  "TwitterReward": {
    "MonitoringIntervalMinutes": 5,
    "BatchFetchSize": 10,
    "DataRetentionDays": 1
  }
}
```

## Test Cases

### 1. Basic Functionality Test
- **Test**: `FetchTweetsManuallyAsync_WithValidConfiguration_ShouldSucceed`
- **Purpose**: Validates basic tweet fetching functionality
- **Requirements**: Valid Twitter API configuration

### 2. Duplicate Handling Test
- **Test**: `FetchTweetsManuallyAsync_MultipleConsecutiveCalls_ShouldHandleDuplicates`
- **Purpose**: Ensures proper duplicate detection and handling
- **Requirements**: Valid Twitter API configuration

### 3. Database Persistence Test
- **Test**: `FetchTweetsManuallyAsync_WithDatabasePersistence_ShouldStoreData`
- **Purpose**: Validates data storage and retrieval operations
- **Requirements**: MongoDB connection, Twitter API configuration

### 4. Configuration Test
- **Test**: `FetchTweetsManuallyAsync_ConfigurationFromFile_ShouldUseCorrectSettings`
- **Purpose**: Verifies configuration loading from `appsettings.json`
- **Requirements**: None (uses default configuration)

### 5. Status Update Test
- **Test**: `GetMonitoringStatusAsync_AfterFetchTweetsManually_ShouldReflectUpdatedStatus`
- **Purpose**: Validates status tracking and updates
- **Requirements**: Valid Twitter API configuration

## Test Features

### Automatic Skipping
Tests automatically skip when Twitter API configuration is missing:
```csharp
SkipIfTwitterConfigurationMissing();
```

### Database Cleanup
Each test automatically cleans up test data:
```csharp
await ExecuteTestWithSetupAsync(async () => {
    // Test code here
});
```

### Comprehensive Logging
Detailed logging for debugging and monitoring:
- Test execution flow
- API call results
- Database operations
- Configuration validation

## Troubleshooting

### Common Issues

1. **MongoDB Connection Error**
   - Ensure MongoDB is running: `mongod`
   - Check connection string in configuration

2. **Twitter API Rate Limits**
   - Tests may fail due to rate limiting
   - Use development configuration with reduced batch sizes

3. **Missing Configuration**
   - Tests will skip if Twitter API credentials are missing
   - This is expected behavior for CI/CD environments

### Debug Logging

Enable trace-level logging in `appsettings.Development.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "GodGPT.TwitterIntegration.Tests": "Trace"
    }
  }
}
```

## Integration with CI/CD

The tests are designed to work in CI/CD environments:
- Automatically skip when credentials are unavailable
- Use in-memory configuration for basic tests
- Provide detailed logging for debugging failures

## Contributing

When adding new tests:
1. Inherit from `TwitterIntegrationTestBase`
2. Use `ExecuteTestWithSetupAsync()` for proper cleanup
3. Add comprehensive logging
4. Handle configuration missing scenarios
5. Update this README with new test descriptions 