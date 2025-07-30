using GodGPT.GAgents.SpeechChat;

namespace GodGPT.GAgents.Awakening.Options;

/// <summary>
/// Configuration options for the awakening system
/// </summary>
public class AwakeningOptions
{
    public bool EnableAwakening { get; set; } = true;
    public int MaxRetryAttempts { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 30;
    public double Temperature { get; set; } = 0.8;
    
    // Multi-language support switch
    public bool EnableLanguageSpecificPrompt { get; set; } = false;
    
    // Prompt template configuration
    public string PromptTemplate { get; set; } = String.Empty;
    
    // Language instruction templates
    public Dictionary<VoiceLanguageEnum, string> LanguageInstructions { get; set; } = new()
    {
        { VoiceLanguageEnum.Chinese, "Use Chinese for the message" },
        { VoiceLanguageEnum.English, "Use English for the message" },
        { VoiceLanguageEnum.Spanish, "Use Spanish for the message" }
    };
}
