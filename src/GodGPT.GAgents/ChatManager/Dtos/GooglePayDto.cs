using System.Text.Json.Serialization;
using Newtonsoft.Json;
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
    [Id(12)] public string OrderId { get; set; } = string.Empty; // Order ID for additional context
    [Id(13)] public double? PriceInPurchasedCurrency { get; set; } // Price from RevenueCat (negative for refunds)
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
    [Id(0)] [JsonProperty("subscriber")] public RevenueCatSubscriber Subscriber { get; set; }
}

/// <summary>
/// RevenueCat subscriber information
/// Contains subscription details that match the actual API response structure
/// </summary>
[GenerateSerializer]
public class RevenueCatSubscriber
{
    [Id(0)] [JsonProperty("app_user_id")] public string AppUserId { get; set; }
    [Id(1)] [JsonProperty("subscriptions")] public Dictionary<string, RevenueCatSubscription> Subscriptions { get; set; } = new Dictionary<string, RevenueCatSubscription>();
    [Id(2)] [JsonProperty("entitlements")] public Dictionary<string, object> Entitlements { get; set; } = new Dictionary<string, object>();
    [Id(3)] [JsonProperty("first_seen")] public string FirstSeen { get; set; }
    [Id(4)] [JsonProperty("last_seen")] public string LastSeen { get; set; }
    [Id(5)] [JsonProperty("original_app_user_id")] public string OriginalAppUserId { get; set; }
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
/// Contains subscription status and transaction details from actual API response
/// </summary>
[GenerateSerializer]
public class RevenueCatSubscription
{
    [Id(0)] [JsonProperty("store_transaction_id")] public string StoreTransactionId { get; set; }
    [Id(1)] [JsonProperty("expires_date")] public string ExpiresDate { get; set; }
    [Id(2)] [JsonProperty("store")] public string Store { get; set; }
    [Id(3)] [JsonProperty("original_purchase_date")] public string OriginalPurchaseDate { get; set; }
    [Id(4)] [JsonProperty("purchase_date")] public string PurchaseDate { get; set; }
    [Id(5)] [JsonProperty("is_sandbox")] public bool IsSandbox { get; set; }
    [Id(6)] [JsonProperty("period_type")] public string PeriodType { get; set; }
    [Id(7)] [JsonProperty("product_plan_identifier")] public string ProductPlanIdentifier { get; set; }
    [Id(8)] [JsonProperty("price")] public RevenueCatPrice Price { get; set; }
    [Id(9)] [JsonProperty("management_url")] public string ManagementUrl { get; set; }
}

/// <summary>
/// RevenueCat price information
/// </summary>
[GenerateSerializer]
public class RevenueCatPrice
{
    [Id(0)] [JsonProperty("amount")] public double Amount { get; set; }
    [Id(1)] [JsonProperty("currency")] public string Currency { get; set; }
}

/// <summary>
/// RevenueCat webhook event structure
/// This represents the format that RevenueCat sends to our webhook endpoint
/// </summary>
[GenerateSerializer]
public class RevenueCatWebhookEvent
{
    [Id(0)] [JsonProperty("api_version")] public string ApiVersion { get; set; }
    [Id(1)] [JsonProperty("event")] public RevenueCatEvent Event { get; set; }
}

/// <summary>
/// RevenueCat event information
/// </summary>
[GenerateSerializer]
public class RevenueCatEvent
{
    [Id(0)] [JsonProperty("id")] public string Id { get; set; }
    [Id(1)] [JsonProperty("type")] public string Type { get; set; }
    [Id(2)] [JsonProperty("event_timestamp_ms")] public long EventTimestampMs { get; set; }
    [Id(3)] [JsonProperty("app_user_id")] public string AppUserId { get; set; }
    [Id(4)] [JsonProperty("aliases")] public List<string> Aliases { get; set; } = new List<string>();
    [Id(5)] [JsonProperty("original_app_user_id")] public string OriginalAppUserId { get; set; }
    [Id(6)] [JsonProperty("product_id")] public string ProductId { get; set; }
    [Id(7)] [JsonProperty("period_type")] public string PeriodType { get; set; }
    [Id(8)] [JsonProperty("purchased_at_ms")] public long? PurchasedAtMs { get; set; }
    [Id(9)] [JsonProperty("expiration_at_ms")] public long? ExpirationAtMs { get; set; }
    [Id(10)] [JsonProperty("environment")] public string Environment { get; set; }
    [Id(11)] [JsonProperty("entitlement_id")] public string EntitlementId { get; set; }
    [Id(12)] [JsonProperty("entitlement_ids")] public List<string> EntitlementIds { get; set; } = new List<string>();
    [Id(13)] [JsonProperty("presented_offering_id")] public string PresentedOfferingId { get; set; }
    [Id(14)] [JsonProperty("transaction_id")] public string TransactionId { get; set; }
    [Id(15)] [JsonProperty("original_transaction_id")] public string OriginalTransactionId { get; set; }
    [Id(16)] [JsonProperty("is_family_share")] public bool? IsFamilyShare { get; set; }
    [Id(17)] [JsonProperty("country_code")] public string CountryCode { get; set; }
    [Id(18)] [JsonProperty("app_id")] public string AppId { get; set; }
    [Id(19)] [JsonProperty("offer_code")] public string OfferCode { get; set; }
    [Id(20)] [JsonProperty("currency")] public string Currency { get; set; }
    [Id(21)] [JsonProperty("price")] public double? Price { get; set; }
    [Id(22)] [JsonProperty("price_in_purchased_currency")] public double? PriceInPurchasedCurrency { get; set; }
    [Id(23)] [JsonProperty("subscriber_attributes")] public Dictionary<string, RevenueCatSubscriberAttribute> SubscriberAttributes { get; set; } = new Dictionary<string, RevenueCatSubscriberAttribute>();
    [Id(24)] [JsonProperty("store")] public string Store { get; set; }
    [Id(25)] [JsonProperty("takehome_percentage")] public double? TakehomePercentage { get; set; }
    [Id(26)] [JsonProperty("commission_percentage")] public double? CommissionPercentage { get; set; }
    [Id(27)] [JsonProperty("cancel_reason")] public string CancelReason { get; set; }
}

/// <summary>
/// RevenueCat subscriber attribute
/// </summary>
[GenerateSerializer]
public class RevenueCatSubscriberAttribute
{
    [Id(0)] [JsonProperty("value")] public string Value { get; set; }
    [Id(1)] [JsonProperty("updated_at_ms")] public long? UpdatedAtMs { get; set; }
}

/// <summary>
/// RevenueCat webhook event types
/// </summary>
public static class RevenueCatWebhookEventTypes
{
    public const string INITIAL_PURCHASE = "INITIAL_PURCHASE";
    public const string RENEWAL = "RENEWAL";
    public const string CANCELLATION = "CANCELLATION";
    public const string UNCANCELLATION = "UNCANCELLATION";
    public const string NON_RENEWING_PURCHASE = "NON_RENEWING_PURCHASE";
    public const string EXPIRATION = "EXPIRATION";
    public const string BILLING_ISSUE = "BILLING_ISSUE";
    public const string PRODUCT_CHANGE = "PRODUCT_CHANGE";
    public const string TRANSFER = "TRANSFER";
}