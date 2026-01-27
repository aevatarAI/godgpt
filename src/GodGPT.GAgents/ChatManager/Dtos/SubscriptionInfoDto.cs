using Aevatar.Application.Grains.Common.Constants;

namespace Aevatar.Application.Grains.ChatManager.UserQuota;

[GenerateSerializer]
public class SubscriptionInfoDto
{
    [Id(0)] public bool IsActive { get; set; } = false;
    [Id(1)] public PlanType PlanType { get; set; } = PlanType.None;
    [Id(2)] public PaymentStatus Status { get; set; }
    [Id(3)] public DateTime StartDate { get; set; }
    [Id(4)] public DateTime EndDate { get; set; }
    [Id(5)] public List<string> SubscriptionIds { get; set; } = new List<string>();
    [Id(6)] public List<string> InvoiceIds { get; set; } = new List<string>();
    [Id(7)] public string? PlatformProductId { get; set; }
    [Id(8)] public string? PlatformPriceId { get; set; }
}
