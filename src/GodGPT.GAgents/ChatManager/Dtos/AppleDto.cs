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
    [Id(2)] public string ProductId { get; set; }
    [Id(3)] public string ReceiptData { get; set; }
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

public class AppStoreSubscriptionInfo
{
    public string OriginalTransactionId { get; set; }
    public string TransactionId { get; set; }
    public string ProductId { get; set; }
    public DateTime PurchaseDate { get; set; }
    public DateTime ExpiresDate { get; set; }
    public bool IsTrialPeriod { get; set; }
    public bool AutoRenewStatus { get; set; }
    public string Environment { get; set; }
    public string LatestReceiptData { get; set; }
}

/// <summary>
/// App Store Server Notification types.
/// See: https://developer.apple.com/documentation/appstoreservernotifications/notificationtype
/// </summary>
public enum AppStoreNotificationType
{
    /// <summary>
    /// Customer subscribes to a subscription for the first time or purchases a one-time purchase.
    /// </summary>
    INITIAL_BUY,
    
    /// <summary>
    /// Customer's subscription automatically renews successfully.
    /// </summary>
    RENEWAL,
    
    /// <summary>
    /// Customer resubscribes to an active subscription that was canceled but is still within its subscription period.
    /// </summary>
    INTERACTIVE_RENEWAL,
    
    /// <summary>
    /// Customer's subscription fails to renew due to a billing issue.
    /// </summary>
    DID_FAIL_TO_RENEW,
    
    /// <summary>
    /// Customer changes the subscription renewal preference, including downgrading, crossgrading, or upgrading.
    /// </summary>
    DID_CHANGE_RENEWAL_PREF,
    
    /// <summary>
    /// Customer changes the subscription renewal status - enables or disables automatic renewal.
    /// </summary>
    DID_CHANGE_RENEWAL_STATUS,
    
    /// <summary>
    /// Customer's subscription successfully renews, possibly after a billing retry.
    /// </summary>
    DID_RENEW,
    
    /// <summary>
    /// Customer's subscription expires after the subscription renewal fails.
    /// </summary>
    EXPIRED,
    
    /// <summary>
    /// Customer's subscription expires after the billing retry period.
    /// </summary>
    GRACE_PERIOD_EXPIRED,
    
    /// <summary>
    /// Customer receives a refund for a purchase.
    /// </summary>
    REFUND,
    
    /// <summary>
    /// Customer's request for a refund is declined.
    /// </summary>
    REFUND_DECLINE,
    
    /// <summary>
    /// Apple extends the subscription renewal date for all subscribers.
    /// </summary>
    RENEWAL_EXTENDED,
    
    /// <summary>
    /// Status of a subscription date extension.
    /// </summary>
    RENEWAL_EXTENSION,
    
    /// <summary>
    /// Customer redeems an offer code.
    /// </summary>
    OFFER_REDEEMED,
    
    /// <summary>
    /// Price of a subscription increases.
    /// </summary>
    PRICE_INCREASE,
    
    /// <summary>
    /// App Store revokes a family shared purchase.
    /// </summary>
    REVOKE,
    
    /// <summary>
    /// Test notification sent by Apple.
    /// </summary>
    TEST,
    
    /// <summary>
    /// Unknown notification type.
    /// </summary>
    UNKNOWN
}

/// <summary>
/// App Store Server Notification subtypes.
/// See: https://developer.apple.com/documentation/appstoreservernotifications/subtype
/// </summary>
public enum AppStoreNotificationSubtype
{
    /// <summary>
    /// Initial purchase of the subscription.
    /// </summary>
    INITIAL_BUY,
    
    /// <summary>
    /// Customer resubscribed or restored their subscription.
    /// </summary>
    RESUBSCRIBE,
    
    /// <summary>
    /// Customer upgraded their subscription.
    /// </summary>
    UPGRADE,
    
    /// <summary>
    /// Customer downgraded their subscription.
    /// </summary>
    DOWNGRADE,
    
    /// <summary>
    /// Customer enabled auto-renewal for their subscription.
    /// </summary>
    AUTO_RENEW_ENABLED,
    
    /// <summary>
    /// Customer disabled auto-renewal for their subscription.
    /// </summary>
    AUTO_RENEW_DISABLED,
    
    /// <summary>
    /// Subscription is in grace period.
    /// </summary>
    GRACE_PERIOD,
    
    /// <summary>
    /// Subscription expired voluntarily (customer disabled auto-renewal).
    /// </summary>
    VOLUNTARY,
    
    /// <summary>
    /// Subscription expired after billing retry period.
    /// </summary>
    BILLING_RETRY,
    
    /// <summary>
    /// Subscription expired because customer didn't consent to a price increase.
    /// </summary>
    PRICE_INCREASE,
    
    /// <summary>
    /// Subscription expired because the product is no longer available.
    /// </summary>
    PRODUCT_NOT_FOR_SALE,
    
