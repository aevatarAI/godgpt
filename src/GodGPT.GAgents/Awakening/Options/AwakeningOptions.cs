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
    public string LLMModel { get; set; } = "gpt-4o-mini";
    public double Temperature { get; set; } = 0.8;
    public int MaxTokens { get; set; } = 200;
    
    // Multi-language support switch
    public bool EnableLanguageSpecificPrompt { get; set; } = false;
    
    // Prompt template configuration
    public string PromptTemplate { get; set; } = "Based on the user's recent conversation content: {CONTENT_SUMMARY}, please generate a personalized awakening level (1-10) and an inspiring awakening sentence in {LANGUAGE}. The response should be motivational and reflect the user's current state and interests. Context: {USER_CONTEXT}. Date: {DATE}. Format your response as JSON: {{\"level\": number, \"message\": \"string\"}}";
    
    // Language instruction templates
    public Dictionary<VoiceLanguageEnum, string> LanguageInstructions { get; set; } = new()
    {
        { VoiceLanguageEnum.Chinese, "Use Chinese for the message" },
        { VoiceLanguageEnum.English, "Use English for the message" },
        { VoiceLanguageEnum.Spanish, "Use Spanish for the message" }
    };
}
