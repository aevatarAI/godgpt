using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Core.Abstractions;

namespace GodGPT.GAgents.FreeTrialCode.SEvents;

[GenerateSerializer]
public class FreeTrialCodeFactoryLogEvent : StateLogEventBase<FreeTrialCodeFactoryLogEvent>
{
}

[GenerateSerializer]
public class InitializeFactoryLogEvent : FreeTrialCodeFactoryLogEvent
{
    [Id(0)] public long BatchId { get; set; }
    [Id(1)] public Guid OperatorUserId { get; set; }
    [Id(2)] public FreeTrialCodeBatchConfig BatchConfig { get; set; }
    [Id(3)] public DateTime CreationTime { get; set; }
    [Id(4)] public FreeTrialCodeFactoryStatus Status { get; set; } = FreeTrialCodeFactoryStatus.Active;
}

[GenerateSerializer]
public class GenerateCodesLogEvent : FreeTrialCodeFactoryLogEvent
{
    [Id(0)] public List<string> GeneratedCodes { get; set; }
    [Id(1)] public int Quantity { get; set; }
    [Id(2)] public FreeTrialCodeFactoryStatus Status { get; set; }
    [Id(3)] public DateTime CreationTime { get; set; }
}

[GenerateSerializer]
public class MarkCodeUsedLogEvent : FreeTrialCodeFactoryLogEvent
{
    [Id(0)] public string Code { get; set; }
    [Id(1)] public string UserId { get; set; }
    [Id(2)] public DateTime UsedAt { get; set; }
}

[GenerateSerializer]
public class UpdateFactoryStatusLogEvent : FreeTrialCodeFactoryLogEvent
{
    [Id(0)] public FreeTrialCodeFactoryStatus Status { get; set; }
    [Id(1)] public string Reason { get; set; }
}
