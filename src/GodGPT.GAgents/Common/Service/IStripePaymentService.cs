using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.Common.Options;

namespace Aevatar.Application.Grains.Common.Service;

/// <summary>
/// Stripe payment service interface for handling Stripe-specific payment operations
/// </summary>
public interface IStripePaymentService
{
    /// <summary>
    /// Get available Stripe products
    /// </summary>
    /// <param name="options">Stripe configuration options</param>
    /// <returns>List of Stripe products</returns>
    Task<List<StripeProductDto>> GetProductsAsync(StripeOptions options);

    /// <summary>
    /// Get or create Stripe customer
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="options">Stripe configuration options</param>
    /// <returns>Stripe customer ID</returns>
    Task<string> GetOrCreateCustomerAsync(string userId, StripeOptions options);

    /// <summary>
    /// Get Stripe customer details
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="options">Stripe configuration options</param>
    /// <returns>Customer response with ephemeral key</returns>
    Task<GetCustomerResponseDto> GetCustomerAsync(string userId, StripeOptions options);

    /// <summary>
    /// Create Stripe checkout session
    /// </summary>
    /// <param name="dto">Checkout session creation parameters</param>
    /// <param name="options">Stripe configuration options</param>
    /// <returns>Session URL or client secret</returns>
    Task<string> CreateCheckoutSessionAsync(CreateCheckoutSessionDto dto, StripeOptions options);

    /// <summary>
    /// Create Stripe subscription
    /// </summary>
    /// <param name="dto">Subscription creation parameters</param>
    /// <param name="options">Stripe configuration options</param>
    /// <returns>Subscription response</returns>
    Task<SubscriptionResponseDto> CreateSubscriptionAsync(CreateSubscriptionDto dto, StripeOptions options);

    /// <summary>
    /// Create Stripe payment sheet
    /// </summary>
    /// <param name="dto">Payment sheet creation parameters</param>
    /// <param name="options">Stripe configuration options</param>
    /// <returns>Payment sheet response</returns>
    Task<PaymentSheetResponseDto> CreatePaymentSheetAsync(CreatePaymentSheetDto dto, StripeOptions options);

    /// <summary>
    /// Process Stripe webhook event
    /// </summary>
    /// <param name="jsonPayload">Webhook JSON payload</param>
    /// <param name="signature">Stripe signature</param>
    /// <param name="options">Stripe configuration options</param>
    /// <returns>Payment verification result</returns>
    Task<PaymentVerificationResultDto> ProcessWebhookEventAsync(string jsonPayload, string signature, StripeOptions options);

    /// <summary>
    /// Cancel Stripe subscription
    /// </summary>
    /// <param name="dto">Cancellation parameters</param>
    /// <param name="options">Stripe configuration options</param>
    /// <returns>Cancellation response</returns>
    Task<CancelSubscriptionResponseDto> CancelSubscriptionAsync(CancelSubscriptionDto dto, StripeOptions options);
}
