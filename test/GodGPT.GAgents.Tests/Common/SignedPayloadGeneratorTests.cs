using GodGPT.GAgents.Tests.Common;
using Newtonsoft.Json;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace GodGPT.GAgents.Tests.Common;

/// <summary>
/// Tests for SignedPayloadGenerator
/// Demonstrates how to generate valid SignedPayload for Apple Store webhook testing
/// </summary>
public class SignedPayloadGeneratorTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public SignedPayloadGeneratorTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public void GenerateTestSignedPayload_ShouldCreateValidJwtStructure()
    {
        // Arrange
        var testPayload = new
        {
            notificationType = "DID_RENEW",
            notificationUUID = Guid.NewGuid().ToString(),
            data = new
            {
                appAppleId = 6743791875,
                bundleId = "com.gpt.god",
                environment = "Sandbox"
            }
        };

        // Act
        var signedPayload = SignedPayloadGenerator.GenerateTestSignedPayload(testPayload);

        // Assert
        signedPayload.ShouldNotBeNullOrEmpty();
        
        // JWT should have 3 parts separated by dots
        var parts = signedPayload.Split('.');
        parts.Length.ShouldBe(3);
        
        _testOutputHelper.WriteLine($"Generated SignedPayload: {signedPayload}");
        
        // Decode and verify header
        var headerJson = DecodeBase64Url(parts[0]);
        var header = JsonConvert.DeserializeObject<dynamic>(headerJson);
        
        _testOutputHelper.WriteLine($"Header: {headerJson}");
        
        // Verify header contains required fields
        ((string)header.alg).ShouldBe("ES256");
        header.x5c.ShouldNotBeNull();
        ((Newtonsoft.Json.Linq.JArray)header.x5c).Count.ShouldBe(3); // Should have 3 certificates in chain
        
        // Decode and verify payload
        var payloadJson = DecodeBase64Url(parts[1]);
        var decodedPayload = JsonConvert.DeserializeObject<dynamic>(payloadJson);
        
        _testOutputHelper.WriteLine($"Payload: {payloadJson}");
        
        // Verify payload contains expected data
        ((string)decodedPayload.notificationType).ShouldBe("DID_RENEW");
        ((long)decodedPayload.data.appAppleId).ShouldBe(6743791875);
        ((string)decodedPayload.data.bundleId).ShouldBe("com.gpt.god");
        ((string)decodedPayload.data.environment).ShouldBe("Sandbox");
    }

    [Fact]
    public void GenerateCompleteTestNotification_ShouldCreateValidWebhookPayload()
    {
        // Act
        var notificationJson = SignedPayloadGenerator.GenerateCompleteTestNotification("REFUND");

        // Assert
        notificationJson.ShouldNotBeNullOrEmpty();
        
        _testOutputHelper.WriteLine($"Complete notification: {notificationJson}");
        
        // Parse the notification
        var notification = JsonConvert.DeserializeObject<dynamic>(notificationJson);
        notification.signedPayload.ShouldNotBeNull();
        
        var signedPayload = (string)notification.signedPayload;
        var parts = signedPayload.Split('.');
        parts.Length.ShouldBe(3);
        
        // Decode the payload to verify it contains correct notification type
        var payloadJson = DecodeBase64Url(parts[1]);
        var payload = JsonConvert.DeserializeObject<dynamic>(payloadJson);
        
        ((string)payload.notificationType).ShouldBe("REFUND");
        payload.data.ShouldNotBeNull();
        payload.data.signedTransactionInfo.ShouldNotBeNull();
        payload.data.signedRenewalInfo.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("DID_RENEW")]
    [InlineData("REFUND")]
    [InlineData("DID_CHANGE_RENEWAL_STATUS")]
    [InlineData("EXPIRED")]
    public void GenerateTestNotificationForDifferentTypes_ShouldWork(string notificationType)
    {
        // Act
        var notificationJson = SignedPayloadGenerator.GenerateCompleteTestNotification(notificationType);

        // Assert
        notificationJson.ShouldNotBeNullOrEmpty();
        
        var notification = JsonConvert.DeserializeObject<dynamic>(notificationJson);
        var signedPayload = (string)notification.signedPayload;
        
        var parts = signedPayload.Split('.');
        var payloadJson = DecodeBase64Url(parts[1]);
        var payload = JsonConvert.DeserializeObject<dynamic>(payloadJson);
        
        ((string)payload.notificationType).ShouldBe(notificationType);
        
        _testOutputHelper.WriteLine($"Generated {notificationType} notification successfully");
    }

    [Fact]
    public void CreateTestNotificationPayload_ShouldContainAllRequiredFields()
    {
        // Act
        var payload = SignedPayloadGenerator.CreateTestNotificationPayload("DID_RENEW");

        // Assert
        var payloadJson = JsonConvert.SerializeObject(payload);
        _testOutputHelper.WriteLine($"Test notification payload: {payloadJson}");
        
        var deserializedPayload = JsonConvert.DeserializeObject<dynamic>(payloadJson);
        
        // Verify all required fields are present
        deserializedPayload.notificationType.ShouldNotBeNull();
        deserializedPayload.notificationUUID.ShouldNotBeNull();
        deserializedPayload.data.ShouldNotBeNull();
        deserializedPayload.data.appAppleId.ShouldNotBeNull();
        deserializedPayload.data.bundleId.ShouldNotBeNull();
        deserializedPayload.data.bundleVersion.ShouldNotBeNull();
        deserializedPayload.data.environment.ShouldNotBeNull();
        deserializedPayload.data.signedTransactionInfo.ShouldNotBeNull();
        deserializedPayload.data.signedRenewalInfo.ShouldNotBeNull();
        deserializedPayload.version.ShouldNotBeNull();
        deserializedPayload.signedDate.ShouldNotBeNull();
    }

    /// <summary>
    /// Helper method to decode Base64URL
    /// </summary>
    private string DecodeBase64Url(string input)
    {
        // Replace URL-safe characters
        string base64 = input.Replace("-", "+").Replace("_", "/");
        
        // Add padding if needed
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        
        // Convert from Base64
        var bytes = Convert.FromBase64String(base64);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}

