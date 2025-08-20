using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GodGPT.GAgents.Tests.Common;

/// <summary>
/// Helper class to generate valid SignedPayload for Apple Store webhook testing
/// This generates test JWT tokens that can pass VerifyJwtSignature validation
/// </summary>
public static class SignedPayloadGenerator
{
    /// <summary>
    /// Generate a test SignedPayload with mock Apple certificate chain
    /// Note: This is for testing purposes only and uses mock certificates
    /// </summary>
    public static string GenerateTestSignedPayload(object payload)
    {
        // Create test header with ES256 algorithm and mock x5c certificate chain
        var header = new
        {
            alg = "ES256",
            x5c = new[]
            {
                // Mock Apple certificate (base64 encoded)
                // In real scenarios, this would be the actual Apple certificate
                GetMockAppleCertificate(),
                GetMockAppleIntermediateCertificate(),
                GetMockAppleRootCertificate()
            }
        };

        // Serialize payload to JSON
        var payloadJson = JsonConvert.SerializeObject(payload);
        
        // Base64URL encode header and payload
        var headerBase64 = Base64UrlEncode(JsonConvert.SerializeObject(header));
        var payloadBase64 = Base64UrlEncode(payloadJson);
        
        // Create the data to sign (header.payload)
        var dataToSign = $"{headerBase64}.{payloadBase64}";
        
        // Generate test signature using ECDSA P-256
        var signature = GenerateTestSignature(dataToSign);
        var signatureBase64 = Base64UrlEncode(signature);
        
        // Combine all parts
        return $"{headerBase64}.{payloadBase64}.{signatureBase64}";
    }

    /// <summary>
    /// Generate a test notification payload structure
    /// </summary>
    public static object CreateTestNotificationPayload(string notificationType = "DID_RENEW")
    {
        return new
        {
            notificationType = notificationType,
            notificationUUID = Guid.NewGuid().ToString(),
            data = new
            {
                appAppleId = 6743791875,
                bundleId = "com.gpt.god",
                bundleVersion = "22",
                environment = "Sandbox",
                signedTransactionInfo = GenerateTestTransactionInfo(),
                signedRenewalInfo = GenerateTestRenewalInfo()
            },
            version = "2.0",
            signedDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    /// <summary>
    /// Generate test transaction info JWT
    /// </summary>
    private static string GenerateTestTransactionInfo()
    {
        var transactionPayload = new
        {
            transactionId = "2000000940381546",
            originalTransactionId = "2000000940378793",
            webOrderLineItemId = "2000000102382776",
            bundleId = "com.gpt.god",
            productId = "monthly20",
            subscriptionGroupIdentifier = "21700706",
            purchaseDate = 1749870821000,
            originalPurchaseDate = 1749869922000,
            expiresDate = 1749871221000,
            quantity = 1,
            type = "Auto-Renewable Subscription",
            inAppOwnershipType = "PURCHASED",
            signedDate = 1749870773110,
            environment = "Sandbox",
            transactionReason = "RENEWAL",
            storefront = "ATG",
            storefrontId = "14354",
            price = 19990,
            currency = "USD",
            appTransactionId = "704587456338412378"
        };

        return GenerateTestSignedPayload(transactionPayload);
    }

    /// <summary>
    /// Generate test renewal info JWT
    /// </summary>
    private static string GenerateTestRenewalInfo()
    {
        var renewalPayload = new
        {
            originalTransactionId = "2000000940378793",
            autoRenewProductId = "monthly20",
            productId = "monthly20",
            autoRenewStatus = 1,
            renewalPrice = 19990,
            currency = "USD",
            signedDate = 1749870773110,
            environment = "Sandbox",
            recentSubscriptionStartDate = 1749869922000,
            renewalDate = 1749871221000,
            appTransactionId = "704587456338412378"
        };

        return GenerateTestSignedPayload(renewalPayload);
    }

    /// <summary>
    /// Base64URL encode (without padding)
    /// </summary>
    private static string Base64UrlEncode(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return Base64UrlEncode(bytes);
    }

    /// <summary>
    /// Base64URL encode bytes (without padding)
    /// </summary>
    private static string Base64UrlEncode(byte[] bytes)
    {
        var base64 = Convert.ToBase64String(bytes);
        return base64.Replace("+", "-")
                     .Replace("/", "_")
                     .Replace("=", "");
    }

    /// <summary>
    /// Generate test ECDSA P-256 signature
    /// Note: This is for testing only and won't pass real Apple validation
    /// </summary>
    private static byte[] GenerateTestSignature(string dataToSign)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var dataBytes = Encoding.UTF8.GetBytes(dataToSign);
        return ecdsa.SignData(dataBytes, HashAlgorithmName.SHA256);
    }

