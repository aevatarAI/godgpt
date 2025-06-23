using Aevatar.Core.Abstractions;

namespace GodGPT.GAgents.ChatManager.UserQuota.SEvents;

[GenerateSerializer]
public abstract class QuotaLogEvent
{
}

[GenerateSerializer]
public class RedeemInitialRewardLogEvent : StateLogEventBase<QuotaLogEvent>
{
    [Id(0)]
    public bool CanReceiveInviteReward { get; set; }
} 