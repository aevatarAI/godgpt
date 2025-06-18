using System.Text.Json.Serialization;
using Aevatar.Application.Grains.Common.Constants;
using System;

namespace Aevatar.Application.Grains.ChatManager.UserBilling;

[GenerateSerializer]
public class VerifyReceiptRequestDto
{
    [Id(0)] public string UserId { get; set; }
    [Id(1)] public bool SandboxMode { get; set; }
    [Id(2)] public string ReceiptData { get; set; }
    [Id(3)] public string TransactionId { get; set; }
}

[GenerateSerializer]
public class VerifyReceiptResponseDto
{
    [Id(0)] public bool IsValid { get; set; }
    [Id(1)] public string Environment { get; set; }
    [Id(2)] public string ProductId { get; set; }
    [Id(3)] public DateTime ExpiresDate { get; set; }
    [Id(4)] public bool IsTrialPeriod { get; set; }
    [Id(5)] public string OriginalTransactionId { get; set; }
    [Id(6)] public string Error { get; set; }
    [Id(7)] public SubscriptionDto Subscription { get; set; }
}

[GenerateSerializer]
public class SubscriptionDto
{
    [Id(0)] public string ProductId { get; set; }
    [Id(1)] public DateTime StartDate { get; set; }
    [Id(2)] public DateTime EndDate { get; set; }
    [Id(3)] public string Status { get; set; }
}

[GenerateSerializer]
public class CreateAppStoreSubscriptionDto
{
    [Id(0)] public string UserId { get; set; }
    [Id(1)] public bool SandboxMode { get; set; } = false;
    [Id(2)] public string TransactionId { get; set; }
}

[GenerateSerializer]
public class AppStoreSubscriptionResponseDto
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string SubscriptionId { get; set; }
    [Id(2)] public DateTime ExpiresDate { get; set; }
    [Id(3)] public string Status { get; set; }
    [Id(4)] public string Error { get; set; }
}

[GenerateSerializer]
public class AppStoreSubscriptionInfo
{
    [Id(0)] public string OriginalTransactionId { get; set; }
    [Id(1)] public string TransactionId { get; set; }
    [Id(2)] public string ProductId { get; set; }
    [Id(3)] public DateTime PurchaseDate { get; set; }
    [Id(4)] public DateTime ExpiresDate { get; set; }
    [Id(5)] public bool IsTrialPeriod { get; set; }
    [Id(6)] public bool AutoRenewStatus { get; set; }
    [Id(7)] public string Environment { get; set; }
    // [Id(8)] public string LatestReceiptData { get; set; }
}

/// <summary>
/// App Store Server Notification types.
/// See: https://developer.apple.com/documentation/appstoreservernotifications/notificationtype
/// </summary>
public enum AppStoreNotificationType
{
    /// <summary>
    /// Unknown notification type.
    /// </summary>
    UNKNOWN,

    /// <summary>
    /// Customer initiated a refund request for a consumable in-app purchase or auto-renewable subscription.
    /// </summary>
    CONSUMPTION_REQUEST,

    /// <summary>
    /// Customer made a change to their subscription plan.
    /// </summary>
    DID_CHANGE_RENEWAL_PREF,

    /// <summary>
    /// Customer made a change to the subscription renewal status.
    /// </summary>
    DID_CHANGE_RENEWAL_STATUS,

    /// <summary>
    /// Subscription failed to renew due to a billing issue.
    /// </summary>
    DID_FAIL_TO_RENEW,

    /// <summary>
    /// Subscription successfully renewed.
    /// </summary>
    DID_RENEW,

    /// <summary>
    /// Subscription expired.
    /// </summary>
    EXPIRED,

    /// <summary>
    /// External purchase token created but not reported.
    /// </summary>
    EXTERNAL_PURCHASE_TOKEN,

    /// <summary>
    /// Billing grace period has ended without renewing the subscription.
    /// </summary>
    GRACE_PERIOD_EXPIRED,

    /// <summary>
    /// Subscription metadata was changed.
    /// </summary>
    METADATA_UPDATE,

    /// <summary>
    /// Subscription was migrated to Advanced Commerce API.
    /// </summary>
    MIGRATION,

    /// <summary>
    /// Customer with an active subscription redeemed a subscription offer.
    /// </summary>
    OFFER_REDEEMED,

