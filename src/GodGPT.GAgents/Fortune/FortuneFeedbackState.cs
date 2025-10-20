using Aevatar.Core.Abstractions;

namespace Aevatar.Application.Grains.Fortune;

/// <summary>
/// Fortune feedback state data
/// </summary>
[GenerateSerializer]
public class FortuneFeedbackState : StateBase
{
    [Id(0)] public string FeedbackId { get; set; } = string.Empty;
    [Id(1)] public string UserId { get; set; } = string.Empty;
    [Id(2)] public Guid PredictionId { get; set; }
    [Id(3)] public int Score { get; set; }
    [Id(4)] public DateTime CreatedAt { get; set; }
    [Id(5)] public DateTime UpdatedAt { get; set; }
}

