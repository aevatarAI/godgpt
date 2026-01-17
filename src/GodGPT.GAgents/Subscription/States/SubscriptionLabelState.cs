using System;
using System.Collections.Generic;
using Aevatar.Application.Grains.Subscription.Models;
using Aevatar.Core.Abstractions;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.States;

/// <summary>
/// State for SubscriptionLabelGAgent, manages all subscription labels.
/// </summary>
[GenerateSerializer]
public class SubscriptionLabelState : StateBase
{
    /// <summary>
    /// All subscription labels indexed by ID.
    /// </summary>
    [Id(0)] 
    public Dictionary<Guid, SubscriptionLabel> Labels { get; set; } = new();
}
