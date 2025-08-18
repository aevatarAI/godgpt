using System.Text.Json.Serialization;
using Aevatar.Application.Grains.Common.Constants;
using System;

namespace Aevatar.Application.Grains.ChatManager.UserBilling;

/// <summary>
/// Internal Google Play purchase verification request (used by transaction verification and webhook)
/// </summary>
[GenerateSerializer]
public class GooglePlayVerificationDto
{
    [Id(0)] public string PurchaseToken { get; set; }     // Google Play Purchase Token
    [Id(1)] public string ProductId { get; set; }         // Product ID
    [Id(2)] public string PackageName { get; set; }       // App package name
    [Id(3)] public string OrderId { get; set; }           // Google Play order ID
    [Id(4)] public string UserId { get; set; }            // User ID
}

/// <summary>
/// Google Play transaction verification request (similar to Apple's transaction verification)
/// RevenueCat automatically handles sandbox/production environment detection
/// </summary>
[GenerateSerializer]
public class GooglePlayTransactionVerificationDto
{
    [Id(0)] public string UserId { get; set; }                    // Internal user ID (from login context)
    [Id(1)] public string TransactionIdentifier { get; set; }     // Transaction/Order ID from RevenueCat (similar to Apple's transactionId)
}

/// <summary>
/// RevenueCat transaction information (similar to Apple's AppStoreJWSTransactionDecodedPayload)
/// </summary>
[GenerateSerializer]
public class RevenueCatTransactionInfo
{
    [Id(0)] public string TransactionId { get; set; }
    [Id(1)] public string ProductId { get; set; }
    [Id(2)] public DateTime? PurchaseDate { get; set; }
    [Id(3)] public DateTime? ExpiresDate { get; set; }
    [Id(4)] public Guid? UserId { get; set; }
    [Id(5)] public decimal Amount { get; set; }
    [Id(6)] public string Currency { get; set; }
}

/// <summary>
/// Payment verification result
/// </summary>
[GenerateSerializer]
public class PaymentVerificationResultDto
{
    [Id(0)] public bool IsValid { get; set; }             // Verification success
    [Id(1)] public string Message { get; set; } = string.Empty;           // Result message
    [Id(2)] public string TransactionId { get; set; } = string.Empty;     // Transaction ID
    [Id(3)] public DateTime? SubscriptionStartDate { get; set; } // Subscription start time
    [Id(4)] public DateTime? SubscriptionEndDate { get; set; }   // Subscription end time
    [Id(5)] public string ErrorCode { get; set; } = string.Empty;         // Error code (when verification fails)
    [Id(6)] public string ProductId { get; set; } = string.Empty;         // Product ID
    [Id(7)] public PaymentPlatform Platform { get; set; }        // Payment platform
    [Id(8)] public int? PaymentState { get; set; }               // Google Play payment state
    [Id(9)] public bool? AutoRenewing { get; set; }              // Auto-renewing subscription status
    [Id(10)] public long? PurchaseTimeMillis { get; set; }       // Purchase time in milliseconds
    [Id(11)] public string PurchaseToken { get; set; }          // Purchase token
}

/// <summary>
/// Google Play RTDN notification data
/// </summary>
[GenerateSerializer]
public class GooglePlayNotificationDto
{
    [Id(0)] public string NotificationType { get; set; }  // Notification type
    [Id(1)] public string PurchaseToken { get; set; }     // Purchase token
    [Id(2)] public string SubscriptionId { get; set; }    // Subscription ID
    [Id(3)] public string ProductId { get; set; }         // Product ID
    [Id(4)] public DateTime NotificationTime { get; set; } // Notification time
    [Id(5)] public string PackageName { get; set; }       // Package name
    [Id(6)] public long EventTimeMillis { get; set; }     // Event time in milliseconds
    [Id(7)] public string Version { get; set; }           // Notification version
}



