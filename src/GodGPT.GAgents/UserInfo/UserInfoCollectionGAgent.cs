using Aevatar.Application.Grains.UserBilling;
using Aevatar.Application.Grains.UserBilling.SEvents;
using Aevatar.Core;
using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.UserInfo;

public interface IUserInfoCollectionGAgent : IGAgent
{
}

[GAgent(nameof(UserInfoCollectionGAgent))]
public class UserInfoCollectionGAgent: GAgentBase<UserInfoCollectionGAgentState, UserInfoCollectionLogEvent>, IUserInfoCollectionGAgent
{
    public override Task<string> GetDescriptionAsync()
    {
        throw new NotImplementedException();
    }
}