/// <summary>
/// Example usage in actual webhook handler tests
/// </summary>
public class AppleWebhookHandlerExampleTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public AppleWebhookHandlerExampleTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public void ExampleUsage_GenerateRefundNotificationForTesting()
    {
        // Generate a test refund notification
        var refundNotificationJson = SignedPayloadGenerator.GenerateCompleteTestNotification("REFUND");
        
        _testOutputHelper.WriteLine("Example: Generated refund notification for webhook testing:");
        _testOutputHelper.WriteLine(refundNotificationJson);
        
        // This can be used in webhook handler tests like:
        // var result = await webhookHandler.HandleAsync(refundNotificationJson);
        // Assert the refund was processed correctly...
        
        refundNotificationJson.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void ExampleUsage_GenerateCustomTransactionForTesting()
    {
        // Create custom transaction data
        var customTransactionPayload = new
        {
            transactionId = "test_transaction_12345",
            originalTransactionId = "test_original_12345", 
            productId = "monthly_subscription_test",
            bundleId = "com.test.app",
            purchaseDate = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds(),
            expiresDate = DateTimeOffset.UtcNow.AddDays(29).ToUnixTimeMilliseconds(),
            environment = "Sandbox",
            quantity = 1,
            type = "Auto-Renewable Subscription"
        };

        // Generate signed payload for this custom transaction
        var signedTransaction = SignedPayloadGenerator.GenerateTestSignedPayload(customTransactionPayload);
        
        _testOutputHelper.WriteLine("Example: Generated custom transaction for testing:");
        _testOutputHelper.WriteLine(signedTransaction);
        
        // This can be used in tests that need specific transaction data
        signedTransaction.ShouldNotBeNullOrEmpty();
    }
}