    /// <summary>
    /// Successful billing recovery of a lapsed subscription.
    /// </summary>
    BILLING_RECOVERY,
    
    /// <summary>
    /// Notification for a renewal extension summary.
    /// </summary>
    SUMMARY,
    
    /// <summary>
    /// Renewal extension failed.
    /// </summary>
    FAILURE,
    
    /// <summary>
    /// Customer has not responded to a price increase notification.
    /// </summary>
    PENDING,
    
    /// <summary>
    /// Customer has accepted a price increase.
    /// </summary>
    ACCEPTED,
    
    /// <summary>
    /// No specific subtype or not applicable.
    /// </summary>
    NONE,
    
    /// <summary>
    /// Unknown subtype.
    /// </summary>
    UNKNOWN
}

/// <summary>
/// App Store Server Notification V2 format (root object)
/// </summary>
public class AppStoreServerNotificationV2
{
    [JsonPropertyName("signedPayload")]
    public string SignedPayload { get; set; }
}

/// <summary>
/// Decoded payload for App Store Server Notification V2
/// </summary>
public class ResponseBodyV2DecodedPayload
{
    /// <summary>
    /// The notification type.
    /// </summary>
    [JsonPropertyName("notificationType")]
    public string NotificationType { get; set; }
    
    /// <summary>
    /// Additional information that qualifies the notification type.
    /// </summary>
    [JsonPropertyName("subtype")]
    public string Subtype { get; set; }
    
    /// <summary>
    /// A unique identifier for the notification.
    /// </summary>
    [JsonPropertyName("notificationUUID")]
    public string NotificationUUID { get; set; }
    
    /// <summary>
    /// The version of the notification.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; }
    
    /// <summary>
    /// The time the server generated the notification.
    /// </summary>
    [JsonPropertyName("signedDate")]
    public long SignedDate { get; set; }
    
    /// <summary>
    /// The data associated with the notification.
    /// </summary>
    [JsonPropertyName("data")]
    public NotificationDataV2 Data { get; set; }
    
    /// <summary>
    /// The server environment that the notification applies to, either sandbox or production.
    /// </summary>
    [JsonPropertyName("environment")]
    public string Environment { get; set; }
}

/// <summary>
/// Data container for App Store Server Notification V2
/// </summary>
public class NotificationDataV2
{
    /// <summary>
    /// The app Apple ID.
    /// </summary>
    [JsonPropertyName("appAppleId")]
    public long? AppAppleId { get; set; }
    
    /// <summary>
    /// The Bundle ID of the app.
    /// </summary>
    [JsonPropertyName("bundleId")]
    public string BundleId { get; set; }
    
    /// <summary>
    /// The Bundle version of the app.
    /// </summary>
    [JsonPropertyName("bundleVersion")]
    public string BundleVersion { get; set; }
    
    /// <summary>
    /// Transaction information signed by the App Store, in JWT format.
    /// </summary>
    [JsonPropertyName("signedTransactionInfo")]
    public string SignedTransactionInfo { get; set; }
    
    /// <summary>
    /// Subscription renewal information signed by the App Store, in JWT format.
    /// </summary>
    [JsonPropertyName("signedRenewalInfo")]
    public string SignedRenewalInfo { get; set; }
}

/// <summary>
/// Decoded Transaction Info from JWT token in V2 notifications
/// </summary>
public class JWSTransactionDecodedPayload
{
    /// <summary>
    /// The original transaction identifier.
    /// </summary>
    [JsonPropertyName("originalTransactionId")]
    public string OriginalTransactionId { get; set; }
    
    /// <summary>
    /// The transaction identifier.
    /// </summary>
    [JsonPropertyName("transactionId")]
    public string TransactionId { get; set; }
    
    /// <summary>
    /// The product identifier.
    /// </summary>
    [JsonPropertyName("productId")]
    public string ProductId { get; set; }
    
    /// <summary>
    /// The purchase date, in milliseconds since 1970.
    /// </summary>
    [JsonPropertyName("purchaseDate")]
    public long PurchaseDate { get; set; }
    
    /// <summary>
    /// The expiration date for a subscription, in milliseconds since 1970.
    /// </summary>
    [JsonPropertyName("expiresDate")]
    public long? ExpiresDate { get; set; }
    
    /// <summary>
    /// A value that indicates whether the transaction was purchased using a promotional offer.
    /// </summary>
    [JsonPropertyName("isTrialPeriod")]
    public bool? IsTrialPeriod { get; set; }
    
    /// <summary>
    /// A value that indicates the in-app ownership type.
    /// </summary>
    [JsonPropertyName("ownershipType")]
    public string OwnershipType { get; set; }
    
    /// <summary>
    /// A string that identifies the version of the subscription.
    /// </summary>
    [JsonPropertyName("subscriptionGroupIdentifier")]
    public string SubscriptionGroupIdentifier { get; set; }
    
