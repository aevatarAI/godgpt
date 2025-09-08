using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.UserInfo;

[GenerateSerializer]
public class UserInfoCollectionGAgentState : StateBase
{
    [Id(0)] public Guid UserId { get; set; }
}