using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AIGAgent.Dtos;
using Aevatar.Application.Grains.Common.Constants;

namespace Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.GEvents;

[GenerateSerializer]
public class AIAgentStatusProxyInitializeGEvent:EventBase
{
    [Id(0)]  public InitializeDto InitializeDto { get; set; }
}

[GenerateSerializer]
public class UpdateProxyInitStatusGEvent : EventBase
{
    [Id(0)] public Guid ProxyId { get; set; }
    [Id(1)] public ProxyInitStatus Status { get; set; }
    [Id(2)] public Guid ParentId { get; set; }
}