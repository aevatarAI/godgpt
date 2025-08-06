using System.Text.Json.Serialization;
using Aevatar.Application.Grains.Common.Constants;
using System;

namespace Aevatar.Application.Grains.ChatManager.UserBilling;

/// <summary>
/// Google Pay Web payment verification request
/// </summary>
[GenerateSerializer]
public class GooglePayVerificationDto
{
    [Id(0)] public string PaymentToken { get; set; }      // Google Pay Payment Token
    [Id(1)] public string ProductId { get; set; }         // Product ID
    [Id(2)] public string OrderId { get; set; }           // Google Pay order ID
    [Id(3)] public string UserId { get; set; }            // User ID
    [Id(4)] public string Environment { get; set; }       // "PRODUCTION" or "TEST"
}

/// <summary>
/// Google Play purchase verification request
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
/// Payment verification result
/// </summary>
[GenerateSerializer]
public class PaymentVerificationResultDto
{
    [Id(0)] public bool IsValid { get; set; }             // Verification success
    [Id(1)] public string Message { get; set; }           // Result message
    [Id(2)] public string TransactionId { get; set; }     // Transaction ID
    [Id(3)] public DateTime? SubscriptionStartDate { get; set; } // Subscription start time
    [Id(4)] public DateTime? SubscriptionEndDate { get; set; }   // Subscription end time
    [Id(5)] public string ErrorCode { get; set; }         // Error code (when verification fails)
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
}

/// <summary>
/// Google Play purchase details from API
/// </summary>
[GenerateSerializer]
public class GooglePlayPurchaseDto
{
    [Id(0)] public string PurchaseToken { get; set; }
    [Id(1)] public string ProductId { get; set; }
    [Id(2)] public long PurchaseTimeMillis { get; set; }
    [Id(3)] public int PurchaseState { get; set; }
    [Id(4)] public string OrderId { get; set; }
    [Id(5)] public string PackageName { get; set; }
    [Id(6)] public bool AutoRenewing { get; set; }
    [Id(7)] public string DeveloperPayload { get; set; }
}

/// <summary>
/// Google Play subscription details from API
/// </summary>
[GenerateSerializer]
public class GooglePlaySubscriptionDto
{
    [Id(0)] public string SubscriptionId { get; set; }
    [Id(1)] public long StartTimeMillis { get; set; }
    [Id(2)] public long ExpiryTimeMillis { get; set; }
    [Id(3)] public bool AutoRenewing { get; set; }
    [Id(4)] public int PaymentState { get; set; }
    [Id(5)] public string OrderId { get; set; }
    [Id(6)] public string PriceAmountMicros { get; set; }
    [Id(7)] public string PriceCurrencyCode { get; set; }
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