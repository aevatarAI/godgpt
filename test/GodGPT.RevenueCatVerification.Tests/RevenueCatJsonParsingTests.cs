using Aevatar.Application.Grains.ChatManager.UserBilling;

namespace GodGPT.RevenueCatVerification.Tests;

/// <summary>
/// Tests for RevenueCat JSON response parsing
/// Tests the first scenario: correct JSON processing from RevenueCat API
/// </summary>
public class RevenueCatJsonParsingTests : RevenueCatVerificationTestBase
{
    [Fact]
    public void TestParseRevenueCatJsonResponse_ShouldCorrectlyParseProvidedSample()
    {
        // Arrange - The exact JSON response provided by the user
        const string sampleJsonResponse = """
        {
            "request_date": "2025-08-19T02:05:37Z",
            "request_date_ms": 1755569137650,
            "subscriber": {
                "entitlements": {},
                "first_seen": "2025-08-11T13:43:12Z",
                "last_seen": "2025-08-18T03:38:39Z",
                "management_url": "https://play.google.com/store/account/subscriptions",
                "non_subscriptions": {},
                "original_app_user_id": "$RCAnonymousID:520ed1732528410d837118eb2f661158",
                "original_application_version": null,
                "original_purchase_date": null,
                "other_purchases": {},
                "subscriptions": {
                    "premium_weekly_test1": {
                        "auto_resume_date": null,
                        "billing_issues_detected_at": null,
                        "display_name": null,
                        "expires_date": "2025-08-19T02:07:00Z",
                        "grace_period_expires_date": null,
                        "is_sandbox": true,
                        "management_url": "https://play.google.com/store/account/subscriptions",
                        "original_purchase_date": "2025-08-19T02:02:00Z",
                        "period_type": "normal",
                        "price": {
                            "amount": 48.0,
                            "currency": "HKD"
                        },
                        "product_plan_identifier": "test1-week-1",
                        "purchase_date": "2025-08-19T02:02:00Z",
                        "refunded_at": null,
                        "store": "play_store",
                        "store_transaction_id": "GPA.3327-7042-0698-86706",
                        "unsubscribe_detected_at": null
                    }
                }
            }
        }
        """;

        // Act - Parse the JSON using the same deserialization logic as the actual implementation
        var revenueCatResponse = JsonConvert.DeserializeObject<RevenueCatSubscriberResponse>(sampleJsonResponse);

        // Assert - Verify the response structure is correctly parsed
        Assert.NotNull(revenueCatResponse);
        Assert.NotNull(revenueCatResponse.Subscriber);
        Assert.NotNull(revenueCatResponse.Subscriber.Subscriptions);
        Assert.True(revenueCatResponse.Subscriber.Subscriptions.ContainsKey("premium_weekly_test1"));

        var subscription = revenueCatResponse.Subscriber.Subscriptions["premium_weekly_test1"];
        Assert.NotNull(subscription);
        
        // Verify critical fields that the verification logic depends on
        Assert.Equal("GPA.3327-7042-0698-86706", subscription.StoreTransactionId);
        Assert.Equal("play_store", subscription.Store);
        Assert.Equal("2025-08-19T02:02:00Z", subscription.PurchaseDate);
        Assert.Equal("2025-08-19T02:07:00Z", subscription.ExpiresDate);
        Assert.True(subscription.IsSandbox);
        Assert.Equal("test1-week-1", subscription.ProductPlanIdentifier);
        
        // Verify price information
        Assert.NotNull(subscription.Price);
        Assert.Equal(48.0, subscription.Price.Amount);
        Assert.Equal("HKD", subscription.Price.Currency);

        Logger.LogInformation("Successfully parsed RevenueCat JSON response with transaction ID: {TransactionId}", 
            subscription.StoreTransactionId);
    }

