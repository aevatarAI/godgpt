namespace Aevatar.Application.Grains.GoogleAuth.Dtos;

/// <summary>
/// Google identity binding information
/// </summary>
[GenerateSerializer]
public class GoogleIdentityBindingDto
{
    [Id(0)] public string GoogleUserId { get; set; }
    [Id(1)] public Guid? UserId { get; set; }
    [Id(2)] public string Email { get; set; }
    [Id(3)] public string DisplayName { get; set; }
    [Id(4)] public DateTime CreatedAt { get; set; }
    [Id(5)] public DateTime? UpdatedAt { get; set; }
}
