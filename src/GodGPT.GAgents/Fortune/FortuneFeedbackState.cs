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
    [Id(3)] public string? PredictionMethod { get; set; } // e.g., "horoscope", "bazi", null for overall
    [Id(4)] public int Rating { get; set; }
    [Id(5)] public List<string> FeedbackTypes { get; set; } = new();
    [Id(6)] public string? Comment { get; set; }
    [Id(7)] public string? Email { get; set; }
    [Id(8)] public bool AgreeToContact { get; set; }
    [Id(9)] public DateTime CreatedAt { get; set; }
    [Id(10)] public DateTime UpdatedAt { get; set; }
}

