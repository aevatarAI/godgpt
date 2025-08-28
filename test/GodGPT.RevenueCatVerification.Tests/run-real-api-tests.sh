#!/bin/bash

# RevenueCat Real API Integration Test Runner
# This script demonstrates how to run tests with a real Bearer token via configuration

echo "RevenueCat Real API Integration Test Runner"
echo "=========================================="

# Check if Bearer token is provided as argument
if [ -z "$1" ]; then
    echo "Usage: $0 <bearer_token>"
    echo ""
    echo "Example:"
    echo "  $0 goog_your_actual_bearer_token_here"
    echo ""
    echo "This will update appsettings.Development.json and run tests equivalent to:"
    echo "  curl -H \"Authorization: Bearer goog_your_token\" \\"
    echo "       \"https://api.revenuecat.com/v1/subscribers/ebb5d7d5-3ae7-39dd-21c1-3a1a4f097144?transaction_id=GPA.3327-7042-0698-86706\""
    echo ""
    echo "Note: The Bearer token should start with 'goog_'"
    echo ""
    echo "Alternative: Manually update appsettings.Development.json:"
    echo "  {"
    echo "    \"GooglePay\": {"
    echo "      \"RevenueCatApiKey\": \"goog_your_actual_bearer_token_here\""
    echo "    }"
    echo "  }"
    exit 1
fi

BEARER_TOKEN="$1"

# Validate Bearer token format
if [[ ! $BEARER_TOKEN =~ ^goog_ ]]; then
    echo "Error: Bearer token must start with 'goog_'"
    echo "Provided token: $BEARER_TOKEN"
    exit 1
fi

echo "Setting up configuration for RevenueCat API testing..."
echo "Bearer Token: ${BEARER_TOKEN:0:8}..."

# Get the script directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
CONFIG_FILE="$SCRIPT_DIR/appsettings.Development.json"

# Backup original config file
if [ -f "$CONFIG_FILE.backup" ]; then
    echo "Backup file already exists. Using existing backup."
else
    echo "Creating backup of appsettings.Development.json..."
    cp "$CONFIG_FILE" "$CONFIG_FILE.backup"
fi

# Update the configuration file
echo "Updating $CONFIG_FILE with Bearer token..."
cat > "$CONFIG_FILE" << EOF
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "System": "Information",
      "Microsoft": "Information",
      "Orleans": "Debug",
      "GodGPT": "Debug"
    }
  },
  "GooglePay": {
    "RevenueCatApiKey": "$BEARER_TOKEN",
    "RevenueCatBaseUrl": "https://api.revenuecat.com/v1"
  }
}
EOF

echo ""
echo "Configuration updated successfully."
echo "Running tests..."
echo ""

# Run the tests
dotnet test test/GodGPT.RevenueCatVerification.Tests/GodGPT.RevenueCatVerification.Tests.csproj \
    --filter "FullyQualifiedName~RevenueCatRealApiIntegrationTests" \
    --logger "console;verbosity=normal"

echo ""
echo "Test run completed."
echo ""
echo "Restoring original configuration..."

# Restore original config file
if [ -f "$CONFIG_FILE.backup" ]; then
    mv "$CONFIG_FILE.backup" "$CONFIG_FILE"
    echo "Original configuration restored."
else
    echo "Warning: No backup file found. Configuration was not restored."
fi