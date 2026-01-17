using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Subscription.Dtos;
using Aevatar.Application.Grains.Subscription.Models;
using Aevatar.Core.Abstractions;
using Orleans;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Subscription;

/// <summary>
/// GAgent interface for managing subscription products.
/// </summary>
public interface ISubscriptionProductGAgent : IGAgent
{
    // CRUD
    Task<SubscriptionProductDto> CreateProductAsync(CreateProductDto dto);
    Task<SubscriptionProductDto> UpdateProductAsync(Guid productId, UpdateProductDto dto);
    Task DeleteProductAsync(Guid productId);
    Task<SubscriptionProductDto> SetProductListedAsync(Guid productId, bool isListed);
    
    // Query - [AlwaysInterleave] for high concurrency reads
    [AlwaysInterleave]
    Task<SubscriptionProduct?> GetProductAsync(Guid productId);
    
    [AlwaysInterleave]
    Task<List<SubscriptionProduct>> GetAllProductsAsync();
    
    [AlwaysInterleave]
    Task<List<SubscriptionProduct>> GetListedProductsByPlatformAsync(PaymentPlatform platform);
    
    [AlwaysInterleave]
    Task<SubscriptionProduct?> GetProductByPlatformProductIdAsync(string platformProductId, PaymentPlatform platform);
}
