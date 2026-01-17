using Aevatar.Application.Grains.Subscription.Dtos;
using Aevatar.Application.Grains.Subscription.Models;
using Aevatar.Core.Abstractions;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Subscription;

/// <summary>
/// GAgent interface for managing subscription labels.
/// </summary>
public interface ISubscriptionLabelGAgent : IGAgent
{
    // CRUD
    Task<SubscriptionLabelDto> CreateLabelAsync(CreateSubscriptionLabelDto dto);
    Task<SubscriptionLabelDto> UpdateLabelAsync(Guid labelId, UpdateSubscriptionLabelDto dto);
    Task DeleteLabelAsync(Guid labelId);
    
    // Query - [AlwaysInterleave] for high concurrency reads
    [AlwaysInterleave]
    Task<SubscriptionLabel?> GetLabelAsync(Guid labelId);
    
    [AlwaysInterleave]
    Task<List<SubscriptionLabel>> GetAllLabelsAsync();
}