/// <summary>
/// Google Play RTDN notification structure
/// </summary>
[GenerateSerializer]
public class GooglePlayRTDNNotification
{
    [Id(0)] public string Version { get; set; }
    [Id(1)] public string PackageName { get; set; }
    [Id(2)] public long EventTimeMillis { get; set; }
    [Id(3)] public GooglePlaySubscriptionNotification SubscriptionNotification { get; set; }
    [Id(4)] public GooglePlayOneTimeProductNotification OneTimeProductNotification { get; set; }
}

/// <summary>
/// Google Play subscription notification
/// </summary>
[GenerateSerializer]
public class GooglePlaySubscriptionNotification
{
    [Id(0)] public string Version { get; set; }
    [Id(1)] public int NotificationType { get; set; }
    [Id(2)] public string PurchaseToken { get; set; }
    [Id(3)] public string SubscriptionId { get; set; }
}

/// <summary>
/// Google Play one-time product notification
/// </summary>
[GenerateSerializer]
public class GooglePlayOneTimeProductNotification
{
    [Id(0)] public string Version { get; set; }
    [Id(1)] public int NotificationType { get; set; }
    [Id(2)] public string PurchaseToken { get; set; }
    [Id(3)] public string Sku { get; set; }
}

/// <summary>
/// Google Play notification types enum
/// </summary>
public enum GooglePlayNotificationType
{
    SUBSCRIPTION_RECOVERED = 1,
    SUBSCRIPTION_RENEWED = 2,
    SUBSCRIPTION_CANCELED = 3,
    SUBSCRIPTION_PURCHASED = 4,
    SUBSCRIPTION_ON_HOLD = 5,
    SUBSCRIPTION_IN_GRACE_PERIOD = 6,
    SUBSCRIPTION_RESTARTED = 7,
    SUBSCRIPTION_PRICE_CHANGE_CONFIRMED = 8,
    SUBSCRIPTION_DEFERRED = 9,
    SUBSCRIPTION_PAUSED = 10,
    SUBSCRIPTION_PAUSE_SCHEDULE_CHANGED = 11,
    SUBSCRIPTION_REVOKED = 12,
    SUBSCRIPTION_EXPIRED = 13
}

/// <summary>
/// RevenueCat subscriber response structure
/// Used for querying transaction information from RevenueCat API
/// </summary>
[GenerateSerializer]
public class RevenueCatSubscriberResponse
{
    [Id(0)] public RevenueCatSubscriber Subscriber { get; set; }
}

/// <summary>
/// RevenueCat subscriber information
/// Contains transaction history and subscription details
/// </summary>
[GenerateSerializer]
public class RevenueCatSubscriber
{
    [Id(0)] public string AppUserId { get; set; }
    [Id(1)] public List<RevenueCatTransaction> Transactions { get; set; } = new List<RevenueCatTransaction>();
    [Id(2)] public Dictionary<string, RevenueCatSubscription> Subscriptions { get; set; } = new Dictionary<string, RevenueCatSubscription>();
}

/// <summary>
/// RevenueCat transaction information
/// Contains the mapping between transaction ID and purchase token
/// </summary>
[GenerateSerializer]
public class RevenueCatTransaction
{
    [Id(0)] public string TransactionId { get; set; }
    [Id(1)] public string OriginalTransactionId { get; set; }
    [Id(2)] public string PurchaseToken { get; set; }
    [Id(3)] public string ProductId { get; set; }
    [Id(4)] public string Store { get; set; }
    [Id(5)] public DateTime PurchaseDate { get; set; }
    [Id(6)] public DateTime? ExpirationDate { get; set; }
}

/// <summary>
/// RevenueCat subscription information
/// Contains subscription status and related transactions
/// </summary>
[GenerateSerializer]
public class RevenueCatSubscription
{
    [Id(0)] public string ProductId { get; set; }
    [Id(1)] public DateTime? ExpirationDate { get; set; }
    [Id(2)] public string Store { get; set; }
    [Id(3)] public bool IsActive { get; set; }
    [Id(4)] public List<string> TransactionIds { get; set; } = new List<string>();
}