using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.GoogleAuth.SEvents;

[GenerateSerializer]
public abstract class GoogleIdentityBindingLogEvent : StateLogEventBase<GoogleIdentityBindingLogEvent>
{
}

[GenerateSerializer]
public class GoogleIdentityBindingCreatedLogEvent : GoogleIdentityBindingLogEvent
{
    [Id(0)] public string GoogleUserId { get; set; }
    [Id(1)] public Guid UserId { get; set; }
    [Id(2)] public string Email { get; set; }
    [Id(3)] public string DisplayName { get; set; }
    [Id(4)] public DateTime CreatedAt { get; set; }
}

[GenerateSerializer]
public class GoogleIdentityBindingUpdatedLogEvent : GoogleIdentityBindingLogEvent
{
    [Id(0)] public string Email { get; set; }
    [Id(1)] public string DisplayName { get; set; }
    [Id(2)] public DateTime UpdatedAt { get; set; }
}