    /// <summary>
    /// Customer purchased a consumable, non-consumable, or non-renewing subscription.
    /// </summary>
    ONE_TIME_CHARGE,

    /// <summary>
    /// Subscription price was changed.
    /// </summary>
    PRICE_CHANGE,

    /// <summary>
    /// System informed the customer of an auto-renewable subscription price increase.
    /// </summary>
    PRICE_INCREASE,

    /// <summary>
    /// App Store successfully refunded a transaction.
    /// </summary>
    REFUND,

    /// <summary>
    /// App Store declined a refund request.
    /// </summary>
    REFUND_DECLINED,

    /// <summary>
    /// App Store reversed a previously granted refund.
    /// </summary>
    REFUND_REVERSED,

    /// <summary>
    /// App Store extended the subscription renewal date.
    /// </summary>
    RENEWAL_EXTENDED,

    /// <summary>
    /// App Store is attempting to extend the subscription renewal date.
    /// </summary>
    RENEWAL_EXTENSION,

    /// <summary>
    /// In-app purchase is no longer available through Family Sharing.
    /// </summary>
    REVOKE,

    /// <summary>
    /// Customer subscribed to an auto-renewable subscription.
    /// </summary>
    SUBSCRIBED,

    /// <summary>
    /// Test notification requested by developer.
    /// </summary>
    TEST
}

/// <summary>
/// App Store Server Notification subtypes.
/// See: https://developer.apple.com/documentation/appstoreservernotifications/subtype
/// </summary>
public enum AppStoreNotificationSubtype
{
    /// <summary>
    /// No specific subtype or not applicable.
    /// </summary>
    NONE,

    /// <summary>
    /// Customer accepted the price increase.
    /// </summary>
    ACCEPTED,

    /// <summary>
    /// Customer enabled subscription auto-renewal.
    /// </summary>
    AUTO_RENEW_ENABLED,

    /// <summary>
    /// Customer disabled subscription auto-renewal.
    /// </summary>
    AUTO_RENEW_DISABLED,

    /// <summary>
    /// Subscription recovered after billing failure.
    /// </summary>
    BILLING_RECOVERY,

    /// <summary>
    /// Subscription expired after billing retry period.
    /// </summary>
    BILLING_RETRY,

    /// <summary>
    /// Customer downgraded their subscription.
    /// </summary>
    DOWNGRADE,

    /// <summary>
    /// Renewal extension failed for specific subscription.
    /// </summary>
    FAILURE,

    /// <summary>
    /// Subscription is in grace period.
    /// </summary>
    GRACE_PERIOD,

    /// <summary>
    /// First time subscription purchase.
    /// </summary>
    INITIAL_BUY,

    /// <summary>
    /// Customer has not responded to price increase.
    /// </summary>
    PENDING,

    /// <summary>
    /// Subscription expired due to price increase.
    /// </summary>
    PRICE_INCREASE,

    /// <summary>
    /// Subscription expired because product not available.
    /// </summary>
    PRODUCT_NOT_FOR_SALE,

    /// <summary>
    /// Customer resubscribed after subscription expired.
    /// </summary>
    RESUBSCRIBE,

    /// <summary>
    /// Renewal extension completed for all eligible subscribers.
    /// </summary>
    SUMMARY,

    /// <summary>
    /// External purchase token not reported.
    /// </summary>
    UNREPORTED,

    /// <summary>
    /// Customer upgraded their subscription.
    /// </summary>
    UPGRADE,

    /// <summary>
    /// Subscription expired voluntarily.
    /// </summary>
    VOLUNTARY,

    /// <summary>
    /// Unknown subtype.
    /// </summary>
    UNKNOWN
}

[GenerateSerializer]
public class ResponseBodyV2DecodedPayload
{
    [Id(0)] public string NotificationType { get; set; }
    [Id(1)] public string Subtype { get; set; }
    [Id(2)] public string NotificationUUID { get; set; }
    [Id(3)] public ResponseBodyV2Data Data { get; set; }
    [Id(4)] public long SignedDate { get; set; }
}

[GenerateSerializer]
public class ResponseBodyV2Data
{
    [Id(0)] public string AppAppleId { get; set; }
    [Id(1)] public string BundleId { get; set; }
    [Id(2)] public string BundleVersion { get; set; }
    [Id(3)] public string Environment { get; set; }
    [Id(4)] public string SignedTransactionInfo { get; set; }
    [Id(5)] public string SignedRenewalInfo { get; set; }
    [Id(6)] public int Status { get; set; }
}

