using System;
using System.Collections.Generic;
using Aevatar.Application.Grains.Subscription.Models;
using Aevatar.Core.Abstractions;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.States;

/// <summary>
/// State for SubscriptionProductGAgent, manages all subscription products.
/// </summary>
[GenerateSerializer]
public class SubscriptionProductState : StateBase
{
    /// <summary>
    /// All subscription products indexed by ID.
    /// </summary>
    [Id(0)] 
    public Dictionary<Guid, SubscriptionProduct> Products { get; set; } = new();
}
