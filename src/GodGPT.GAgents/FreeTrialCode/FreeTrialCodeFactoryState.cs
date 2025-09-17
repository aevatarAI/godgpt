using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Core.Abstractions;

namespace GodGPT.GAgents.FreeTrialCode;

[GenerateSerializer]
public class FreeTrialCodeFactoryState : StateBase
{
    [Id(0)] public long? BatchId { get; set; }
    [Id(1)] public Guid OperatorUserId { get; set; }
    [Id(2)] public FreeTrialCodeBatchConfig BatchConfig { get; set; }
    [Id(3)] public DateTime CreationTime { get; set; }
    [Id(4)] public FreeTrialCodeFactoryStatus Status { get; set; }
    [Id(5)] public HashSet<string> GeneratedCodes { get; set; } = new();
    [Id(6)] public HashSet<string> UsedCodes { get; set; } = new();
    [Id(7)] public int TotalCodesGenerated { get; set; } = 0;
    [Id(8)] public int UsedCount { get; set; } = 0;
    [Id(9)] public DateTime LastGenerationTime { get; set; }
}
