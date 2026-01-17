using System.ComponentModel.DataAnnotations;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.Dtos;

/// <summary>
/// DTO for updating a subscription label.
/// </summary>
[GenerateSerializer]
public class UpdateSubscriptionLabelDto
{
    /// <summary>
    /// Updated label NameKey.
    /// </summary>
    [Id(0)]
    [MaxLength(128)]
    public string NameKey { get; set; } = string.Empty;
}
