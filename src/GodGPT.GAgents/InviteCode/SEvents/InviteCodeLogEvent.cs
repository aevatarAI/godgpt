using Aevatar.Core.Abstractions;

namespace GodGPT.GAgents.Invitation.SEvents;

[GenerateSerializer]
public class InviteCodeLogEvent : StateLogEventBase<InviteCodeLogEvent>
{
}

[GenerateSerializer]
public class InitializeInviteCodeLogEvent : InviteCodeLogEvent
{
    [Id(0)] public string InviterId { get; set; }
    [Id(1)] public DateTime CreatedAt { get; set; }
}

[GenerateSerializer]
public class DeactivateInviteCodeLogEvent : InviteCodeLogEvent
{
}

[GenerateSerializer]
public class IncrementUsageCountLogEvent : InviteCodeLogEvent
{
} 