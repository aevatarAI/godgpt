using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Agents.Invitation;

[GenerateSerializer]
public class InviteCodeState : StateBase
{
    [Id(0)] public string InviterId { get; set; }
    [Id(1)] public DateTime CreatedAt { get; set; }
    [Id(2)] public bool IsActive { get; set; }
    [Id(3)] public int UsageCount { get; set; }
} 