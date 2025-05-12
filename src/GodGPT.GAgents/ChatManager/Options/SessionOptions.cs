namespace Aevatar.Application.Grains.Agents.ChatManager.Options
{
    /// <summary>
    /// Session options
    /// </summary>
    public class SessionOptions
    {
        /// <summary>
        /// Whether to enable voice input
        /// </summary>
        public bool EnableVoiceInput { get; set; } = false;
        
        /// <summary>
        /// Whether to enable voice output
        /// </summary>
        public bool EnableVoiceOutput { get; set; } = false;
        
        /// <summary>
        /// Voice name
        /// </summary>
        public string VoiceName { get; set; } = "zh-CN-XiaoxiaoNeural";
        
        /// <summary>
        /// Session title
        /// </summary>
        public string Title { get; set; } = "";
        
        /// <summary>
        /// System prompt
        /// </summary>
        public string SystemPrompt { get; set; } = "";
    }
} 