using Orleans.Concurrency;

namespace GodGPT.GAgents.MineAI.Dtos
{
    /// <summary>
    /// Request DTO for awakening score calculation
    /// </summary>
    [Immutable]
    [GenerateSerializer]
    public class AwakeningScoreRequest
    {
        /// <summary>
        /// Unique identifier for the request
        /// </summary>
        [Id(0)]
        public string Id { get; set; }

        /// <summary>
        /// System identifier for authentication
        /// </summary>
        [Id(1)]
        public string SystemId { get; set; }

        /// <summary>
        /// Request timestamp
        /// </summary>
        [Id(2)]
        public string Timestamp { get; set; }

        /// <summary>
        /// Request signature for verification
        /// </summary>
        [Id(3)]
        public string Signature { get; set; }

        /// <summary>
        /// User's prompt for score calculation
        /// </summary>
        [Id(4)]
        public string Prompt { get; set; }
    }
} 