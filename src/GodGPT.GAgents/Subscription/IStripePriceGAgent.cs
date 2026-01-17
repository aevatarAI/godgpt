using Aevatar.Core.Abstractions;
using System.Threading.Tasks;
using Orleans;

namespace Aevatar.Application.Grains.Subscription;

/// <summary>
/// GAgent interface for Stripe-specific price operations.
/// Handles webhooks for price.created, price.updated, price.deleted events.
/// </summary>
public interface IStripePriceGAgent : IGrainWithGuidKey
{
    /// <summary>
    /// Handle Stripe price webhook event with signature validation.
    /// Processes price.created, price.updated, price.deleted events.
    /// </summary>
    /// <param name="json">Raw JSON payload from Stripe webhook</param>
    /// <param name="signature">Stripe-Signature header value</param>
    /// <returns>True if the event was handled successfully</returns>
    Task<bool> HandleWebhookAsync(string json, string signature);
}
