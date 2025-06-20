using Orleans.Concurrency;

namespace GodGPT.GAgents.MineAI.Dtos
{
    /// <summary>
    /// Response DTO for awakening score calculation
    /// </summary>
    [Immutable]
    [GenerateSerializer]
    public class AwakeningScoreResponse
    {
        /// <summary>
        /// Original request ID
        /// </summary>
        [Id(0)]
        public string Id { get; set; }

        /// <summary>
        /// Whether the calculation was successful
        /// </summary>
        [Id(1)]
        public bool IsSuccess { get; set; }

        /// <summary>
        /// Calculated awakening score (1-100)
        /// </summary>
        [Id(2)]
        public int Score { get; set; }

        /// <summary>
        /// Additional message or explanation
        /// </summary>
        [Id(3)]
        public string Message { get; set; }
    }
} 