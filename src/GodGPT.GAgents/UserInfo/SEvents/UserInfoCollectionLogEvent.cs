using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Core.Abstractions;
using Aevatar.Application.Grains.ChatManager.UserBilling;

namespace Aevatar.Application.Grains.UserBilling.SEvents;

[GenerateSerializer]
public class UserInfoCollectionLogEvent : StateLogEventBase<UserInfoCollectionLogEvent>
{
}

[GenerateSerializer]
public class AddUserInfoLogEvent : UserInfoCollectionLogEvent
{
    [Id(0)] public Guid UserId { get; set; }
}

