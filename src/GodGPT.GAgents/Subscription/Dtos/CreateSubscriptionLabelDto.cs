using System.ComponentModel.DataAnnotations;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.Dtos;

/// <summary>
/// DTO for creating a subscription label.
/// </summary>
[GenerateSerializer]
public class CreateSubscriptionLabelDto
{
    /// <summary>
    /// Unique label NameKey, used as localization resource key.
    /// Should be snake_case (e.g., "most_popular", "best_value").
    /// </summary>
    [Id(0)]
    [Required]
    [MaxLength(128)]
    public string NameKey { get; set; } = string.Empty;
}
