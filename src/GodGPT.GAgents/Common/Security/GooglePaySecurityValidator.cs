using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aevatar.Application.Grains.Common.Options;

namespace Aevatar.Application.Grains.Common.Security
{
    /// <summary>
    /// Security validator for Google Pay/Play webhook messages
    /// Validates Pub/Sub push message signatures to prevent spoofing attacks
    /// </summary>
    public class GooglePaySecurityValidator
    {
        private readonly ILogger<GooglePaySecurityValidator> _logger;
        private readonly GooglePayOptions _options;

        public GooglePaySecurityValidator(
            ILogger<GooglePaySecurityValidator> logger,
            IOptions<GooglePayOptions> options)
        {
            _logger = logger;
            _options = options.Value;
        }

        /// <summary>
        /// Verify Pub/Sub push message signature using Google's RSA public key
        /// This prevents attackers from sending fake notifications to our webhook
        /// </summary>
        /// <param name="message">Raw message body</param>
        /// <param name="signature">X-Goog-Signature header value</param>
        /// <returns>True if signature is valid, false otherwise</returns>
        public bool VerifyPubSubSignature(string message, string signature)
        {
            try
            {
                if (string.IsNullOrEmpty(_options.RsaPublicKey))
                {
                    _logger.LogWarning("[GooglePaySecurityValidator] RSA public key not configured - cannot verify message signature");
                    return false;
                }

                if (string.IsNullOrEmpty(signature))
                {
                    _logger.LogWarning("[GooglePaySecurityValidator] No signature provided in X-Goog-Signature header");
                    return false;
                }

                _logger.LogDebug("[GooglePaySecurityValidator] Verifying Pub/Sub message signature");

                // 1. Parse RSA public key from base64
                var rsa = RSA.Create();
                var publicKeyBytes = Convert.FromBase64String(_options.RsaPublicKey);
                rsa.ImportRSAPublicKey(publicKeyBytes, out _);

                // 2. Verify signature
                var messageBytes = Encoding.UTF8.GetBytes(message);
                var signatureBytes = Convert.FromBase64String(signature);

                var isValid = rsa.VerifyData(messageBytes, signatureBytes, 
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                if (isValid)
                {
                    _logger.LogInformation("[GooglePaySecurityValidator] Pub/Sub message signature verification successful");
                }
                else
                {
                    _logger.LogWarning("[GooglePaySecurityValidator] Pub/Sub message signature verification failed");
                }

                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GooglePaySecurityValidator] Failed to verify Pub/Sub message signature");
                return false;
            }
        }

        /// <summary>
        /// Security checks for RevenueCat webhook requests
        /// </summary>
        /// <param name="userAgent">User-Agent header</param>
        /// <param name="contentType">Content-Type header</param>
        /// <param name="authorizationHeader">Authorization header (optional)</param>
        /// <returns>True if request appears to be from RevenueCat, false otherwise</returns>
        public bool ValidateRequestHeaders(string userAgent, string contentType, string authorizationHeader = null)
        {
            try
            {
                // Check User-Agent - only accept RevenueCat
                if (!string.IsNullOrEmpty(userAgent))
                {
                    if (!userAgent.Contains("RevenueCat", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("[GooglePaySecurityValidator] Invalid User-Agent, expected RevenueCat: {UserAgent}", userAgent);
                        return false;
                    }
                    
                    _logger.LogDebug("[GooglePaySecurityValidator] Valid RevenueCat User-Agent detected: {UserAgent}", userAgent);
                }
                else
                {
                    _logger.LogWarning("[GooglePaySecurityValidator] Missing User-Agent header");
                    return false;
                }

                // Check Content-Type
                if (!string.IsNullOrEmpty(contentType) && 
                    !contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("[GooglePaySecurityValidator] Invalid Content-Type: {ContentType}", contentType);
                    return false;
                }

                // Check Authorization header using RevenueCat API Key
                if (!string.IsNullOrEmpty(_options.RevenueCatApiKey))
                {
                    if (string.IsNullOrEmpty(authorizationHeader))
                    {
                        _logger.LogWarning("[GooglePaySecurityValidator] Missing Authorization header, but RevenueCat API key is configured");
                        return false;
                    }

                    // Support both formats: direct API key or "Bearer API_KEY"
                    bool isValid = false;
                    if (authorizationHeader.Equals(_options.RevenueCatApiKey, StringComparison.Ordinal))
                    {
                        // Direct API key format
                        isValid = true;
                        _logger.LogDebug("[GooglePaySecurityValidator] Valid Authorization header (direct API key format)");
                    }
                    else if (authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        // Bearer token format: "Bearer goog_xxxxx"
                        var token = authorizationHeader.Substring(7); // Remove "Bearer " prefix
                        if (token.Equals(_options.RevenueCatApiKey, StringComparison.Ordinal))
                        {
                            isValid = true;
                            _logger.LogDebug("[GooglePaySecurityValidator] Valid Authorization header (Bearer format)");
                        }
                    }

                    if (!isValid)
                    {
                        // Mask authorization header in error logs to prevent leakage
                        var maskedAuth = authorizationHeader.Length > 4 ? 
                            authorizationHeader.Substring(0, 4) + "***" : "***";
                        _logger.LogWarning("[GooglePaySecurityValidator] Invalid Authorization header value: {maskedAuth}", maskedAuth);
                        return false;
                    }
                }
                else
                {
                    _logger.LogInformation("[GooglePaySecurityValidator] No RevenueCat API key configured - skipping Authorization header validation");
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GooglePaySecurityValidator] Error validating request headers");
                return false;
            }
        }
    }
}
