using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Agents.Anonymous.SEvents;

/// <summary>
/// Base event log for Anonymous User GAgent
/// </summary>
[GenerateSerializer]
public class AnonymousUserEventLog : StateLogEventBase<AnonymousUserEventLog>
{
}

/// <summary>
/// Event log for creating a guest session
/// </summary>
[GenerateSerializer]
public class CreateGuestSessionEventLog : AnonymousUserEventLog
{
    [Id(0)] public Guid SessionId { get; set; }
    [Id(1)] public string? Guider { get; set; }
    [Id(2)] public DateTime CreateAt { get; set; }
}

/// <summary>
/// Event log for guest chat activity
/// </summary>
[GenerateSerializer]
public class GuestChatEventLog : AnonymousUserEventLog
{
    [Id(0)] public int ChatCount { get; set; }
    [Id(1)] public DateTime ChatAt { get; set; }
    [Id(2)] public bool SessionUsed { get; set; }
}

/// <summary>
/// Event log for initializing anonymous user record
/// </summary>
[GenerateSerializer]
public class InitializeAnonymousUserEventLog : AnonymousUserEventLog
{
    [Id(0)] public string UserHashId { get; set; } = string.Empty;
    [Id(1)] public DateTime CreatedAt { get; set; }
} 