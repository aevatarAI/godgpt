using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Core.Abstractions;
using Aevatar.Application.Grains.Agents.Invitation;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Invitation;

public interface IInvitationGAgent : IGAgent
{
    /// <summary>
    /// Generate a new invite code for the user
    /// </summary>
    Task<string> GenerateInviteCodeAsync();

    /// <summary>
    /// Get invitation statistics for the user
    /// </summary>
    [ReadOnly]
    Task<InvitationStatsDto> GetInvitationStatsAsync();

    /// <summary>
    /// Get reward tiers based on current invitation count
    /// </summary>
    [ReadOnly]
    Task<List<RewardTierDto>> GetRewardTiersAsync();

    /// <summary>
    /// Get reward history for the user
    /// </summary>
    [ReadOnly]
    Task<List<RewardHistoryDto>> GetRewardHistoryAsync();

    /// <summary>
    /// Process new user registration with invite code
    /// </summary>
    Task<bool> ProcessInviteeRegistrationAsync(string inviteeId);

    /// <summary>
    /// Process invitee's first chat completion
    /// </summary>
    Task ProcessInviteeChatCompletionAsync(string inviteeId);

    /// <summary>
    /// Process invitee's subscription purchase
    /// </summary>
    Task ProcessInviteeSubscriptionAsync(string inviteeId, PlanType planType, bool isUltimate, string invoiceId);

    /// <summary>
    /// Mark a scheduled reward as issued
    /// </summary>
    /// <param name="inviteeId">The ID of the invitee</param>
    /// <param name="invoiceId">The invoice ID of the reward</param>
    Task MarkRewardAsIssuedAsync(string inviteeId, string invoiceId);
}

[GenerateSerializer]
public class InvitationStatsDto
{
    [Id(0)] public int TotalInvites { get; set; }
    [Id(1)] public int ValidInvites { get; set; }
    [Id(2)] public int PendingInvites { get; set; }
    [Id(3)] public int TotalCreditsEarned { get; set; }
    [Id(4)] public string InviteCode { get; set; }
}

[GenerateSerializer]
public class RewardTierDto
{
    [Id(0)] public int InviteCount { get; set; }
    [Id(1)] public int Credits { get; set; }
    [Id(2)] public bool IsCompleted { get; set; }
}

[GenerateSerializer]
public class RewardHistoryDto
{
    [Id(0)] public string InviteeId { get; set; }
    [Id(1)] public int Credits { get; set; }
    [Id(2)] public string RewardType { get; set; }
    [Id(3)] public DateTime IssuedAt { get; set; }
    [Id(4)] public bool IsScheduled { get; set; }
    [Id(5)] public DateTime? ScheduledDate { get; set; }
    [Id(6)] public string InvoiceId { get; set; }
} 