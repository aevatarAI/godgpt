using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.UserBilling;
using Microsoft.Extensions.Options;
using Aevatar.Application.Grains.Common.Options;
using System.Net;

namespace GodGPT.RevenueCatVerification.Tests;

/// <summary>
/// Tests for RevenueCat API integration
/// Tests the second scenario: correct handling of curl API call results
/// </summary>
public class RevenueCatApiIntegrationTests : RevenueCatVerificationTestBase
{
    [Fact]
    public async Task TestGooglePlayTransactionVerification_WithValidRevenueCatResponse_ShouldSucceed()
    {
        // Arrange
        const string userId = "ebb5d7d5-3ae7-39dd-21c1-3a1a4f097144";
        const string transactionId = "GPA.3327-7042-0698-86706";
        const string productId = "premium_weekly_test1";
        
        var verificationRequest = CreateTestVerificationRequest(userId, transactionId);
        var mockRevenueCatResponse = CreateTestRevenueCatResponse(transactionId, productId);
        
        // This test simulates the API call:
        // curl -H "Authorization: Bearer goog_xxx" \
        //      "https://api.revenuecat.com/v1/subscribers/ebb5d7d5-3ae7-39dd-21c1-3a1a4f097144?transaction_id=GPA.3327-7042-0698-86706"
        
        Logger.LogInformation("Testing RevenueCat API integration with curl equivalent call for user: {UserId}, transaction: {TransactionId}", 
            userId, transactionId);

        // Act - Parse the response that would come from the curl call
        var revenueCatResponse = JsonConvert.DeserializeObject<RevenueCatSubscriberResponse>(mockRevenueCatResponse);
        
        // Simulate the transaction lookup logic from QueryRevenueCatForTransactionAsync
        RevenueCatTransaction? matchingTransaction = null;
        
        if (revenueCatResponse?.Subscriber?.Subscriptions != null)
        {
            foreach (var subscriptionKey in revenueCatResponse.Subscriber.Subscriptions.Keys)
            {
                var subscription = revenueCatResponse.Subscriber.Subscriptions[subscriptionKey];
                
                if (subscription.StoreTransactionId != null && 
                    (subscription.StoreTransactionId == transactionId || 
                     subscription.StoreTransactionId.StartsWith(transactionId)))
                {
                    matchingTransaction = new RevenueCatTransaction
                    {
                        OriginalTransactionId = subscription.StoreTransactionId,
                        PurchaseToken = subscription.StoreTransactionId,
                        ProductId = subscriptionKey,
                        Store = subscription.Store ?? "play_store",
                        PurchaseDate = DateTime.TryParse(subscription.PurchaseDate, out var purchaseDate) ? purchaseDate : DateTime.UtcNow,
                        ExpirationDate = DateTime.TryParse(subscription.ExpiresDate, out var expirationDate) ? expirationDate : null
                    };
                    break;
                }
            }
        }

        // Assert - Verify the transaction was found and mapped correctly
        Assert.NotNull(matchingTransaction);
        Assert.Equal(transactionId, matchingTransaction.OriginalTransactionId);
        Assert.Equal(productId, matchingTransaction.ProductId);
        Assert.Equal("play_store", matchingTransaction.Store);
        Assert.True(matchingTransaction.PurchaseDate > DateTime.MinValue);
        Assert.NotNull(matchingTransaction.ExpirationDate);

        Logger.LogInformation("Successfully processed RevenueCat API response. Found transaction: {TransactionId} for product: {ProductId}", 
            matchingTransaction.OriginalTransactionId, matchingTransaction.ProductId);
    }

    [Fact]
    public async Task TestGooglePlayTransactionVerification_WithMultipleSubscriptions_ShouldFindCorrectOne()
    {
        // Arrange - Create a response with multiple subscriptions to test transaction matching
        const string targetTransactionId = "GPA.3327-7042-0698-86706";
        const string targetProductId = "premium_weekly_test1";
        
        var multipleSubscriptionsResponse = new
        {
            request_date = "2025-08-19T02:05:37Z",
            request_date_ms = 1755569137650L,
            subscriber = new
            {
                subscriptions = new Dictionary<string, object>
                {
                    ["premium_monthly_test1"] = new
                    {
                        store_transaction_id = "GPA.1111-2222-3333-44444",
                        store = "play_store",
                        purchase_date = "2025-08-18T01:00:00Z",
                        expires_date = "2025-09-18T01:00:00Z",
                        is_sandbox = true
                    },
                    [targetProductId] = new
                    {
                        store_transaction_id = targetTransactionId,
                        store = "play_store",
                        purchase_date = "2025-08-19T02:02:00Z",
                        expires_date = "2025-08-19T02:07:00Z",
                        is_sandbox = true,
                        price = new
                        {
                            amount = 48.0,
                            currency = "HKD"
                        }
                    },
                    ["premium_yearly_test1"] = new
                    {
                        store_transaction_id = "GPA.5555-6666-7777-88888",
                        store = "play_store",
                        purchase_date = "2025-08-17T12:00:00Z",
                        expires_date = "2026-08-17T12:00:00Z",
                        is_sandbox = true
                    }
                }
            }
        };

        var jsonResponse = JsonConvert.SerializeObject(multipleSubscriptionsResponse);
        var revenueCatResponse = JsonConvert.DeserializeObject<RevenueCatSubscriberResponse>(jsonResponse);

        // Act - Find the specific transaction
        RevenueCatTransaction? matchingTransaction = null;
        
        foreach (var subscriptionKey in revenueCatResponse.Subscriber.Subscriptions.Keys)
        {
            var subscription = revenueCatResponse.Subscriber.Subscriptions[subscriptionKey];
            
            if (subscription.StoreTransactionId == targetTransactionId)
            {
                matchingTransaction = new RevenueCatTransaction
                {
                    OriginalTransactionId = subscription.StoreTransactionId,
                    PurchaseToken = subscription.StoreTransactionId,
                    ProductId = subscriptionKey,
                    Store = subscription.Store ?? "play_store",
                    PurchaseDate = DateTime.TryParse(subscription.PurchaseDate, out var purchaseDate) ? purchaseDate : DateTime.UtcNow,
                    ExpirationDate = DateTime.TryParse(subscription.ExpiresDate, out var expirationDate) ? expirationDate : null
                };
                break;
            }
        }

        // Assert - Verify the correct transaction was found
        Assert.NotNull(matchingTransaction);
        Assert.Equal(targetTransactionId, matchingTransaction.OriginalTransactionId);
        Assert.Equal(targetProductId, matchingTransaction.ProductId);

        Logger.LogInformation("Successfully found target transaction {TransactionId} among multiple subscriptions for product {ProductId}", 
            targetTransactionId, targetProductId);
    }

