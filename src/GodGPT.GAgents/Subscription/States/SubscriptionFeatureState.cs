using System;
using System.Collections.Generic;
using Aevatar.Application.Grains.Subscription.Models;
using Aevatar.Core.Abstractions;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.States;

/// <summary>
/// State for SubscriptionFeatureGAgent, manages all subscription features.
/// </summary>
[GenerateSerializer]
public class SubscriptionFeatureState : StateBase
{
    /// <summary>
    /// All subscription features indexed by ID.
    /// </summary>
    [Id(0)] 
    public Dictionary<Guid, SubscriptionFeature> Features { get; set; } = new();
}