/// <summary>
/// Data container for App Store Server Notification V2
/// </summary>

[GenerateSerializer]
public class NotificationDataV2
{
    /// <summary>
    /// The app Apple ID.
    /// </summary>
    [Id(0)] public long? AppAppleId { get; set; }
    
    /// <summary>
    /// The Bundle ID of the app.
    /// </summary>
    [Id(1)] public string BundleId { get; set; }
    
    /// <summary>
    /// The Bundle version of the app.
    /// </summary>
    [Id(2)] public string BundleVersion { get; set; }
    
    /// <summary>
    /// Transaction information signed by the App Store, in JWT format.
    /// </summary>
    [Id(3)] public string SignedTransactionInfo { get; set; }
    
    /// <summary>
    /// Subscription renewal information signed by the App Store, in JWT format.
    /// </summary>
    [Id(4)] public string SignedRenewalInfo { get; set; }
}

/// <summary>
/// Decoded Transaction Info from JWT token in V2 notifications
/// </summary>
[GenerateSerializer]
public class JWSTransactionDecodedPayload
{
    /// <summary>
    /// The original transaction identifier.
    /// </summary>
    [JsonPropertyName("originalTransactionId")]
    [Id(0)] public string OriginalTransactionId { get; set; }
    
    /// <summary>
    /// The transaction identifier.
    /// </summary>
    [JsonPropertyName("transactionId")]
    [Id(1)] public string TransactionId { get; set; }
    
    /// <summary>
    /// The product identifier.
    /// </summary>
    [JsonPropertyName("productId")]
    [Id(2)] public string ProductId { get; set; }
    
    /// <summary>
    /// The purchase date, in milliseconds since 1970.
    /// </summary>
    [JsonPropertyName("purchaseDate")]
    [Id(3)] public long PurchaseDate { get; set; }
    
    /// <summary>
    /// The expiration date for a subscription, in milliseconds since 1970.
    /// </summary>
    [JsonPropertyName("expiresDate")]
    [Id(4)] public long? ExpiresDate { get; set; }
    
    /// <summary>
    /// A value that indicates whether the transaction was purchased using a promotional offer.
    /// </summary>
    [JsonPropertyName("isTrialPeriod")]
    [Id(5)] public bool? IsTrialPeriod { get; set; }
    
    /// <summary>
    /// A value that indicates the in-app ownership type.
    /// </summary>
    [JsonPropertyName("ownershipType")]
    [Id(6)] public string OwnershipType { get; set; }
    
    /// <summary>
    /// A string that identifies the version of the subscription.
    /// </summary>
    [JsonPropertyName("subscriptionGroupIdentifier")]
    [Id(7)] public string SubscriptionGroupIdentifier { get; set; }
    
    /// <summary>
    /// A value that represents the user's status within the family.
    /// </summary>
    [JsonPropertyName("type")]
    [Id(8)] public string Type { get; set; }
    
    /// <summary>
    /// A UUID you created to identify the user's account in your system.
    /// </summary>
    [JsonPropertyName("appAccountToken")]
    [Id(9)] public string AppAccountToken { get; set; }
}

/// <summary>
/// V1版本的App Store服务器通知
/// </summary>
[GenerateSerializer]
public class AppStoreServerNotification
{
    [JsonPropertyName("notification_type")]
    [Id(0)] public string NotificationType { get; set; }
    
    [JsonPropertyName("subtype")]
    [Id(1)] public string Subtype { get; set; }
    
    [JsonPropertyName("environment")]
    [Id(2)] public string Environment { get; set; }
    
    [JsonPropertyName("auto_renew_status")]
    [Id(3)] public bool AutoRenewStatus { get; set; }
    
    [JsonPropertyName("auto_renew_product_id")]
    [Id(4)] public string AutoRenewProductId { get; set; }
    
    [JsonPropertyName("unified_receipt")]
    [Id(5)] public UnifiedReceiptInfo UnifiedReceipt { get; set; }
}

[GenerateSerializer]
public class UnifiedReceiptInfo
{
    [JsonPropertyName("latest_receipt")]
    [Id(0)] public string LatestReceipt { get; set; }
    