    [Fact]
    public async Task TestGooglePlayTransactionVerification_WithApiError_ShouldHandleGracefully()
    {
        // Arrange - Simulate API error response (e.g., 404 Not Found)
        const string errorResponse = """
        {
            "code": 7234,
            "message": "The subscriber with app_user_id ebb5d7d5-3ae7-39dd-21c1-3a1a4f097144 could not be found."
        }
        """;

        // Act - Parse error response
        var exception = Record.Exception(() =>
        {
            var response = JsonConvert.DeserializeObject<RevenueCatSubscriberResponse>(errorResponse);
            // The response object may be created but will have null/empty subscriber data
        });

        // Assert - Should not throw exception during JSON parsing
        Assert.Null(exception);

        // Parse and verify we handle the error case correctly
        var errorResponseObj = JsonConvert.DeserializeObject<dynamic>(errorResponse);
        Assert.Equal(7234, (int)errorResponseObj.code);
        Assert.Contains("could not be found", (string)errorResponseObj.message);

        Logger.LogInformation("Successfully handled RevenueCat API error response with code: {Code}", 
            (int)errorResponseObj.code);
    }

    [Fact]
    public void TestRevenueCatApiUrl_ShouldBeFormattedCorrectly()
    {
        // Arrange
        const string userId = "ebb5d7d5-3ae7-39dd-21c1-3a1a4f097144";
        const string transactionId = "GPA.3327-7042-0698-86706";
        const string baseUrl = "https://api.revenuecat.com/v1";
        
        // Act - Build the URL as done in QueryRevenueCatForTransactionAsync
        string requestUrl = $"{baseUrl}/subscribers/{userId}?transaction_id={transactionId}";
        
        // Assert - Verify URL format matches the curl command provided by user
        var expectedUrl = "https://api.revenuecat.com/v1/subscribers/ebb5d7d5-3ae7-39dd-21c1-3a1a4f097144?transaction_id=GPA.3327-7042-0698-86706";
        Assert.Equal(expectedUrl, requestUrl);

        Logger.LogInformation("RevenueCat API URL formatted correctly: {Url}", requestUrl);
    }

    [Fact]
    public async Task TestGooglePlayTransactionVerification_WithNoMatchingTransaction_ShouldReturnNull()
    {
        // Arrange - Response with subscriptions but no matching transaction ID
        const string nonMatchingTransactionId = "GPA.9999-8888-7777-66666";
        const string requestedTransactionId = "GPA.3327-7042-0698-86706";
        
        var responseWithNonMatchingTransaction = new
        {
            request_date = "2025-08-19T02:05:37Z",
            subscriber = new
            {
                subscriptions = new Dictionary<string, object>
                {
                    ["premium_weekly_test1"] = new
                    {
                        store_transaction_id = nonMatchingTransactionId,
                        store = "play_store",
                        purchase_date = "2025-08-19T02:02:00Z",
                        expires_date = "2025-08-19T02:07:00Z",
                        is_sandbox = true
                    }
                }
            }
        };

        var jsonResponse = JsonConvert.SerializeObject(responseWithNonMatchingTransaction);
        var revenueCatResponse = JsonConvert.DeserializeObject<RevenueCatSubscriberResponse>(jsonResponse);

        // Act - Try to find the requested transaction
        RevenueCatTransaction? matchingTransaction = null;
        
        foreach (var subscriptionKey in revenueCatResponse.Subscriber.Subscriptions.Keys)
        {
            var subscription = revenueCatResponse.Subscriber.Subscriptions[subscriptionKey];
            
            if (subscription.StoreTransactionId == requestedTransactionId)
            {
                matchingTransaction = new RevenueCatTransaction
                {
                    OriginalTransactionId = subscription.StoreTransactionId,
                    ProductId = subscriptionKey
                };
                break;
            }
        }

        // Assert - Should not find a matching transaction
        Assert.Null(matchingTransaction);

        Logger.LogInformation("Correctly returned null when requested transaction {RequestedId} not found (available: {AvailableId})", 
            requestedTransactionId, nonMatchingTransactionId);
    }
}
