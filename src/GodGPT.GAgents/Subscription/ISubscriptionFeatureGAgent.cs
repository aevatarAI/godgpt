using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Application.Grains.Subscription.Dtos;
using Aevatar.Application.Grains.Subscription.Enums;
using Aevatar.Application.Grains.Subscription.Models;
using Aevatar.Core.Abstractions;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Subscription;

/// <summary>
/// GAgent interface for managing subscription features.
/// </summary>
public interface ISubscriptionFeatureGAgent : IGAgent
{
    // CRUD
    Task<SubscriptionFeatureDto> CreateFeatureAsync(CreateSubscriptionFeatureDto dto);
    Task<SubscriptionFeatureDto> UpdateFeatureAsync(Guid featureId, UpdateSubscriptionFeatureDto dto);
    Task DeleteFeatureAsync(Guid featureId);
    Task ReorderFeaturesAsync(List<SubscriptionFeatureOrderItemDto> orders);
    
    // Query - [AlwaysInterleave] for high concurrency reads
    [AlwaysInterleave]
    Task<SubscriptionFeature?> GetFeatureAsync(Guid featureId);
    
    [AlwaysInterleave]
    Task<List<SubscriptionFeature>> GetAllFeaturesAsync();
    
    [AlwaysInterleave]
    Task<List<SubscriptionFeature>> GetFeaturesByIdsAsync(List<Guid> featureIds);
    
    [AlwaysInterleave]
    Task<List<SubscriptionFeature>> GetFeaturesByTypeAsync(SubscriptionFeatureType type);
}
