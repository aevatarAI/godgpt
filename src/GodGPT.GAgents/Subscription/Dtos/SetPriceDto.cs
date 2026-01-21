using System.ComponentModel.DataAnnotations;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Subscription.Enums;
using Orleans;

namespace Aevatar.Application.Grains.Subscription.Dtos;

/// <summary>
/// DTO for setting a platform price.
/// </summary>
[GenerateSerializer]
public class SetPriceDto
{
    [Id(0)]
    [Required]
    public PaymentPlatform Platform { get; set; }
    
    [Id(1)]
    [MaxLength(128)]
    public string? PlatformPriceId { get; set; }
    
    [Id(2)]
    [Required]
    [Range(0.01, double.MaxValue)]
    public decimal Price { get; set; }
    
    [Id(3)]
    [Required]
    [MaxLength(3)]
    public string Currency { get; set; } = "USD";
}
