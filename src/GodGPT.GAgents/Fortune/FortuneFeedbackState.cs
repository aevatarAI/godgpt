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
    [Obsolete]
    [Id(3)] public string? PredictionMethod { get; set; } // e.g., "opportunity", "bazi", "astrology", "tarot" (Obsolete)
    [Obsolete]
    [Id(4)] public int Rating { get; set; }
    [Obsolete]
    [Id(5)] public List<string> FeedbackTypes { get; set; } = new();
    [Obsolete]
    [Id(6)] public string? Comment { get; set; }
    [Obsolete]
    [Id(7)] public string? Email { get; set; }
    [Obsolete]
    [Id(8)] public bool AgreeToContact { get; set; }
    [Obsolete]
    [Id(9)] public DateTime CreatedAt { get; set; }
    [Obsolete]
    [Id(10)] public DateTime UpdatedAt { get; set; }
    [Id(11)] public Dictionary<string, FeedbackDetail> MethodFeedbacks { get; set; } = new();
}

/// <summary>
/// Feedback detail for a specific prediction method
/// </summary>
[GenerateSerializer]
public class FeedbackDetail
{
    [Id(0)] public string PredictionMethod { get; set; } // e.g., "opportunity", "bazi", "astrology", "tarot"
    [Id(1)] public int Rating { get; set; }
    [Id(2)] public List<string> FeedbackTypes { get; set; } = new();
    [Id(3)] public string? Comment { get; set; }
    [Id(4)] public string? Email { get; set; }
    [Id(5)] public bool AgreeToContact { get; set; }
    [Id(6)] public DateTime CreatedAt { get; set; }
    [Id(7)] public DateTime UpdatedAt { get; set; }
}
