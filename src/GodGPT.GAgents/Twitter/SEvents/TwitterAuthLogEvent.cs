using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Twitter.SEvents;

[GenerateSerializer]
public abstract class TwitterAuthLogEvent : StateLogEventBase<TwitterAuthLogEvent>
{
}

[GenerateSerializer]
public class SetCodeVerifierLogEvent : TwitterAuthLogEvent
{
    [Id(0)] public string CodeVerifier { get; set; }
}

[GenerateSerializer]
public class TwitterAccountBoundLogEvent : TwitterAuthLogEvent
{
    [Id(0)] public string UserId { get; set; }

    [Id(1)] public string TwitterId { get; set; }

    [Id(2)] public string Username { get; set; }

    [Id(3)] public string AccessToken { get; set; }

    [Id(4)] public string RefreshToken { get; set; }

    [Id(5)] public DateTime TokenExpiresAt { get; set; }
    
    [Id(6)] public string ProfileImageUrl { get; set; }
}