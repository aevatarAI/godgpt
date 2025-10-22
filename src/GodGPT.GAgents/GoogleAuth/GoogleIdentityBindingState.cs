using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.GoogleAuth;

/// <summary>
/// State for Google identity binding
/// </summary>
[GenerateSerializer]
public class GoogleIdentityBindingState : StateBase
{
    /// <summary>
    /// Google user ID
    /// </summary>
    [Id(0)]
    public string GoogleUserId { get; set; }

    /// <summary>
    /// System user ID
    /// </summary>
    [Id(1)]
    public Guid? UserId { get; set; }

    /// <summary>
    /// Google email
    /// </summary>
    [Id(2)]
    public string Email { get; set; }

    /// <summary>
    /// Google display name
    /// </summary>
    [Id(3)]
    public string DisplayName { get; set; }

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
