using System.Text.Json.Serialization;
using Aevatar.Application.Grains.Common.Constants;

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

public class AppStoreServerNotification
{
    [JsonPropertyName("notification_type")]
    public string NotificationType { get; set; }
    
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

public enum AppStoreNotificationType
{
    INITIAL_BUY,
    RENEWAL,
    INTERACTIVE_RENEWAL,
    CANCEL,
    REFUND
} 