    [Fact]
    public void TestRevenueCatTransactionMapping_ShouldCorrectlyMapToInternalFormat()
    {
        // Arrange
        const string transactionId = "GPA.3327-7042-0698-86706";
        const string productId = "premium_weekly_test1";
        var sampleJsonResponse = CreateTestRevenueCatResponse(transactionId, productId);
        
        // Act - Parse and map to internal transaction format
        var revenueCatResponse = JsonConvert.DeserializeObject<RevenueCatSubscriberResponse>(sampleJsonResponse);
        var subscription = revenueCatResponse.Subscriber.Subscriptions[productId];
        
        // Map to RevenueCatTransaction (similar to what QueryRevenueCatForTransactionAsync does)
        var mappedTransaction = new RevenueCatTransaction
        {
            OriginalTransactionId = subscription.StoreTransactionId,
            PurchaseToken = subscription.StoreTransactionId,
            ProductId = productId,
            Store = subscription.Store ?? "play_store",
            PurchaseDate = DateTime.TryParse(subscription.PurchaseDate, out var purchaseDate) ? purchaseDate : DateTime.UtcNow,
            ExpirationDate = DateTime.TryParse(subscription.ExpiresDate, out var expirationDate) ? expirationDate : null
        };

        // Assert - Verify the mapping is correct
        Assert.Equal(transactionId, mappedTransaction.OriginalTransactionId);
        Assert.Equal(transactionId, mappedTransaction.PurchaseToken);
        Assert.Equal(productId, mappedTransaction.ProductId);
        Assert.Equal("play_store", mappedTransaction.Store);
        Assert.True(mappedTransaction.PurchaseDate > DateTime.MinValue);
        Assert.NotNull(mappedTransaction.ExpirationDate);
        Assert.True(mappedTransaction.ExpirationDate > mappedTransaction.PurchaseDate);

        Logger.LogInformation("Successfully mapped RevenueCat response to internal transaction format. " +
            "ProductId: {ProductId}, TransactionId: {TransactionId}, Store: {Store}", 
            mappedTransaction.ProductId, mappedTransaction.OriginalTransactionId, mappedTransaction.Store);
    }

    [Fact]
    public void TestRevenueCatJsonParsing_WithMissingFields_ShouldHandleGracefully()
    {
        // Arrange - JSON with some missing fields to test robustness
        const string incompleteJsonResponse = """
        {
            "request_date": "2025-08-19T02:05:37Z",
            "subscriber": {
                "subscriptions": {
                    "premium_weekly_test1": {
                        "store_transaction_id": "GPA.3327-7042-0698-86706",
                        "store": "play_store",
                        "is_sandbox": true
                    }
                }
            }
        }
        """;

        // Act & Assert - Should not throw exception even with missing fields
        var exception = Record.Exception(() =>
        {
            var response = JsonConvert.DeserializeObject<RevenueCatSubscriberResponse>(incompleteJsonResponse);
            Assert.NotNull(response);
            Assert.NotNull(response.Subscriber);
            Assert.NotNull(response.Subscriber.Subscriptions);
            
            var subscription = response.Subscriber.Subscriptions["premium_weekly_test1"];
            Assert.Equal("GPA.3327-7042-0698-86706", subscription.StoreTransactionId);
            Assert.Equal("play_store", subscription.Store);
            Assert.True(subscription.IsSandbox);
        });

        Assert.Null(exception);
        Logger.LogInformation("Successfully handled RevenueCat JSON response with missing optional fields");
    }

    [Fact]
    public void TestRevenueCatJsonParsing_WithInvalidJson_ShouldThrowJsonException()
    {
        // Arrange
        const string invalidJson = "{ invalid json format }";

        // Act & Assert
        Assert.Throws<JsonReaderException>(() =>
        {
            JsonConvert.DeserializeObject<RevenueCatSubscriberResponse>(invalidJson);
        });

        Logger.LogInformation("Correctly rejected invalid JSON format");
    }

    [Fact]
    public void TestRevenueCatJsonParsing_WithEmptySubscriptions_ShouldReturnEmptyDictionary()
    {
        // Arrange
        const string emptySubscriptionsJson = """
        {
            "request_date": "2025-08-19T02:05:37Z",
            "subscriber": {
                "subscriptions": {}
            }
        }
        """;

        // Act
        var response = JsonConvert.DeserializeObject<RevenueCatSubscriberResponse>(emptySubscriptionsJson);

        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.Subscriber);
        Assert.NotNull(response.Subscriber.Subscriptions);
        Assert.Empty(response.Subscriber.Subscriptions);

        Logger.LogInformation("Successfully handled RevenueCat response with empty subscriptions");
    }
}
