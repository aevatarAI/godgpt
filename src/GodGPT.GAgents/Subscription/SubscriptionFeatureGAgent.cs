using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Application.Grains.Subscription.Dtos;
using Aevatar.Application.Grains.Subscription.Enums;
using Aevatar.Application.Grains.Subscription.Models;
using Aevatar.Application.Grains.Subscription.SEvents;
using Aevatar.Application.Grains.Subscription.States;
using Aevatar.Core;
using Aevatar.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Orleans.Providers;

namespace Aevatar.Application.Grains.Subscription;

/// <summary>
/// GAgent for managing subscription features with event sourcing.
/// </summary>
[StorageProvider(ProviderName = "PubSubStore")]
[LogConsistencyProvider(ProviderName = "LogStorage")]
[GAgent(nameof(SubscriptionFeatureGAgent))]
public class SubscriptionFeatureGAgent : 
    GAgentBase<SubscriptionFeatureState, SubscriptionFeatureEventBase>,
    ISubscriptionFeatureGAgent
{
    private readonly ILogger<SubscriptionFeatureGAgent> _logger;

    public SubscriptionFeatureGAgent(ILogger<SubscriptionFeatureGAgent> logger)
    {
        _logger = logger;
    }

    #region CRUD Operations

    public async Task<SubscriptionFeatureDto> CreateFeatureAsync(CreateSubscriptionFeatureDto dto)
    {
        var featureId = Guid.NewGuid();
        
        _logger.LogInformation("Creating subscription feature: {FeatureId}, NameKey: {NameKey}, Type: {Type}", 
            featureId, dto.NameKey, dto.Type);

        RaiseEvent(new SubscriptionFeatureCreatedEvent
        {
            FeatureId = featureId,
            NameKey = dto.NameKey,
            DescriptionKey = dto.DescriptionKey,
            Type = dto.Type,
            DisplayOrder = dto.DisplayOrder,
            Usage = dto.Usage
        });
        
        await ConfirmEvents();
        
        return MapToDto(State.Features[featureId]);
    }

    public async Task<SubscriptionFeatureDto> UpdateFeatureAsync(
        Guid featureId, UpdateSubscriptionFeatureDto dto)
    {
        if (!State.Features.ContainsKey(featureId))
            throw new KeyNotFoundException($"Feature not found: {featureId}");
        
        _logger.LogInformation("Updating subscription feature: {FeatureId}", featureId);

        RaiseEvent(new SubscriptionFeatureUpdatedEvent
        {
            FeatureId = featureId,
            NameKey = dto.NameKey,
            DescriptionKey = dto.DescriptionKey,
            Type = dto.Type,
            DisplayOrder = dto.DisplayOrder,
            Usage = dto.Usage
        });
        
        await ConfirmEvents();
        
        return MapToDto(State.Features[featureId]);
    }

    public async Task DeleteFeatureAsync(Guid featureId)
    {
        if (!State.Features.ContainsKey(featureId))
            throw new KeyNotFoundException($"Feature not found: {featureId}");
        
        _logger.LogInformation("Deleting subscription feature: {FeatureId}", featureId);

        RaiseEvent(new SubscriptionFeatureDeletedEvent { FeatureId = featureId });
        
        await ConfirmEvents();
    }

    public async Task ReorderFeaturesAsync(List<SubscriptionFeatureOrderItemDto> orders)
    {
        _logger.LogInformation("Reordering {Count} subscription features", orders.Count);

        RaiseEvent(new SubscriptionFeaturesReorderedEvent
        {
            Orders = orders.Select(o => new SubscriptionFeatureOrderItem
            {
                FeatureId = o.FeatureId,
                DisplayOrder = o.DisplayOrder
            }).ToList()
        });
        
        await ConfirmEvents();
    }

    #endregion

    #region Query Operations (AlwaysInterleave for high concurrency)

    public Task<SubscriptionFeature?> GetFeatureAsync(Guid featureId)
    {
        State.Features.TryGetValue(featureId, out var feature);
        return Task.FromResult(feature);
    }

    public Task<List<SubscriptionFeature>> GetAllFeaturesAsync()
    {
        var features = State.Features.Values
            .OrderBy(f => f.DisplayOrder)
            .ToList();
        return Task.FromResult(features);
    }

    public Task<List<SubscriptionFeature>> GetFeaturesByIdsAsync(List<Guid> featureIds)
    {
        var features = State.Features.Values
            .Where(f => featureIds.Contains(f.Id))
            .OrderBy(f => f.DisplayOrder)
            .ToList();
        return Task.FromResult(features);
    }

    public Task<List<SubscriptionFeature>> GetFeaturesByTypeAsync(SubscriptionFeatureType type)
    {
        var features = State.Features.Values
            .Where(f => f.Type == type)
            .OrderBy(f => f.DisplayOrder)
            .ToList();
        return Task.FromResult(features);
    }

    public Task<List<SubscriptionFeature>> GetFeaturesByUsageAsync(SubscriptionFeatureUsage usage)
    {
        var features = State.Features.Values
            .Where(f => f.Usage == usage)
            .OrderBy(f => f.DisplayOrder)
            .ToList();
        return Task.FromResult(features);
    }

    #endregion

    #region Abstract Implementation

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Manages subscription features.");
    }

    protected sealed override void GAgentTransitionState(
        SubscriptionFeatureState state,
        StateLogEventBase<SubscriptionFeatureEventBase> @event)
    {
        switch (@event)
        {
            case SubscriptionFeatureCreatedEvent created:
                state.Features[created.FeatureId] = new SubscriptionFeature
                {
                    Id = created.FeatureId,
                    NameKey = created.NameKey,
                    DescriptionKey = created.DescriptionKey,
                    Type = created.Type,
                    DisplayOrder = created.DisplayOrder,
                    Usage = created.Usage,
                    CreatedAt = DateTime.UtcNow
                };
                break;
                
            case SubscriptionFeatureUpdatedEvent updated:
                if (state.Features.TryGetValue(updated.FeatureId, out var feature))
                {
                    if (updated.NameKey != null) feature.NameKey = updated.NameKey;
                    if (updated.DescriptionKey != null) feature.DescriptionKey = updated.DescriptionKey;
                    if (updated.Type.HasValue) feature.Type = updated.Type.Value;
                    if (updated.DisplayOrder.HasValue) feature.DisplayOrder = updated.DisplayOrder.Value;
                    if (updated.Usage.HasValue) feature.Usage = updated.Usage.Value;
                    feature.UpdatedAt = DateTime.UtcNow;
                }
                break;
                
            case SubscriptionFeatureDeletedEvent deleted:
                state.Features.Remove(deleted.FeatureId);
                break;
                
            case SubscriptionFeaturesReorderedEvent reordered:
                foreach (var order in reordered.Orders)
                {
                    if (state.Features.TryGetValue(order.FeatureId, out var f))
                    {
                        f.DisplayOrder = order.DisplayOrder;
                        f.UpdatedAt = DateTime.UtcNow;
                    }
                }
                break;
        }
    }

    #endregion

    #region Private Helpers

    private static SubscriptionFeatureDto MapToDto(SubscriptionFeature feature)
    {
        return new SubscriptionFeatureDto
        {
            Id = feature.Id,
            NameKey = feature.NameKey,
            Name = feature.NameKey,
            Description = feature.DescriptionKey,
            Type = feature.Type,
            TypeName = feature.Type.ToString(),
            DisplayOrder = feature.DisplayOrder,
            Usage = feature.Usage
        };
    }

    #endregion
}