    /// <summary>
    /// Get mock Apple leaf certificate (base64 encoded)
    /// This is a placeholder - in real testing you might want to use actual test certificates
    /// </summary>
    private static string GetMockAppleCertificate()
    {
        // This is a mock certificate for testing purposes
        // In real scenarios, you would use actual Apple test certificates
        return "MIIEMDCCAlegAwIBAgIQfTlfk0fNvFWvzC1YIANsXjAKBggqhkjOPQQDAzB1MUQwQgYDVQQDDDtBcHBsZSBXb3JsZHdpZGUgRGV2ZWxvcGVyIFJlbGF0aW9ucyBDZXJ0aWZpY2F0aW9uIEF1dGhvcml0eTELMAkGA1UECwwCRzYxEzARBgNVBAoMCkFwcGxlIEluYy4xCzAJBgNVBAYTAlVTMB4XDTIzMDkxMjE5NTE1M1oXDTI1MTAxMTE5NTE1MlowgZIxQDA+BgNVBAMMN1Byb2QgRUNDIE1hYyBBcHAgU3RvcmUgYW5kIGlUdW5lcyBTdG9yZSBSZWNlaXB0IFNpZ25pbmcxLDAqBgNVBAsMI0FwcGxlIFdvcmxkd2lkZSBEZXZlbG9wZXIgUmVsYXRpb25zMRMwEQYDVQQKDApBcHBsZSBJbmMuMQswCQYDVQQGEwJVUzBZMBMGByqGSM49AgEGCCqGSM49AwEHA0IABEFEYe/JqTqyQv/dtXkauDHCSXV129FYRV/0xiB24nCQkzQf3asHJONR5r0RA0aLvJ432hy1SZMouvyfpm26jXSjggIIMIICBDAMBgNVHRMBAf8EAjAAMB8GA1UdIwQYMBaAFD8vlCNR01DJmig97bB85c+lkGKZMHAGCCsGAQUFBwEBBGQwYjAtBggrBgEFBQcwAoYhaHR0cDovL2NlcnRzLmFwcGxlLmNvbS93d2RyZzYuZGVyMDEGCCsGAQUFBzABhiVodHRwOi8vb2NzcC5hcHBsZS5jb20vb2NzcDAzLXd3ZHJnNjAyMIIBHgYDVR0gBIIBFTCCARE";
    }

