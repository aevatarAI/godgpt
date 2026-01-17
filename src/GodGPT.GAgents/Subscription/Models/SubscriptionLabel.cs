using System;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.Models;

/// <summary>
/// Subscription label model embedded in GAgent state.
/// </summary>
[GenerateSerializer]
public class SubscriptionLabel
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public string NameKey { get; set; } = string.Empty;
    [Id(2)] public DateTime CreatedAt { get; set; }
    [Id(3)] public DateTime? UpdatedAt { get; set; }
}