    [JsonPropertyName("latest_receipt_info")]
    [Id(1)] public List<LatestReceiptInfo> LatestReceiptInfo { get; set; }
    
    [JsonPropertyName("pending_renewal_info")]
    [Id(2)] public List<PendingRenewalInfo> PendingRenewalInfo { get; set; }
    
    [JsonPropertyName("status")]
    [Id(3)] public int Status { get; set; }
}

[GenerateSerializer]
public class LatestReceiptInfo
{
    [JsonPropertyName("transaction_id")]
    [Id(0)] public string TransactionId { get; set; }
    
    [JsonPropertyName("original_transaction_id")]
    [Id(1)] public string OriginalTransactionId { get; set; }
    
    [JsonPropertyName("product_id")]
    [Id(2)] public string ProductId { get; set; }
    
    [JsonPropertyName("purchase_date_ms")]
    [Id(3)] public string PurchaseDateMs { get; set; }
    
    [JsonPropertyName("expires_date_ms")]
    [Id(4)] public string ExpiresDateMs { get; set; }
    
    [JsonPropertyName("is_trial_period")]
    [Id(5)] public string IsTrialPeriod { get; set; }
}

[GenerateSerializer]
public class PendingRenewalInfo
{
    [JsonPropertyName("auto_renew_product_id")]
    [Id(0)] public string AutoRenewProductId { get; set; }
    
    [JsonPropertyName("auto_renew_status")]
    [Id(1)] public string AutoRenewStatus { get; set; }
    
    [JsonPropertyName("original_transaction_id")]
    [Id(2)] public string OriginalTransactionId { get; set; }
}
[GenerateSerializer]
public class AppleVerifyReceiptResponse
{
    [JsonPropertyName("status")]
    [Id(0)] public int Status { get; set; }
    
    [JsonPropertyName("environment")]
    [Id(1)] public string Environment { get; set; }
    
    [JsonPropertyName("receipt")]
    [Id(2)] public ReceiptInfo Receipt { get; set; }
    
    [JsonPropertyName("latest_receipt")]
    [Id(3)] public string LatestReceipt { get; set; }
    
    [JsonPropertyName("latest_receipt_info")]
    [Id(4)] public List<LatestReceiptInfo> LatestReceiptInfo { get; set; }
    
    [JsonPropertyName("pending_renewal_info")]
    [Id(5)] public List<PendingRenewalInfo> PendingRenewalInfo { get; set; }
}

[GenerateSerializer]
public class ReceiptInfo
{
    [JsonPropertyName("in_app")]
    [Id(0)] public List<InAppPurchaseInfo> InApp { get; set; }
}

[GenerateSerializer]
public class InAppPurchaseInfo
{
    [JsonPropertyName("transaction_id")]
    [Id(0)] public string TransactionId { get; set; }
    
    [JsonPropertyName("original_transaction_id")]
    [Id(1)] public string OriginalTransactionId { get; set; }
    
    [JsonPropertyName("product_id")]
    [Id(2)] public string ProductId { get; set; }
    
    [JsonPropertyName("purchase_date_ms")]
    [Id(3)] public string PurchaseDateMs { get; set; }
    
    [JsonPropertyName("expires_date_ms")]
    [Id(4)] public string ExpiresDateMs { get; set; }
    
    [JsonPropertyName("is_trial_period")]
    [Id(5)] public string IsTrialPeriod { get; set; }
} 

[GenerateSerializer]
public class AppStoreServerNotificationV2
{
    [Id(0)]
    public string SignedPayload { get; set; }
}

[GenerateSerializer]
public class JWSRenewalInfoDecodedPayload
{
    [Id(1)] public string OriginalTransactionId { get; set; }
    [Id(2)] public string AutoRenewProductId { get; set; }
    [Id(3)] public string ProductId { get; set; }
    [Id(4)] public int AutoRenewStatus { get; set; }
    [Id(5)] public decimal RenewalPrice { get; set; }
    [Id(6)] public string Currency { get; set; }
    [Id(7)] public long SignedDate { get; set; }
    [Id(8)] public string Environment { get; set; }
    [Id(9)] public long RecentSubscriptionStartDate { get; set; }
    [Id(10)] public long RenewalDate { get; set; }
    [Id(11)] public string AppTransactionId { get; set; }
}