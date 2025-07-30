using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AIGAgent.Dtos;

namespace Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.GEvents;
[GenerateSerializer]
public class AIAgentStatusProxyInitializeGEvent:EventBase
{
    [Id(0)]  public InitializeDto InitializeDto { get; set; }
}