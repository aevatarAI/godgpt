using System;
using Aevatar.Core.Abstractions;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.SEvents;

/// <summary>
/// Base event class for label events.
/// </summary>
[GenerateSerializer]
public class SubscriptionLabelEventBase : StateLogEventBase<SubscriptionLabelEventBase>
{
    [Id(0)] public override Guid Id { get; set; } = Guid.NewGuid();
}

/// <summary>
/// Event raised when a label is created.
/// </summary>
[GenerateSerializer]
public class SubscriptionLabelCreatedEvent : SubscriptionLabelEventBase
{
    [Id(1)] public Guid LabelId { get; set; }
    [Id(2)] public string NameKey { get; set; } = string.Empty;
}

/// <summary>
/// Event raised when a label is updated.
/// </summary>
[GenerateSerializer]
public class SubscriptionLabelUpdatedEvent : SubscriptionLabelEventBase
{
    [Id(1)] public Guid LabelId { get; set; }
    [Id(2)] public string NameKey { get; set; } = string.Empty;
}

/// <summary>
/// Event raised when a label is deleted.
/// </summary>
[GenerateSerializer]
public class SubscriptionLabelDeletedEvent : SubscriptionLabelEventBase
{
    [Id(1)] public Guid LabelId { get; set; }
}