    /// <summary>
    /// Get mock Apple intermediate certificate
    /// </summary>
    private static string GetMockAppleIntermediateCertificate()
    {
        return "MIIDFjCCApygAwIBAgIUIsGhRwp0c2nvU4YSycafPTjzbNcwCgYIKoZIzj0EAwMwZzEbMBkGA1UEAwwSQXBwbGUgUm9vdCBDQSAtIEczMSYwJAYDVQQLDB1BcHBsZSBDZXJ0aWZpY2F0aW9uIEF1dGhvcml0eTETMBEGA1UECgwKQXBwbGUgSW5jLjELMAkGA1UEBhMCVVMwHhcNMjEwMzE3MjAzNzEwWhcNMzYwMzE5MDAwMDAwWjB1MUQwQgYDVQQDDDtBcHBsZSBXb3JsZHdpZGUgRGV2ZWxvcGVyIFJlbGF0aW9ucyBDZXJ0aWZpY2F0aW9uIEF1dGhvcml0eTELMAkGA1UECwwCRzYxEzARBgNVBAoMCkFwcGxlIEluYy4xCzAJBgNVBAYTAlVTMHYwEAYHKoZIzj0CAQYFK4EEACIDYgAEbsQKC94PrlWmZXnXgtxzdVJL8T0SGYngDRGpngn3N6PT8JMEb7FDi4bBmPhCnZ3/sq6PF/cGcKXWsL5vOteRhyJ45x3ASP7cOCk+aao90fcpxSv/EZFbniAbNgZGhIhpIo4H6MIH3MBIGA1UdEwEB/wQIMOYBAf8CAQAwHwYDVR0jBBgwFoAUu7DeVgijiJqkipnevr3rr9rLJKswRgYIKwYBBQUHAQEEOjA4MDYGCCsGAQUFBzABhipodHRwOi8vb2NzcC5hcHBsZS5jb20vb2NzcDAzLWFwcGxlcm9vdGNhZzMwNwYDVR0fBDAwLjAsoCqgKIYmaHR0cDovL2NybC5hcHBsZS5jb20vYXBwbGVyb290Y2FnMy5jcmwwHQYDVR0OBBYEFJqwiiO2Ne60WczQzU9OGGfOQ9CQMAoGCCqGSM49BAMDA2gAMGUCMQDFQetrjxUQjFn2Zu4pJvJZU1WCMYm=";
    }

    /// <summary>
    /// Get mock Apple root certificate
    /// </summary>
    private static string GetMockAppleRootCertificate()
    {
        return "MIICQzCCAcmgAwIBAgIILcX8iNLFS5UwCgYIKoZIzj0EAwMwZzEbMBkGA1UEAwwSQXBwbGUgUm9vdCBDQSAtIEczMSYwJAYDVQQLDB1BcHBsZSBDZXJ0aWZpY2F0aW9uIEF1dGhvcml0eTETMBEGA1UECgwKQXBwbGUgSW5jLjELMAkGA1UEBhMCVVMwHhcNMTQwNDMwMTgxOTA2WhcNMzkwNDMwMTgxOTA2WjBnMRswGQYDVQQDDBJBcHBsZSBSb290IENBIC0gRzMxJjAkBgNVBAsMHUFwcGxlIENlcnRpZmljYXRpb24gQXV0aG9yaXR5MRMwEQYDVQQKDApBcHBsZSBJbmMuMQswCQYDVQQGEwJVUzB2MBAGByqGSM49AgEGBSuBBAAiA2IABJjpLz1AcqTtkyJygRMc3RCV8cWjTnHcFBbZDuWmBSp3ZHtfTjjTuxxEtX/1H7YyYl3J6YRbTzBPEVoA/VhYDKX1DyxNB0cTddqXl5dvMVztK517IdvYuVTZXpmkOlEKMaNCMEAwHQYDVR0OBBYEFLuw3qFYM4iapIqZ3r6966/ayyqrMA8GA1UdEwEB/wQFMAMBAf8wDgYDVR0PAQH/BAQDAgEGMAoGCCqGSM49BAMDA2gAMGUCMQCD6cHEFl4aXTQY2e3v9GwOAEZLuN+yRhHFD/3meoyhpmvOwgPUnPWTxnS4at+qIxUCMG1mihDK1A3UT82NQz60imOlM27jbdoXt2QfyFMm+YhidDkLF1vLUagM6BgD56KyKA==";
    }

    /// <summary>
    /// Create a complete Apple Store webhook notification for testing
    /// </summary>
    public static string GenerateCompleteTestNotification(string notificationType = "DID_RENEW")
    {
        var payload = CreateTestNotificationPayload(notificationType);
        var signedPayload = GenerateTestSignedPayload(payload);
        
        return JsonConvert.SerializeObject(new { signedPayload });
    }
}
