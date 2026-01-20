using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Subscription.Dtos;
using Aevatar.Application.Grains.Subscription.Models;
using Aevatar.Application.Grains.Subscription.SEvents;
using Aevatar.Application.Grains.Subscription.States;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Orleans.Providers;

namespace Aevatar.Application.Grains.Subscription;

/// <summary>
/// GAgent for managing subscription products with event sourcing.
/// </summary>
[StorageProvider(ProviderName = "PubSubStore")]
[LogConsistencyProvider(ProviderName = "LogStorage")]
[GAgent(nameof(SubscriptionProductGAgent))]
public class SubscriptionProductGAgent : 
    GAgentBase<SubscriptionProductState, ProductEventBase>,
    ISubscriptionProductGAgent
{
    private readonly ILogger<SubscriptionProductGAgent> _logger;

    public SubscriptionProductGAgent(ILogger<SubscriptionProductGAgent> logger)
    {
        _logger = logger;
    }

    #region CRUD Operations

    public async Task<SubscriptionProduct> CreateProductAsync(CreateProductDto dto)
    {
        var productId = Guid.NewGuid();
        
        _logger.LogInformation("Creating subscription product: {ProductId}, Platform: {Platform}", 
            productId, dto.Platform);

        RaiseEvent(new ProductCreatedEvent
        {
            ProductId = productId,
            NameKey = dto.NameKey,
            LabelId = dto.LabelId,
            PlanType = dto.PlanType,
            DescriptionKey = dto.DescriptionKey,
            HighlightKey = dto.HighlightKey,
            IsUltimate = dto.IsUltimate,
            FeatureIds = dto.FeatureIds,
            PlatformProductId = dto.PlatformProductId,
            Platform = dto.Platform,
            DisplayOrder = dto.DisplayOrder
        });
        
        await ConfirmEvents();
        
        return State.Products[productId];
    }

    public async Task<SubscriptionProduct> UpdateProductAsync(Guid productId, UpdateProductDto dto)
    {
        if (!State.Products.ContainsKey(productId))
            throw new KeyNotFoundException($"Product not found: {productId}");
        
        _logger.LogInformation("Updating subscription product: {ProductId}", productId);

        RaiseEvent(new ProductUpdatedEvent
        {
            ProductId = productId,
            NameKey = dto.NameKey,
            LabelId = dto.LabelId,
            PlanType = dto.PlanType,
            DescriptionKey = dto.DescriptionKey,
            HighlightKey = dto.HighlightKey,
            IsUltimate = dto.IsUltimate,
            FeatureIds = dto.FeatureIds,
            DisplayOrder = dto.DisplayOrder
        });
        
        await ConfirmEvents();
        
        return State.Products[productId];
    }

    public async Task DeleteProductAsync(Guid productId)
    {
        if (!State.Products.ContainsKey(productId))
            throw new KeyNotFoundException($"Product not found: {productId}");
        
        _logger.LogInformation("Deleting subscription product: {ProductId}", productId);

        RaiseEvent(new ProductDeletedEvent { ProductId = productId });
        
        await ConfirmEvents();
    }

    public async Task<SubscriptionProduct> SetProductListedAsync(Guid productId, bool isListed)
    {
        if (!State.Products.ContainsKey(productId))
            throw new KeyNotFoundException($"Product not found: {productId}");
        
        _logger.LogInformation("Setting product {ProductId} listed status to: {IsListed}", 
            productId, isListed);

        RaiseEvent(new ProductListedEvent
        {
            ProductId = productId,
            IsListed = isListed
        });
        
        await ConfirmEvents();
        
        return State.Products[productId];
    }

    #endregion

    #region Query Operations (AlwaysInterleave for high concurrency)

    public Task<SubscriptionProduct?> GetProductAsync(Guid productId)
    {
        State.Products.TryGetValue(productId, out var product);
        return Task.FromResult(product);
    }

    public Task<List<SubscriptionProduct>> GetAllProductsAsync()
    {
        return Task.FromResult(State.Products.Values.ToList());
    }

    public Task<List<SubscriptionProduct>> GetListedProductsByPlatformAsync(PaymentPlatform platform)
    {
        var products = State.Products.Values
            .Where(p => p.Platform == platform && p.IsListed == true)
            .ToList();
        return Task.FromResult(products);
    }

    public Task<SubscriptionProduct?> GetProductByPlatformProductIdAsync(
        string platformProductId, PaymentPlatform platform)
    {
        var product = State.Products.Values
            .FirstOrDefault(p => p.PlatformProductId == platformProductId && p.Platform == platform);
        return Task.FromResult(product);
    }

    #endregion

    #region Abstract Implementation

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Manages subscription products.");
    }

    protected sealed override void GAgentTransitionState(
        SubscriptionProductState state,
        StateLogEventBase<ProductEventBase> @event)
    {
        switch (@event)
        {
            case ProductCreatedEvent created:
                state.Products[created.ProductId] = new SubscriptionProduct
                {
                    Id = created.ProductId,
                    NameKey = created.NameKey,
                    LabelId = created.LabelId,
                    PlanType = created.PlanType,
                    DescriptionKey = created.DescriptionKey,
                    HighlightKey = created.HighlightKey,
                    IsUltimate = created.IsUltimate,
                    FeatureIds = created.FeatureIds ?? new List<Guid>(),
                    PlatformProductId = created.PlatformProductId,
                    Platform = created.Platform,
                    IsListed = null,
                    CreatedAt = DateTime.UtcNow,
                    DisplayOrder = created.DisplayOrder
                };
                break;
                
            case ProductUpdatedEvent updated:
                if (state.Products.TryGetValue(updated.ProductId, out var product))
                {
                    if (updated.NameKey != null) product.NameKey = updated.NameKey;
                    if (updated.LabelId.HasValue) 
                        product.LabelId = updated.LabelId.Value == Guid.Empty ? null : updated.LabelId;
                    if (updated.PlanType.HasValue) product.PlanType = updated.PlanType.Value;
                    if (updated.DescriptionKey != null) product.DescriptionKey = updated.DescriptionKey;
                    if (updated.HighlightKey != null) product.HighlightKey = updated.HighlightKey;
                    if (updated.IsUltimate.HasValue) product.IsUltimate = updated.IsUltimate.Value;
                    if (updated.FeatureIds != null) product.FeatureIds = updated.FeatureIds;
                    if (updated.DisplayOrder.HasValue) product.DisplayOrder = updated.DisplayOrder.Value;
                    product.UpdatedAt = DateTime.UtcNow;
                }
                break;
                
            case ProductDeletedEvent deleted:
                state.Products.Remove(deleted.ProductId);
                break;
                
            case ProductListedEvent listed:
                if (state.Products.TryGetValue(listed.ProductId, out var p))
                {
                    p.IsListed = listed.IsListed;
                    p.UpdatedAt = DateTime.UtcNow;
                }
                break;
        }
    }

    #endregion
}