    /// <summary>
    /// A value that represents the user's status within the family.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; }
    
    /// <summary>
    /// A UUID you created to identify the user's account in your system.
    /// </summary>
    [JsonPropertyName("appAccountToken")]
    public string AppAccountToken { get; set; }
}

/// <summary>
/// Decoded Renewal Info from JWT token in V2 notifications
/// </summary>
public class JWSRenewalInfoDecodedPayload
{
    /// <summary>
    /// The current renewal preference.
    /// </summary>
    [JsonPropertyName("autoRenewProductId")]
    public string AutoRenewProductId { get; set; }
    
    /// <summary>
    /// The auto-renewal status.
    /// </summary>
    [JsonPropertyName("autoRenewStatus")]
    public int AutoRenewStatus { get; set; }
    
    /// <summary>
    /// The reason auto-renewal status changed.
    /// </summary>
    [JsonPropertyName("renewalDate")]
    public long? RenewalDate { get; set; }
    
    /// <summary>
    /// The original transaction identifier.
    /// </summary>
    [JsonPropertyName("originalTransactionId")]
    public string OriginalTransactionId { get; set; }
    
    /// <summary>
    /// The product identifier.
    /// </summary>
    [JsonPropertyName("productId")]
    public string ProductId { get; set; }
}

/// <summary>
/// V1版本的App Store服务器通知
/// </summary>
public class AppStoreServerNotification
{
    [JsonPropertyName("notification_type")]
    public string NotificationType { get; set; }
    
    [JsonPropertyName("subtype")]
    public string Subtype { get; set; }
    
    [JsonPropertyName("environment")]
    public string Environment { get; set; }
    
    [JsonPropertyName("auto_renew_status")]
    public bool AutoRenewStatus { get; set; }
    
    [JsonPropertyName("auto_renew_product_id")]
    public string AutoRenewProductId { get; set; }
    
    [JsonPropertyName("unified_receipt")]
    public UnifiedReceiptInfo UnifiedReceipt { get; set; }
}

public class UnifiedReceiptInfo
{
    [JsonPropertyName("latest_receipt")]
    public string LatestReceipt { get; set; }
    
    [JsonPropertyName("latest_receipt_info")]
    public List<LatestReceiptInfo> LatestReceiptInfo { get; set; }
    
    [JsonPropertyName("pending_renewal_info")]
    public List<PendingRenewalInfo> PendingRenewalInfo { get; set; }
    
    [JsonPropertyName("status")]
    public int Status { get; set; }
}

public class LatestReceiptInfo
{
    [JsonPropertyName("transaction_id")]
    public string TransactionId { get; set; }
    
    [JsonPropertyName("original_transaction_id")]
    public string OriginalTransactionId { get; set; }
    
    [JsonPropertyName("product_id")]
    public string ProductId { get; set; }
    
    [JsonPropertyName("purchase_date_ms")]
    public string PurchaseDateMs { get; set; }
    
    [JsonPropertyName("expires_date_ms")]
    public string ExpiresDateMs { get; set; }
    
    [JsonPropertyName("is_trial_period")]
    public string IsTrialPeriod { get; set; }
}

public class PendingRenewalInfo
{
    [JsonPropertyName("auto_renew_product_id")]
    public string AutoRenewProductId { get; set; }
    
    [JsonPropertyName("auto_renew_status")]
    public string AutoRenewStatus { get; set; }
    
    [JsonPropertyName("original_transaction_id")]
    public string OriginalTransactionId { get; set; }
}

public class AppleVerifyReceiptResponse
{
    [JsonPropertyName("status")]
    public int Status { get; set; }
    
    [JsonPropertyName("environment")]
    public string Environment { get; set; }
    
    [JsonPropertyName("receipt")]
    public ReceiptInfo Receipt { get; set; }
    
    [JsonPropertyName("latest_receipt")]
    public string LatestReceipt { get; set; }
    
    [JsonPropertyName("latest_receipt_info")]
    public List<LatestReceiptInfo> LatestReceiptInfo { get; set; }
    
    [JsonPropertyName("pending_renewal_info")]
    public List<PendingRenewalInfo> PendingRenewalInfo { get; set; }
}

public class ReceiptInfo
{
    [JsonPropertyName("in_app")]
    public List<InAppPurchaseInfo> InApp { get; set; }
}

public class InAppPurchaseInfo
{
    [JsonPropertyName("transaction_id")]
    public string TransactionId { get; set; }
    
    [JsonPropertyName("original_transaction_id")]
    public string OriginalTransactionId { get; set; }
    
    [JsonPropertyName("product_id")]
    public string ProductId { get; set; }
    
    [JsonPropertyName("purchase_date_ms")]
    public string PurchaseDateMs { get; set; }
    
    [JsonPropertyName("expires_date_ms")]
    public string ExpiresDateMs { get; set; }
    
    [JsonPropertyName("is_trial_period")]
    public string IsTrialPeriod { get; set; }
} 