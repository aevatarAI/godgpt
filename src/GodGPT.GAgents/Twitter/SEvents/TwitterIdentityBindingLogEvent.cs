using System;
using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Twitter.SEvents;

/// <summary>
/// Base event class for Twitter identity binding events
/// </summary>
[GenerateSerializer]
public abstract class TwitterIdentityBindingLogEvent : StateLogEventBase<TwitterIdentityBindingLogEvent>
{
}

/// <summary>
/// Event for when a new Twitter identity binding is created
/// </summary>
[GenerateSerializer]
public class TwitterIdentityBindingCreatedLogEvent : TwitterIdentityBindingLogEvent
{
    [Id(0)]
    public string TwitterUserId { get; set; }
    /// <summary>
    /// System user ID
    /// </summary>
    [Id(1)]
    public Guid UserId { get; set; }

    /// <summary>
    /// Twitter username
    /// </summary>
    [Id(2)]
    public string TwitterUsername { get; set; }

    /// <summary>
    /// Twitter profile image URL
    /// </summary>
    [Id(3)]
    public string ProfileImageUrl { get; set; }

    /// <summary>
    /// When the binding was created
    /// </summary>
    [Id(4)]
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Event for when a Twitter identity binding is updated
/// </summary>
[GenerateSerializer]
public class TwitterIdentityBindingUpdatedLogEvent : TwitterIdentityBindingLogEvent
{
    [Id(0)]
    public string TwitterUserId { get; set; }
    /// <summary>
    /// System user ID
    /// </summary>
    [Id(1)]
    public Guid UserId { get; set; }

    /// <summary>
    /// Twitter username
    /// </summary>
    [Id(2)]
    public string TwitterUsername { get; set; }

    /// <summary>
    /// Twitter profile image URL
    /// </summary>
    [Id(3)]
    public string ProfileImageUrl { get; set; }

    /// <summary>
    /// When the binding was updated
    /// </summary>
    [Id(4)]
    public DateTime UpdatedAt { get; set; }
}