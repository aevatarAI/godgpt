using System;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.ChatAgent.GAgent.State;

namespace Aevatar.Application.Grains.Twitter;

/// <summary>
/// State for Twitter identity binding
/// </summary>
[GenerateSerializer]
public class TwitterIdentityBindingState : StateBase
{
    /// <summary>
    /// Twitter user ID
    /// </summary>
    [Id(0)]
    public string TwitterUserId { get; set; }

    /// <summary>
    /// System user ID
    /// </summary>
    [Id(1)]
    public Guid? UserId { get; set; }

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

    /// <summary>
    /// When the binding was last updated
    /// </summary>
    [Id(5)]
    public DateTime? UpdatedAt { get; set; }
}