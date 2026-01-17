using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
/// GAgent for managing subscription labels with event sourcing.
/// </summary>
[StorageProvider(ProviderName = "PubSubStore")]
[LogConsistencyProvider(ProviderName = "LogStorage")]
[GAgent(nameof(SubscriptionLabelGAgent))]
public class SubscriptionLabelGAgent : 
    GAgentBase<SubscriptionLabelState, SubscriptionLabelEventBase>,
    ISubscriptionLabelGAgent
{
    private readonly ILogger<SubscriptionLabelGAgent> _logger;

    public SubscriptionLabelGAgent(ILogger<SubscriptionLabelGAgent> logger)
    {
        _logger = logger;
    }

    #region CRUD Operations

    public async Task<SubscriptionLabelDto> CreateLabelAsync(CreateSubscriptionLabelDto dto)
    {
        var labelId = Guid.NewGuid();
        
        _logger.LogInformation("Creating subscription label: {LabelId}, NameKey: {NameKey}", 
            labelId, dto.NameKey);

        RaiseEvent(new SubscriptionLabelCreatedEvent
        {
            LabelId = labelId,
            NameKey = dto.NameKey
        });
        
        await ConfirmEvents();
        
        return MapToDto(State.Labels[labelId]);
    }

    public async Task<SubscriptionLabelDto> UpdateLabelAsync(Guid labelId, UpdateSubscriptionLabelDto dto)
    {
        if (!State.Labels.ContainsKey(labelId))
            throw new KeyNotFoundException($"Label not found: {labelId}");
        
        _logger.LogInformation("Updating subscription label: {LabelId}", labelId);

        RaiseEvent(new SubscriptionLabelUpdatedEvent
        {
            LabelId = labelId,
            NameKey = dto.NameKey
        });
        
        await ConfirmEvents();
        
        return MapToDto(State.Labels[labelId]);
    }

    public async Task DeleteLabelAsync(Guid labelId)
    {
        if (!State.Labels.ContainsKey(labelId))
            throw new KeyNotFoundException($"Label not found: {labelId}");
        
        _logger.LogInformation("Deleting subscription label: {LabelId}", labelId);

        RaiseEvent(new SubscriptionLabelDeletedEvent { LabelId = labelId });
        
        await ConfirmEvents();
    }

    #endregion

    #region Query Operations (AlwaysInterleave for high concurrency)

    public Task<SubscriptionLabel?> GetLabelAsync(Guid labelId)
    {
        State.Labels.TryGetValue(labelId, out var label);
        return Task.FromResult(label);
    }

    public Task<List<SubscriptionLabel>> GetAllLabelsAsync()
    {
        return Task.FromResult(State.Labels.Values.ToList());
    }

    #endregion

    #region Abstract Implementation

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Manages subscription labels.");
    }

    protected sealed override void GAgentTransitionState(
        SubscriptionLabelState state,
        StateLogEventBase<SubscriptionLabelEventBase> @event)
    {
        switch (@event)
        {
            case SubscriptionLabelCreatedEvent created:
                state.Labels[created.LabelId] = new SubscriptionLabel
                {
                    Id = created.LabelId,
                    NameKey = created.NameKey,
                    CreatedAt = DateTime.UtcNow
                };
                break;
                
            case SubscriptionLabelUpdatedEvent updated:
                if (state.Labels.TryGetValue(updated.LabelId, out var label))
                {
                    label.NameKey = updated.NameKey;
                    label.UpdatedAt = DateTime.UtcNow;
                }
                break;
                
            case SubscriptionLabelDeletedEvent deleted:
                state.Labels.Remove(deleted.LabelId);
                break;
        }
    }

    #endregion

    #region Private Helpers

    private static SubscriptionLabelDto MapToDto(SubscriptionLabel label)
    {
        return new SubscriptionLabelDto
        {
            Id = label.Id,
            NameKey = label.NameKey,
            Name = label.NameKey,
            CreatedAt = label.CreatedAt
        };
    }

    #endregion
}
